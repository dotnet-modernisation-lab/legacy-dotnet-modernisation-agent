using System.Text.Json;
using LegacyDotNetModernisationAgent.Analysis;
using LegacyDotNetModernisationAgent.Strategy;

namespace LegacyDotNetModernisationAgent.Commands;

/// <summary>
/// Builds the evidence bundle, asks a strategy model for the narrative, validates it against the bundle and writes the
/// report. The model is a seam: "deterministic" (default, no model) or "file:&lt;path&gt;" (replay a saved result).
/// Exit code 0 when the strategy is accepted, 3 when validation found violations (the report is still written).
/// </summary>
public sealed class StrategyCommand
{
    private static readonly JsonSerializerOptions JsonOptions = LegacyMigrationAnalysisInputBuilder.JsonOptions;

    public async Task<int> ExecuteAsync(string repositoryPath, string artifactsDirectory, string modelSpec, CancellationToken cancellationToken)
    {
        var assessment = new LegacyDotNetRepositoryAnalyzer().Analyze(repositoryPath);
        var graph = new ArchitecturalGraphBuilder().Build(assessment);
        var analysis = new MigrationCharacteristicPolicy().Evaluate(assessment, graph);

        var input = LegacyMigrationAnalysisInputBuilder.Build(assessment, graph, analysis);
        var hash = LegacyMigrationAnalysisInputBuilder.Hash(input);
        var prompt = StrategyPrompt.Create(input, hash);

        IStrategyModel model = modelSpec switch
        {
            "deterministic" => new DeterministicStrategyModel(),
            _ when modelSpec.StartsWith("file:", StringComparison.OrdinalIgnoreCase) => new ReplayStrategyModel(modelSpec["file:".Length..]),
            _ => throw new InvalidOperationException($"Unknown model '{modelSpec}'. Use 'deterministic' or 'file:<path to a strategy JSON>'.")
        };

        var strategy = await model.ProduceAsync(input, hash, prompt, cancellationToken);
        var report = StrategyValidator.Validate(input, hash, strategy);

        Directory.CreateDirectory(artifactsDirectory);

        await File.WriteAllTextAsync(Path.Combine(artifactsDirectory, "legacy-migration-input.json"), LegacyMigrationAnalysisInputBuilder.Serialize(input), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(artifactsDirectory, "legacy-strategy-prompt.txt"), prompt, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(artifactsDirectory, "legacy-migration-strategy.json"), JsonSerializer.Serialize(strategy, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(artifactsDirectory, "legacy-strategy-validation.json"), JsonSerializer.Serialize(report, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(artifactsDirectory, "migration-strategy.md"), StrategyReportWriter.Markdown(input, hash, strategy, report), cancellationToken);
        StrategyReportWriter.Word(Path.Combine(artifactsDirectory, "migration-strategy.docx"), input, hash, strategy, report);

        Console.WriteLine($"{input.Repository}: strategy by {strategy.Model}, evidence {hash[..12]}");
        Console.WriteLine($"  input: {input.Gates.Count} gate(s), {input.Components.Count} component(s) listed ({input.OmittedComponents.Sum(o => o.Count)} summarised), {input.CoverageGaps.Count} gap(s), {input.Aspects.Count(a => a.Coverage == AspectCatalogue.None)} aspect(s) with no detection");
        Console.WriteLine($"  strategy: {strategy.GateNotes.Count} gate note(s), {strategy.Phases.Count} phase(s), {strategy.Risks.Count} risk(s), {strategy.OpenQuestions.Count} open question(s)");
        Console.WriteLine($"  claims: {report.Claims.Count(c => c.Status == StrategyValidator.Verified)} verified, {report.Claims.Count(c => c.Status == StrategyValidator.Unverified)} unverified, {report.Claims.Count(c => c.Status == StrategyValidator.Rejected)} rejected");

        if (report.Accepted)
        {
            Console.WriteLine("  validation: accepted");
        }
        else
        {
            Console.WriteLine($"  validation: NOT accepted, {report.Violations.Count} violation(s)");

            foreach (var violation in report.Violations)
            {
                Console.WriteLine($"    [{violation.Code}] {violation.Subject}: {violation.Message}");
            }
        }

        Console.WriteLine($"Wrote strategy artifacts to {artifactsDirectory}");

        return report.Accepted ? 0 : 3;
    }
}
