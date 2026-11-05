using System.Text.RegularExpressions;
using LegacyDotNetModernisationAgent.Analysis.Facts;
using LegacyDotNetModernisationAgent.Models;

namespace LegacyDotNetModernisationAgent.Analysis;

/// <summary>
/// Detects ASP.NET Web Forms / System.Web coupling from markup, code-behind (VB.NET or C#) and Web.config.
/// Every finding is evidence (what, where, which line); interpretation is left to policy and the Strategy Agent.
/// </summary>
public sealed partial class WebFormsAnalyzer
{
    public const string WebForms = "AspNetWebForms";
    public const string SystemWeb = "SystemWeb";
    public const string PageLifecycle = "PageLifecycle";
    public const string Session = "Session";
    public const string ServerControl = "ServerControl";
    public const string MasterPage = "MasterPage";
    public const string GlobalAsax = "GlobalAsax";

    private static readonly string[] WebBaseTypes = ["Page", "MasterPage", "UserControl"];

    [GeneratedRegex(@"<%@\s*(?<kind>Page|Control|Master|Application|ServiceHost)\b(?<attrs>[^%]*)%>", RegexOptions.IgnoreCase)]
    private static partial Regex Directive();

    [GeneratedRegex(@"(?<name>\w+)\s*=\s*""(?<value>[^""]*)""")]
    private static partial Regex Attribute();

    [GeneratedRegex(@"<asp:(?<control>\w+)", RegexOptions.IgnoreCase)]
    private static partial Regex ServerControlTag();

    [GeneratedRegex(@"^(Page|Master|Control)_(Init|Load|PreRender|Unload|Render|PreInit|InitComplete|LoadComplete)$", RegexOptions.IgnoreCase)]
    private static partial Regex LifecycleMethod();

    [GeneratedRegex(@"^(Application|Session)_\w+$", RegexOptions.IgnoreCase)]
    private static partial Regex GlobalAsaxHandler();

