using LegacyDotNetModernisationAgent.Analysis.Facts;
using LegacyDotNetModernisationAgent.Models;

namespace LegacyDotNetModernisationAgent.Analysis;

/// <summary>
/// Detects cross-cutting concerns that every migration has to re-home: logging and monitoring, error handling, and
/// in-process caching or application state. Detection is by name on the neutral fact model (syntax only): it reports
/// where a signal occurs, not whether it is reached at runtime. Evidence is "Category: detail".
/// </summary>
public sealed class CrossCuttingAnalyzer
{
    public const string CrossCutting = "CrossCutting";

    public const string Logging = "Logging";
    public const string ErrorHandling = "ErrorHandling";
    public const string Caching = "Caching";

    private static readonly (string Prefix, string Category, string Label)[] ImportSignals =
    [
        ("log4net", Logging, "log4net"),
        ("NLog", Logging, "NLog"),
        ("Serilog", Logging, "Serilog"),
        ("Common.Logging", Logging, "Common.Logging"),
        ("Microsoft.Practices.EnterpriseLibrary.Logging", Logging, "Enterprise Library Logging"),
        ("Microsoft.ApplicationInsights", Logging, "Application Insights"),
        ("Elmah", ErrorHandling, "ELMAH error logging"),
        ("System.Runtime.Caching", Caching, "System.Runtime.Caching"),
        ("Microsoft.Practices.EnterpriseLibrary.Caching", Caching, "Enterprise Library Caching"),
        ("StackExchange.Redis", Caching, "Redis client")
    ];

    private static readonly string[] ErrorMethods = ["Application_Error", "Page_Error", "OnError", "Application_OnError"];
    private static readonly string[] CacheMembers = ["Insert", "Add", "Get", "Remove"];

    public IReadOnlyList<LegacyTechnologyFinding> Analyze(IReadOnlyList<SourceFacts> sources)
    {
        var findings = new List<LegacyTechnologyFinding>();

        foreach (var source in sources)
        {
            void Add(string category, string detail, string? subject, int line) =>
                findings.Add(new LegacyTechnologyFinding(CrossCutting, $"{category}: {detail}", source.Path, line, subject));

            foreach (var import in source.Imports)
            {
                foreach (var (prefix, category, label) in ImportSignals)
                {
                    if (import.Name.Equals(prefix, StringComparison.OrdinalIgnoreCase) || import.Name.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase))
                    {
                        Add(category, $"imports {label}", source.Types.FirstOrDefault()?.Name, import.Line);
                    }
                }
            }

            foreach (var type in source.Types)
            {
                foreach (var method in type.Methods.Where(method => ErrorMethods.Contains(method.Name, StringComparer.OrdinalIgnoreCase)))
                {
                    Add(ErrorHandling, $"{type.Name}.{method.Name} handles errors at application or page level", type.Name, method.StartLine);
                }

                foreach (var attribute in type.Attributes.Concat(type.Methods.SelectMany(method => method.Attributes))
                             .Where(attribute => attribute.Name is "HandleError" or "HandleErrorAttribute"))
                {
                    Add(ErrorHandling, "uses the HandleError attribute", type.Name, attribute.Line);
                }
            }

            foreach (var creation in source.Creations)
            {
                var name = creation.TypeName.Split('.')[^1];

                if (name is "EventLog" or "TraceSource" or "EventSource")
                {
                    Add(Logging, $"creates {name}", creation.Type, creation.Line);
                }
                else if (name is "MemoryCache" or "ObjectCache")
                {
                    Add(Caching, $"creates {name} (in-process)", creation.Type, creation.Line);
                }
            }

            foreach (var invocation in source.Invocations)
            {
                var qualifier = invocation.Qualifier?.Split('.')[^1] ?? "";

                if (qualifier is "Trace" or "TraceSource" or "EventLog" && !invocation.IsIndexer
                    && invocation.CalleeName is "Write" or "WriteLine" or "TraceError" or "TraceWarning" or "TraceInformation" or "WriteEntry" or "WriteIf" or "WriteLineIf")
                {
                    Add(Logging, $"calls {qualifier}.{invocation.CalleeName}", invocation.Type, invocation.Line);
                }
                else if (invocation.CalleeName == "GetLogger" && qualifier is "LogManager" or "LoggerFactory")
                {
                    Add(Logging, $"calls {qualifier}.GetLogger", invocation.Type, invocation.Line);
                }

                if (qualifier == "Server" && invocation.CalleeName is "GetLastError" or "ClearError")
                {
                    Add(ErrorHandling, $"calls Server.{invocation.CalleeName}", invocation.Type, invocation.Line);
                }

                if (invocation.CalleeName == "Application" && invocation.IsIndexer
                    || qualifier == "Application" && invocation.CalleeName is "Lock" or "UnLock" or "Add" or "Remove" or "Set")
                {
                    Add(Caching, "uses Application state (shared in-process memory)", invocation.Type, invocation.Line);
                }

                if (invocation.CalleeName == "Cache" && invocation.IsIndexer
                    || qualifier == "Cache" && CacheMembers.Contains(invocation.CalleeName, StringComparer.Ordinal))
                {
                    Add(Caching, "uses the ASP.NET cache (in-process)", invocation.Type, invocation.Line);
                }
            }

            foreach (var access in source.MemberAccesses.Where(access => access.Qualifier == "HttpRuntime" && access.Member == "Cache"))
            {
                Add(Caching, "uses HttpRuntime.Cache (in-process)", access.Type, access.Line);
            }
        }

        return findings
            .DistinctBy(finding => (finding.Evidence, finding.Location, finding.Line, finding.Subject))
            .ToList();
    }
}
