using Azure.AI.Projects;
using Azure.Core;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Foundry;

namespace LegacyDotNetModernisationAgent.Strategy;

public enum FoundryAuthenticationMode
{
    /// <summary>Local developer machine: the DefaultAzureCredential chain (for example az login).</summary>
    Development,

    /// <summary>CI: the Azure CLI session created by GitHub OIDC, with no long-lived secret. Exactly this credential, not a chain.</summary>
    AzureCli,

    /// <summary>Production hosting: system- or user-assigned managed identity.</summary>
    ManagedIdentity
}

/// <summary>
/// Foundry connection settings, read from FOUNDRY_PROJECT_ENDPOINT, FOUNDRY_MODEL, FOUNDRY_AUTH_MODE and
/// FOUNDRY_MANAGED_IDENTITY_CLIENT_ID (the same variables as the Spring modernisation prototype). Nothing secret is
/// stored: authentication is by Azure identity, never by key.
/// </summary>
public sealed record FoundryProjectSettings(Uri Endpoint, string Model, TokenCredential Credential)
{
    public static FoundryProjectSettings FromEnvironment()
    {
        var endpoint = Environment.GetEnvironmentVariable("FOUNDRY_PROJECT_ENDPOINT");
        var model = Environment.GetEnvironmentVariable("FOUNDRY_MODEL");
        var authenticationMode = Environment.GetEnvironmentVariable("FOUNDRY_AUTH_MODE");

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new InvalidOperationException("FOUNDRY_PROJECT_ENDPOINT is not configured.");
        }

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri))
        {
            throw new InvalidOperationException("FOUNDRY_PROJECT_ENDPOINT is not a valid absolute URI.");
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new InvalidOperationException("FOUNDRY_MODEL is not configured.");
        }

        if (!Enum.TryParse<FoundryAuthenticationMode>(authenticationMode, ignoreCase: true, out var mode))
        {
            throw new InvalidOperationException("FOUNDRY_AUTH_MODE is not configured or is invalid (Development, AzureCli or ManagedIdentity).");
        }

        var clientId = Environment.GetEnvironmentVariable("FOUNDRY_MANAGED_IDENTITY_CLIENT_ID");

        TokenCredential credential = mode switch
        {
            FoundryAuthenticationMode.Development => new DefaultAzureCredential(),
            FoundryAuthenticationMode.AzureCli => new AzureCliCredential(),
            _ => new ManagedIdentityCredential(string.IsNullOrWhiteSpace(clientId) ? ManagedIdentityId.SystemAssigned : ManagedIdentityId.FromUserAssignedClientId(clientId))
        };

        return new FoundryProjectSettings(endpointUri, model, credential);
    }
}

/// <summary>
/// The Strategy Agent on Azure AI Foundry. It is read-only (no tools are given to it), receives only the evidence bundle in
/// the prompt, and returns the structured strategy. What it returns is then held to the same validator as any other model.
/// The tool, not the model, records which deployment produced the result.
/// </summary>
public sealed class FoundryStrategyModel : IStrategyModel
{
    private const string Instructions =
        """
        You are a read-only legacy .NET modernisation strategy agent.
        Explain deterministic migration evidence supplied by the application and produce a clear migration strategy.
        The evidence is authoritative. You may recommend and explain; you must not decide a gate, change a classification,
        invent facts, or cite any ID that is not in the evidence. Describe missing evidence as unavailable, never as inferred.
        Do not propose shell, git or database commands.
        """;

    private readonly AIAgent agent;
    private readonly string deployment;

    public FoundryStrategyModel(FoundryProjectSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        deployment = settings.Model;

        agent = new AIProjectClient(settings.Endpoint, settings.Credential).AsAIAgent(
            model: settings.Model,
            name: "LegacyMigrationStrategyAgent",
            instructions: Instructions);
    }

    public string Name => $"foundry:{deployment}";

    public async Task<LegacyMigrationStrategy> ProduceAsync(LegacyMigrationAnalysisInput input, string inputSha256, string prompt, CancellationToken cancellationToken)
    {
        var response = await agent.RunAsync<LegacyMigrationStrategy>(prompt, cancellationToken: cancellationToken);

        var strategy = response.Result
            ?? throw new InvalidOperationException("The strategy agent returned no structured result.");

        // The tool knows which deployment it called and which bundle it sent, so it records both itself:
        // a model is unreliable at copying a 64-character hash and cannot be trusted to name itself.
        return strategy with { Model = Name, InputSha256 = inputSha256 };
    }
}
