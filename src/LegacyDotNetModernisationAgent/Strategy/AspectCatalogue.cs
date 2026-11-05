namespace LegacyDotNetModernisationAgent.Strategy;

/// <summary>
/// The migration aspects matrix (docs/migration-aspects.md) as data. Coverage is what the tool can detect from a
/// repository: Detected, Partial, Hint or None. A "None" aspect is always an open question for a person.
/// SignalPrefixes are graph node id prefixes: how many graph nodes match says whether this estate shows the aspect at all.
/// </summary>
public static class AspectCatalogue
{
    public sealed record Aspect(int Id, string Name, string Coverage, string Decider, string[] SignalPrefixes, string[]? SignalKinds = null);

    public const string None = "None";

    public static readonly IReadOnlyList<Aspect> All =
    [
        new(1, "Project structure and dependencies", "Detected", "Tool", ["assembly:", "lib:"]),
        new(2, "UI technology", "Detected", "Human (D-UI-TARGET)", [], ["Page", "MasterPage", "UserControl", "GlobalAsax"]),
        new(3, "Business-object framework", "Detected", "Human (D-CSLA-TARGET, D-DATAPORTAL-TARGET)", ["lib:Csla"], ["CslaBusinessObject"]),
        new(4, "Service boundaries", "Detected", "Human (D-WCF-TARGET)", [], ["WcfContract", "WcfService", "WcfClient", "WcfHost"]),
        new(5, "Data access", "Partial", "Human (D-DATA-CONTRACT)", ["sp:", "integration:Transactions"]),
        new(6, "Database", "Partial", "Human, with DBA (D-DATABASE-TARGET)", ["db:"]),
        new(7, "Data migration and cutover", None, "Human", []),
        new(8, "State and session", "Detected", "Human (D-STATE-MANAGEMENT)", ["state:"]),
        new(9, "Authentication and authorization", "Detected", "Human (D-AUTH-TARGET)", ["auth:"]),
        new(10, "HTTP pipeline", "Detected", "Human (D-HTTP-PIPELINE)", [], ["HttpModule", "HttpHandler", "MessageHandler"]),
        new(11, "Configuration and secrets", "Partial", "Human", ["config:"]),
        new(12, "Security posture", "Hint", "Security reviewer", []),
        new(13, "Logging, monitoring and health", "Partial", "Human (D-OBSERVABILITY)", ["xcut:Logging"]),
        new(14, "Error handling", "Partial", "Human (D-ERROR-HANDLING)", ["xcut:ErrorHandling"]),
        new(15, "Background and scheduled work", "Partial", "Human (D-INTEGRATION-HOSTING)", ["integration:ScheduledWork"]),
        new(16, "Integrations", "Partial", "Human (D-INTEGRATION-*)", ["integration:Messaging", "integration:FileSystem", "integration:Email", "integration:ExternalHttp", "integration:Ftp", "integration:ComInterop", "integration:ChildProcess"]),
        new(17, "Caching and performance", "Partial", "Human (D-CACHING)", ["xcut:Caching"]),
        new(18, "Concurrency and threading", "Partial", "Human", ["fw:System.Web"]),
        new(19, "Localisation and time", "Partial", "Rule only", ["xcut:Localisation", "xcut:TimeHandling"]),
        new(20, "Reporting and documents", "Partial", "Human (D-REPORTING)", ["xcut:Reporting"]),
        new(21, "Third-party and binary dependencies", "Partial", "Human", ["assembly:"]),
        new(22, "Build, CI and packaging", None, "Human (builds are decided by MSBuild outside the tool)", []),
        new(23, "Deployment and hosting", "Partial", "Human", []),
        new(24, "Behaviour baseline", "Partial", "Human approves", []),
        new(25, "Test coverage", None, "n/a (deliberately not assumed)", []),
        new(26, "Compliance and audit", None, "Human", []),
        new(27, "Non-technical factors", None, "Human", [])
    ];
}
