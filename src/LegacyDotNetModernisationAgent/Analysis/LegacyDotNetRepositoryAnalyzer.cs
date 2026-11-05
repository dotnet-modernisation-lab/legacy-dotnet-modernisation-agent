using System.Diagnostics;
using LegacyDotNetModernisationAgent.Analysis.Facts;
using LegacyDotNetModernisationAgent.Models;

namespace LegacyDotNetModernisationAgent.Analysis;

/// <summary>
/// Milestone B: deterministic inventory of a legacy .NET repository.
/// Technology findings are added by the Milestone C analysers; this phase only records facts
/// about projects, frameworks, references and packages.
/// </summary>
public sealed class LegacyDotNetRepositoryAnalyzer
{
    public LegacyDotNetProjectAssessment Analyze(string repositoryPath)
    {
        var root = Path.GetFullPath(repositoryPath);

        var discovery = ProjectDiscovery.Discover(root);
        var unreadable = new List<CoverageGap>();
        var projects = new List<DotNetProjectAssessment>();

        foreach (var path in discovery.ProjectPaths)
        {
            try
            {
                projects.Add(ProjectFileParser.Parse(root, path));
            }
            catch (Exception exception) when (exception is System.Xml.XmlException or IOException or NullReferenceException)
            {
                unreadable.Add(new CoverageGap(
                    "ProjectUnreadable",
                    $"Project could not be parsed: {exception.Message}",
                    Path.GetRelativePath(root, path).Replace('\\', '/'),
                    "Its code, references and technologies are not part of the assessment."));
            }
        }

        if (projects.Count == 0)
        {
            throw new InvalidOperationException($"No analysable .vbproj or .csproj projects were found under {root}.");
        }

        projects = projects.OrderBy(project => project.Path, StringComparer.Ordinal).ToList();

        var sources = SourceFactLoader.Load(root, projects);
        var configs = WebConfigReader.ReadAll(root, projects);

        var webForms = new WebFormsAnalyzer().Analyze(root, projects, sources, configs);
        var csla = new CslaAnalyzer().Analyze(sources, configs);
        var wcf = new WcfAnalyzer().Analyze(root, projects, sources, configs);
        var dataAccess = new LegacyDataAccessAnalyzer().Analyze(root, projects, sources, configs);
        var configurationReads = new ConfigurationReadAnalyzer().Analyze(sources);
        var assemblies = new LegacyAssemblyAnalyzer().Analyze(root, projects, csla.UsedTypeNames);
        var structure = new TypeDependencyAnalyzer().Analyze(projects, sources);
        var authentication = new AuthenticationAnalyzer().Analyze(sources, configs, projects);
        var pipeline = new HttpPipelineAnalyzer().Analyze(sources, configs);

        var technologies = webForms
            .Concat(csla.Findings)
            .Concat(wcf.Findings)
            .Concat(dataAccess.Findings)
            .Concat(configurationReads)
            .Concat(authentication.Findings)
            .Concat(pipeline)
            .OrderBy(finding => finding.Technology, StringComparer.Ordinal)
            .ThenBy(finding => finding.Location, StringComparer.Ordinal)
            .ThenBy(finding => finding.Line ?? 0)
            .ThenBy(finding => finding.Evidence, StringComparer.Ordinal)
            .ToList();

        return new LegacyDotNetProjectAssessment(
            new DirectoryInfo(root).Name,
            ReadCommitSha(root),
            ResolvePlatform(projects),
            projects,
            technologies,
            BuildDependencies(projects),
            assemblies,
            wcf.Assessment,
            dataAccess.Assessment,
            structure.DeclaredTypes,
            structure.Dependencies,
            CoverageAnalyzer.Analyze(projects, discovery.Gaps.Concat(unreadable).ToList()),
            authentication.Assessment);
    }

    private static DotNetPlatform ResolvePlatform(IReadOnlyList<DotNetProjectAssessment> projects)
    {
        var platforms = projects.Select(project => project.Platform).Distinct().ToList();

        return platforms.Count == 1 ? platforms[0] : DotNetPlatform.Unknown;
    }

    private static List<ComponentDependency> BuildDependencies(IReadOnlyList<DotNetProjectAssessment> projects)
    {
        var byPath = projects.ToDictionary(project => project.Path, StringComparer.OrdinalIgnoreCase);
        var dependencies = new List<ComponentDependency>();

        foreach (var project in projects)
        {
            foreach (var reference in project.ProjectReferences)
            {
                var target = byPath.TryGetValue(reference.Path, out var found)
                    ? found.Name
                    : reference.Path;

                dependencies.Add(new ComponentDependency(project.Name, target, DependencyKind.ProjectReference));
            }

            dependencies.AddRange(project.AssemblyReferences
                .Where(reference => reference.Kind == ReferenceKind.LocalBinary)
                .Select(reference => new ComponentDependency(
                    project.Name, reference.HintPath!, DependencyKind.LocalBinaryReference)));

            dependencies.AddRange(project.Packages
                .Select(package => new ComponentDependency(
                    project.Name, $"{package.Id} {package.Version}", DependencyKind.NuGetPackageReference)));
        }

        return dependencies
            .OrderBy(dependency => dependency.From, StringComparer.Ordinal)
            .ThenBy(dependency => dependency.Kind)
            .ThenBy(dependency => dependency.To, StringComparer.Ordinal)
            .ToList();
    }

    private static string ReadCommitSha(string root)
    {
        try
        {
            var info = new ProcessStartInfo("git", "rev-parse HEAD")
            {
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(info)!;
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();

            return process.ExitCode == 0 ? output : "unknown";
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return "unknown";
        }
    }
}
