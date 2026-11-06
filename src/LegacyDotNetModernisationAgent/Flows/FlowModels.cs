namespace LegacyDotNetModernisationAgent.Flows;

/// <summary>One method the flow passes through. Basis says how the tool knew the call went there; nothing is followed on a guess.</summary>
public sealed record FlowStep(string Type, string Method, string Basis, string Location, int Line, string? Boundary);

/// <summary>Where a flow stops being code the tool can follow: data, an outside system, session, configuration or authentication.</summary>
public sealed record FlowTerminal(string Kind, string Target, string Detail, string Location, int Line);

/// <summary>A point where the tool could not (or chose not to) follow, with the reason.</summary>
public sealed record FlowGap(string Point, string Reason, string Location, int Line);

public sealed record Flow(
    string Id,
    string Unit,
    string Project,
    string EntryType,
    string EntryMethod,
    string EntryKind,
    string EntryEvidence,
    IReadOnlyList<string> Inputs,
    IReadOnlyList<string> Access,
    IReadOnlyList<FlowStep> Steps,
    IReadOnlyList<FlowTerminal> Terminals,
    IReadOnlyList<FlowGap> CouldNotFollow,
    int UnfollowedCalls,
    string Coverage);

public sealed record FlowSet(string Repository, string CommitSha, IReadOnlyList<Flow> Flows, IReadOnlyList<string> Notes);
