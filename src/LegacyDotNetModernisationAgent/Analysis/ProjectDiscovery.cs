using System.Xml.Linq;
using LegacyDotNetModernisationAgent.Models;

namespace LegacyDotNetModernisationAgent.Analysis;

public sealed record DiscoveryResult(
    IReadOnlyList<string> ProjectPaths,
    IReadOnlyList<CoverageGap> Gaps);

/// <summary>
/// Finds the projects to analyse without assuming a particular layout.
/// If solutions exist, their projects are analysed and any other project file is reported as not analysed.
/// If none exist, every project file found is analysed. Unsupported project types are always reported.
/// </summary>
public static class ProjectDiscovery
{
    private static readonly string[] ExcludedDirectories = ["bin", "obj", ".git", ".vs", "node_modules", "packages", "TestResults"];
    private static readonly string[] SupportedProjects = [".vbproj", ".csproj"];

    private static readonly string[] UnsupportedProjects =
        [".fsproj", ".vcxproj", ".njsproj", ".sqlproj", ".wixproj", ".dtproj", ".rptproj", ".wapproj", ".shproj"];

    public static DiscoveryResult Discover(string root)
    {
        var files = Walk(root).ToList();
        var gaps = new List<CoverageGap>();

        string Rel(string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

        var found = files
            .Where(file => SupportedProjects.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var file in files.Where(file => UnsupportedProjects.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)))
        {
            gaps.Add(new CoverageGap("UnsupportedProjectType", $"{Path.GetExtension(file)} project is not analysed", Rel(file),
                "Its code, references and technologies are not part of the assessment."));
        }

        var solutions = files
            .Where(file => Path.GetExtension(file).Equals(".sln", StringComparison.OrdinalIgnoreCase)
                        || Path.GetExtension(file).Equals(".slnx", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .ToList();

        if (solutions.Count == 0)
        {
            return new DiscoveryResult(found.Order(StringComparer.Ordinal).ToList(), gaps);
        }

        var inSolution = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var solution in solutions)
        {
            try
            {
                foreach (var entry in SolutionParser.Parse(solution))
                {
                    var path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(solution)!, entry.RelativePath));
                    var extension = Path.GetExtension(path);

                    if (!SupportedProjects.Contains(extension, StringComparer.OrdinalIgnoreCase))
                    {
                        if (!UnsupportedProjects.Contains(extension, StringComparer.OrdinalIgnoreCase) && extension.Length > 0)
                        {
                            gaps.Add(new CoverageGap("UnsupportedProjectType", $"{extension} project referenced by {Path.GetFileName(solution)} is not analysed", Rel(path),
                                "Its code, references and technologies are not part of the assessment."));
                        }

                        continue;
                    }

                    if (File.Exists(path))
                    {
                        inSolution.Add(path);
                    }
                    else
                    {
                        gaps.Add(new CoverageGap("MissingProject", $"{Path.GetFileName(solution)} lists a project that does not exist", Rel(path),
                            "The solution refers to code that is not in the repository."));
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or System.Xml.XmlException)
            {
                gaps.Add(new CoverageGap("SolutionUnreadable", $"Solution could not be read: {exception.Message}", Rel(solution),
                    "Projects it lists are not analysed unless found another way."));
            }
        }

        foreach (var orphan in found.Where(path => !inSolution.Contains(path)).Order(StringComparer.Ordinal))
        {
            gaps.Add(new CoverageGap("ProjectNotInSolution", "Project file is not part of any solution and was not analysed", Rel(orphan),
                "Its code, references and technologies are missing from the assessment."));
        }

        // A solution that yields no readable projects must not hide the projects that do exist.
        var analysed = inSolution.Count > 0 ? inSolution : found.ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new DiscoveryResult(analysed.Order(StringComparer.Ordinal).ToList(), gaps);
    }

    private static IEnumerable<string> Walk(string root)
    {
        var pending = new Stack<string>([root]);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();

            IEnumerable<string> subdirectories;
            IEnumerable<string> entries;

            try
            {
                subdirectories = Directory.EnumerateDirectories(directory).ToList();
                entries = Directory.EnumerateFiles(directory).ToList();
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
            {
                continue;
            }

            foreach (var entry in entries)
            {
                yield return entry;
            }

            foreach (var subdirectory in subdirectories.Where(path => !ExcludedDirectories.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)))
            {
                pending.Push(subdirectory);
            }
        }
    }
}
