using LegacyDotNetModernisationAgent.Models;

namespace LegacyDotNetModernisationAgent.Strategy;

/// <summary>A group of code that lives together (a folder) inside one part of the estate, with what it depends on and what depends on it.</summary>
public sealed record Component(
    string Unit,
    string Name,
    IReadOnlyList<string> Layers,
    IReadOnlyList<string> MainItems,
    IReadOnlyList<string> DependsOn,
    IReadOnlyList<string> UsedBy);

public sealed record ComponentFact(string Statement, string Where);

/// <summary>The component a graph node belongs to.</summary>
public sealed record ComponentRef(string Unit, string Name);

public sealed record ComponentMap(
    IReadOnlyList<Component> Components,
    IReadOnlyList<ComponentFact> Loops,
    IReadOnlyList<ComponentFact> DirectReaches,
    IReadOnlyList<ComponentFact> MostUsed,
    IReadOnlyDictionary<string, ComponentRef> OfNode);

/// <summary>
/// Groups the code of each part by folder and shows how the groups depend on each other. A component is the first folder below the
/// project directory (one level deeper when a project keeps everything in a single folder), prefixed with the project name when a
/// part has several projects. Namespaces are not in the graph, so folders are the grouping. Only dependencies in code count:
/// references between assemblies and links made by naming convention are left out, as they are when sizing.
/// A component of another part is always named with its part, so two folders with the same name are never confused.
/// </summary>
public static class ComponentMapBuilder
{
    private const string Root = "(project root)";
    private const int MostUsedShown = 5;
    private const int FactsShown = 10;

    private static readonly HashSet<EdgeType> DependencyTypes = [EdgeType.Uses, EdgeType.Invokes, EdgeType.DelegatesTo, EdgeType.Inherits, EdgeType.ImplementsContract];

    private readonly record struct Key(string Unit, string Name);

    public static ComponentMap Build(LegacyDotNetProjectAssessment assessment, LegacyDependencyGraph graph, UnitMap unitMap)
    {
        var projectDirectories = assessment.Projects.ToDictionary(project => project.Name, project => Normalise(Path.GetDirectoryName(project.Path) ?? ""), StringComparer.Ordinal);
        var codeNodes = graph.Nodes
            .Where(node => node.Project is not null && node.Location is not null && !DeployableUnits.TechnologyKinds.Contains(node.Kind) && unitMap.UnitOfNode.ContainsKey(node.Id))
            .ToList();

        var deeper = DeeperProjects(codeNodes, projectDirectories);
        var projectsOfUnit = codeNodes.GroupBy(node => unitMap.UnitOfNode[node.Id])
            .ToDictionary(group => group.Key, group => group.Select(node => node.Project!).Distinct(StringComparer.Ordinal).Count(), StringComparer.Ordinal);

        var keyOfNode = codeNodes.ToDictionary(node => node.Id, node =>
        {
            var unit = unitMap.UnitOfNode[node.Id];
            var folder = Folder(node, projectDirectories, deeper.Contains(node.Project!));

            return new Key(unit, projectsOfUnit[unit] > 1 ? $"{node.Project}/{folder}" : folder);
        }, StringComparer.Ordinal);

        var nodeById = graph.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var dependsOn = new Dictionary<Key, HashSet<Key>>();
        var usedBy = new Dictionary<Key, HashSet<Key>>();
        var otherParts = new Dictionary<Key, SortedSet<string>>();

        foreach (var key in keyOfNode.Values.Distinct())
        {
            dependsOn[key] = [];
            usedBy[key] = [];
            otherParts[key] = new SortedSet<string>(StringComparer.Ordinal);
        }

        foreach (var edge in graph.Edges.Where(edge => DependencyTypes.Contains(edge.Type) && edge.Basis is not ("Convention" or "AssemblyMetadata")))
        {
            if (!keyOfNode.TryGetValue(edge.From, out var from))
            {
                continue;
            }

            if (keyOfNode.TryGetValue(edge.To, out var to))
            {
                if (from != to)
                {
                    dependsOn[from].Add(to);
                    usedBy[to].Add(from);
                }
            }
            else if (unitMap.UnitOfNode.TryGetValue(edge.To, out var otherUnit) && otherUnit != from.Unit && otherUnit != DeployableUnits.NotPlacedName
                     && nodeById.TryGetValue(edge.To, out var target) && !DeployableUnits.TechnologyKinds.Contains(target.Kind) && target.Project is not null)
            {
                otherParts[from].Add($"the {CustomerSummaryBuilder.DisplayName(otherUnit)}");
            }
        }

        // From the point of view of a component, one in the same part is named plainly and one in another part with its part.
        string Label(Key viewer, Key other) => other.Unit == viewer.Unit ? other.Name : Qualified(other);

        string Order(Key key) => $"{key.Unit}\u0001{key.Name}";

        var components = codeNodes
            .GroupBy(node => keyOfNode[node.Id])
            .OrderBy(group => Order(group.Key), StringComparer.Ordinal)
            .Select(group => new Component(
                group.Key.Unit,
                group.Key.Name,
                Layers(group),
                group.Select(node => node.Name).Distinct(StringComparer.Ordinal).Order(StringComparer.OrdinalIgnoreCase).ToList(),
                dependsOn[group.Key].OrderBy(Order, StringComparer.Ordinal).Select(other => Label(group.Key, other)).Concat(otherParts[group.Key]).ToList(),
                usedBy[group.Key].OrderBy(Order, StringComparer.Ordinal).Select(other => Label(group.Key, other)).ToList()))
            .ToList();

        return new ComponentMap(
            components,
            Loops(dependsOn, Order),
            DirectReaches(graph, nodeById),
            MostUsed(components),
            keyOfNode.ToDictionary(item => item.Key, item => new ComponentRef(item.Value.Unit, item.Value.Name), StringComparer.Ordinal));
    }

