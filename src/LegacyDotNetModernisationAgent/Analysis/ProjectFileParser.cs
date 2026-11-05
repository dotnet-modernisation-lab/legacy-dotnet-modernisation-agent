using System.Xml.Linq;
using LegacyDotNetModernisationAgent.Models;

namespace LegacyDotNetModernisationAgent.Analysis;

/// <summary>
/// Reads an old-style (or SDK-style) .vbproj/.csproj. Matches on XML local names so the
/// MSBuild 2003 namespace does not matter. Deterministic: all lists are sorted.
/// </summary>
public static class ProjectFileParser
{
    private const string WebApplicationGuid = "349C5851-65DF-11DA-9384-00065B846F21";
    private const string WcfGuid = "3D9AD99F-2412-4246-B90B-4EAA41C64699";

    private static readonly string[] WebExtensions =
        [".aspx", ".ascx", ".svc", ".asmx", ".ashx", ".master", ".asax"];

    private static readonly string[] ConfigFileNames =
        ["web.config", "app.config", "packages.config", "web.debug.config", "web.release.config"];

    public static DotNetProjectAssessment Parse(string repositoryRoot, string projectPath)
    {
        var document = XDocument.Load(projectPath);
        var projectDirectory = Path.GetDirectoryName(projectPath)!;
        var root = document.Root!;

        string? Property(string name) =>
            root.Descendants()
                .Where(element => element.Name.LocalName == name)
                .Select(element => element.Value.Trim())
                .FirstOrDefault(value => value.Length > 0);

        var language = Path.GetExtension(projectPath).ToLowerInvariant() switch
        {
            ".vbproj" => ProjectLanguage.VisualBasic,
            ".csproj" => ProjectLanguage.CSharp,
            _ => ProjectLanguage.Unknown
        };

        var (platform, targetFramework) = ResolveFramework(
            Property("TargetFrameworkVersion"),
            Property("TargetFramework"));

        var typeGuids = (Property("ProjectTypeGuids") ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(guid => guid.Trim('{', '}', ' ').ToUpperInvariant())
            .ToList();

        var items = root.Descendants()
            .Where(element => element.Attribute("Include") is not null)
            .ToList();

        var projectReferences = items
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => new ProjectReferenceInfo(
                (string?)element.Elements().FirstOrDefault(child => child.Name.LocalName == "Name") ?? "",
                ToRepositoryPath(repositoryRoot, projectDirectory, (string)element.Attribute("Include")!)))
            .OrderBy(reference => reference.Path, StringComparer.Ordinal)
            .ToList();

        var assemblyReferences = items
            .Where(element => element.Name.LocalName == "Reference")
            .Select(element => ParseAssemblyReference(repositoryRoot, projectDirectory, element))
            .OrderBy(reference => reference.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var projectRelativePath = ToRepositoryPath(repositoryRoot, projectDirectory, Path.GetFileName(projectPath));
        var packages = new List<PackageReferenceInfo>();

        packages.AddRange(items
            .Where(element => element.Name.LocalName == "PackageReference")
            .Select(element => new PackageReferenceInfo(
                (string)element.Attribute("Include")!,
                (string?)element.Attribute("Version")
                    ?? (string?)element.Elements().FirstOrDefault(child => child.Name.LocalName == "Version")
                    ?? "",
                null,
                projectRelativePath)));

        var packagesConfig = Path.Combine(projectDirectory, "packages.config");

        if (File.Exists(packagesConfig))
        {
            packages.AddRange(PackagesConfigParser.Parse(
                packagesConfig,
                ToRepositoryPath(repositoryRoot, projectDirectory, "packages.config")));
        }

        var files = InventoryFiles(repositoryRoot, projectDirectory, items);

        return new DotNetProjectAssessment(
            Path.GetFileNameWithoutExtension(projectPath),
            projectRelativePath,
            Property("AssemblyName"),
            language,
            platform,
            targetFramework,
            ClassifyKind(typeGuids, Property("OutputType"), files),
            root.Attribute("Sdk") is not null,
            projectReferences,
            assemblyReferences,
            packages,
            files);
    }

    private static (DotNetPlatform Platform, string? TargetFramework) ResolveFramework(
        string? frameworkVersion,
        string? targetFramework)
    {
        if (frameworkVersion is not null)
        {
            // "v4.8" -> "net48"
            return (DotNetPlatform.DotNetFramework,
                "net" + frameworkVersion.TrimStart('v', 'V').Replace(".", ""));
        }

        if (targetFramework is not null)
        {
            var platform = targetFramework.StartsWith("net4", StringComparison.OrdinalIgnoreCase)
                ? DotNetPlatform.DotNetFramework
                : DotNetPlatform.DotNetCore;

            return (platform, targetFramework);
        }

        return (DotNetPlatform.Unknown, null);
    }

    private static ProjectKind ClassifyKind(
        IReadOnlyList<string> typeGuids,
        string? outputType,
        ProjectFileInventory files)
    {
        var hasSvc = files.WebFiles.Any(file => file.EndsWith(".svc", StringComparison.OrdinalIgnoreCase));
        var hasPages = files.WebFiles.Any(file => file.EndsWith(".aspx", StringComparison.OrdinalIgnoreCase));

        if (typeGuids.Contains(WcfGuid) || (hasSvc && !hasPages))
        {
            return ProjectKind.WcfServiceHost;
        }

        if (typeGuids.Contains(WebApplicationGuid) || hasPages)
        {
            return ProjectKind.WebFormsApplication;
        }

        return outputType is not null && outputType.Contains("Exe", StringComparison.OrdinalIgnoreCase)
            ? ProjectKind.Executable
            : ProjectKind.ClassLibrary;
    }

    private static AssemblyReferenceInfo ParseAssemblyReference(
        string repositoryRoot,
        string projectDirectory,
        XElement element)
    {
        var include = (string)element.Attribute("Include")!;
        var parts = include.Split(',', StringSplitOptions.TrimEntries);
        var name = parts[0];

        var version = parts
            .Skip(1)
            .Where(part => part.StartsWith("Version=", StringComparison.OrdinalIgnoreCase))
            .Select(part => part["Version=".Length..])
            .FirstOrDefault();

        var hint = element.Elements()
            .FirstOrDefault(child => child.Name.LocalName == "HintPath")?.Value.Trim();

        if (hint is null)
        {
            return new AssemblyReferenceInfo(name, version, ReferenceKind.FrameworkAssembly, null, null);
        }

        var absolute = Path.GetFullPath(
            Path.Combine(projectDirectory, hint.Replace('\\', Path.DirectorySeparatorChar)));

        var kind = hint.Replace('\\', '/').Contains("/packages/", StringComparison.OrdinalIgnoreCase)
            ? ReferenceKind.NuGetPackage
            : ReferenceKind.LocalBinary;

        // A restored package may legitimately be absent before restore; local binaries must exist.
        return new AssemblyReferenceInfo(
            name,
            version,
            kind,
            ToRepositoryPath(repositoryRoot, repositoryRoot, absolute),
            File.Exists(absolute));
    }

    private static ProjectFileInventory InventoryFiles(
        string repositoryRoot,
        string projectDirectory,
        IReadOnlyList<XElement> items)
    {
        var source = items
            .Where(element => element.Name.LocalName == "Compile")
            .Select(element => ToRepositoryPath(repositoryRoot, projectDirectory, (string)element.Attribute("Include")!))
            .ToList();

        var onDisk = Directory
            .EnumerateFiles(projectDirectory, "*", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(projectDirectory, file))
            .ToList();

        // SDK-style projects wildcard their sources; fall back to the files on disk.
        if (source.Count == 0)
        {
            source = onDisk
                .Where(file => Path.GetExtension(file).ToLowerInvariant() is ".vb" or ".cs")
                .Select(file => ToRepositoryPath(repositoryRoot, repositoryRoot, file))
                .ToList();
        }

        List<string> Select(Func<string, bool> predicate) =>
            onDisk
                .Where(predicate)
                .Select(file => ToRepositoryPath(repositoryRoot, repositoryRoot, file))
                .Order(StringComparer.Ordinal)
                .ToList();

        return new ProjectFileInventory(
            source.Order(StringComparer.Ordinal).ToList(),
            Select(file => WebExtensions.Contains(Path.GetExtension(file).ToLowerInvariant())),
            Select(file => ConfigFileNames.Contains(Path.GetFileName(file).ToLowerInvariant())));
    }

    private static bool IsBuildOutput(string projectDirectory, string file)
    {
        var first = Path.GetRelativePath(projectDirectory, file).Replace('\\', '/').Split('/')[0];

        return first.Equals("bin", StringComparison.OrdinalIgnoreCase)
            || first.Equals("obj", StringComparison.OrdinalIgnoreCase)
            || first.Equals(".vs", StringComparison.OrdinalIgnoreCase);
    }

    private static string ToRepositoryPath(string repositoryRoot, string baseDirectory, string path)
    {
        var absolute = Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(baseDirectory, path.Replace('\\', Path.DirectorySeparatorChar)));

        return Path.GetRelativePath(repositoryRoot, absolute).Replace('\\', '/');
    }
}
