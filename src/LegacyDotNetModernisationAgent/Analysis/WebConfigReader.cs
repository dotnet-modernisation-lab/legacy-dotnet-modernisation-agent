using System.Text.RegularExpressions;
using System.Xml.Linq;
using LegacyDotNetModernisationAgent.Models;

namespace LegacyDotNetModernisationAgent.Analysis;

public sealed record AppSetting(string Key, string Value, bool Redacted);

/// <summary>One authentication or authorization setting. Detail never contains a key, password or other secret.</summary>
public sealed record AuthConfigItem(string Kind, string Detail, string Scope);

public sealed record WebConfigModel(
    string Path,
    IReadOnlyList<AppSetting> AppSettings,
    string? SessionStateMode,
    string? AuthenticationMode,
    string? CompilationTargetFramework,
    IReadOnlyList<WcfEndpointInfo> Endpoints,
    IReadOnlyList<AuthConfigItem> Auth);

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
            (string?)Elements("authentication").FirstOrDefault(element => element.Attribute("mode") is not null)?.Attribute("mode"),
            (string?)Elements("compilation").FirstOrDefault()?.Attribute("targetFramework"),
            endpoints,
            ReadAuth(root, settings));
    }

    private static string Attrs(XElement element, params string[] names) =>
        string.Join(", ", names.Where(name => element.Attribute(name) is not null).Select(name => $"{name}={(string?)element.Attribute(name)}"));

    private static string ScopeOf(XElement element) =>
        element.Ancestors().FirstOrDefault(ancestor => ancestor.Name.LocalName == "location") is { } location
            ? $"location:{(string?)location.Attribute("path") ?? "."}"
            : "application";

    /// <summary>
    /// Authentication and authorization configuration. Secrets are never copied: machineKey records only its
    /// algorithms, identity records only that a password is present.
    /// </summary>
    private static List<AuthConfigItem> ReadAuth(XElement root, IReadOnlyList<AppSetting> settings)
    {
        var items = new List<AuthConfigItem>();
        var all = root.Descendants().ToList();

        foreach (var element in all)
        {
            var name = element.Name.LocalName;
            var parent = element.Parent?.Name.LocalName;

            switch (name)
            {
                case "authentication" when parent == "system.web":
                    items.Add(new AuthConfigItem("AuthenticationMode", $"mode={(string?)element.Attribute("mode") ?? "(default)"}", ScopeOf(element)));
                    break;

                case "forms" when parent == "authentication":
                    items.Add(new AuthConfigItem("Forms", Attrs(element, "loginUrl", "timeout", "name", "protection", "requireSSL", "slidingExpiration", "cookieless"), ScopeOf(element)));
                    break;

                case "allow" or "deny" when parent == "authorization":
                    items.Add(new AuthConfigItem("AuthorizationRule", $"{name} {Attrs(element, "users", "roles", "verbs")}".Trim(), ScopeOf(element)));
                    break;

                case "membership":
                    items.Add(new AuthConfigItem("Membership", Attrs(element, "defaultProvider", "userIsOnlineTimeWindow"), ScopeOf(element)));
                    items.AddRange(element.Descendants().Where(child => child.Name.LocalName == "add" && child.Attribute("type") is not null)
                        .Select(child => new AuthConfigItem("MembershipProvider", Attrs(child, "name", "type"), ScopeOf(element))));
                    break;

                case "roleManager":
                    items.Add(new AuthConfigItem("RoleManager", Attrs(element, "enabled", "defaultProvider", "cacheRolesInCookie"), ScopeOf(element)));
                    items.AddRange(element.Descendants().Where(child => child.Name.LocalName == "add" && child.Attribute("type") is not null)
                        .Select(child => new AuthConfigItem("RoleProvider", Attrs(child, "name", "type"), ScopeOf(element))));
                    break;

                case "identity" when parent == "system.web":
                    items.Add(new AuthConfigItem("Impersonation",
                        $"{Attrs(element, "impersonate")}{(element.Attribute("userName") is null ? "" : ", userName present")}{(element.Attribute("password") is null ? "" : ", password present")}".Trim(' ', ','), ScopeOf(element)));
                    break;

                case "machineKey":
                    items.Add(new AuthConfigItem("MachineKey", $"set ({Attrs(element, "validation", "decryption")}); key values not recorded", ScopeOf(element)));
                    break;

                case "anonymousAuthentication" or "windowsAuthentication" or "basicAuthentication" or "digestAuthentication" or "clientCertificateMappingAuthentication":
                    items.Add(new AuthConfigItem("IisAuthentication", $"{name} {Attrs(element, "enabled")}".Trim(), ScopeOf(element)));
                    break;

                case "security" when element.Ancestors().Any(ancestor => ancestor.Name.LocalName == "bindings"):
                    {
                        var binding = element.Parent;
                        var bindingKind = binding?.Parent?.Name.LocalName ?? "binding";
                        var detail = new List<string> { $"{bindingKind} {(string?)binding?.Attribute("name")}".Trim(), Attrs(element, "mode") };

                        foreach (var child in element.Elements().Where(child => child.Name.LocalName is "transport" or "message"))
                        {
                            detail.Add($"{child.Name.LocalName}.{Attrs(child, "clientCredentialType")}");
                        }

                        items.Add(new AuthConfigItem("WcfBindingSecurity", string.Join("; ", detail.Where(part => part.Length > 0)), ScopeOf(element)));
                        break;
                    }

                case "serviceCredentials" or "clientCredentials" or "serviceAuthorization" or "serviceSecurityAudit":
                    items.Add(new AuthConfigItem("WcfCredentialsBehavior", $"{name} {Attrs(element, "principalPermissionMode", "roleProviderName", "serviceAuthorizationManagerType")}".Trim(), ScopeOf(element)));
                    break;
            }
        }

        // Settings that configure OWIN, ADFS or Azure AD sign-in (names only; values may identify tenants).
        items.AddRange(settings
            .Where(setting => setting.Key.StartsWith("owin:", StringComparison.OrdinalIgnoreCase) || setting.Key.StartsWith("ida:", StringComparison.OrdinalIgnoreCase)
                              || setting.Key.StartsWith("adfs:", StringComparison.OrdinalIgnoreCase))
            .Select(setting => new AuthConfigItem("ExternalIdentitySetting", $"key {setting.Key}", "application")));

        return items;
    }
}