    public IReadOnlyList<LegacyTechnologyFinding> Analyze(
        string repositoryRoot,
        IReadOnlyList<DotNetProjectAssessment> projects,
        IReadOnlyList<SourceFacts> sources,
        IReadOnlyList<WebConfigModel> configs)
    {
        var findings = new List<LegacyTechnologyFinding>();

        foreach (var file in projects
                     .SelectMany(project => project.Files.WebFiles)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .Order(StringComparer.Ordinal))
        {
            AnalyzeMarkup(repositoryRoot, file, findings);
        }

        foreach (var source in sources)
        {
            AnalyzeCode(source, findings);
        }

        foreach (var config in configs)
        {
            if (config.SessionStateMode is not null)
            {
                findings.Add(new LegacyTechnologyFinding(Session, $"sessionState mode={config.SessionStateMode}", config.Path));
            }

            if (config.AuthenticationMode is not null && Path.GetFileName(config.Path).Equals("web.config", StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(new LegacyTechnologyFinding(SystemWeb, $"authentication mode={config.AuthenticationMode}", config.Path));
            }
        }

        return findings;
    }

    private static void AnalyzeMarkup(string repositoryRoot, string relativePath, List<LegacyTechnologyFinding> findings)
    {
        var extension = Path.GetExtension(relativePath).ToLowerInvariant();

        if (extension is not (".aspx" or ".ascx" or ".master" or ".asax"))
        {
            return;
        }

        var text = File.ReadAllText(Path.Combine(repositoryRoot, relativePath));
        var directive = Directive().Match(text);

        if (directive.Success)
        {
            var attributes = Attribute().Matches(directive.Groups["attrs"].Value)
                .ToDictionary(match => match.Groups["name"].Value, match => match.Groups["value"].Value, StringComparer.OrdinalIgnoreCase);

            var kind = directive.Groups["kind"].Value;
            var technology = kind.Equals("Application", StringComparison.OrdinalIgnoreCase) ? GlobalAsax
                : kind.Equals("Master", StringComparison.OrdinalIgnoreCase) ? MasterPage
                : WebForms;

            attributes.TryGetValue("Inherits", out var inherits);
            findings.Add(new LegacyTechnologyFinding(technology, $"<%@ {kind} %>", relativePath, LineOf(text, directive.Index), inherits));

            if (attributes.TryGetValue("MasterPageFile", out var master))
            {
                findings.Add(new LegacyTechnologyFinding(MasterPage, $"MasterPageFile={master}", relativePath, LineOf(text, directive.Index), inherits));
            }
        }

        foreach (var control in ServerControlTag().Matches(text)
                     .GroupBy(match => match.Groups["control"].Value, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            findings.Add(new LegacyTechnologyFinding(
                ServerControl,
                $"asp:{control.Key} (x{control.Count()})",
                relativePath,
                LineOf(text, control.First().Index)));
        }
    }

    private static void AnalyzeCode(SourceFacts source, List<LegacyTechnologyFinding> findings)
    {
        foreach (var import in source.Imports.Where(import => import.Name.StartsWith("System.Web", StringComparison.OrdinalIgnoreCase)))
        {
            findings.Add(new LegacyTechnologyFinding(SystemWeb, $"Imports {import.Name}", source.Path, import.Line));
        }

        foreach (var type in source.Types.Where(type => type.Kind == "Class"))
        {
            var webBase = type.Bases.FirstOrDefault(baseType =>
                baseType.Kind == "Inherits"
                && WebBaseTypes.Any(name => Is(baseType.SimpleName, name))
                && (baseType.Compact.StartsWith("System.Web.UI.", StringComparison.OrdinalIgnoreCase)
                    || source.IsCodeBehind("aspx", "ascx", "master")));

            var isWebClass = webBase is not null;

            if (webBase is not null)
            {
                findings.Add(new LegacyTechnologyFinding(WebForms, $"Inherits {webBase.Display}", source.Path, webBase.Line, type.Name));
            }

            foreach (var method in type.Methods)
            {
                if (LifecycleMethod().IsMatch(method.Name))
                {
                    findings.Add(new LegacyTechnologyFinding(PageLifecycle, method.Name, source.Path, method.StartLine, type.Name));
                }
                else if (GlobalAsaxHandler().IsMatch(method.Name) && source.IsCodeBehind("asax"))
                {
                    findings.Add(new LegacyTechnologyFinding(GlobalAsax, method.Name, source.Path, method.StartLine, type.Name));
                }
            }

            if (isWebClass)
            {
                foreach (var identifier in source.Identifiers.Where(id => id.Type == type.Name && Is(id.Name, "IsPostBack")))
                {
                    findings.Add(new LegacyTechnologyFinding(PageLifecycle, "IsPostBack", source.Path, identifier.Line, type.Name));
                }
            }

            foreach (var access in source.Invocations.Where(call => call.Type == type.Name && Is(call.CalleeText, "Session")))
            {
                var key = access.FirstString ?? "?";

                findings.Add(new LegacyTechnologyFinding(
                    Session,
                    $"Session(\"{key}\") {(access.IsAssignmentTarget ? "write" : "read")}",
                    source.Path,
                    access.Line,
                    type.Name));
            }

            foreach (var member in source.MemberAccesses.Where(access => access.Type == type.Name))
            {
                if (Is(member.Qualifier, "HttpContext") && Is(member.Member, "Current"))
                {
                    findings.Add(new LegacyTechnologyFinding(SystemWeb, "HttpContext.Current", source.Path, member.Line, type.Name));
                }
                else if (isWebClass && member.QualifierIsPlainName
                         && (Is(member.Qualifier, "Request") || Is(member.Qualifier, "Response") || Is(member.Qualifier, "Server")))
                {
                    findings.Add(new LegacyTechnologyFinding(SystemWeb, $"{member.Qualifier}.{member.Member}", source.Path, member.Line, type.Name));
                }
            }
        }
    }

    private static bool Is(string? left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static int LineOf(string text, int index)
    {
        var line = 1;

        for (var position = 0; position < index && position < text.Length; position++)
        {
            if (text[position] == '\n')
            {
                line++;
            }
        }

        return line;
    }
}
