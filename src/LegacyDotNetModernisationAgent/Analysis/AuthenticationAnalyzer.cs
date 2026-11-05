using System.Text.RegularExpressions;
using LegacyDotNetModernisationAgent.Analysis.Facts;
using LegacyDotNetModernisationAgent.Models;

namespace LegacyDotNetModernisationAgent.Analysis;

public sealed record AuthenticationAnalysisResult(
    AuthenticationAssessment Assessment,
    IReadOnlyList<LegacyTechnologyFinding> Findings);

/// <summary>
/// Detects how an estate authenticates and authorizes, from code (VB.NET and C# through the fact model),
/// configuration and packages. Evidence is recorded as "Kind: detail". Secrets are never recorded: a credential-like
/// setting is reported by key name only. This reports what is declared and used, not whether it is configured safely.
/// </summary>
public sealed partial class AuthenticationAnalyzer
{
    public const string Authentication = "Authentication";
    public const string Authorization = "Authorization";

    public const string Forms = "FormsAuthentication";
    public const string Membership = "MembershipRoleProvider";
    public const string Windows = "WindowsAuthentication";
    public const string AspNetIdentity = "AspNetIdentityOwin";
    public const string Claims = "ClaimsIdentityModel";
    public const string CustomPrincipal = "CustomPrincipal";
    public const string SharedSecret = "SharedSecretOrTrustedSubsystem";
    public const string IdentityChecks = "IdentityChecksInCode";
    public const string AuthorizationAttributes = "AuthorizationAttributes";
    public const string CslaRules = "CslaAuthorizationRules";
    public const string WcfSecurity = "WcfSecurity";
    public const string IisAuthentication = "IisAuthentication";
    public const string Impersonation = "Impersonation";
    public const string MachineKey = "MachineKey";
    public const string NoAuthentication = "NoAuthenticationConfigured";
    public const string ExternalIdentity = "ExternalIdentityProvider";
    public const string DirectoryServices = "ActiveDirectory";
    public const string CustomPipeline = "CustomAuthPipeline";

    // ASP.NET pipeline events at which custom code can authenticate or authorize a request.
    private static readonly string[] AuthEvents = ["AuthenticateRequest", "PostAuthenticateRequest", "AuthorizeRequest", "PostAuthorizeRequest"];

    private static readonly string[] MembershipApi = ["ValidateUser", "GetUser", "CreateUser", "DeleteUser", "UpdateUser", "FindUsersByName", "GetUserNameByEmail", "GeneratePassword"];
    private static readonly string[] RolesApi = ["IsUserInRole", "GetRolesForUser", "GetAllRoles", "AddUserToRole", "AddUserToRoles", "RemoveUserFromRole", "RoleExists", "CreateRole"];
    private static readonly string[] ProviderBases = ["MembershipProvider", "RoleProvider", "ProfileProvider"];
    private static readonly string[] PrincipalBases = ["IPrincipal", "IIdentity", "GenericPrincipal", "BusinessPrincipalBase", "CslaPrincipal"];
    private static readonly string[] AuthorizationBases = ["AuthorizeAttribute", "AuthorizationFilterAttribute", "ServiceAuthorizationManager", "AuthorizationAttribute"];
    private static readonly string[] AuthorizationAttributeNames = ["Authorize", "AllowAnonymous", "PrincipalPermission", "RequiresAuthentication"];

    private static readonly (string Prefix, string Kind)[] ImportKinds =
    [
        ("Microsoft.AspNet.Identity", AspNetIdentity),
        ("Microsoft.Owin.Security", AspNetIdentity),
        ("System.IdentityModel", Claims),
        ("Microsoft.IdentityModel", Claims),
        ("System.Security.Claims", Claims),
        ("Microsoft.Identity.Client", ExternalIdentity),
        ("System.DirectoryServices", DirectoryServices)
    ];

    private static readonly (string Prefix, string Kind)[] PackageKinds =
    [
        ("Microsoft.AspNet.Identity", AspNetIdentity),
        ("Microsoft.Owin.Security", AspNetIdentity),
        ("IdentityServer", ExternalIdentity),
        ("IdentityModel", Claims),
        ("Microsoft.IdentityModel", Claims),
        ("System.IdentityModel", Claims),
        ("Microsoft.Identity.Client", ExternalIdentity),
        ("System.DirectoryServices", DirectoryServices)
    ];

