using System.Runtime.CompilerServices;
using Microsoft.Build.Definition;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Locator;
using LegacyDotNetModernisationAgent.Models;

namespace LegacyDotNetModernisationAgent.Analysis;

/// <summary>What MSBuild evaluation (no build, no restore) knows about a project.</summary>
public sealed record ProjectEvaluation(
    string? TargetFramework,
    IReadOnlyList<PackageReferenceInfo> Packages);

/// <summary>
/// Evaluates a project with MSBuild so that Directory.Build.props, conditions and central package
/// management are seen. It never builds. Missing imports (for example the Web Application targets that
/// ship with Visual Studio) are ignored. Callers fall back to plain XML parsing when this is unavailable.
/// </summary>
public static class MsBuildEvaluator
{
    private static readonly object Gate = new();
    private static bool? available;

    /// <summary>Registers an installed MSBuild once. False when none can be found.</summary>
    public static bool IsAvailable()
    {
        lock (Gate)
        {
            if (available is null)
            {
                try
                {
                    if (!MSBuildLocator.IsRegistered)
                    {
                        MSBuildLocator.RegisterDefaults();
                    }

                    available = true;
                }
                catch (InvalidOperationException)
                {
                    available = false;
                }
            }

            return available.Value;
        }
    }

    /// <summary>Evaluates one project; returns null and the reason when MSBuild cannot.</summary>
    public static ProjectEvaluation? TryEvaluate(string repositoryRoot, string projectPath, out string? failure)
    {
        try
        {
            failure = null;

            return Evaluate(repositoryRoot, projectPath);
        }
        catch (Exception exception) when (exception.GetType().Namespace == "Microsoft.Build.Exceptions" || exception is IOException)
        {
            // Messages can embed absolute paths; keep artifacts identical across machines.
            failure = exception.Message
                .Replace(projectPath, ToRepositoryPath(repositoryRoot, projectPath), StringComparison.OrdinalIgnoreCase)
                .Replace(Path.GetFullPath(repositoryRoot), ".", StringComparison.OrdinalIgnoreCase)
                .Trim();

            return null;
        }
    }

    // Kept out of line so MSBuild types are not loaded before MSBuildLocator has registered.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ProjectEvaluation Evaluate(string repositoryRoot, string projectPath)
    {
        using var collection = new ProjectCollection();

        var project = Project.FromFile(projectPath, new ProjectOptions
        {
            ProjectCollection = collection,
            LoadSettings = ProjectLoadSettings.IgnoreMissingImports
                | ProjectLoadSettings.IgnoreInvalidImports
                | ProjectLoadSettings.IgnoreEmptyImports
        });

        var projectRelativePath = ToRepositoryPath(repositoryRoot, projectPath);

        // Central package management supplies the version through PackageVersion items.
        var centralVersions = project.GetItems("PackageVersion")
            .GroupBy(item => item.EvaluatedInclude, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().GetMetadataValue("Version"), StringComparer.OrdinalIgnoreCase);

        var packages = project.GetItems("PackageReference")
            .Select(item =>
            {
                var version = item.GetMetadataValue("VersionOverride");

                if (version.Length == 0)
                {
                    version = item.GetMetadataValue("Version");
                }

                if (version.Length == 0)
                {
                    centralVersions.TryGetValue(item.EvaluatedInclude, out var central);
                    version = central ?? "";
                }

                return new PackageReferenceInfo(item.EvaluatedInclude, version, null, projectRelativePath);
            })
            .OrderBy(package => package.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var targetFramework = project.GetPropertyValue("TargetFrameworks");

        if (targetFramework.Length == 0)
        {
            targetFramework = project.GetPropertyValue("TargetFramework");
        }

        return new ProjectEvaluation(targetFramework.Length == 0 ? null : targetFramework, packages);
    }

    private static string ToRepositoryPath(string root, string path) =>
        Path.GetRelativePath(root, Path.GetFullPath(path)).Replace('\\', '/');
}
