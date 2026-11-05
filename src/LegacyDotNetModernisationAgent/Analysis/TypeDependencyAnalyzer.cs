using LegacyDotNetModernisationAgent.Analysis.Facts;
using LegacyDotNetModernisationAgent.Models;

namespace LegacyDotNetModernisationAgent.Analysis;

public sealed record TypeStructure(
    IReadOnlyList<DeclaredTypeInfo> DeclaredTypes,
    IReadOnlyList<TypeDependency> Dependencies);

/// <summary>
/// Declares every class/interface in the estate and records which declared type refers to which.
/// A name is resolved in the referring project first, then in the projects it references (nearest first).
/// A name that stays ambiguous is skipped rather than guessed. Partial declarations (a code-behind plus its
/// designer file) are one type: the primary file is the one that is not a designer file.
/// </summary>
public sealed class TypeDependencyAnalyzer
{
    private sealed record Part(DeclaredTypeInfo Info, SourceFacts Source, TypeFact Type);

    public TypeStructure Analyze(
        IReadOnlyList<DotNetProjectAssessment> projects,
        IReadOnlyList<SourceFacts> sources)
    {
        var parts = new List<Part>();

        foreach (var source in sources)
        {
            var imports = source.Imports
                .Select(import => import.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.Ordinal)
                .ToList();

            foreach (var type in source.Types)
            {
                parts.Add(new Part(
                    new DeclaredTypeInfo($"{source.Project}/{type.Name}", type.Name, type.Kind, source.Project, source.Path, type.Line, imports),
                    source,
                    type));
            }
        }

        // One declared type per ID; its primary part first so its evidence wins ties.
        var merged = parts
            .GroupBy(part => part.Info.Id, StringComparer.Ordinal)
            .Select(group => group.OrderBy(part => IsDesignerFile(part.Source.Path)).ThenBy(part => part.Source.Path, StringComparer.Ordinal).ToList())
            .ToList();

        var byName = merged
            .Select(group => group[0].Info)
            .GroupBy(info => info.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        var distance = ProjectDistances(projects);
        var dependencies = new Dictionary<(string From, string To), TypeDependency>();

        foreach (var group in merged)
        {
            var info = group[0].Info;

            foreach (var (_, source, type) in group)
            {
                var candidates = type.Bases
                    .SelectMany(baseType => baseType.Names.Select(name => (Name: name, Kind: baseType.Kind, Line: baseType.Line)))
                    .Concat(source.Identifiers.Where(identifier => identifier.Type == type.Name)
                        .Select(identifier => (identifier.Name, Kind: "Reference", identifier.Line)));

                foreach (var (name, kind, line) in candidates)
                {
                    if (!byName.TryGetValue(name, out var matches))
                    {
                        continue;
                    }

                    var target = Resolve(info.Project, matches, distance);

                    if (target is null || target.Id == info.Id)
                    {
                        continue;
                    }

                    var key = (info.Id, target.Id);
                    var dependency = new TypeDependency(info.Id, target.Id, kind, source.Path, line);

                    // Keep the strongest, then the earliest, evidence for each pair.
                    if (!dependencies.TryGetValue(key, out var existing)
                        || Rank(kind) > Rank(existing.Kind)
                        || (Rank(kind) == Rank(existing.Kind) && existing.Location == dependency.Location && dependency.Line < existing.Line))
                    {
                        dependencies[key] = dependency;
                    }
                }
            }
        }

        return new TypeStructure(
            merged.Select(group => group[0].Info).OrderBy(info => info.Id, StringComparer.Ordinal).ToList(),
            dependencies.Values
                .OrderBy(dependency => dependency.From, StringComparer.Ordinal)
                .ThenBy(dependency => dependency.To, StringComparer.Ordinal)
                .ToList());
    }

    private static bool IsDesignerFile(string path) =>
        path.Contains(".designer.", StringComparison.OrdinalIgnoreCase);

    private static int Rank(string kind) => kind switch { "Inherits" => 3, "Implements" => 3, _ => 1 };

    private static DeclaredTypeInfo? Resolve(
        string fromProject,
        IReadOnlyList<DeclaredTypeInfo> candidates,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> distance)
    {
        var visible = candidates
            .Select(candidate => (Candidate: candidate, Distance: candidate.Project == fromProject
                ? 0
                : distance.TryGetValue(fromProject, out var map) && map.TryGetValue(candidate.Project, out var hops) ? hops : -1))
            .Where(item => item.Distance >= 0)
            .OrderBy(item => item.Distance)
            .ToList();

        if (visible.Count == 0)
        {
            return null;
        }

        var nearest = visible.Where(item => item.Distance == visible[0].Distance).ToList();

        return nearest.Count == 1 ? nearest[0].Candidate : null;
    }

    /// <summary>Hops from each project to every project it (transitively) references.</summary>
    private static Dictionary<string, IReadOnlyDictionary<string, int>> ProjectDistances(IReadOnlyList<DotNetProjectAssessment> projects)
    {
        var byPath = projects.ToDictionary(project => project.Path, StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, IReadOnlyDictionary<string, int>>();

        foreach (var start in projects)
        {
            var distances = new Dictionary<string, int>();
            var queue = new Queue<(DotNetProjectAssessment Project, int Hops)>();
            queue.Enqueue((start, 0));

            while (queue.Count > 0)
            {
                var (current, hops) = queue.Dequeue();

                foreach (var reference in current.ProjectReferences)
                {
                    if (byPath.TryGetValue(reference.Path, out var next) && next.Name != start.Name && !distances.ContainsKey(next.Name))
                    {
                        distances[next.Name] = hops + 1;
                        queue.Enqueue((next, hops + 1));
                    }
                }
            }

            result[start.Name] = distances;
        }

        return result;
    }
}
