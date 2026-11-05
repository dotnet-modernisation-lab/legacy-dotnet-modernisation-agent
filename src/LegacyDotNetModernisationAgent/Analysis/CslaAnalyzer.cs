using System.Text.RegularExpressions;
using LegacyDotNetModernisationAgent.Analysis.Facts;
using LegacyDotNetModernisationAgent.Models;

namespace LegacyDotNetModernisationAgent.Analysis;

public sealed record CslaAnalysisResult(
    IReadOnlyList<LegacyTechnologyFinding> Findings,
    IReadOnlySet<string> UsedTypeNames);

/// <summary>
/// Detects CSLA usage in source: base classes, DataPortal calls, DataPortal_* handlers and configuration.
/// Whether the binary really is CSLA 2.0.3.0 is NOT decided here; that comes from LegacyAssemblyAnalyzer.
/// </summary>
public sealed partial class CslaAnalyzer
{
    public const string Csla = "Csla";
    public const string CslaDataPortal = "CslaDataPortal";

    private static readonly string[] BusinessBaseTypes =
        ["ReadOnlyBase", "BusinessBase", "ReadOnlyListBase", "BusinessListBase", "NameValueListBase",
         "CommandBase", "SmartDate", "CriteriaBase"];

    private static readonly string[] DataPortalOperations = ["Fetch", "Create", "Update", "Delete", "Execute"];

    [GeneratedRegex(@"^DataPortal_(Fetch|Create|Update|Insert|Delete|DeleteSelf|Execute)$", RegexOptions.IgnoreCase)]
    private static partial Regex DataPortalHandler();

    public CslaAnalysisResult Analyze(
        IReadOnlyList<SourceFacts> sources,
        IReadOnlyList<WebConfigModel> configs)
    {
        var findings = new List<LegacyTechnologyFinding>();
        var usedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in sources)
        {
            foreach (var import in source.Imports.Where(import => import.Name.StartsWith("Csla", StringComparison.OrdinalIgnoreCase)))
            {
                findings.Add(new LegacyTechnologyFinding(Csla, $"Imports {import.Name}", source.Path, import.Line));
            }

            foreach (var type in source.Types.Where(type => type.Kind == "Class"))
            {
                foreach (var baseType in type.Bases.Where(baseType => baseType.Kind == "Inherits"))
                {
                    var qualifiedCsla = baseType.Compact.StartsWith("Csla.", StringComparison.OrdinalIgnoreCase);

                    if (!qualifiedCsla && !BusinessBaseTypes.Any(known => string.Equals(known, baseType.SimpleName, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    usedTypes.Add(baseType.SimpleName);

                    findings.Add(new LegacyTechnologyFinding(Csla, $"Inherits {baseType.Display}", source.Path, baseType.Line, type.Name));
                }

                foreach (var method in type.Methods.Where(method => DataPortalHandler().IsMatch(method.Name)))
                {
                    findings.Add(new LegacyTechnologyFinding(CslaDataPortal, method.Name, source.Path, method.StartLine, type.Name));
                }
            }

            foreach (var call in source.Invocations.Where(call =>
                         string.Equals(call.Qualifier, "DataPortal", StringComparison.OrdinalIgnoreCase)
                         && DataPortalOperations.Any(operation => string.Equals(operation, call.CalleeName, StringComparison.OrdinalIgnoreCase))))
            {
                usedTypes.Add("DataPortal");

                findings.Add(new LegacyTechnologyFinding(
                    CslaDataPortal,
                    $"DataPortal.{call.CalleeName}{call.TypeArgumentDisplay}",
                    source.Path,
                    call.Line,
                    call.Type));
            }
        }

        foreach (var config in configs)
        {
            foreach (var setting in config.AppSettings.Where(setting => setting.Key.StartsWith("Csla", StringComparison.OrdinalIgnoreCase)))
            {
                findings.Add(new LegacyTechnologyFinding(Csla, $"appSettings[{setting.Key}]={setting.Value}", config.Path));
            }
        }

        return new CslaAnalysisResult(findings, usedTypes);
    }
}
