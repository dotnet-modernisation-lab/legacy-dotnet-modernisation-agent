using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using LegacyDotNetModernisationAgent.Models;

namespace LegacyDotNetModernisationAgent.Analysis;

/// <summary>
/// Detects ASP.NET Web Forms / System.Web coupling from markup, VB code-behind and Web.config.
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
        IReadOnlyList<VbSource> sources,
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

    private static void AnalyzeCode(VbSource source, List<LegacyTechnologyFinding> findings)
    {
        foreach (var import in source.Root.Imports
                     .SelectMany(statement => statement.ImportsClauses)
                     .OfType<SimpleImportsClauseSyntax>()
                     .Where(clause => VbSyntax.Compact(clause.Name).StartsWith("System.Web", StringComparison.OrdinalIgnoreCase)))
        {
            findings.Add(new LegacyTechnologyFinding(SystemWeb, $"Imports {VbSyntax.Compact(import.Name)}", source.Path, VbSyntax.Line(import)));
        }

        foreach (var type in source.Root.DescendantNodes().OfType<ClassBlockSyntax>())
        {
            var webBase = type.Inherits
                .SelectMany(inherits => inherits.Types)
                .FirstOrDefault(baseType =>
                    WebBaseTypes.Any(name => VbSyntax.Is(VbSyntax.SimpleName(baseType), name))
                    && (VbSyntax.Compact(baseType).StartsWith("System.Web.UI.", StringComparison.OrdinalIgnoreCase)
                        || source.Path.EndsWith(".aspx.vb", StringComparison.OrdinalIgnoreCase)
                        || source.Path.EndsWith(".ascx.vb", StringComparison.OrdinalIgnoreCase)
                        || source.Path.EndsWith(".master.vb", StringComparison.OrdinalIgnoreCase)));

            var isWebClass = webBase is not null;
            var className = type.ClassStatement.Identifier.Text;

            if (webBase is not null)
            {
                findings.Add(new LegacyTechnologyFinding(WebForms, $"Inherits {VbSyntax.Text(webBase)}", source.Path, VbSyntax.Line(webBase), className));
            }

            foreach (var node in type.DescendantNodes())
            {
                switch (node)
                {
                    case MethodBlockSyntax method:
                        {
                            var name = method.SubOrFunctionStatement.Identifier.Text;

                            if (LifecycleMethod().IsMatch(name))
                            {
                                findings.Add(new LegacyTechnologyFinding(PageLifecycle, name, source.Path, VbSyntax.Line(method), className));
                            }
                            else if (GlobalAsaxHandler().IsMatch(name) && source.Path.EndsWith(".asax.vb", StringComparison.OrdinalIgnoreCase))
                            {
                                findings.Add(new LegacyTechnologyFinding(GlobalAsax, name, source.Path, VbSyntax.Line(method), className));
                            }

                            break;
                        }

                    case IdentifierNameSyntax identifier when isWebClass && VbSyntax.Is(identifier.Identifier.Text, "IsPostBack"):
                        findings.Add(new LegacyTechnologyFinding(PageLifecycle, "IsPostBack", source.Path, VbSyntax.Line(identifier), className));
                        break;

                    case InvocationExpressionSyntax invocation
                        when invocation.Expression is IdentifierNameSyntax { Identifier.Text: var sessionName }
                             && VbSyntax.Is(sessionName, "Session"):
                        {
                            var key = VbSyntax.FirstStringArgument(invocation) ?? "?";
                            var access = invocation.Parent is AssignmentStatementSyntax assignment && assignment.Left == invocation
                                ? "write" : "read";

                            findings.Add(new LegacyTechnologyFinding(Session, $"Session(\"{key}\") {access}", source.Path, VbSyntax.Line(invocation), className));
                            break;
                        }

                    case MemberAccessExpressionSyntax member:
                        {
                            var left = VbSyntax.SimpleName(member.Expression);
                            var right = member.Name.Identifier.Text;

                            if (VbSyntax.Is(left, "HttpContext") && VbSyntax.Is(right, "Current"))
                            {
                                findings.Add(new LegacyTechnologyFinding(SystemWeb, "HttpContext.Current", source.Path, VbSyntax.Line(member), className));
                            }
                            else if (isWebClass && member.Expression is IdentifierNameSyntax
                                     && (VbSyntax.Is(left, "Request") || VbSyntax.Is(left, "Response") || VbSyntax.Is(left, "Server")))
                            {
                                findings.Add(new LegacyTechnologyFinding(SystemWeb, $"{left}.{right}", source.Path, VbSyntax.Line(member), className));
                            }

                            break;
                        }
                }
            }
        }
    }

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
