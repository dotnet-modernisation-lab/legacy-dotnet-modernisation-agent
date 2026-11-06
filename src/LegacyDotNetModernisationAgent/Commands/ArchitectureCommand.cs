using LegacyDotNetModernisationAgent.Analysis;
using LegacyDotNetModernisationAgent.Flows;
using LegacyDotNetModernisationAgent.Strategy;

namespace LegacyDotNetModernisationAgent.Commands;

/// <summary>Writes architecture.docx: the overview diagram, the parts, who uses them, and the entry points of each part with what they do. No model is used.</summary>
public sealed class ArchitectureCommand
{
    public async Task<int> ExecuteAsync(string repositoryPath, string artifactsDirectory, CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(repositoryPath);
        var assessment = new LegacyDotNetRepositoryAnalyzer().Analyze(root);
        var graph = new ArchitecturalGraphBuilder().Build(assessment);
        var analysis = new MigrationCharacteristicPolicy().Evaluate(assessment, graph);
        var input = LegacyMigrationAnalysisInputBuilder.Build(assessment, graph, analysis);
        var unitMap = DeployableUnits.Build(assessment, graph);
        var context = EstateContextBuilder.Build(assessment, graph, unitMap);
        var flows = new FlowBuilder().Build(root, assessment, graph, unitMap);
        var summary = CustomerSummaryBuilder.Build(assessment, graph, analysis, input, unitMap, context, flows);
        var svg = ArchitectureDiagram.OverviewSvg(input, context);

        Directory.CreateDirectory(artifactsDirectory);

        await File.WriteAllTextAsync(Path.Combine(artifactsDirectory, "architecture-overview.svg"), svg, cancellationToken);

        var png = SvgRenderer.ToPng(svg);

        await File.WriteAllBytesAsync(Path.Combine(artifactsDirectory, "architecture-overview.png"), png.Png, cancellationToken);
        ArchitectureDocumentWriter.Word(Path.Combine(artifactsDirectory, "architecture.docx"), assessment.RepositoryName, summary, context, flows, png);

        Console.WriteLine($"{assessment.RepositoryName}: architecture document with {summary.Areas.Count} part(s) and {flows.Flows.Count} flow(s)");
        Console.WriteLine($"Wrote architecture.docx, architecture-overview.svg and architecture-overview.png to {artifactsDirectory}");

        return 0;
    }
}
