using LegacyDotNetModernisationAgent.Models;

namespace LegacyDotNetModernisationAgent.Strategy;

/// <summary>
/// Groups components into work areas (one per architectural layer), gives each a relative T-shirt size from a fixed rubric, and
/// orders them so that what others depend on comes first. It is deterministic: the same facts always give the same sizes and order.
/// A size is a relative effort indicator, not an estimate in days; the weights are assumptions to calibrate against a real project.
/// </summary>
public static class WorkAreaSizer
{
    private static readonly Dictionary<MigrationCharacteristic, int> Weights = new()
    {
        [MigrationCharacteristic.RequiresRecompile] = 1,
        [MigrationCharacteristic.RequiresLibraryUpgrade] = 2,
        [MigrationCharacteristic.RequiresFrameworkReplacement] = 3,
        [MigrationCharacteristic.RequiresArchitecturalReplacement] = 5,
        [MigrationCharacteristic.RequiresBehaviourPreservation] = 1,
        [MigrationCharacteristic.RequiresHumanDecision] = 2
    };

    private const int UnassessedWeight = 3;

    private static readonly (int Max, string Size)[] Sizes = [(5, "XS"), (15, "S"), (40, "M"), (100, "L")];

    private static readonly string[] LayerOrder = ["Persistence", "DataAccess", "BusinessObjects", "ServiceBoundary", "Infrastructure", "Hosting", "Presentation", "Unclassified"];

    public static readonly IReadOnlyList<string> Rubric =
    [
        "Each component scores points per distinct migration characteristic: RequiresRecompile 1, RequiresLibraryUpgrade 2, RequiresFrameworkReplacement 3, RequiresArchitecturalReplacement 5, RequiresBehaviourPreservation 1, RequiresHumanDecision 2; +3 if the component is unassessed.",
        "A work area is the sum for the components in one architectural layer. Size: XS up to 5 points, S up to 15, M up to 40, L up to 100, XL above 100.",
        "Sizes are relative effort indicators from these fixed weights, not estimates in days. Calibrate the weights against a completed migration before using them for planning.",
        "Confidence is Low when the area has unassessed components or waits on decision gates, Medium when it only needs behaviour to be preserved, High otherwise.",
        "Order puts what other areas depend on first (an area is placed after the areas it depends on); where layers depend on each other in a cycle, layer order breaks the tie and the area says so."
    ];

    public static IReadOnlyList<InputWorkArea> Build(
        LegacyDependencyGraph graph,
        MigrationAnalysis analysis,
        IReadOnlyDictionary<string, List<string>> gatesOfNode)
    {
        var layerOf = graph.Nodes.ToDictionary(node => node.Id, node => node.Layer, StringComparer.Ordinal);
        var members = analysis.Components
            .Where(component => layerOf.ContainsKey(component.NodeId))
            .GroupBy(component => layerOf[component.NodeId], StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        // An area depends on another when one of its nodes uses a node of the other (convention-only links do not count).
        var dependsOn = members.Keys.ToDictionary(layer => layer, _ => new SortedSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);

        foreach (var edge in graph.Edges.Where(edge => edge.Basis != "Convention"))
        {
            if (layerOf.TryGetValue(edge.From, out var from) && layerOf.TryGetValue(edge.To, out var to)
                && from != to && dependsOn.ContainsKey(from) && dependsOn.ContainsKey(to))
            {
                dependsOn[from].Add(to);
            }
        }

        var ordered = new List<(string Layer, string? Note)>();
        var remaining = members.Keys.OrderBy(Rank).ThenBy(layer => layer, StringComparer.Ordinal).ToList();

        while (remaining.Count > 0)
        {
            var ready = remaining.Where(layer => dependsOn[layer].All(dependency => !remaining.Contains(dependency, StringComparer.Ordinal))).ToList();

            if (ready.Count > 0)
            {
                ordered.Add((ready[0], null));
                remaining.Remove(ready[0]);
            }
            else
            {
                // A dependency cycle between layers: take the lowest layer and say that the order is a judgement.
                var chosen = remaining[0];
                var cycle = dependsOn[chosen].Where(dependency => remaining.Contains(dependency, StringComparer.Ordinal)).ToList();

                ordered.Add((chosen, $"Mutual dependency with {string.Join(", ", cycle)}; placed by layer order, so these areas may need to move together."));
                remaining.Remove(chosen);
            }
        }

        var areas = new List<InputWorkArea>();

        foreach (var (layer, note) in ordered)
        {
            var components = members[layer];
            var points = 0;
            var drivers = new SortedDictionary<string, int>(StringComparer.Ordinal);

            foreach (var component in components)
            {
                foreach (var characteristic in component.Characteristics.Distinct())
                {
                    points += Weights[characteristic];
                    drivers[characteristic.ToString()] = drivers.GetValueOrDefault(characteristic.ToString()) + 1;
                }

                if (component.Disposition?.StartsWith("Unassessed", StringComparison.Ordinal) == true)
                {
                    points += UnassessedWeight;
                    drivers["Unassessed"] = drivers.GetValueOrDefault("Unassessed") + 1;
                }
            }

            var gates = components
                .SelectMany(component => gatesOfNode.GetValueOrDefault(component.NodeId, []))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToList();

            var unassessed = drivers.ContainsKey("Unassessed");
            var confidence = unassessed || gates.Count > 0 ? "Low"
                : drivers.ContainsKey(nameof(MigrationCharacteristic.RequiresBehaviourPreservation)) ? "Medium"
                : "High";

            areas.Add(new InputWorkArea(
                ordered.FindIndex(item => item.Layer == layer) + 1,
                layer,
                components.Count,
                points,
                SizeOf(points),
                confidence,
                drivers.Select(driver => $"{driver.Value} x {driver.Key}").ToList(),
                gates,
                dependsOn[layer].Order(StringComparer.Ordinal).ToList(),
                note));
        }

        return areas;
    }

    public static string SizeOf(int points) =>
        Sizes.FirstOrDefault(size => points <= size.Max).Size ?? "XL";

    private static int Rank(string layer)
    {
        var index = Array.IndexOf(LayerOrder, layer);

        return index < 0 ? LayerOrder.Length : index;
    }
}
