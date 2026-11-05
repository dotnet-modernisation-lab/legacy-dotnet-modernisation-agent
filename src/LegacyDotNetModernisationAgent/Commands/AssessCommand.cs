using System.Text.Json;
using System.Text.Json.Serialization;
using LegacyDotNetModernisationAgent.Analysis;

namespace LegacyDotNetModernisationAgent.Commands;

public sealed class AssessCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task ExecuteAsync(string repositoryPath, string artifactsDirectory, CancellationToken cancellationToken)
    {
        var assessment = new LegacyDotNetRepositoryAnalyzer().Analyze(repositoryPath);

        Directory.CreateDirectory(artifactsDirectory);

        var outputPath = Path.Combine(artifactsDirectory, "legacy-project-assessment.json");

        await File.WriteAllTextAsync(
            outputPath,
            JsonSerializer.Serialize(assessment, JsonOptions),
            cancellationToken);

        Console.WriteLine($"{assessment.RepositoryName} @ {assessment.CommitSha}: {assessment.Projects.Count} projects, {assessment.Platform}");

        foreach (var project in assessment.Projects)
        {
            Console.WriteLine($"  {project.Name,-24} {project.Language,-12} {project.TargetFramework,-7} {project.Kind}");
        }

        foreach (var group in assessment.Technologies.GroupBy(finding => finding.Technology).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            Console.WriteLine($"  [{group.Key}] {group.Count()} findings");
        }

        Console.WriteLine($"  authentication: {assessment.Authentication.Mechanisms.Count} mechanism(s), {assessment.Authentication.AccessRules.Count} declared access rule(s)");

        foreach (var mechanism in assessment.Authentication.Mechanisms)
        {
            Console.WriteLine($"    {mechanism.Kind} ({mechanism.EvidenceCount}): {mechanism.Summary}");
        }

        if (assessment.Authentication.ReviewHints.Count > 0)
        {
            Console.WriteLine($"  security review hints ({assessment.Authentication.ReviewHints.Count}; hints only, not findings about enforcement):");

            foreach (var hint in assessment.Authentication.ReviewHints)
            {
                Console.WriteLine($"    [{hint.Id}] {hint.Observation} ({hint.Location})");
            }
        }

        Console.WriteLine($"  coverage: {assessment.Coverage.Gaps.Count} gap(s) in what was analysed");

        foreach (var gap in assessment.Coverage.Gaps)
        {
            Console.WriteLine($"    [{gap.Category}] {gap.What}{(gap.Location is null ? "" : $" ({gap.Location})")}");
        }

        foreach (var binary in assessment.Assemblies)
        {
            Console.WriteLine($"  binary {binary.Name} {binary.Version} CLR {binary.ClrMetadataVersion} token {binary.PublicKeyToken ?? "none"} ({binary.PublicTypeCount} public types)");
        }

        var graph = new ArchitecturalGraphBuilder().Build(assessment);
        var graphPath = Path.Combine(artifactsDirectory, "legacy-dependency-graph.json");

        await File.WriteAllTextAsync(graphPath, JsonSerializer.Serialize(graph, JsonOptions), cancellationToken);

        Console.WriteLine($"  graph: {graph.Nodes.Count} nodes, {graph.Edges.Count} edges, {graph.Boundaries.Count} layer boundaries, {graph.Paths.Count} page-to-stored-procedure paths");

        var analysis = new MigrationCharacteristicPolicy().Evaluate(assessment, graph);
        var analysisPath = Path.Combine(artifactsDirectory, "migration-analysis.json");

        await File.WriteAllTextAsync(analysisPath, JsonSerializer.Serialize(analysis, JsonOptions), cancellationToken);

        Console.WriteLine($"  policy: {analysis.Components.Count} components assessed, {analysis.DecisionGates.Count} human decision gates, {analysis.PotentiallyPortableAreas.Count} potentially portable");

        Console.WriteLine($"Wrote {outputPath}");
        Console.WriteLine($"Wrote {graphPath}");
        Console.WriteLine($"Wrote {analysisPath}");
    }
}