    [GeneratedRegex("secret|password|pwd|apikey|api_key|token|trusted", RegexOptions.IgnoreCase)]
    private static partial Regex CredentialLikeKey();

    private sealed record Hit(string Technology, string Kind, string Detail, string Location, int? Line, string? Subject);

    public AuthenticationAnalysisResult Analyze(
        IReadOnlyList<SourceFacts> sources,
        IReadOnlyList<WebConfigModel> configs,
        IReadOnlyList<DotNetProjectAssessment> projects)
    {
        var hits = new List<Hit>();
        var rules = new List<AccessRule>();

        foreach (var source in sources)
        {
            AnalyzeCode(source, hits, rules);
        }

        foreach (var config in configs)
        {
            AnalyzeConfig(config, hits, rules);
        }

        foreach (var project in projects)
        {
            foreach (var package in project.Packages)
            {
                var match = PackageKinds.FirstOrDefault(entry => package.Id.StartsWith(entry.Prefix, StringComparison.OrdinalIgnoreCase));

                if (match.Kind is not null)
                {
                    hits.Add(new Hit(Authentication, match.Kind, $"package {package.Id} {package.Version}", project.Path, null, null));
                }
            }

            foreach (var reference in project.AssemblyReferences)
            {
                var match = PackageKinds.FirstOrDefault(entry => reference.Name.StartsWith(entry.Prefix, StringComparison.OrdinalIgnoreCase));

                if (match.Kind is not null)
                {
                    hits.Add(new Hit(Authentication, match.Kind, $"assembly reference {reference.Name}", project.Path, null, null));
                }
            }
        }

        var findings = hits
            .Select(hit => new LegacyTechnologyFinding(hit.Technology, $"{hit.Kind}: {hit.Detail}", hit.Location, hit.Line, hit.Subject))
            .ToList();

        var mechanisms = hits
            .GroupBy(hit => hit.Kind, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new AuthMechanism(
                group.Key,
                group.OrderBy(hit => hit.Subject is null ? 0 : 1).ThenBy(hit => hit.Detail, StringComparer.Ordinal).First().Detail,
                group.Count(),
                group.Select(hit => hit.Location).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Take(5).ToList()))
            .ToList();

        return new AuthenticationAnalysisResult(
            new AuthenticationAssessment(
                mechanisms,
                rules.OrderBy(rule => rule.Source, StringComparer.Ordinal).ThenBy(rule => rule.Location, StringComparer.Ordinal)
                    .ThenBy(rule => rule.Line ?? 0).ThenBy(rule => rule.Target, StringComparer.Ordinal).ToList(),
                SecurityHintAnalyzer.Analyze(configs)),
            findings);
    }

