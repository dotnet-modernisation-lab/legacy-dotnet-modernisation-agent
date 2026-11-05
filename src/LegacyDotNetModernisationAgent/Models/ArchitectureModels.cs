namespace LegacyDotNetModernisationAgent.Models;

public enum EdgeType
{
    Uses,
    Inherits,
    Invokes,
    Hosts,
    References,
    DelegatesTo,
    ImplementsContract,
    ReadsConfiguration,
    UsesSession,
    UsesAuthentication,
    BackedBy,
    UsesIntegration
}

public enum MigrationCharacteristic
{
    Portable,
    RequiresRecompile,
    RequiresLibraryUpgrade,
    RequiresFrameworkReplacement,
    RequiresArchitecturalReplacement,
    RequiresBehaviourPreservation,
    RequiresHumanDecision
}

/// <summary>Kind drives layer and policy; both are derived from detected facts, never from names alone.</summary>
public sealed record GraphNode(
    string Id,
    string Name,
    string Kind,
    string Layer,
    string? Project,
    string? Location,
    int? Line,
    string? Detail,
    IReadOnlyList<string> Technologies);

public sealed record GraphEdge(
    string From,
    string To,
    EdgeType Type,
    string Basis,
    string Evidence,
    string? Location,
    int? Line);

/// <summary>An edge that crosses from one architectural layer to another.</summary>
public sealed record ArchitecturalBoundary(
    string From,
    string To,
    EdgeType Type,
    string FromLayer,
    string ToLayer);

/// <summary>"If this changes, what is affected?" Reverse reachability over dependency edges.</summary>
public sealed record ImpactEntry(
    string NodeId,
    IReadOnlyList<string> AffectedNodes,
    IReadOnlyList<string> AffectedPages);

public sealed record GraphPathStep(string NodeId, string Kind, EdgeType? EdgeToNext, bool EdgeForward, IReadOnlyList<string> Technologies);

/// <summary>Shortest connection between a page and a stored-procedure contract (edge direction preserved).</summary>
public sealed record RepresentativePath(
    string From,
    string To,
    IReadOnlyList<GraphPathStep> Steps);

public sealed record LegacyDependencyGraph(
    IReadOnlyList<GraphNode> Nodes,
    IReadOnlyList<GraphEdge> Edges,
    IReadOnlyList<ArchitecturalBoundary> Boundaries,
    IReadOnlyList<ImpactEntry> Impact,
    IReadOnlyList<RepresentativePath> Paths);

// ---- Migration analysis (deterministic policy output) ---------------------------------------

public sealed record PolicyRuleHit(
    string RuleId,
    string Reason,
    string Evidence);

public sealed record ComponentMigrationAssessment(
    string NodeId,
    string Kind,
    IReadOnlyList<MigrationCharacteristic> Characteristics,
    string? Disposition,
    IReadOnlyList<PolicyRuleHit> Rules);

/// <summary>A decision only a person may make; the policy lists it, the Strategy Agent cannot resolve it.</summary>
public sealed record DecisionGate(
    string Id,
    string Title,
    IReadOnlyList<string> AffectedNodes,
    IReadOnlyList<string> RuleIds);

public sealed record MigrationAnalysis(
    IReadOnlyList<ComponentMigrationAssessment> Components,
    IReadOnlyList<DecisionGate> DecisionGates,
    IReadOnlyList<string> PotentiallyPortableAreas,
    IReadOnlyList<string> NotPortableBecauseCoupled);
