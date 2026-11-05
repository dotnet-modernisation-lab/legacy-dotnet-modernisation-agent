using LegacyDotNetModernisationAgent.Analysis.Facts;
using LegacyDotNetModernisationAgent.Models;

namespace LegacyDotNetModernisationAgent.Analysis;

/// <summary>
/// Detects work that happens outside the request/response path and the systems a legacy application talks to:
/// background and scheduled work, queues, files, email, external HTTP/FTP calls, COM and native interop, child
/// processes and transactions. These are the places where hosting assumptions hide. Evidence is "Category: detail".
/// Detection is by name on the neutral fact model (syntax only), so it reports where a signal occurs, not whether it is
/// reached at runtime. Server.MapPath and path literals are recorded as the literal path text only.
/// </summary>
public sealed class IntegrationAnalyzer
{
    public const string Integration = "Integration";

    public const string ScheduledWork = "ScheduledWork";
    public const string Messaging = "Messaging";
    public const string FileSystem = "FileSystem";
    public const string Email = "Email";
    public const string ExternalHttp = "ExternalHttp";
    public const string Ftp = "Ftp";
    public const string ComInterop = "ComInterop";
    public const string ChildProcess = "ChildProcess";
    public const string Transactions = "Transactions";

    private static readonly string[] ServiceBases = ["ServiceBase", "Installer"];

    // (type name, imports that make the short name unambiguous, category, label)
    private static readonly (string Type, string[] RequiresImport, string Category, string Label)[] Creations =
    [
        ("Timer", ["System.Threading", "System.Timers"], ScheduledWork, "Timer"),
        ("Thread", ["System.Threading"], ScheduledWork, "Thread"),
        ("BackgroundWorker", [], ScheduledWork, "BackgroundWorker"),
        ("MessageQueue", [], Messaging, "MSMQ MessageQueue"),
        ("FileSystemWatcher", [], FileSystem, "FileSystemWatcher"),
        ("FileStream", [], FileSystem, "FileStream"),
        ("StreamReader", [], FileSystem, "StreamReader"),
        ("StreamWriter", [], FileSystem, "StreamWriter"),
        ("SmtpClient", [], Email, "SmtpClient"),
        ("MailMessage", [], Email, "MailMessage"),
        ("WebClient", [], ExternalHttp, "WebClient"),
        ("HttpClient", [], ExternalHttp, "HttpClient"),
        ("RestClient", [], ExternalHttp, "RestClient"),
        ("FtpWebRequest", [], Ftp, "FtpWebRequest"),
        ("TransactionScope", [], Transactions, "TransactionScope"),
        ("CommittableTransaction", [], Transactions, "CommittableTransaction")
    ];

    // (qualifier, member, category): qualifier is the simple name written before the call.
    private static readonly (string Qualifier, string Member, string Category)[] Calls =
    [
        ("ThreadPool", "QueueUserWorkItem", ScheduledWork),
        ("Task", "Run", ScheduledWork),
        ("Task", "StartNew", ScheduledWork),
        ("Factory", "StartNew", ScheduledWork),
        ("HostingEnvironment", "QueueBackgroundWorkItem", ScheduledWork),
        ("HostingEnvironment", "RegisterObject", ScheduledWork),
        ("Process", "Start", ChildProcess),
        ("WebRequest", "Create", ExternalHttp),
        ("HttpWebRequest", "Create", ExternalHttp),
        ("Marshal", "GetActiveObject", ComInterop),
        ("Marshal", "ReleaseComObject", ComInterop),
        ("Marshal", "GetTypeFromProgID", ComInterop),
        ("Type", "GetTypeFromProgID", ComInterop),
        ("Activator", "CreateInstance", ComInterop),
        ("CreateObject", "", ComInterop),
        ("Server", "CreateObject", ComInterop),
        ("Server", "MapPath", FileSystem)
    ];

    private static readonly string[] FileClasses = ["File", "Directory", "Path", "FileInfo", "DirectoryInfo"];
    private static readonly string[] FileMembersIgnored = ["Combine", "GetFileName", "GetExtension", "GetFileNameWithoutExtension", "GetDirectoryName", "GetFullPath", "Join"];

    private static readonly (string Prefix, string Category, string Label)[] ImportSignals =
    [
        ("System.Messaging", Messaging, "System.Messaging (MSMQ)"),
        ("System.Net.Mail", Email, "System.Net.Mail"),
        ("System.Transactions", Transactions, "System.Transactions"),
        ("System.ServiceProcess", ScheduledWork, "System.ServiceProcess (Windows service)"),
        ("System.Runtime.InteropServices", ComInterop, "System.Runtime.InteropServices"),
        ("Microsoft.Office.Interop", ComInterop, "Office interop"),
        ("Quartz", ScheduledWork, "Quartz scheduler"),
        ("Hangfire", ScheduledWork, "Hangfire"),
        ("FluentScheduler", ScheduledWork, "FluentScheduler"),
        ("System.Web.Hosting", ScheduledWork, "System.Web.Hosting")
    ];