    private static string Qualified(Key key) => $"{CustomerSummaryBuilder.DisplayName(key.Unit)}: {key.Name}";

    private static string Normalise(string path) => path.Replace('\\', '/');

    /// <summary>A project that keeps all its code in one folder would be a single component; split it one level deeper so the map shows something.</summary>
    private static HashSet<string> DeeperProjects(List<GraphNode> codeNodes, Dictionary<string, string> projectDirectories)
    {
        var deeper = new HashSet<string>(StringComparer.Ordinal);

        foreach (var project in codeNodes.GroupBy(node => node.Project!))
        {
            var firsts = project.Select(node => Folder(node, projectDirectories, false)).Distinct(StringComparer.Ordinal).ToList();

            if (firsts.Count == 1 && firsts[0] != Root)
            {
                deeper.Add(project.Key);
            }
        }

        return deeper;
    }

    private static string Folder(GraphNode node, Dictionary<string, string> projectDirectories, bool deeper)
    {
        var directory = Normalise(Path.GetDirectoryName(node.Location!) ?? "");
        var projectDirectory = projectDirectories.GetValueOrDefault(node.Project!, "");
        var relative = projectDirectory.Length > 0 && directory.StartsWith(projectDirectory, StringComparison.OrdinalIgnoreCase)
            ? directory[projectDirectory.Length..].TrimStart('/')
            : directory;

        if (relative.Length == 0)
        {
            return Root;
        }

        var segments = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);

        return string.Join('/', segments.Take(deeper ? 2 : 1));
    }

    private static List<string> Layers(IEnumerable<GraphNode> nodes)
    {
        var layers = nodes.Select(node => node.Layer).Where(layer => layer != "Unclassified").Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

        return layers.Count == 0 ? ["not classified"] : layers;
    }

    /// <summary>Strongly connected sets of components: groups that depend on each other directly or through others, in a loop.</summary>
    private static List<ComponentFact> Loops(Dictionary<Key, HashSet<Key>> dependsOn, Func<Key, string> order)
    {
        var nodes = dependsOn.Keys.OrderBy(order, StringComparer.Ordinal).ToList();
        var index = new Dictionary<Key, int>();
        var low = new Dictionary<Key, int>();
        var stack = new Stack<Key>();
        var onStack = new HashSet<Key>();
        var loops = new List<List<Key>>();
        var counter = 0;

        void Visit(Key node)
        {
            index[node] = low[node] = counter++;
            stack.Push(node);
            onStack.Add(node);

            foreach (var next in dependsOn[node].OrderBy(order, StringComparer.Ordinal))
            {
                if (!index.ContainsKey(next))
                {
                    Visit(next);
                    low[node] = Math.Min(low[node], low[next]);
                }
                else if (onStack.Contains(next))
                {
                    low[node] = Math.Min(low[node], index[next]);
                }
            }

            if (low[node] == index[node])
            {
                var group = new List<Key>();
                Key member;

                do
                {
                    member = stack.Pop();
                    onStack.Remove(member);
                    group.Add(member);
                }
                while (member != node);

                if (group.Count > 1)
                {
                    loops.Add(group.OrderBy(order, StringComparer.Ordinal).ToList());
                }
            }
        }

        foreach (var node in nodes.Where(node => !index.ContainsKey(node)))
        {
            Visit(node);
        }

        return loops
            .OrderBy(loop => order(loop[0]), StringComparer.Ordinal)
            .Select(loop =>
            {
                var names = string.Join(", ", loop.Select(Qualified));
                var shown = string.Join(", ", loop.Take(8).Select(Qualified)) + (loop.Count > 8 ? $" and {loop.Count - 8} more" : "");

                return new ComponentFact($"{loop.Count} components depend on each other in a loop, directly or through the others: {shown}.", names);
            })
            .ToList();
    }

    /// <summary>Code in the presentation layer that depends directly on data access or persistence, skipping the layers between. A fact about the code; whether it is wrong depends on the intended design, which the tool cannot see.</summary>
    private static List<ComponentFact> DirectReaches(LegacyDependencyGraph graph, Dictionary<string, GraphNode> nodeById) =>
        graph.Edges
            .Where(edge => DependencyTypes.Contains(edge.Type) && edge.Basis is not ("Convention" or "AssemblyMetadata")
                           && nodeById.TryGetValue(edge.From, out var from) && nodeById.TryGetValue(edge.To, out var to)
                           && from.Layer == "Presentation" && to.Layer is "DataAccess" or "Persistence")
            .Select(edge => new ComponentFact($"{nodeById[edge.From].Name} (presentation code) depends directly on {nodeById[edge.To].Name} ({nodeById[edge.To].Layer.ToLowerInvariant()}) without going through a business or service layer.",
                $"{edge.Location}:{edge.Line}"))
            .DistinctBy(fact => fact.Statement)
            .OrderBy(fact => fact.Statement, StringComparer.Ordinal)
            .Take(FactsShown)
            .ToList();

    private static List<ComponentFact> MostUsed(List<Component> components) =>
        components
            .Where(component => component.UsedBy.Count > 0)
            .OrderByDescending(component => component.UsedBy.Count)
            .ThenBy(component => component.Unit, StringComparer.Ordinal)
            .ThenBy(component => component.Name, StringComparer.Ordinal)
            .Take(MostUsedShown)
            .Select(component => new ComponentFact(
                $"{CustomerSummaryBuilder.DisplayName(component.Unit)}: {component.Name} is used by {component.UsedBy.Count} other component(s): changing it affects {string.Join(", ", component.UsedBy.Take(4))}{(component.UsedBy.Count > 4 ? " and others" : "")}.",
                component.Unit))
            .ToList();
}
