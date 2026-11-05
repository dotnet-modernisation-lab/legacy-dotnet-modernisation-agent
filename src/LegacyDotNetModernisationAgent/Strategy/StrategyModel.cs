using System.Text.Json;

namespace LegacyDotNetModernisationAgent.Strategy;

/// <summary>
/// The seam between the deterministic tool and whatever produces the narrative. A real model, a replayed result from a
/// file, and the built-in deterministic baseline all implement it; the validator treats them identically.
/// </summary>
public interface IStrategyModel
{
    string Name { get; }

    Task<LegacyMigrationStrategy> ProduceAsync(LegacyMigrationAnalysisInput input, string inputSha256, string prompt, CancellationToken cancellationToken);
}

/// <summary>Reads a previously produced strategy from a JSON file. Used to prove the validator without a model.</summary>
public sealed class ReplayStrategyModel(string path) : IStrategyModel
{
    public string Name => $"replay:{Path.GetFileName(path)}";

    public async Task<LegacyMigrationStrategy> ProduceAsync(LegacyMigrationAnalysisInput input, string inputSha256, string prompt, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);

        return await JsonSerializer.DeserializeAsync<LegacyMigrationStrategy>(stream, LegacyMigrationAnalysisInputBuilder.JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException($"{path} does not contain a strategy.");
    }
}

/// <summary>
/// A strategy built from the evidence alone: every gate, every open question, and a phase order that follows the layers
/// (data first, user interface last) because dependencies point downwards. It invents nothing and offers no options; it
/// is the floor a model-written strategy must improve on, and a complete result when no model is available.
/// </summary>
public sealed class DeterministicStrategyModel : IStrategyModel
{
    public const string ModelName = "deterministic-baseline";

    public string Name => ModelName;

    private static readonly string[] LayerOrder = ["Persistence", "DataAccess", "BusinessObjects", "ServiceBoundary", "Infrastructure", "Hosting", "Presentation", "Unclassified"];

    public Task<LegacyMigrationStrategy> ProduceAsync(LegacyMigrationAnalysisInput input, string inputSha256, string prompt, CancellationToken cancellationToken)
    {
        var gateNotes = input.Gates
            .Select(gate => new GateNote(
                gate.Id,
                $"A person must decide: {gate.Title}. It affects {gate.AffectedNodes.Count} component(s). No option is recommended without further evidence.",
                [],
                "Low",
                [gate.Id, .. gate.RuleIds, .. gate.AffectedNodes.Take(5)]))
            .ToList();

        var phases = new List<ProposedPhase>();
        var number = 1;

        foreach (var layer in input.Components.GroupBy(component => component.Layer).OrderBy(group => LayerRank(group.Key)).ThenBy(group => group.Key, StringComparer.Ordinal))
        {
            var members = layer.OrderBy(component => component.NodeId, StringComparer.Ordinal).ToList();
            var prerequisites = members.SelectMany(component => component.GateIds).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

            if (members.Any(component => component.Disposition?.StartsWith("Unassessed", StringComparison.Ordinal) == true) && !prerequisites.Contains("D-UNASSESSED-TECH"))
            {
                prerequisites.Add("D-UNASSESSED-TECH");
            }

            phases.Add(new ProposedPhase(
                number++,
                $"{layer.Key} layer",
                $"{members.Count} component(s) in the {layer.Key} layer. Dependencies point from the user interface towards data, so lower layers are listed first; each phase waits on the decision gates that affect its components.",
                members.Select(component => component.NodeId).ToList(),
                prerequisites.Where(gate => input.Gates.Any(g => g.Id == gate)).ToList(),
                [.. prerequisites, .. members.Take(5).Select(component => component.NodeId)]));
        }

        var risks = input.Components
            .Where(component => component.Characteristics.Contains("RequiresArchitecturalReplacement"))
            .GroupBy(component => component.Kind)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new StrategyRisk(
                $"Architectural replacement: {group.Key}",
                $"{group.Count()} {group.Key} component(s) are classified as needing architectural replacement, not a mechanical upgrade.",
                group.Select(component => component.NodeId).Take(5).ToList()))
            .ToList();

        var questions = new List<OpenQuestion>();

        foreach (var aspect in input.Aspects.Where(aspect => aspect.Coverage == AspectCatalogue.None))
        {
            questions.Add(new OpenQuestion(aspect.Id, null, $"The tool cannot see '{aspect.Name}' from the repository. What is the position, and who owns it?", aspect.Decider));
        }

        foreach (var aspect in input.Aspects.Where(aspect => aspect.Coverage != AspectCatalogue.None && aspect.SignalCount == 0 && aspect.Id is not (1 or 12 or 21 or 23 or 24)))
        {
            questions.Add(new OpenQuestion(aspect.Id, null, $"No '{aspect.Name}' signals were found in the repository. Confirm none exists outside it (for example scheduled tasks or server configuration).", aspect.Decider));
        }

        foreach (var gap in input.CoverageGaps)
        {
            questions.Add(new OpenQuestion(null, gap.Key, $"Coverage gap ({gap.Category}): {gap.What}. {gap.Impact}", "Human"));
        }

        return Task.FromResult(new LegacyMigrationStrategy(
            inputSha256,
            ModelName,
            $"{input.Repository}: {input.ProjectCount} project(s), {input.ComponentsAssessed} component(s) assessed, {input.Gates.Count} decision gate(s) and {input.CoverageGaps.Count} coverage gap(s). " +
            "This baseline lists what a person must decide and what the tool could not see; it recommends no options.",
            input.Gates.Select(gate => gate.Id).ToList(),
            gateNotes,
            phases,
            risks,
            [],
            questions));
    }

    private static int LayerRank(string layer)
    {
        var index = Array.IndexOf(LayerOrder, layer);

        return index < 0 ? LayerOrder.Length : index;
    }
}
