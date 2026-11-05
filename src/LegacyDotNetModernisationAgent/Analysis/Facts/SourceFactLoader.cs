using LegacyDotNetModernisationAgent.Models;

namespace LegacyDotNetModernisationAgent.Analysis.Facts;

public static class SourceFactLoader
{
    public static readonly string[] SupportedLanguages = ["VisualBasic", "CSharp"];

    public static IReadOnlyList<SourceFacts> Load(string repositoryRoot, IReadOnlyList<DotNetProjectAssessment> projects)
    {
        var facts = new List<SourceFacts>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var project in projects)
        {
            foreach (var relative in project.Files.SourceFiles)
            {
                var extension = System.IO.Path.GetExtension(relative).ToLowerInvariant();
                var full = System.IO.Path.Combine(repositoryRoot, relative);

                if (extension is not (".vb" or ".cs") || !File.Exists(full) || !seen.Add(relative))
                {
                    continue;
                }

                var text = File.ReadAllText(full);

                facts.Add(extension == ".vb"
                    ? VbFactExtractor.Extract(relative, project.Name, text)
                    : CSharpFactExtractor.Extract(relative, project.Name, text));
            }
        }

        return BaseKindResolver.Resolve(facts.OrderBy(file => file.Path, StringComparer.Ordinal).ToList());
    }
}

/// <summary>
/// C# has one base list; VB has Inherits and Implements. For files that did not say, decide per entry:
/// a declared interface is an interface, a declared class is the base class, and for an external type the
/// first entry of a class is its base class unless it follows the I-prefix interface convention.
/// </summary>
public static class BaseKindResolver
{
    public static IReadOnlyList<SourceFacts> Resolve(IReadOnlyList<SourceFacts> files)
    {
        var interfaces = files.SelectMany(file => file.Types).Where(type => type.Kind == "Interface")
            .Select(type => type.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var classes = files.SelectMany(file => file.Types).Where(type => type.Kind != "Interface")
            .Select(type => type.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return files.Select(file => file with { Types = file.Types.Select(type => ResolveType(type, interfaces, classes)).ToList() }).ToList();
    }

    private static TypeFact ResolveType(TypeFact type, HashSet<string> interfaces, HashSet<string> classes)
    {
        if (type.Bases.All(@base => @base.Kind != "Unresolved"))
        {
            return type;
        }

        var baseClassTaken = false;
        var resolved = new List<BaseFact>();

        foreach (var @base in type.Bases)
        {
            if (@base.Kind != "Unresolved")
            {
                resolved.Add(@base);
                continue;
            }

            string kind;

            if (type.Kind == "Interface" || interfaces.Contains(@base.SimpleName))
            {
                kind = type.Kind == "Interface" ? "Inherits" : "Implements";
            }
            else if (classes.Contains(@base.SimpleName) && !baseClassTaken)
            {
                kind = "Inherits";
            }
            else if (LooksLikeInterface(@base.SimpleName) || baseClassTaken)
            {
                kind = "Implements";
            }
            else
            {
                kind = "Inherits";
            }

            baseClassTaken |= kind == "Inherits" && type.Kind != "Interface";
            resolved.Add(@base with { Kind = kind });
        }

        return type with { Bases = resolved };
    }

    private static bool LooksLikeInterface(string name) =>
        name.Length > 1 && name[0] == 'I' && char.IsUpper(name[1]);
}
