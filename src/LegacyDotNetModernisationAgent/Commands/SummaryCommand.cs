using LegacyDotNetModernisationAgent.Analysis;
using LegacyDotNetModernisationAgent.Strategy;

namespace LegacyDotNetModernisationAgent.Commands;

/// <summary>
/// Writes the customer summary: a short, plain-language Word document and Markdown file, with the architecture overview
/// next to them. It uses only the deterministic facts, so it needs no model and is the same on every run.
/// </summary>
public sealed class SummaryCommand
{
    public async Task<int> ExecuteAsync(string repositoryPath, string artifactsDirectory, CancellationToken cancellationToken)
    {
        var assessment = new LegacyDotNetRepositoryAnalyzer().Analyze(repositoryPath);
        var graph = new ArchitecturalGraphBuilder().Build(assessment);
        var analysis = new MigrationCharacteristicPolicy().Evaluate(assessment, graph);
        var input = LegacyMigrationAnalysisInputBuilder.Build(assessment, graph, analysis);
        var unitMap = DeployableUnits.Build(assessment, graph);
        var context = EstateContextBuilder.Build(assessment, graph, unitMap);

        var flows = new Flows.FlowBuilder().Build(Path.GetFullPath(repositoryPath), assessment, graph, unitMap);
        var summary = CustomerSummaryBuilder.Build(assessment, graph, analysis, input, unitMap, context, flows);

        Directory.CreateDirectory(artifactsDirectory);

        await File.WriteAllTextAsync(Path.Combine(artifactsDirectory, "architecture-overview.svg"), ArchitectureDiagram.OverviewSvg(input, context), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(artifactsDirectory, "customer-summary.md"), CustomerSummaryWriter.Markdown(summary, "architecture-overview.svg"), cancellationToken);
        CustomerSummaryWriter.Word(Path.Combine(artifactsDirectory, "customer-summary.docx"), summary);

        Console.WriteLine($"{input.Repository}: customer summary with {summary.Areas.Count} part(s), {summary.Decisions.Count} decision(s), {summary.NotSeen.Count} open question(s)");
        Console.WriteLine($"Wrote customer-summary.docx, customer-summary.md and architecture-overview.svg to {artifactsDirectory}");

        return 0;
    }
}