    public IReadOnlyList<LegacyTechnologyFinding> Analyze(IReadOnlyList<SourceFacts> sources)
    {
        var findings = new List<LegacyTechnologyFinding>();

        foreach (var source in sources)
        {
            void Add(string category, string detail, string? subject, int line) =>
                findings.Add(new LegacyTechnologyFinding(Integration, $"{category}: {detail}", source.Path, line, subject));

            var imports = source.Imports.Select(import => import.Name).ToList();

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

            foreach (var type in source.Types.Where(type => type.Kind == "Class"))
            {
                foreach (var baseType in type.Bases.Where(baseType => ServiceBases.Contains(baseType.SimpleName, StringComparer.OrdinalIgnoreCase)))
                {
                    var service = baseType.SimpleName.Equals("ServiceBase", StringComparison.OrdinalIgnoreCase);

                    Add(ScheduledWork, service ? $"{type.Name} is a Windows service (ServiceBase)" : $"{type.Name} is an installer (Installer)", type.Name, baseType.Line);
                }

                foreach (var attribute in type.Methods.SelectMany(method => method.Attributes).Where(attribute => attribute.Name.Equals("DllImport", StringComparison.OrdinalIgnoreCase)))
                {
                    Add(ComInterop, $"native call via DllImport({attribute.Arguments.FirstOrDefault() ?? ""})", type.Name, attribute.Line);
                }
            }

            foreach (var creation in source.Creations)
            {
                var name = LastSegment(creation.TypeName);

                foreach (var (type, requires, category, label) in Creations)
                {
                    if (name.Equals(type, StringComparison.OrdinalIgnoreCase)
                        && (requires.Length == 0 || requires.Any(required => imports.Contains(required, StringComparer.OrdinalIgnoreCase))))
                    {
                        Add(category, $"creates {label}", creation.Type, creation.Line);
                    }
                }
            }

            foreach (var invocation in source.Invocations.Where(invocation => !invocation.IsIndexer))
            {
                var qualifier = invocation.Qualifier is null ? "" : LastSegment(invocation.Qualifier);

                foreach (var (callQualifier, member, category) in Calls)
                {
                    var hit = member.Length == 0
                        ? invocation.CalleeName.Equals(callQualifier, StringComparison.OrdinalIgnoreCase) && qualifier.Length == 0
                        : invocation.CalleeName.Equals(member, StringComparison.OrdinalIgnoreCase) && qualifier.Equals(callQualifier, StringComparison.OrdinalIgnoreCase);

                    if (hit)
                    {
                        var detail = category == FileSystem && member == "MapPath"
                            ? $"maps a server path{(invocation.FirstString is null ? "" : $" ({invocation.FirstString})")}"
                            : $"calls {(member.Length == 0 ? callQualifier : $"{callQualifier}.{member}")}";

                        Add(category, detail, invocation.Type, invocation.Line);
                    }
                }

                if (FileClasses.Contains(qualifier, StringComparer.OrdinalIgnoreCase) && !FileMembersIgnored.Contains(invocation.CalleeName, StringComparer.OrdinalIgnoreCase)
                    && !qualifier.Equals("Path", StringComparison.OrdinalIgnoreCase))
                {
                    Add(FileSystem, $"calls {qualifier}.{invocation.CalleeName}", invocation.Type, invocation.Line);
                }

                if (invocation.CalleeName.Equals("BeginTransaction", StringComparison.OrdinalIgnoreCase))
                {
                    Add(Transactions, "calls BeginTransaction (database transaction)", invocation.Type, invocation.Line);
                }
            }

            foreach (var literal in source.Literals)
            {
                // Only the scheme is recorded: an FTP URL can carry credentials.
                if (literal.Value.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase) || literal.Value.StartsWith("ftps://", StringComparison.OrdinalIgnoreCase) || literal.Value.StartsWith("sftp://", StringComparison.OrdinalIgnoreCase))
                {
                    Add(Ftp, $"FTP endpoint literal ({literal.Value[..literal.Value.IndexOf(':')]})", literal.Type, literal.Line);
                }

                if (IsNetworkOrAbsolutePath(literal.Value))
                {
                    Add(FileSystem, $"hard-coded path {literal.Value}", literal.Type, literal.Line);
                }
            }
        }

        return findings
            .DistinctBy(finding => (finding.Evidence, finding.Location, finding.Line, finding.Subject))
            .ToList();
    }

    private static string LastSegment(string name) => name.Split('.')[^1];

    /// <summary>A UNC share (\\host\share) or a drive-letter path (C:\folder). URLs and relative paths are not matched.</summary>
    private static bool IsNetworkOrAbsolutePath(string value) =>
        value.Length >= 5
        && (value.StartsWith(@"\\", StringComparison.Ordinal) && value.IndexOf('\\', 2) > 2
            || char.IsLetter(value[0]) && value[1] == ':' && value[2] is '\\' or '/' && value.Length > 3 && !value.Contains("://", StringComparison.Ordinal));
}
