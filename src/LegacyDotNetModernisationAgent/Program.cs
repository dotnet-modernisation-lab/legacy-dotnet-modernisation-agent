using LegacyDotNetModernisationAgent.Commands;

using var cancellationTokenSource = new CancellationTokenSource();

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellationTokenSource.Cancel();
};

var command = args.FirstOrDefault()?.Trim().ToLowerInvariant();

try
{
    switch (command)
    {
        case "assess":
            {
                if (args.Length < 2)
                {
                    Console.Error.WriteLine("Usage: assess <repositoryPath> [artifactsDirectory]");
                    return 2;
                }

                await new AssessCommand().ExecuteAsync(
                    args[1],
                    args.Length > 2 ? args[2] : "Artifacts",
                    cancellationTokenSource.Token);

                return 0;
            }

        default:
            Console.Error.WriteLine("Usage: assess <repositoryPath> [artifactsDirectory]");
            return 2;
    }
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Cancelled.");
    return 130;
}
