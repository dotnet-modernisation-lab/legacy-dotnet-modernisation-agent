using LegacyDotNetModernisationAgent.Analysis.Facts;
using LegacyDotNetModernisationAgent.Models;

namespace LegacyDotNetModernisationAgent.Analysis;

/// <summary>Records every ConfigurationManager.AppSettings["key"] read: the evidence for ReadsConfiguration edges.</summary>
public sealed class ConfigurationReadAnalyzer
{
    public const string ConfigurationRead = "ConfigurationRead";

    public IReadOnlyList<LegacyTechnologyFinding> Analyze(IReadOnlyList<SourceFacts> sources)
    {
        return sources
            .SelectMany(source => source.Invocations
                .Where(call => string.Equals(call.CalleeText, "ConfigurationManager.AppSettings", StringComparison.OrdinalIgnoreCase)
                               && call.FirstString is not null)
                .Select(call => new LegacyTechnologyFinding(
                    ConfigurationRead,
                    $"appSettings[{call.FirstString}]",
                    source.Path,
                    call.Line,
                    call.Type)))
            .ToList();
    }
}
