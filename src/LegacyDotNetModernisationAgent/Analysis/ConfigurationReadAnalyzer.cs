using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using LegacyDotNetModernisationAgent.Models;

namespace LegacyDotNetModernisationAgent.Analysis;

/// <summary>Records every ConfigurationManager.AppSettings("key") read: the evidence for ReadsConfiguration edges.</summary>
public sealed class ConfigurationReadAnalyzer
{
    public const string ConfigurationRead = "ConfigurationRead";

    public IReadOnlyList<LegacyTechnologyFinding> Analyze(IReadOnlyList<VbSource> sources)
    {
        var findings = new List<LegacyTechnologyFinding>();

        foreach (var source in sources)
        {
            foreach (var invocation in source.Root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (!VbSyntax.Is(VbSyntax.Compact(invocation.Expression), "ConfigurationManager.AppSettings"))
                {
                    continue;
                }

                var key = VbSyntax.FirstStringArgument(invocation);

                if (key is null)
                {
                    continue;
                }

                findings.Add(new LegacyTechnologyFinding(
                    ConfigurationRead,
                    $"appSettings[{key}]",
                    source.Path,
                    VbSyntax.Line(invocation),
                    VbSyntax.EnclosingClass(invocation)));
            }
        }

        return findings;
    }
}
