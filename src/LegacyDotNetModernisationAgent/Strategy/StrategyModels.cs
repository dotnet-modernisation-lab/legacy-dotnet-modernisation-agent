namespace LegacyDotNetModernisationAgent.Strategy;

// ---- Input: a bounded, ID-addressable bundle of deterministic evidence -----------------------
// The Strategy Agent reads only this. Everything it may cite (nodes, rules, gates, gap keys, aspect ids) is listed here.

public sealed record InputGate(
    string Id,
    string Title,
    IReadOnlyList<string> AffectedNodes,
    IReadOnlyList<string> RuleIds);

public sealed record InputComponent(
    string NodeId,
    string Name,
    string Kind,
    string Layer,
    IReadOnlyList<string> Characteristics,
    string? Disposition,
    IReadOnlyList<string> RuleIds,
    IReadOnlyList<string> GateIds);

public sealed record InputGap(
    string Key,
    string Category,
    string What,
    string? Location,
    string Impact);

public sealed record InputAspect(
    int Id,
    string Name,
    string Coverage,
    string Decider,
    int SignalCount);

public sealed record InputOmission(string Kind, int Count);

public sealed record LegacyMigrationAnalysisInput(
    string SchemaVersion,
    string Repository,
    string CommitSha,
    string Platform,
    int ProjectCount,
    int NodeCount,
    int ComponentsAssessed,
    IReadOnlyList<InputGate> Gates,
    IReadOnlyList<InputComponent> Components,
    IReadOnlyList<InputOmission> OmittedComponents,
    IReadOnlyList<InputGap> CoverageGaps,
    IReadOnlyList<InputAspect> Aspects,
    IReadOnlyList<string> PotentiallyPortableAreas,
    IReadOnlyList<string> ReviewHints);

// ---- Result: narrative only. There is deliberately no field for a characteristic, disposition or gate outcome. ----

public sealed record GateOption(string Name, string Advantages, string Drawbacks);

public sealed record GateNote(
    string GateId,
    string Recommendation,
    IReadOnlyList<GateOption> Options,
    string Confidence,
    IReadOnlyList<string> Evidence);

public sealed record ProposedPhase(
    int Number,
    string Title,
    string Rationale,
    IReadOnlyList<string> NodeIds,
    IReadOnlyList<string> PrerequisiteGates,
    IReadOnlyList<string> Evidence);

public sealed record StrategyRisk(string Title, string Description, IReadOnlyList<string> Evidence);

public sealed record StrategyRecommendation(string Text, IReadOnlyList<string> Evidence);

/// <summary>A question for a person. Exactly one of AspectId or GapKey says what it is about.</summary>
public sealed record OpenQuestion(int? AspectId, string? GapKey, string Question, string Audience);

public sealed record LegacyMigrationStrategy(
    string InputSha256,
    string Model,
    string Summary,
    IReadOnlyList<string> SummaryEvidence,
    IReadOnlyList<GateNote> GateNotes,
    IReadOnlyList<ProposedPhase> Phases,
    IReadOnlyList<StrategyRisk> Risks,
    IReadOnlyList<StrategyRecommendation> Recommendations,
    IReadOnlyList<OpenQuestion> OpenQuestions);

// ---- Validation: a list of violations and a status per claim, never an exception on the first problem ----

public sealed record Violation(string Code, string Subject, string Message);

public sealed record ClaimStatus(string Claim, string Status, string Detail);

public sealed record StrategyValidationReport(
    bool Accepted,
    IReadOnlyList<Violation> Violations,
    IReadOnlyList<ClaimStatus> Claims);
