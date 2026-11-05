namespace LegacyDotNetModernisationAgent.Models;

// ---- Binary (assembly) evidence -------------------------------------------------------------

public sealed record AssemblyReferenceEvidence(string Name, string Version);

public sealed record AssemblyMemberSignature(
    string Accessibility,
    string Name,
    string Signature);

public sealed record AssemblyTypeDescription(
    string FullName,
    string? BaseType,
    IReadOnlyList<AssemblyMemberSignature> Methods);

/// <summary>Facts read directly from a supplied binary's metadata (never loaded or executed).</summary>
public sealed record LegacyAssemblyEvidence(
    string Path,
    string Name,
    string Version,
    string ClrMetadataVersion,
    string? PublicKeyToken,
    string Sha256,
    IReadOnlyList<AssemblyReferenceEvidence> ReferencedAssemblies,
    int PublicTypeCount,
    IReadOnlyList<string> PublicTypes,
    IReadOnlyList<AssemblyTypeDescription> TypesUsedBySource,
    IReadOnlyList<string> ReferencedByProjects);

// ---- WCF ------------------------------------------------------------------------------------

public sealed record WcfOperationInfo(string Name, IReadOnlyList<string> FaultContracts);

public sealed record WcfContractInfo(
    string Name,
    IReadOnlyList<WcfOperationInfo> Operations,
    string Location,
    int Line);

public sealed record WcfServiceInfo(
    string Name,
    IReadOnlyList<string> ImplementedContracts,
    string Location,
    int Line);

public sealed record WcfSvcFileInfo(string Path, string? Service);

public sealed record WcfEndpointInfo(
    string Source,
    string Role,
    string? ServiceName,
    string Address,
    string Binding,
    string Contract);

/// <summary>A WCF client created in code (ChannelFactory) rather than from configuration.</summary>
public sealed record WcfClientUsage(
    string Contract,
    string? Binding,
    string? AddressSettingKey,
    string Class,
    string Location,
    int Line);

public sealed record WcfAssessment(
    IReadOnlyList<WcfContractInfo> Contracts,
    IReadOnlyList<WcfServiceInfo> Services,
    IReadOnlyList<WcfSvcFileInfo> SvcFiles,
    IReadOnlyList<WcfEndpointInfo> Endpoints,
    IReadOnlyList<WcfClientUsage> Clients,
    IReadOnlyList<string> DataContracts);

// ---- Data access ----------------------------------------------------------------------------

public sealed record StoredProcedureParameter(string Name, string Location, int Line);

public sealed record StoredProcedureImplementation(
    string DalClass,
    string Method,
    string ReturnType,
    string Basis,
    string Location,
    int Line);

public sealed record DalCallSite(
    string CallerClass,
    string CallerMethod,
    string DalMethod,
    string Location,
    int Line);

public sealed record StoredProcedureContractInfo(
    string Name,
    string DefinedIn,
    string Location,
    int Line,
    IReadOnlyList<StoredProcedureParameter> Parameters,
    IReadOnlyList<StoredProcedureImplementation> Implementations,
    IReadOnlyList<DalCallSite> CallSites,
    int DirectReferenceCount);

public sealed record DalClassInfo(string Name, string Location, int Line);

/// <summary>A file/JSON stand-in for a physical database. Test/fixture infrastructure, not an architecture.</summary>
public sealed record FixtureBackingInfo(
    string Mechanism,
    string? ConfigurationKey,
    IReadOnlyList<string> FixtureFiles,
    string Location);

public sealed record DataAccessAssessment(
    IReadOnlyList<DalClassInfo> DalClasses,
    IReadOnlyList<StoredProcedureContractInfo> StoredProcedures,
    IReadOnlyList<FixtureBackingInfo> Fixtures);

// ---- Type-level structure (input to the architectural graph) -------------------------------

public sealed record DeclaredTypeInfo(
    string Id,
    string Name,
    string Kind,
    string Project,
    string Location,
    int Line,
    IReadOnlyList<string> Imports);

/// <summary>
/// "From uses To" at type level, resolved through project references.
/// Kind is Reference, Inherits or Implements; names that match several visible types are skipped, never guessed.
/// </summary>
public sealed record TypeDependency(
    string From,
    string To,
    string Kind,
    string Location,
    int Line);

// ---- Coverage: what the tool did and did not understand ------------------------------------

/// <summary>A thing the analysis could not assess. Gaps are findings: silence must never read as "clean".</summary>
public sealed record CoverageGap(
    string Category,
    string What,
    string? Location,
    string Impact);

public sealed record AnalysisCoverage(
    IReadOnlyDictionary<string, int> SourceFilesByLanguage,
    int ProjectsAnalysed,
    IReadOnlyList<CoverageGap> Gaps);

// ---- Authentication and authorization ------------------------------------------------------

/// <summary>An authentication or identity mechanism found in code, configuration or packages.</summary>
public sealed record AuthMechanism(
    string Kind,
    string Summary,
    int EvidenceCount,
    IReadOnlyList<string> Locations);

/// <summary>
/// A declared access rule (CSLA AuthorizationRules, [Authorize]-style attribute, web.config allow/deny).
/// These are static facts: they say what is declared, not what the running app enforces.
/// </summary>
public sealed record AccessRule(
    string Source,
    string Target,
    string Operation,
    string Principals,
    string Location,
    int? Line);

/// <summary>
/// An observation about a well-known risky setting, for a security reviewer. It is a hint, not a finding about
/// enforcement or exploitability, and it never feeds migration policy.
/// </summary>
public sealed record ReviewHint(
    string Id,
    string Observation,
    string Location);

public sealed record AuthenticationAssessment(
    IReadOnlyList<AuthMechanism> Mechanisms,
    IReadOnlyList<AccessRule> AccessRules,
    IReadOnlyList<ReviewHint> ReviewHints);

// ---- Database facts (T-SQL files and inline SQL) --------------------------------------------

/// <summary>
/// An object defined in a .sql file (procedure, function, view, trigger, table) or a script of loose statements.
/// Reads/Writes are table names; Calls are procedures executed; Features are SQL Server specifics a migration must know about.
/// </summary>
public sealed record SqlObjectInfo(
    string Kind,
    string Name,
    string Location,
    int Line,
    IReadOnlyList<string> Parameters,
    IReadOnlyList<string> Reads,
    IReadOnlyList<string> Writes,
    IReadOnlyList<string> Calls,
    IReadOnlyList<string> Features);

/// <summary>A literal in code that parses as T-SQL. Only the verb and the objects it touches are recorded, never the text.</summary>
public sealed record InlineSqlInfo(
    string Statement,
    string? Type,
    string Location,
    int Line,
    IReadOnlyList<string> Reads,
    IReadOnlyList<string> Writes,
    IReadOnlyList<string> Calls,
    IReadOnlyList<string> Features);

public sealed record DatabaseAssessment(
    IReadOnlyList<string> SqlFiles,
    IReadOnlyList<SqlObjectInfo> Objects,
    IReadOnlyList<InlineSqlInfo> InlineStatements);
