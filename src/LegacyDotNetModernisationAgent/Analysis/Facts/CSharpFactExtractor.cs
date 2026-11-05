using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LegacyDotNetModernisationAgent.Analysis.Facts;

/// <summary>
/// Fills the neutral fact model from C# syntax trees (syntax only; no semantic model, so it works without
/// the estate's references). Notes on where C# differs from VB.NET:
/// - indexing (Session["k"], AppSettings["k"]) is an element access, emitted as an invocation with IsIndexer;
/// - one base list: entries are Unresolved and BaseKindResolver decides Inherits or Implements;
/// - members with no access modifier are private, except interface members, which are public.
/// </summary>
public static class CSharpFactExtractor
{
    public static SourceFacts Extract(string path, string project, string text)
    {
        var root = (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(text).GetRoot();

        var imports = root.DescendantNodes().OfType<UsingDirectiveSyntax>()
            .Where(directive => directive.Alias is null && directive.Name is not null)
            .Select(directive => new ImportFact(Compact(directive.Name!), Line(directive)))
            .ToList();

        var types = root.DescendantNodes().OfType<TypeDeclarationSyntax>().Select(ToTypeFact).ToList();

        var invocations = root.DescendantNodes().OfType<InvocationExpressionSyntax>().Select(ToInvocation)
            .Concat(root.DescendantNodes().OfType<ElementAccessExpressionSyntax>().Select(ToIndexer))
            .OrderBy(call => call.Line)
            .ToList();

        var memberAccesses = root.DescendantNodes().OfType<MemberAccessExpressionSyntax>()
            .Select(member => new MemberAccessFact(
                SimpleName(member.Expression),
                member.Expression is IdentifierNameSyntax,
                member.Name.Identifier.Text,
                NearestClass(member),
                EnclosingMethod(member),
                Line(member)))
            .ToList();

        var creations = root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>()
            .Select(creation =>
            {
                var argument = FirstTypeArgument(creation.Type);

                return new CreationFact(
                    SimpleName(creation.Type),
                    argument,
                    argument is null ? "" : $"<{argument}>",
                    NearestClass(creation),
                    EnclosingMethod(creation),
                    Line(creation));
            })
            .ToList();

        var literals = root.DescendantNodes().OfType<LiteralExpressionSyntax>()
            .Where(literal => literal.IsKind(SyntaxKind.StringLiteralExpression))
            .Select(literal => new LiteralFact(
                literal.Token.ValueText,
                literal.Ancestors().OfType<VariableDeclaratorSyntax>().FirstOrDefault()?.Identifier.Text,
                NearestClass(literal),
                EnclosingMethod(literal),
                Line(literal)))
            .ToList();

        var identifiers = root.DescendantNodes()
            .Where(node => node is IdentifierNameSyntax or GenericNameSyntax)
            .Select(node => new IdentifierFact(SimpleName(node), NearestDeclaredType(node), Line(node)))
            .ToList();

        return new SourceFacts(path, project, "CSharp", imports, types, invocations, memberAccesses, creations, literals, identifiers);
    }

    private static TypeFact ToTypeFact(TypeDeclarationSyntax type)
    {
        var isInterface = type.IsKind(SyntaxKind.InterfaceDeclaration);

        var bases = type.BaseList?.Types
            .Select(entry => new BaseFact(
                Text(entry.Type),
                Compact(entry.Type),
                SimpleName(entry.Type),
                entry.Type.DescendantNodesAndSelf().Where(node => node is IdentifierNameSyntax or GenericNameSyntax).Select(SimpleName).ToList(),
                Line(entry),
                "Unresolved"))
            .ToList() ?? [];

        var methods = type.Members.OfType<MethodDeclarationSyntax>()
            .Select(method => new MethodFact(
                method.Identifier.Text,
                Line(method),
                EndLine(method),
                isInterface || method.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PublicKeyword)),
                method.ReturnType.ToString().Trim(),
                Attributes(method.AttributeLists)))
            .ToList();

