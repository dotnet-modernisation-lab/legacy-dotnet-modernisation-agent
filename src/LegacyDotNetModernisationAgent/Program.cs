using LegacyDotNetModernisationAgent.Commands;

const string Usage = """
    Usage:
      assess <repositoryPath> [artifactsDirectory]            facts, graph and migration analysis
      strategy <repositoryPath> [artifactsDirectory] [--model=deterministic|foundry|file:<strategy.json>]
                                                              evidence bundle, strategy, validation, Markdown and Word report
      diagram <repositoryPath> [artifactsDirectory]           architecture overview (SVG, Mermaid) and a detail view per area
      flows <repositoryPath> [artifactsDirectory]             what each entry point does, read from the code (flows.json, flows.md)
      architecture <repositoryPath> [artifactsDirectory]      architecture Word document: diagram, parts, users and the flows of each part
      wsdl <fileOrAddress> [artifactsDirectory]               read a SOAP service description into comparable facts
      wsdl-check <description> <repositoryPath> [dir]          check a service description against the contracts found in the code
      wsdl-compare <old> <new> [artifactsDirectory]           compare two service descriptions; returns 1 if anything breaks callers
      offering <markdownPath> [output.docx]                  Word version of the sales offering Markdown
      summary <repositoryPath> [artifactsDirectory]           customer summary (Word, Markdown) in plain language, with the overview diagram
    """;

using var cancellationTokenSource = new CancellationTokenSource();

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellationTokenSource.Cancel();
};

var positional = args.Where(argument => !argument.StartsWith("--", StringComparison.Ordinal)).ToArray();
var command = positional.FirstOrDefault()?.Trim().ToLowerInvariant();

if (positional.Length < (command is "wsdl-compare" or "wsdl-check" ? 3 : 2) || command is not ("assess" or "strategy" or "diagram" or "summary" or "offering" or "flows" or "wsdl" or "wsdl-compare" or "wsdl-check" or "architecture"))
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

    if (command == "architecture")
    {
        return await new ArchitectureCommand().ExecuteAsync(positional[1], artifacts, cancellationTokenSource.Token);
    }

    if (command == "wsdl")
    {
        return await new WsdlCommand().ReadAsync(positional[1], artifacts, cancellationTokenSource.Token);
    }

    if (command == "wsdl-check")
    {
        return await new WsdlCommand().CheckAsync(positional[1], positional[2], positional.Length > 3 ? positional[3] : "Artifacts", cancellationTokenSource.Token);
    }

    if (command == "wsdl-compare")
    {
        return await new WsdlCommand().CompareAsync(positional[1], positional[2], positional.Length > 3 ? positional[3] : "Artifacts", cancellationTokenSource.Token);
    }

    if (command == "flows")
    {
        return await new FlowsCommand().ExecuteAsync(positional[1], artifacts, cancellationTokenSource.Token);
    }

    if (command == "offering")
    {
        var output = positional.Length > 2 ? positional[2] : Path.ChangeExtension(positional[1], ".docx");
        LegacyDotNetModernisationAgent.Strategy.SalesOfferingWriter.Word(positional[1], output);
        Console.WriteLine($"Wrote {output}");

        return 0;
    }

    if (command == "summary")
    {
        return await new SummaryCommand().ExecuteAsync(positional[1], artifacts, cancellationTokenSource.Token);
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

    return 2;
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
