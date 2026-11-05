using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LegacyDotNetModernisationAgent.Models;

namespace LegacyDotNetModernisationAgent.Strategy;

/// <summary>
/// Projects the three deterministic artifacts into the one bundle the Strategy Agent may read. The bundle is bounded
/// (components with no bound characteristic are counted, not listed), sorted, and hashed, so a returned strategy can be
/// tied to exactly the evidence it was given.
/// </summary>
public static class LegacyMigrationAnalysisInputBuilder
{
    public const string SchemaVersion = "1";
    public const int MaxComponents = 150;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true, // models return camelCase; artifacts are written PascalCase
        NewLine = "\n", // the hash must not depend on the operating system
        Converters = { new JsonStringEnumConverter() }
    };

    public static LegacyMigrationAnalysisInput Build(
        LegacyDotNetProjectAssessment assessment,
        LegacyDependencyGraph graph,
        MigrationAnalysis analysis)
    {
        var nodes = graph.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);

        var gates = analysis.DecisionGates
            .OrderBy(gate => gate.Id, StringComparer.Ordinal)
            .Select(gate => new InputGate(gate.Id, gate.Title, gate.AffectedNodes.Order(StringComparer.Ordinal).ToList(), gate.RuleIds.Order(StringComparer.Ordinal).ToList()))
            .ToList();

        var gatesOfNode = gates
            .SelectMany(gate => gate.AffectedNodes.Select(node => (Node: node, Gate: gate.Id)))
            .GroupBy(pair => pair.Node, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(pair => pair.Gate).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList(), StringComparer.Ordinal);

        var attention = analysis.Components
            .Where(component => component.Characteristics.Any(characteristic => characteristic != MigrationCharacteristic.RequiresRecompile)
                || component.Disposition is not null && component.Disposition.StartsWith("Unassessed", StringComparison.Ordinal)
                || gatesOfNode.ContainsKey(component.NodeId))
            .OrderByDescending(component => gatesOfNode.ContainsKey(component.NodeId))
            .ThenBy(component => component.NodeId, StringComparer.Ordinal)
            .ToList();

        var listed = attention.Take(MaxComponents).ToList();

        var omitted = attention.Skip(MaxComponents)
            .Concat(analysis.Components.Except(attention))
            .GroupBy(component => component.Kind)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new InputOmission(group.Key, group.Count()))
            .ToList();

        var components = listed
            .OrderBy(component => component.NodeId, StringComparer.Ordinal)
            .Select(component =>
            {
                var node = nodes.GetValueOrDefault(component.NodeId);

                return new InputComponent(
                    component.NodeId,
                    node?.Name ?? component.NodeId,
                    component.Kind,
                    node?.Layer ?? "Unknown",
                    component.Characteristics.Select(characteristic => characteristic.ToString()).Order(StringComparer.Ordinal).ToList(),
                    component.Disposition,
                    component.Rules.Select(rule => rule.RuleId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList(),
                    gatesOfNode.GetValueOrDefault(component.NodeId, []));
            })
            .ToList();

        var gaps = assessment.Coverage.Gaps
            .Select(gap => new InputGap(GapKey(gap), gap.Category, gap.What, gap.Location, gap.Impact))
            .OrderBy(gap => gap.Key, StringComparer.Ordinal)
            .ToList();

        var aspects = AspectCatalogue.All
            .Select(aspect => new InputAspect(aspect.Id, aspect.Name, aspect.Coverage, aspect.Decider, SignalCount(aspect, graph.Nodes)))
            .ToList();

        return new LegacyMigrationAnalysisInput(
            SchemaVersion,
            assessment.RepositoryName,
            assessment.CommitSha,
            assessment.Platform.ToString(),
            assessment.Projects.Count,
            graph.Nodes.Count,
            analysis.Components.Count,
            gates,
            components,
            omitted,
            gaps,
            aspects,
            analysis.PotentiallyPortableAreas.Order(StringComparer.Ordinal).ToList(),
            assessment.Authentication.ReviewHints.Select(hint => $"{hint.Id} ({hint.Location})").Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList());
    }

    /// <summary>A stable key for a coverage gap: category, location and a short hash of the description.</summary>
    public static string GapKey(CoverageGap gap)
    {
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(gap.What)))[..8].ToLowerInvariant();

        return $"gap:{gap.Category}:{gap.Location ?? "-"}:{digest}";
    }

    public static string Serialize(LegacyMigrationAnalysisInput input) => JsonSerializer.Serialize(input, JsonOptions);

    public static string Hash(LegacyMigrationAnalysisInput input) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Serialize(input)))).ToLowerInvariant();

    private static int SignalCount(AspectCatalogue.Aspect aspect, IReadOnlyList<GraphNode> nodes) =>
        nodes.Count(node => aspect.SignalPrefixes.Any(prefix => node.Id.StartsWith(prefix, StringComparison.Ordinal))
            || aspect.SignalKinds is not null && aspect.SignalKinds.Contains(node.Kind, StringComparer.Ordinal));
}