        // struct and record are treated as classes for architecture purposes
        return new TypeFact(type.Identifier.Text, isInterface ? "Interface" : "Class", Line(type), EndLine(type), Attributes(type.AttributeLists), bases, methods);
    }

    private static List<AttributeFact> Attributes(SyntaxList<AttributeListSyntax> lists) =>
        lists.SelectMany(list => list.Attributes)
            .Select(attribute => new AttributeFact(
                SimpleName(attribute.Name),
                attribute.ArgumentList?.Arguments.Select(argument => Compact(argument)).ToList() ?? [],
                Line(attribute)))
            .ToList();

    private static InvocationFact ToInvocation(InvocationExpressionSyntax invocation)
    {
        var member = invocation.Expression as MemberAccessExpressionSyntax;
        var calleeName = member is null ? invocation.Expression : member.Name;
        var typeArgument = FirstTypeArgument(calleeName);

        return new InvocationFact(
            Compact(invocation.Expression),
            SimpleName(invocation.Expression),
            member is null ? null : SimpleName(member.Expression),
            typeArgument,
            typeArgument is null ? "" : $"<{typeArgument}>",
            FirstString(invocation.ArgumentList.Arguments),
            IsIndexer: false,
            IsAssignmentTarget: false,
            NearestClass(invocation),
            EnclosingMethod(invocation),
            Line(invocation));
    }

    private static InvocationFact ToIndexer(ElementAccessExpressionSyntax element)
    {
        var member = element.Expression as MemberAccessExpressionSyntax;

        return new InvocationFact(
            Compact(element.Expression),
            SimpleName(element.Expression),
            member is null ? null : SimpleName(member.Expression),
            null,
            "",
            FirstString(element.ArgumentList.Arguments),
            IsIndexer: true,
            element.Parent is AssignmentExpressionSyntax assignment && assignment.Left == element,
            NearestClass(element),
            EnclosingMethod(element),
            Line(element));
    }

    private static string? FirstString(SeparatedSyntaxList<ArgumentSyntax> arguments) =>
        arguments.FirstOrDefault()?.Expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression)
            ? literal.Token.ValueText
            : null;

    // ---- helpers ------------------------------------------------------------------------

    private static int Line(SyntaxNode node) => node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

    private static int EndLine(SyntaxNode node) => node.GetLocation().GetLineSpan().EndLinePosition.Line + 1;

    private static string Compact(SyntaxNode node) => new(node.ToString().Where(character => !char.IsWhiteSpace(character)).ToArray());

    private static string Text(SyntaxNode node) => Regex.Replace(node.ToString().Trim(), @"\s+", " ");

    /// <summary>Rightmost identifier: Csla.ReadOnlyBase&lt;T&gt; -> ReadOnlyBase.</summary>
    private static string SimpleName(SyntaxNode? node) => node switch
    {
        QualifiedNameSyntax qualified => SimpleName(qualified.Right),
        AliasQualifiedNameSyntax alias => SimpleName(alias.Name),
        GenericNameSyntax generic => generic.Identifier.Text,
        IdentifierNameSyntax identifier => identifier.Identifier.Text,
        MemberAccessExpressionSyntax member => SimpleName(member.Name),
        InvocationExpressionSyntax invocation => SimpleName(invocation.Expression),
        _ => node?.ToString().Trim() ?? ""
    };

    private static string? FirstTypeArgument(SyntaxNode? node) => node switch
    {
        QualifiedNameSyntax qualified => FirstTypeArgument(qualified.Right),
        GenericNameSyntax generic => generic.TypeArgumentList.Arguments.FirstOrDefault()?.ToString(),
        MemberAccessExpressionSyntax member => FirstTypeArgument(member.Name),
        _ => null
    };

    private static string? NearestClass(SyntaxNode node) =>
        node.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault(type => !type.IsKind(SyntaxKind.InterfaceDeclaration))?.Identifier.Text;

    private static string? NearestDeclaredType(SyntaxNode node) =>
        node.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault()?.Identifier.Text;

    private static string? EnclosingMethod(SyntaxNode node) =>
        node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault()?.Identifier.Text;
}
