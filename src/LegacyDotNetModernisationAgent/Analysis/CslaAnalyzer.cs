using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
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
        IReadOnlyList<VbSource> sources,
        IReadOnlyList<WebConfigModel> configs)
    {
        var findings = new List<LegacyTechnologyFinding>();
        var usedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in sources)
        {
            foreach (var import in source.Root.Imports
                         .SelectMany(statement => statement.ImportsClauses)
                         .OfType<SimpleImportsClauseSyntax>()
                         .Where(clause => VbSyntax.Compact(clause.Name).StartsWith("Csla", StringComparison.OrdinalIgnoreCase)))
            {
                findings.Add(new LegacyTechnologyFinding(Csla, $"Imports {VbSyntax.Compact(import.Name)}", source.Path, VbSyntax.Line(import)));
            }

            foreach (var type in source.Root.DescendantNodes().OfType<ClassBlockSyntax>())
            {
                var className = type.ClassStatement.Identifier.Text;

                foreach (var baseType in type.Inherits.SelectMany(inherits => inherits.Types))
                {
                    var name = VbSyntax.SimpleName(baseType);
                    var qualifiedCsla = VbSyntax.Compact(baseType).StartsWith("Csla.", StringComparison.OrdinalIgnoreCase);

                    if (!qualifiedCsla && !BusinessBaseTypes.Any(known => VbSyntax.Is(known, name)))
                    {
                        continue;
                    }

                    usedTypes.Add(name);

                    findings.Add(new LegacyTechnologyFinding(
                        Csla,
                        $"Inherits {VbSyntax.Text(baseType)}",
                        source.Path,
                        VbSyntax.Line(baseType),
                        className));
                }

                foreach (var method in type.Members.OfType<MethodBlockSyntax>())
                {
                    var name = method.SubOrFunctionStatement.Identifier.Text;

                    if (DataPortalHandler().IsMatch(name))
                    {
                        findings.Add(new LegacyTechnologyFinding(CslaDataPortal, name, source.Path, VbSyntax.Line(method), className));
                    }
                }
            }

            foreach (var invocation in source.Root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (invocation.Expression is not MemberAccessExpressionSyntax member
                    || !VbSyntax.Is(VbSyntax.SimpleName(member.Expression), "DataPortal")
                    || !DataPortalOperations.Any(operation => VbSyntax.Is(operation, member.Name.Identifier.Text)))
                {
                    continue;
                }

                usedTypes.Add("DataPortal");

                var businessType = VbSyntax.FirstTypeArgument(member.Name);
                var evidence = $"DataPortal.{member.Name.Identifier.Text}" + (businessType is null ? "" : $"(Of {businessType})");

                findings.Add(new LegacyTechnologyFinding(
                    CslaDataPortal,
                    evidence,
                    source.Path,
                    VbSyntax.Line(invocation),
                    VbSyntax.EnclosingClass(invocation)));
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
