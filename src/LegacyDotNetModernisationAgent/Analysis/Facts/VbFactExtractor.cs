using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace LegacyDotNetModernisationAgent.Analysis.Facts;

/// <summary>Fills the neutral fact model from VB.NET syntax trees (syntax only; nothing is compiled).</summary>
public static class VbFactExtractor
{
    public static SourceFacts Extract(string path, string project, string text)
    {
        var root = (CompilationUnitSyntax)VisualBasicSyntaxTree.ParseText(text).GetRoot();

        var imports = root.Imports
            .SelectMany(statement => statement.ImportsClauses)
            .OfType<SimpleImportsClauseSyntax>()
            .Select(clause => new ImportFact(Compact(clause.Name), Line(clause)))
            .ToList();

        var types = root.DescendantNodes()
            .Where(node => node is ClassBlockSyntax or InterfaceBlockSyntax or ModuleBlockSyntax)
            .Select(ToTypeFact)
            .ToList();

        var invocations = root.DescendantNodes().OfType<InvocationExpressionSyntax>().Select(ToInvocation).ToList();

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
                    argument is null ? "" : $"(Of {argument})",
                    NearestClass(creation),
                    EnclosingMethod(creation),
                    Line(creation));
            })
            .ToList();

        var literals = root.DescendantNodes().OfType<LiteralExpressionSyntax>()
            .Where(literal => literal.Kind() == SyntaxKind.StringLiteralExpression)
            .Select(literal => new LiteralFact(
                literal.Token.ValueText,
                literal.Ancestors().OfType<VariableDeclaratorSyntax>().FirstOrDefault()?.Names.FirstOrDefault()?.Identifier.Text,
                NearestClass(literal),
                EnclosingMethod(literal),
                Line(literal)))
            .ToList();

        var identifiers = root.DescendantNodes()
            .Where(node => node is IdentifierNameSyntax or GenericNameSyntax)
            .Select(node => new IdentifierFact(SimpleName(node), NearestDeclaredType(node), Line(node)))
            .ToList();

        return new SourceFacts(path, project, "VisualBasic", imports, types, invocations, memberAccesses, creations, literals, identifiers);
    }

    private static TypeFact ToTypeFact(SyntaxNode node)
    {
        switch (node)
        {
            case ClassBlockSyntax type:
                return new TypeFact(
                    type.ClassStatement.Identifier.Text, "Class", Line(type), EndLine(type),
                    Attributes(type.ClassStatement.AttributeLists),
                    Bases(type.Inherits.SelectMany(statement => statement.Types), "Inherits")
                        .Concat(Bases(type.Implements.SelectMany(statement => statement.Types), "Implements")).ToList(),
                    type.Members.OfType<MethodBlockSyntax>().Select(method => ToMethod(method.SubOrFunctionStatement, method)).ToList());

            case ModuleBlockSyntax module:
                return new TypeFact(
                    module.ModuleStatement.Identifier.Text, "Module", Line(module), EndLine(module),
                    Attributes(module.ModuleStatement.AttributeLists),
                    [],
                    module.Members.OfType<MethodBlockSyntax>().Select(method => ToMethod(method.SubOrFunctionStatement, method)).ToList());

            default:
                var contract = (InterfaceBlockSyntax)node;

                return new TypeFact(
                    contract.InterfaceStatement.Identifier.Text, "Interface", Line(contract), EndLine(contract),
                    Attributes(contract.InterfaceStatement.AttributeLists),
                    Bases(contract.Inherits.SelectMany(statement => statement.Types), "Inherits").ToList(),
                    contract.Members.OfType<MethodStatementSyntax>().Select(method => ToMethod(method, method, isInterfaceMember: true)).ToList());
        }
    }

    private static MethodFact ToMethod(MethodStatementSyntax statement, SyntaxNode block, bool isInterfaceMember = false)
    {
        // VB members with no access modifier are Public; Private, Protected and Friend are not.
        var modifiers = statement.Modifiers.Select(modifier => modifier.Kind()).ToList();
        var isPublic = isInterfaceMember
            || modifiers.Contains(SyntaxKind.PublicKeyword)
            || !modifiers.Any(kind => kind is SyntaxKind.PrivateKeyword or SyntaxKind.ProtectedKeyword or SyntaxKind.FriendKeyword);

        return new MethodFact(
            statement.Identifier.Text,
            Line(block),
            EndLine(block),
            isPublic,
            (statement.AsClause as SimpleAsClauseSyntax)?.Type.ToString().Trim() ?? "(none)",
            Attributes(statement.AttributeLists));
    }

    private static IEnumerable<BaseFact> Bases(IEnumerable<TypeSyntax> types, string kind) =>
        types.Select(type => new BaseFact(
            Text(type),
            Compact(type),
            SimpleName(type),
            type.DescendantNodesAndSelf().Where(node => node is IdentifierNameSyntax or GenericNameSyntax).Select(SimpleName).ToList(),
            Line(type),
            kind));

    private static List<AttributeFact> Attributes(SyntaxList<AttributeListSyntax> lists) =>
        lists.SelectMany(list => list.Attributes)
            .Select(attribute => new AttributeFact(
                SimpleName(attribute.Name),
                attribute.ArgumentList?.Arguments.Select(Compact).ToList() ?? [],
                Line(attribute)))
            .ToList();

    private static InvocationFact ToInvocation(InvocationExpressionSyntax invocation)
    {
        var member = invocation.Expression as MemberAccessExpressionSyntax;
        var calleeName = member is null ? invocation.Expression : member.Name;
        var typeArgument = FirstTypeArgument(calleeName);

        // Literal string arguments in order; null where an argument is not a string literal.
        var stringArguments = invocation.ArgumentList?.Arguments
            .Select(argument => (argument as SimpleArgumentSyntax)?.Expression is LiteralExpressionSyntax literal && literal.Kind() == SyntaxKind.StringLiteralExpression
                ? literal.Token.ValueText
                : null)
            .ToList() ?? [];

        return new InvocationFact(
            Compact(invocation.Expression),
            SimpleName(invocation.Expression),
            member is null ? null : SimpleName(member.Expression),
            typeArgument,
            typeArgument is null ? "" : $"(Of {typeArgument})",
            stringArguments.FirstOrDefault(),
            stringArguments,
            IsIndexer: false,
            invocation.Parent is AssignmentStatementSyntax assignment && assignment.Left == invocation,
            NearestClass(invocation),
            EnclosingMethod(invocation),
            Line(invocation));
    }

    // ---- helpers ------------------------------------------------------------------------

    private static int Line(SyntaxNode node) => node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

    private static int EndLine(SyntaxNode node) => node.GetLocation().GetLineSpan().EndLinePosition.Line + 1;

    private static string Compact(SyntaxNode node) => new(node.ToString().Where(character => !char.IsWhiteSpace(character)).ToArray());

    private static string Text(SyntaxNode node) => Regex.Replace(node.ToString().Trim(), @"\s+", " ");

    /// <summary>Rightmost identifier: Csla.ReadOnlyBase(Of T) -> ReadOnlyBase.</summary>
    private static string SimpleName(SyntaxNode? node) => node switch
    {
        QualifiedNameSyntax qualified => SimpleName(qualified.Right),
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
        node.Ancestors().OfType<ClassBlockSyntax>().FirstOrDefault()?.ClassStatement.Identifier.Text;

    private static string? NearestDeclaredType(SyntaxNode node)
    {
        foreach (var ancestor in node.Ancestors())
        {
            switch (ancestor)
            {
                case ClassBlockSyntax type: return type.ClassStatement.Identifier.Text;
                case InterfaceBlockSyntax type: return type.InterfaceStatement.Identifier.Text;
                case ModuleBlockSyntax type: return type.ModuleStatement.Identifier.Text;
            }
        }

        return null;
    }

    private static string? EnclosingMethod(SyntaxNode node) =>
        node.Ancestors().OfType<MethodBlockSyntax>().FirstOrDefault()?.SubOrFunctionStatement.Identifier.Text;
}
