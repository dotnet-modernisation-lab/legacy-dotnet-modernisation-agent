using System.Diagnostics;
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

        var solutionPath = Directory
            .EnumerateFiles(root, "*.sln", SearchOption.TopDirectoryOnly)
            .Order(StringComparer.Ordinal)
            .FirstOrDefault()
            ?? throw new FileNotFoundException($"No .sln found in {root}");

        var projects = SolutionParser
            .Parse(solutionPath)
            .Select(entry => Path.GetFullPath(Path.Combine(root, entry.RelativePath)))
            .Where(path => path.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase)
                        || path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .Select(path => ProjectFileParser.Parse(root, path))
            .OrderBy(project => project.Path, StringComparer.Ordinal)
            .ToList();

        return new LegacyDotNetProjectAssessment(
            new DirectoryInfo(root).Name,
            ReadCommitSha(root),
            ResolvePlatform(projects),
            projects,
            [],
            BuildDependencies(projects));
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
