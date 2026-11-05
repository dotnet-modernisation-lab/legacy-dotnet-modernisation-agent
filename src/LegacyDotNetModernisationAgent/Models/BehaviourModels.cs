namespace LegacyDotNetModernisationAgent.Models;

/// <summary>
/// One behaviour that must survive migration. Kind "Dynamic" contracts hold values observed from the running
/// legacy app by the probe; kind "Derived" contracts hold facts derived from the analysis. The contract
/// evaluator (not AI) decides whether a migrated system still satisfies them.
/// </summary>
public sealed record BehaviourContract(
    string Id,
    string Title,
    string Category,
    string Kind,
    IReadOnlyDictionary<string, string> Observed,
    IReadOnlyList<string> RelatedNodes,
    string Notes);

public sealed record BehaviourBaseline(
    string RepositoryName,
    string CommitSha,
    string Probe,
    IReadOnlyList<BehaviourContract> Contracts);

public sealed record BehaviourDrift(
    string ContractId,
    string Key,
    string? Baseline,
    string? Current);
