namespace LegacyDotNetModernisationAgent.Analysis.Facts;

// A language-neutral, syntax-only description of one source file. Each language has an extractor that fills it;
// every analyser reads only this model, so a technology rule is written once and works for VB.NET and C#.

public sealed record ImportFact(string Name, int Line);

/// <summary>Attribute with its simple name and its arguments as compact text (e.g. "GetType(X)" or "typeof(X)").</summary>
public sealed record AttributeFact(string Name, IReadOnlyList<string> Arguments, int Line);

/// <summary>
/// One entry of a base/implements list. Kind is Inherits or Implements; a language whose syntax does not
/// distinguish them (C#) emits Unresolved and BaseKindResolver decides using the whole estate.
/// </summary>
public sealed record BaseFact(
    string Display,
    string Compact,
    string SimpleName,
    IReadOnlyList<string> Names,
    int Line,
    string Kind);

public sealed record MethodFact(
    string Name,
    int StartLine,
    int EndLine,
    bool IsPublic,
    string ReturnType,
    IReadOnlyList<AttributeFact> Attributes);

public sealed record TypeFact(
    string Name,
    string Kind,
    int Line,
    int EndLine,
    IReadOnlyList<AttributeFact> Attributes,
    IReadOnlyList<BaseFact> Bases,
    IReadOnlyList<MethodFact> Methods);

/// <summary>
/// A call or an indexed access. Both languages meet here: VB writes Session("k") and C# writes Session["k"];
/// both have CalleeText "Session". Type is the nearest enclosing class, Method the enclosing method.
/// </summary>
public sealed record InvocationFact(
    string CalleeText,
    string CalleeName,
    string? Qualifier,
    string? TypeArgument,
    string TypeArgumentDisplay,
    string? FirstString,
    bool IsIndexer,
    bool IsAssignmentTarget,
    string? Type,
    string? Method,
    int Line);

public sealed record MemberAccessFact(
    string Qualifier,
    bool QualifierIsPlainName,
    string Member,
    string? Type,
    string? Method,
    int Line);

public sealed record CreationFact(
    string TypeName,
    string? TypeArgument,
    string? Type,
    string? Method,
    int Line);

public sealed record LiteralFact(
    string Value,
    string? Declarator,
    string? Type,
    string? Method,
    int Line);

/// <summary>A name used in code. Type is the nearest enclosing declared type (class, interface or module).</summary>
public sealed record IdentifierFact(string Name, string? Type, int Line);

public sealed record SourceFacts(
    string Path,
    string Project,
    string Language,
    IReadOnlyList<ImportFact> Imports,
    IReadOnlyList<TypeFact> Types,
    IReadOnlyList<InvocationFact> Invocations,
    IReadOnlyList<MemberAccessFact> MemberAccesses,
    IReadOnlyList<CreationFact> Creations,
    IReadOnlyList<LiteralFact> Literals,
    IReadOnlyList<IdentifierFact> Identifiers)
{
    /// <summary>True for ASP.NET code-behind/global files in either language (.aspx.vb, .aspx.cs, Global.asax.cs ...).</summary>
    public bool IsCodeBehind(params string[] markupExtensions) =>
        markupExtensions.Any(extension =>
            Path.EndsWith($".{extension}.vb", StringComparison.OrdinalIgnoreCase)
            || Path.EndsWith($".{extension}.cs", StringComparison.OrdinalIgnoreCase));
}
