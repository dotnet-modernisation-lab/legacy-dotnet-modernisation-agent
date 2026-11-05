using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using LegacyDotNetModernisationAgent.Models;

namespace LegacyDotNetModernisationAgent.Analysis;

/// <summary>A parsed VB source file (syntax only; nothing is compiled or executed).</summary>
public sealed record VbSource(string Path, string Project, CompilationUnitSyntax Root);

public static class VbSourceLoader
{
    public static IReadOnlyList<VbSource> Load(string repositoryRoot, IReadOnlyList<DotNetProjectAssessment> projects)
    {
        var sources = new List<VbSource>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var project in projects)
        {
            foreach (var relative in project.Files.SourceFiles.Where(file =>
                         file.EndsWith(".vb", StringComparison.OrdinalIgnoreCase)))
            {
                var full = System.IO.Path.Combine(repositoryRoot, relative);

                if (!File.Exists(full) || !seen.Add(relative))
                {
                    continue;
                }

                var tree = VisualBasicSyntaxTree.ParseText(File.ReadAllText(full));
                sources.Add(new VbSource(relative, project.Name, (CompilationUnitSyntax)tree.GetRoot()));
            }
        }

        return sources.OrderBy(source => source.Path, StringComparer.Ordinal).ToList();
    }
}

internal static class VbSyntax
{
    public static int Line(SyntaxNode node) =>
        node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

    /// <summary>Rightmost identifier: Csla.ReadOnlyBase(Of T) -> ReadOnlyBase.</summary>
    public static string SimpleName(SyntaxNode? node) => node switch
    {
        QualifiedNameSyntax qualified => SimpleName(qualified.Right),
        GenericNameSyntax generic => generic.Identifier.Text,
        IdentifierNameSyntax identifier => identifier.Identifier.Text,
        MemberAccessExpressionSyntax member => SimpleName(member.Name),
        InvocationExpressionSyntax invocation => SimpleName(invocation.Expression),
        _ => node?.ToString().Trim() ?? ""
    };

    public static string? FirstTypeArgument(SyntaxNode? node) => node switch
    {
        QualifiedNameSyntax qualified => FirstTypeArgument(qualified.Right),
        GenericNameSyntax generic => generic.TypeArgumentList.Arguments.FirstOrDefault()?.ToString(),
        MemberAccessExpressionSyntax member => FirstTypeArgument(member.Name),
        _ => null
    };

    public static string Compact(SyntaxNode node) =>
        new string(node.ToString().Where(character => !char.IsWhiteSpace(character)).ToArray());

    /// <summary>Display text: whitespace collapsed to single spaces (keeps "Of").</summary>
    public static string Text(SyntaxNode node) =>
        System.Text.RegularExpressions.Regex.Replace(node.ToString().Trim(), @"\s+", " ");

    public static string? EnclosingClass(SyntaxNode node) =>
        node.Ancestors().OfType<ClassBlockSyntax>().FirstOrDefault()?.ClassStatement.Identifier.Text;

    public static string? EnclosingMethod(SyntaxNode node) =>
        node.Ancestors().OfType<MethodBlockSyntax>().FirstOrDefault()?.SubOrFunctionStatement.Identifier.Text;

    public static string? FirstStringArgument(InvocationExpressionSyntax invocation)
    {
        var argument = invocation.ArgumentList?.Arguments.FirstOrDefault() as SimpleArgumentSyntax;

        return argument?.Expression is LiteralExpressionSyntax literal
            && literal.Kind() == SyntaxKind.StringLiteralExpression
            ? literal.Token.ValueText
            : null;
    }

    public static bool Is(string? left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
