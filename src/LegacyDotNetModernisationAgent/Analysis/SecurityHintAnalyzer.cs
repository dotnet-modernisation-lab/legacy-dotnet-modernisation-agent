using System.Text.RegularExpressions;
using LegacyDotNetModernisationAgent.Models;

namespace LegacyDotNetModernisationAgent.Analysis;

/// <summary>
/// Observations about well-known risky configuration, for a security reviewer. These are hints only:
/// the tool does not test enforcement or exploitability, no values are recorded, and the output never feeds migration policy.
/// </summary>
public static partial class SecurityHintAnalyzer
{
    [GeneratedRegex(@"(?<name>\w+)=(?<value>[^,;)]*)")]
    private static partial Regex Pair();

    private static readonly string[] WeakValidation = ["SHA1", "MD5"];
    private static readonly string[] WeakDecryption = ["3DES", "DES"];

    public static IReadOnlyList<ReviewHint> Analyze(IReadOnlyList<WebConfigModel> configs)
    {
        var hints = new List<ReviewHint>();

        foreach (var config in configs.OrderBy(config => config.Path, StringComparer.Ordinal))
        {
            void Add(string id, string observation) => hints.Add(new ReviewHint(id, observation, config.Path));

            Dictionary<string, string> Values(string detail) =>
                Pair().Matches(detail).ToDictionary(match => match.Groups["name"].Value, match => match.Groups["value"].Value.Trim(), StringComparer.OrdinalIgnoreCase);

            foreach (var forms in config.Auth.Where(item => item.Kind == "Forms"))
            {
                var values = Values(forms.Detail);

                if (!values.TryGetValue("requireSSL", out var requireSsl) || !requireSsl.Equals("true", StringComparison.OrdinalIgnoreCase))
                {
                    Add("H-FORMS-SSL", "The forms authentication cookie is not restricted to SSL (requireSSL is not true).");
                }

                if (values.TryGetValue("protection", out var protection) && !protection.Equals("All", StringComparison.OrdinalIgnoreCase))
                {
                    Add("H-FORMS-PROTECTION", $"Forms authentication protection is {protection}, not All.");
                }
            }

            foreach (var machineKey in config.Auth.Where(item => item.Kind == "MachineKey"))
            {
                var values = Values(machineKey.Detail);

                if (values.TryGetValue("validation", out var validation) && WeakValidation.Contains(validation, StringComparer.OrdinalIgnoreCase))
                {
                    Add("H-MACHINEKEY-ALGORITHM", $"machineKey validation uses {validation}, a legacy algorithm.");
                }

                if (values.TryGetValue("decryption", out var decryption) && WeakDecryption.Contains(decryption, StringComparer.OrdinalIgnoreCase))
                {
                    Add("H-MACHINEKEY-ALGORITHM", $"machineKey decryption uses {decryption}, a legacy algorithm.");
                }
            }

            // ASP.NET applies the first matching rule in a scope, so an allow-everyone rule hides later denies.
            foreach (var scope in config.Auth.Where(item => item.Kind == "AuthorizationRule").GroupBy(item => item.Scope))
            {
                var rules = scope.ToList();
                var firstAllowAll = rules.FindIndex(rule => rule.Detail.StartsWith("allow", StringComparison.OrdinalIgnoreCase) && rule.Detail.Contains("users=*", StringComparison.Ordinal));

                if (firstAllowAll >= 0 && rules.Skip(firstAllowAll + 1).Any(rule => rule.Detail.StartsWith("deny", StringComparison.OrdinalIgnoreCase)))
                {
                    Add("H-AUTH-RULE-ORDER", $"In {scope.Key}, an allow-everyone rule comes before a deny rule, so the deny never applies (first match wins).");
                }
            }

            if (config.Auth.Any(item => item is { Kind: "AuthenticationMode" } && item.Detail.Equals("mode=None", StringComparison.OrdinalIgnoreCase))
                && config.Auth.Any(item => item.Kind == "AuthorizationRule" && item.Detail.StartsWith("allow", StringComparison.OrdinalIgnoreCase) && item.Detail.Contains("users=*", StringComparison.Ordinal)))
            {
                Add("H-ANONYMOUS-ALLOWED", "Authentication mode is None and authorization allows all users: every caller is anonymous at this layer.");
            }

            foreach (var key in config.AppSettings.Where(setting => setting.Redacted).Select(setting => setting.Key).Order(StringComparer.Ordinal))
            {
                Add("H-CREDENTIAL-IN-CONFIG", $"Credential-like setting {key} is stored in configuration (value not recorded).");
            }

            if (config.Auth.Any(item => item.Kind == "Impersonation" && item.Detail.Contains("password present", StringComparison.Ordinal)))
            {
                Add("H-IMPERSONATION-PASSWORD", "An impersonation password is stored in configuration (value not recorded).");
            }

            var securedBindings = config.Auth.Where(item => item.Kind == "WcfBindingSecurity").Select(item => item.Detail).ToList();

            foreach (var endpoint in config.Endpoints.Where(endpoint => endpoint.Role == "service" && endpoint.Binding.Equals("basicHttpBinding", StringComparison.OrdinalIgnoreCase)).DistinctBy(endpoint => endpoint.Contract))
            {
                if (!securedBindings.Any(detail => detail.StartsWith("basicHttpBinding", StringComparison.OrdinalIgnoreCase)))
                {
                    Add("H-WCF-NO-SECURITY", $"Service endpoint for {endpoint.Contract} uses basicHttpBinding with no security configuration (the default is no transport or message security).");
                }
            }

            foreach (var detail in securedBindings.Where(detail => detail.Contains("mode=None", StringComparison.OrdinalIgnoreCase)))
            {
                Add("H-WCF-NO-SECURITY", $"A WCF binding sets security mode None ({detail}).");
            }
        }

        return hints
            .DistinctBy(hint => (hint.Id, hint.Observation, hint.Location))
            .OrderBy(hint => hint.Id, StringComparer.Ordinal)
            .ThenBy(hint => hint.Location, StringComparer.Ordinal)
            .ThenBy(hint => hint.Observation, StringComparer.Ordinal)
            .ToList();
    }
}
