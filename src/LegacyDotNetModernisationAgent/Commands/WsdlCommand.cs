using System.Text.Json;
using System.Text.Json.Serialization;
using LegacyDotNetModernisationAgent.ServiceContracts;

namespace LegacyDotNetModernisationAgent.Commands;

/// <summary>
/// Reads a service description (WSDL 1.1, file or address), or compares two. Nothing is called except to fetch the description
/// and the files it imports. Comparing returns 1 when anything is Breaking, so it can gate a migration.
/// </summary>
public sealed class WsdlCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public async Task<int> ReadAsync(string location, string artifactsDirectory, CancellationToken cancellationToken)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var contract = await new WsdlReader().ReadAsync(location, http, cancellationToken);

        Directory.CreateDirectory(artifactsDirectory);

        await File.WriteAllTextAsync(Path.Combine(artifactsDirectory, "service-contract.json"), Normalise(JsonSerializer.Serialize(contract, JsonOptions)), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(artifactsDirectory, "service-contract.md"), Normalise(ServiceContractWriter.Markdown(contract)), cancellationToken);

        Console.WriteLine($"{contract.Facts.Count(fact => fact.Kind == "operation")} operation(s) read; {contract.NotAnalysed.Count} thing(s) not analysed");
        Console.WriteLine($"Wrote service-contract.json and service-contract.md to {artifactsDirectory}");

        return 0;
    }

    public async Task<int> CompareAsync(string oldLocation, string newLocation, string artifactsDirectory, CancellationToken cancellationToken)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var before = await new WsdlReader().ReadAsync(oldLocation, http, cancellationToken);
        var after = await new WsdlReader().ReadAsync(newLocation, http, cancellationToken);
        var diff = ContractComparer.Compare(before, after);

        Directory.CreateDirectory(artifactsDirectory);

        await File.WriteAllTextAsync(Path.Combine(artifactsDirectory, "service-contract-diff.json"), Normalise(JsonSerializer.Serialize(diff, JsonOptions)), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(artifactsDirectory, "service-contract-diff.md"), Normalise(ServiceContractWriter.Markdown(diff)), cancellationToken);

        var counts = diff.Changes.GroupBy(change => change.Class).OrderBy(group => group.Key).Select(group => $"{group.Count()} {group.Key.ToString().ToLowerInvariant()}");

        Console.WriteLine(diff.Changes.Count == 0 ? "No differences found." : $"Differences: {string.Join(", ", counts)}");

        if (diff.NotAnalysed.Count > 0)
        {
            Console.WriteLine($"Incomplete: {diff.NotAnalysed.Count} thing(s) could not be read or compared, so absence of a difference proves nothing for them. See the report.");
        }

        Console.WriteLine($"Wrote service-contract-diff.json and service-contract-diff.md to {artifactsDirectory}");

        return diff.Changes.Any(change => change.Class == ChangeClass.Breaking) ? 1 : 0;
    }

    private static string Normalise(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);
}
