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

        Console.WriteLine($"Wrote {outputPath}");
    }
}
