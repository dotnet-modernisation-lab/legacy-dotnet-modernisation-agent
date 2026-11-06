namespace LegacyDotNetModernisationAgent.Flows;

/// <summary>One method the flow reaches, in the order the tool followed calls; Depth is how many calls from the entry point it is. Basis says how the tool knew the call went there; nothing is followed on a guess.</summary>
public sealed record FlowStep(int Depth, string Type, string Method, string Basis, string Location, int Line, int EndLine, string? Boundary);

/// <summary>Where a flow stops being code the tool can follow, at the step (an index into the flow's steps) where it was found: data, an outside system, session, configuration or authentication.</summary>
public sealed record FlowTerminal(int Step, string Kind, string Target, string Detail, string Location, int Line);

/// <summary>A point where the tool could not (or chose not to) follow, with the reason.</summary>
public sealed record FlowGap(int Step, string Point, string Reason, string Location, int Line);

/// <summary>A declared access rule that mentions the entry point, in the order it appears in its file (order matters for configuration rules).</summary>
public sealed record FlowRule(string Source, string Target, string Operation, string Principals, string Location, int? Line);

public sealed record Flow(
    string Id,
    string Unit,
    string Project,
    string EntryType,
    string EntryMethod,
    string EntryKind,
    string EntryEvidence,
    IReadOnlyList<string> Inputs,
    IReadOnlyList<FlowRule> Access,
    IReadOnlyList<FlowStep> Steps,
    IReadOnlyList<FlowTerminal> Terminals,
    IReadOnlyList<FlowGap> CouldNotFollow,
    int UnfollowedCalls,
    string Coverage);

public sealed record FlowSet(string Repository, string CommitSha, IReadOnlyList<Flow> Flows, IReadOnlyList<string> Notes);
