using LegacyDotNetModernisationAgent.Commands;

const string Usage = """
    Usage:
      assess <repositoryPath> [artifactsDirectory]            facts, graph and migration analysis
      behave <repositoryPath> [artifactsDirectory] [--verify] record (or re-check) the behaviour baseline;
                                                              needs the solution built and IIS Express installed
      strategy <repositoryPath> [artifactsDirectory] [--model=deterministic|foundry|file:<strategy.json>]
                                                              evidence bundle, strategy, validation, Markdown and Word report
      diagram <repositoryPath> [artifactsDirectory]           architecture overview (SVG, Mermaid) and a detail view per area
    """;

using var cancellationTokenSource = new CancellationTokenSource();

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellationTokenSource.Cancel();
};

var verify = args.Contains("--verify", StringComparer.OrdinalIgnoreCase);
var positional = args.Where(argument => !argument.StartsWith("--", StringComparison.Ordinal)).ToArray();
var command = positional.FirstOrDefault()?.Trim().ToLowerInvariant();

if (positional.Length < 2 || command is not ("assess" or "behave" or "strategy" or "diagram"))
{
    Console.Error.WriteLine(Usage);

    return 2;
}

var artifacts = positional.Length > 2 ? positional[2] : "Artifacts";

try
{
    if (command == "assess")
    {
        await new AssessCommand().ExecuteAsync(positional[1], artifacts, cancellationTokenSource.Token);

        return 0;
    }

    if (command == "diagram")
    {
        return await new DiagramCommand().ExecuteAsync(positional[1], artifacts, cancellationTokenSource.Token);
    }

    if (command == "strategy")
    {
        var modelOption = args.FirstOrDefault(argument => argument.StartsWith("--model=", StringComparison.OrdinalIgnoreCase));

        return await new StrategyCommand().ExecuteAsync(positional[1], artifacts, modelOption is null ? "deterministic" : modelOption["--model=".Length..], cancellationTokenSource.Token);
    }

    return await new BehaviourCommand().ExecuteAsync(positional[1], artifacts, verify, cancellationTokenSource.Token);
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Cancelled.");

    return 130;
}
catch (Exception exception) when (exception is InvalidOperationException or FileNotFoundException or TimeoutException)
{
    Console.Error.WriteLine($"Error: {exception.Message}");

    return 1;
}