    private static void AnalyzeCode(SourceFacts source, List<Hit> hits, List<AccessRule> rules)
    {
        void Add(string technology, string kind, string detail, int? line, string? subject) =>
            hits.Add(new Hit(technology, kind, detail, source.Path, line, subject));

        foreach (var import in source.Imports)
        {
            var match = ImportKinds.FirstOrDefault(entry => import.Name.StartsWith(entry.Prefix, StringComparison.OrdinalIgnoreCase));

            if (match.Kind is not null)
            {
                Add(Authentication, match.Kind, $"Imports {import.Name}", import.Line, null);
            }
        }

        foreach (var type in source.Types)
        {
            foreach (var baseType in type.Bases)
            {
                if (ProviderBases.Contains(baseType.SimpleName, StringComparer.OrdinalIgnoreCase))
                {
                    Add(Authentication, Membership, $"{type.Name} derives from {baseType.SimpleName}", baseType.Line, type.Name);
                }
                else if (PrincipalBases.Contains(baseType.SimpleName, StringComparer.OrdinalIgnoreCase))
                {
                    Add(Authentication, CustomPrincipal, $"{type.Name} derives from or implements {baseType.SimpleName}", baseType.Line, type.Name);
                }
                else if (AuthorizationBases.Contains(baseType.SimpleName, StringComparer.OrdinalIgnoreCase))
                {
                    Add(Authorization, AuthorizationAttributes, $"custom authorization: {type.Name} derives from {baseType.SimpleName}", baseType.Line, type.Name);
                }
            }

            foreach (var pipelineEvent in source.Identifiers
                         .Where(identifier => identifier.Type == type.Name && AuthEvents.Contains(identifier.Name, StringComparer.OrdinalIgnoreCase))
                         .GroupBy(identifier => identifier.Name, StringComparer.OrdinalIgnoreCase))
            {
                Add(Authentication, CustomPipeline, $"{type.Name} hooks {pipelineEvent.Key}", pipelineEvent.Min(identifier => identifier.Line), type.Name);
            }

            foreach (var method in type.Methods.Where(method => method.Name.StartsWith("Application_", StringComparison.OrdinalIgnoreCase)
                                                                 && AuthEvents.Any(name => method.Name.Equals($"Application_{name}", StringComparison.OrdinalIgnoreCase))))
            {
                Add(Authentication, CustomPipeline, $"{type.Name}.{method.Name} (Global.asax handler)", method.StartLine, type.Name);
            }

            AddAttributeRules(type.Attributes, type.Name, type.Name, source.Path, hits, rules);

            foreach (var method in type.Methods)
            {
                AddAttributeRules(method.Attributes, $"{type.Name}.{method.Name}", type.Name, source.Path, hits, rules);
            }
        }

        foreach (var member in source.MemberAccesses)
        {
            if (Is(member.Qualifier, "FormsAuthentication"))
            {
                Add(Authentication, Forms, $"FormsAuthentication.{member.Member}", member.Line, member.Type);
            }
            else if (Is(member.Qualifier, "Membership") && MembershipApi.Contains(member.Member, StringComparer.OrdinalIgnoreCase))
            {
                Add(Authentication, Membership, $"Membership.{member.Member}", member.Line, member.Type);
            }
            else if (Is(member.Qualifier, "Roles") && RolesApi.Contains(member.Member, StringComparer.OrdinalIgnoreCase))
            {
                Add(Authentication, Membership, $"Roles.{member.Member}", member.Line, member.Type);
            }
            else if (Is(member.Qualifier, "WindowsIdentity") || Is(member.Qualifier, "WindowsPrincipal"))
            {
                Add(Authentication, Windows, $"{member.Qualifier}.{member.Member}", member.Line, member.Type);
            }
            else if (Is(member.Qualifier, "ClaimsPrincipal") && Is(member.Member, "Current"))
            {
                Add(Authentication, Claims, "ClaimsPrincipal.Current", member.Line, member.Type);
            }
            else if (Is(member.Qualifier, "Thread") && Is(member.Member, "CurrentPrincipal"))
            {
                Add(Authentication, CustomPrincipal, "Thread.CurrentPrincipal", member.Line, member.Type);
            }
            else if (Is(member.Member, "IsAuthenticated"))
            {
                Add(Authorization, IdentityChecks, $"{member.Qualifier}.IsAuthenticated", member.Line, member.Type);
            }
        }

        foreach (var creation in source.Creations)
        {
            var kind = creation.TypeName switch
            {
                _ when Is(creation.TypeName, "GenericPrincipal") || Is(creation.TypeName, "GenericIdentity") => CustomPrincipal,
                _ when Is(creation.TypeName, "WindowsPrincipal") || Is(creation.TypeName, "WindowsIdentity") => Windows,
                _ when Is(creation.TypeName, "ClaimsPrincipal") || Is(creation.TypeName, "ClaimsIdentity") => Claims,
                _ when Is(creation.TypeName, "FormsAuthenticationTicket") || Is(creation.TypeName, "FormsIdentity") => Forms,
                _ => null
            };

            if (kind is not null)
            {
                Add(Authentication, kind, $"creates {creation.TypeName}", creation.Line, creation.Type);
            }
        }

        foreach (var call in source.Invocations)
        {
            if (Is(call.CalleeName, "IsInRole"))
            {
                Add(Authorization, IdentityChecks, "IsInRole", call.Line, call.Type);
            }
            else if (Is(call.Qualifier, "AuthorizationRules") && (call.CalleeName.StartsWith("Allow", StringComparison.OrdinalIgnoreCase) || call.CalleeName.StartsWith("Deny", StringComparison.OrdinalIgnoreCase)))
            {
                var target = call.StringArguments.FirstOrDefault() ?? "(not a literal)";
                var principals = string.Join(", ", call.StringArguments.Skip(1).Where(argument => argument is not null));

                rules.Add(new AccessRule(CslaRules, $"{call.Type}.{target}", call.CalleeName, principals, source.Path, call.Line));
                Add(Authorization, CslaRules, $"{call.CalleeName}(\"{target}\"{(principals.Length == 0 ? "" : $", \"{principals}\"")})", call.Line, call.Type);
            }
            else if (Is(call.CalleeText, "ConfigurationManager.AppSettings") && call.FirstString is { } key && CredentialLikeKey().IsMatch(key))
            {
                // Key name only: the value is never read or recorded.
                Add(Authentication, SharedSecret, $"reads credential-like setting {key}", call.Line, call.Type);
            }
        }
    }

    private static void AddAttributeRules(
        IReadOnlyList<AttributeFact> attributes,
        string target,
        string subject,
        string path,
        List<Hit> hits,
        List<AccessRule> rules)
    {
        foreach (var attribute in attributes.Where(attribute => AuthorizationAttributeNames.Any(name => Is(attribute.Name, name) || Is(attribute.Name, name + "Attribute"))))
        {
            var principals = string.Join(", ", attribute.Arguments);

            rules.Add(new AccessRule(AuthorizationAttributes, target, attribute.Name, principals, path, attribute.Line));
            hits.Add(new Hit(Authorization, AuthorizationAttributes, $"[{attribute.Name}{(principals.Length == 0 ? "" : $"({principals})")}] on {target}", path, attribute.Line, subject));
        }
    }

    private static void AnalyzeConfig(WebConfigModel config, List<Hit> hits, List<AccessRule> rules)
    {
        foreach (var item in config.Auth)
        {
            switch (item.Kind)
            {
                case "AuthenticationMode":
                    {
                        var mode = item.Detail.Replace("mode=", "", StringComparison.Ordinal);
                        var kind = mode.ToLowerInvariant() switch
                        {
                            "forms" => Forms,
                            "windows" => Windows,
                            "none" => NoAuthentication,
                            _ => ExternalIdentity
                        };

                        hits.Add(new Hit(Authentication, kind, $"config authentication {item.Detail}", config.Path, null, null));
                        break;
                    }

                case "Forms":
                    hits.Add(new Hit(Authentication, Forms, $"config forms {item.Detail}", config.Path, null, null));
                    break;

                case "AuthorizationRule":
                    {
                        var parts = item.Detail.Split(' ', 2);

                        rules.Add(new AccessRule("WebConfig", item.Scope, parts[0], parts.Length > 1 ? parts[1] : "", config.Path, null));
                        hits.Add(new Hit(Authorization, "WebConfigAuthorization", $"{item.Scope}: {item.Detail}", config.Path, null, null));
                        break;
                    }

                case "Membership" or "MembershipProvider" or "RoleManager" or "RoleProvider":
                    hits.Add(new Hit(Authentication, Membership, $"config {item.Kind} {item.Detail}".Trim(), config.Path, null, null));
                    break;

                case "Impersonation":
                    hits.Add(new Hit(Authentication, Impersonation, $"config identity {item.Detail}", config.Path, null, null));
                    break;

                case "MachineKey":
                    hits.Add(new Hit(Authentication, MachineKey, $"config machineKey {item.Detail}", config.Path, null, null));
                    break;

                case "IisAuthentication" when item.Detail.Contains("enabled=true", StringComparison.OrdinalIgnoreCase):
                    hits.Add(new Hit(Authentication, IisAuthentication, $"config IIS {item.Detail}", config.Path, null, null));
                    break;

                case "WcfBindingSecurity" or "WcfCredentialsBehavior":
                    hits.Add(new Hit(Authentication, WcfSecurity, $"config {item.Detail}", config.Path, null, null));
                    break;

                case "ExternalIdentitySetting":
                    hits.Add(new Hit(Authentication, item.Detail.StartsWith("key owin:", StringComparison.OrdinalIgnoreCase) ? AspNetIdentity : ExternalIdentity,
                        $"config {item.Detail}", config.Path, null, null));
                    break;
            }
        }
    }

    private static bool Is(string? left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
