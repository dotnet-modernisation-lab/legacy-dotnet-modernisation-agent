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
