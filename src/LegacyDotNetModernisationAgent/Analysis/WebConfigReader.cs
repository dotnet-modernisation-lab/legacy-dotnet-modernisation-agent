using System.Text.RegularExpressions;
using System.Xml.Linq;
using LegacyDotNetModernisationAgent.Models;

namespace LegacyDotNetModernisationAgent.Analysis;

public sealed record AppSetting(string Key, string Value, bool Redacted);

public sealed record WebConfigModel(
    string Path,
    IReadOnlyList<AppSetting> AppSettings,
    string? SessionStateMode,
    string? AuthenticationMode,
    string? CompilationTargetFramework,
    IReadOnlyList<WcfEndpointInfo> Endpoints);

/// <summary>Reads Web.config / App.config. Secret-looking appSettings values are redacted in all output.</summary>
public static partial class WebConfigReader
{
    [GeneratedRegex("secret|password|pwd|token|apikey|connectionstring", RegexOptions.IgnoreCase)]
    private static partial Regex SecretKey();

    public static IReadOnlyList<WebConfigModel> ReadAll(
        string repositoryRoot,
        IReadOnlyList<DotNetProjectAssessment> projects)
    {
        return projects
            .SelectMany(project => project.Files.ConfigurationFiles)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(file => System.IO.Path.GetFileName(file).Equals("web.config", StringComparison.OrdinalIgnoreCase)
                        || System.IO.Path.GetFileName(file).Equals("app.config", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .Select(file => Read(repositoryRoot, file))
            .ToList();
    }

    private static WebConfigModel Read(string repositoryRoot, string relativePath)
    {
        var root = XDocument.Load(System.IO.Path.Combine(repositoryRoot, relativePath)).Root!;

        IEnumerable<XElement> Elements(string name) =>
            root.Descendants().Where(element => element.Name.LocalName == name);

        var settings = Elements("appSettings")
            .SelectMany(section => section.Elements().Where(element => element.Name.LocalName == "add"))
            .Select(element =>
            {
                var key = (string?)element.Attribute("key") ?? "";
                var redacted = SecretKey().IsMatch(key);

                return new AppSetting(key, redacted ? "[redacted]" : (string?)element.Attribute("value") ?? "", redacted);
            })
            .ToList();

        var endpoints = new List<WcfEndpointInfo>();

        foreach (var endpoint in Elements("endpoint"))
        {
            var parent = endpoint.Parent!;
            var isService = parent.Name.LocalName == "service";

            endpoints.Add(new WcfEndpointInfo(
                relativePath,
                isService ? "service" : "client",
                isService ? (string?)parent.Attribute("name") : (string?)endpoint.Attribute("name"),
                (string?)endpoint.Attribute("address") ?? "",
                (string?)endpoint.Attribute("binding") ?? "",
                (string?)endpoint.Attribute("contract") ?? ""));
        }

        return new WebConfigModel(
            relativePath,
            settings,
            (string?)Elements("sessionState").FirstOrDefault()?.Attribute("mode"),
            (string?)Elements("authentication").FirstOrDefault()?.Attribute("mode"),
            (string?)Elements("compilation").FirstOrDefault()?.Attribute("targetFramework"),
            endpoints);
    }
}
