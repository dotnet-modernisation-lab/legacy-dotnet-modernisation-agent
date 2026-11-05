using System.Text.Json;
using System.Text.Json.Serialization;
using LegacyDotNetModernisationAgent.Analysis;
using LegacyDotNetModernisationAgent.Behaviour;
using LegacyDotNetModernisationAgent.Models;

namespace LegacyDotNetModernisationAgent.Commands;

/// <summary>
/// Records (or, with --verify, re-checks) the behaviour baseline by running the built legacy app under IIS Express.
/// The solution must already be built; this command never builds or edits the repository.
/// </summary>
public sealed class BehaviourCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<int> ExecuteAsync(string repositoryPath, string artifactsDirectory, bool verify, CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(repositoryPath);
        var assessment = new LegacyDotNetRepositoryAnalyzer().Analyze(root);
        var graph = new ArchitecturalGraphBuilder().Build(assessment);
        var targets = BehaviourProbe.ResolveTargets(root, assessment, graph);

        Console.WriteLine($"Probing {assessment.RepositoryName} @ {assessment.CommitSha}");

        var observed = await new BehaviourProbe().RunAsync(targets, message => Console.WriteLine($"  {message}"), cancellationToken);
        var current = BehaviourBaselineBuilder.Build(assessment, graph, observed);
        var baselinePath = Path.Combine(artifactsDirectory, "behaviour-baseline.json");

        if (!verify)
        {
            Directory.CreateDirectory(artifactsDirectory);
            await File.WriteAllTextAsync(baselinePath, JsonSerializer.Serialize(current, JsonOptions), cancellationToken);

            Console.WriteLine($"Recorded {current.Contracts.Count} behaviour contracts ({observed.Count} observations).");
            Console.WriteLine($"Wrote {baselinePath}");

            return 0;
        }

        var baseline = JsonSerializer.Deserialize<BehaviourBaseline>(await File.ReadAllTextAsync(baselinePath, cancellationToken), JsonOptions)
            ?? throw new InvalidOperationException($"{baselinePath} could not be read.");

        var drift = BehaviourBaselineBuilder.Compare(baseline, current);

        if (drift.Count == 0)
        {
            Console.WriteLine($"VERIFIED: all {baseline.Contracts.Count(contract => contract.Kind == "Dynamic")} dynamic contracts match the baseline.");

            return 0;
        }

        Console.WriteLine($"DRIFT: {drift.Count} difference(s) from the baseline");

        foreach (var item in drift)
        {
            Console.WriteLine($"  {item.ContractId} {item.Key}: baseline=\"{item.Baseline}\" current=\"{item.Current}\"");
        }

        return 1;
    }
}
