using System.Text.Json;
using System.Text.Json.Serialization;
using LegacyDotNetModernisationAgent.Analysis;
using LegacyDotNetModernisationAgent.Flows;
using LegacyDotNetModernisationAgent.Strategy;

namespace LegacyDotNetModernisationAgent.Commands;

/// <summary>Writes flows.json and flows.md: what each entry point of the application does, read from the code. Nothing is run or built.</summary>
public sealed class FlowsCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public async Task<int> ExecuteAsync(string repositoryPath, string artifactsDirectory, CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(repositoryPath);
        var assessment = new LegacyDotNetRepositoryAnalyzer().Analyze(root);
        var graph = new ArchitecturalGraphBuilder().Build(assessment);
        var unitMap = DeployableUnits.Build(assessment, graph);
        var flows = new FlowBuilder().Build(root, assessment, graph, unitMap);

        Directory.CreateDirectory(artifactsDirectory);

        await File.WriteAllTextAsync(Path.Combine(artifactsDirectory, "flows.json"), JsonSerializer.Serialize(flows, JsonOptions).Replace("\r\n", "\n", StringComparison.Ordinal), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(artifactsDirectory, "flows.md"), FlowWriter.Markdown(flows, Strategy.CustomerSummaryBuilder.DisplayName), cancellationToken);

        Console.WriteLine($"{assessment.RepositoryName}: {flows.Flows.Count} flow(s) from {flows.Flows.Select(flow => flow.Unit).Distinct().Count()} part(s)");
        Console.WriteLine($"Wrote flows.json and flows.md to {artifactsDirectory}");

        return 0;
    }
}
