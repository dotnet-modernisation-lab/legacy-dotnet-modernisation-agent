using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using LegacyDotNetModernisationAgent.Models;

namespace LegacyDotNetModernisationAgent.Analysis;

public sealed record TypeStructure(
    IReadOnlyList<DeclaredTypeInfo> DeclaredTypes,
    IReadOnlyList<TypeDependency> Dependencies);

/// <summary>
/// Declares every class/interface in the estate and records which declared type refers to which.
/// A name is resolved in the referring project first, then in the projects it references (nearest first).
/// A name that stays ambiguous is skipped rather than guessed.
/// </summary>
public sealed class TypeDependencyAnalyzer
{
    public TypeStructure Analyze(
        IReadOnlyList<DotNetProjectAssessment> projects,
        IReadOnlyList<VbSource> sources)
    {
        var declared = new List<(DeclaredTypeInfo Info, VbSource Source, SyntaxNode Node)>();

        foreach (var source in sources)
        {
            var imports = source.Root.Imports
                .SelectMany(statement => statement.ImportsClauses)
                .OfType<SimpleImportsClauseSyntax>()
                .Select(clause => VbSyntax.Compact(clause.Name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.Ordinal)
                .ToList();

            foreach (var node in source.Root.DescendantNodes())
            {
                var (name, kind) = node switch
                {
                    ClassBlockSyntax type => (type.ClassStatement.Identifier.Text, "Class"),
                    InterfaceBlockSyntax type => (type.InterfaceStatement.Identifier.Text, "Interface"),
                    ModuleBlockSyntax type => (type.ModuleStatement.Identifier.Text, "Module"),
                    _ => (null, "")
                };

                if (name is null)
                {
                    continue;
                }

                declared.Add((new DeclaredTypeInfo($"{source.Project}/{name}", name, kind, source.Project, source.Path, VbSyntax.Line(node), imports), source, node));
            }
        }

        var byName = declared
            .GroupBy(item => item.Info.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Info).ToList(), StringComparer.OrdinalIgnoreCase);

        var distance = ProjectDistances(projects);
        var dependencies = new Dictionary<(string From, string To), TypeDependency>();

        foreach (var (info, source, node) in declared)
        {
            var heritage = HeritageNames(node);

            foreach (var identifier in node.DescendantNodes().Where(IsNameNode))
            {
                var name = VbSyntax.SimpleName(identifier);

                if (!byName.TryGetValue(name, out var candidates))
                {
                    continue;
                }

                var target = Resolve(info.Project, candidates, distance);

                if (target is null || target.Id == info.Id)
                {
                    continue;
                }

                var kind = heritage.TryGetValue(identifier, out var heritageKind) ? heritageKind : "Reference";
                var key = (info.Id, target.Id);
                var dependency = new TypeDependency(info.Id, target.Id, kind, source.Path, VbSyntax.Line(identifier));

                // Keep the strongest, then the earliest, evidence for each pair.
                if (!dependencies.TryGetValue(key, out var existing)
                    || Rank(kind) > Rank(existing.Kind)
                    || (Rank(kind) == Rank(existing.Kind) && dependency.Line < existing.Line))
                {
                    dependencies[key] = dependency;
                }
            }
        }

        return new TypeStructure(
            declared.Select(item => item.Info).OrderBy(info => info.Id, StringComparer.Ordinal).ToList(),
            dependencies.Values
                .OrderBy(dependency => dependency.From, StringComparer.Ordinal)
                .ThenBy(dependency => dependency.To, StringComparer.Ordinal)
                .ToList());
    }

    private static bool IsNameNode(SyntaxNode node) => node is IdentifierNameSyntax or GenericNameSyntax;

    private static int Rank(string kind) => kind switch { "Inherits" => 3, "Implements" => 3, _ => 1 };

    private static Dictionary<SyntaxNode, string> HeritageNames(SyntaxNode type)
    {
        var heritage = new Dictionary<SyntaxNode, string>();

        var (inherits, implements) = type switch
        {
            ClassBlockSyntax block => (block.Inherits.SelectMany(statement => statement.Types), block.Implements.SelectMany(statement => statement.Types)),
            InterfaceBlockSyntax block => (block.Inherits.SelectMany(statement => statement.Types), Enumerable.Empty<TypeSyntax>()),
            _ => (Enumerable.Empty<TypeSyntax>(), Enumerable.Empty<TypeSyntax>())
        };

        foreach (var (types, kind) in new[] { (inherits, "Inherits"), (implements, "Implements") })
        {
            foreach (var typeSyntax in types)
            {
                foreach (var name in typeSyntax.DescendantNodesAndSelf().Where(IsNameNode))
                {
                    heritage[name] = kind;
                }
            }
        }

        return heritage;
    }

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
