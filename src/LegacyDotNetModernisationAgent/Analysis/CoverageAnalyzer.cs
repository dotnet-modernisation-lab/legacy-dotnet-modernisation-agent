using LegacyDotNetModernisationAgent.Models;

namespace LegacyDotNetModernisationAgent.Analysis;

/// <summary>
/// Reports what the analysis did not cover. A technology we recognise but cannot yet analyse is a gap, not a pass:
/// the estate may depend on it in ways the assessment cannot see.
/// </summary>
public static class CoverageAnalyzer
{
    /// <summary>Assemblies/packages that signal a technology with no analyser yet. Extend as analysers are added.</summary>
    private static readonly (string Name, string Technology)[] UnanalysedSignals =
    [
        ("EntityFramework", "Entity Framework"),
        ("System.Data.Entity", "Entity Framework 4/5"),
        ("NHibernate", "NHibernate"),
        ("System.Runtime.Remoting", ".NET Remoting"),
        ("System.EnterpriseServices", "COM+ / Enterprise Services"),
        ("System.Web.Services", "ASMX / SOAP web services"),
        ("System.Web.Mvc", "ASP.NET MVC"),
        ("System.Web.Http", "ASP.NET Web API"),
        ("Microsoft.AspNet.SignalR", "SignalR"),
        ("Microsoft.Practices", "Enterprise Library / Unity"),
        ("System.Messaging", "MSMQ"),
        ("System.Workflow", "Windows Workflow Foundation"),
        ("System.Windows.Forms", "Windows Forms"),
        ("PresentationFramework", "WPF"),
        ("System.DirectoryServices", "Active Directory / LDAP"),
        ("System.Data.OracleClient", "Oracle (System.Data.OracleClient)")
    ];

    public static AnalysisCoverage Analyze(
        IReadOnlyList<DotNetProjectAssessment> projects,
        IReadOnlyList<CoverageGap> discoveryGaps)
    {
        var gaps = new List<CoverageGap>(discoveryGaps);

        foreach (var project in projects)
        {
            if (project.Language != ProjectLanguage.VisualBasic && project.Files.SourceFiles.Count > 0)
            {
                gaps.Add(new CoverageGap(
                    "SourceNotScanned",
                    $"{project.Language} source ({project.Files.SourceFiles.Count} files) is inventoried but not scanned for technologies",
                    project.Path,
                    "Web Forms, CSLA, WCF and data-access usage in this source is not detected."));
            }

            foreach (var reference in project.AssemblyReferences.Where(r => r is { Kind: ReferenceKind.LocalBinary, BinaryExists: false }))
            {
                gaps.Add(new CoverageGap(
                    "MissingBinary",
                    $"Referenced binary {reference.Name} is not on disk",
                    reference.HintPath,
                    "Its version and API cannot be established from the binary."));
            }

            var names = project.AssemblyReferences.Select(r => r.Name)
                .Concat(project.Packages.Select(p => p.Id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var (signal, technology) in UnanalysedSignals)
            {
                var hit = names.FirstOrDefault(name => name.Equals(signal, StringComparison.OrdinalIgnoreCase)
                                                    || name.StartsWith(signal + ".", StringComparison.OrdinalIgnoreCase));

                if (hit is not null)
                {
                    gaps.Add(new CoverageGap(
                        "UnanalysedTechnology",
                        $"{technology} is referenced ({hit}) but has no analyser",
                        project.Path,
                        "Its coupling and migration impact are not assessed; treat as a human decision."));
                }
            }
        }

        var byLanguage = projects
            .GroupBy(project => project.Language.ToString())
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Sum(project => project.Files.SourceFiles.Count), StringComparer.Ordinal);

        return new AnalysisCoverage(
            byLanguage,
            projects.Count,
            gaps
                .OrderBy(gap => gap.Category, StringComparer.Ordinal)
                .ThenBy(gap => gap.Location, StringComparer.Ordinal)
                .ThenBy(gap => gap.What, StringComparer.Ordinal)
                .ToList());
    }
}
