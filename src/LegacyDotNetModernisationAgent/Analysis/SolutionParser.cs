using System.Text.RegularExpressions;

namespace LegacyDotNetModernisationAgent.Analysis;

public sealed record SolutionProjectEntry(
    string Name,
    string RelativePath,
    string ProjectTypeGuid,
    string ProjectGuid);

public static partial class SolutionParser
{
    private const string SolutionFolderTypeGuid = "2150E333-8FDC-42A3-9474-1A3956D46DE8";

    [GeneratedRegex(
        "^Project\\(\"\\{(?<type>[^}]+)\\}\"\\)\\s*=\\s*\"(?<name>[^\"]*)\",\\s*\"(?<path>[^\"]*)\",\\s*\"\\{(?<guid>[^}]+)\\}\"",
        RegexOptions.Multiline)]
    private static partial Regex ProjectLine();

    public static IReadOnlyList<SolutionProjectEntry> Parse(string solutionPath)
    {
        var text = File.ReadAllText(solutionPath);

        return ProjectLine()
            .Matches(text)
            .Select(match => new SolutionProjectEntry(
                match.Groups["name"].Value,
                match.Groups["path"].Value.Replace('\\', '/'),
                match.Groups["type"].Value.ToUpperInvariant(),
                match.Groups["guid"].Value.ToUpperInvariant()))
            .Where(entry => entry.ProjectTypeGuid != SolutionFolderTypeGuid)
            .ToList();
    }
}
