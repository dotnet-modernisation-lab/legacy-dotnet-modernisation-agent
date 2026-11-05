namespace LegacyDotNetModernisationAgent.Models;

public enum DotNetPlatform
{
    Unknown,
    DotNetFramework,
    DotNetCore
}

public enum ProjectLanguage
{
    Unknown,
    VisualBasic,
    CSharp
}

public enum ProjectKind
{
    ClassLibrary,
    WebFormsApplication,
    WcfServiceHost,
    Executable
}

public enum ReferenceKind
{
    FrameworkAssembly,
    LocalBinary,
    NuGetPackage
}

public enum DependencyKind
{
    ProjectReference,
    LocalBinaryReference,
    NuGetPackageReference
}

public sealed record AssemblyReferenceInfo(
    string Name,
    string? Version,
    ReferenceKind Kind,
    string? HintPath,
    bool? BinaryExists);

public sealed record PackageReferenceInfo(
    string Id,
    string Version,
    string? TargetFramework,
    string Source);

public sealed record ProjectReferenceInfo(
    string Name,
    string Path);

public sealed record ProjectFileInventory(
    IReadOnlyList<string> SourceFiles,
    IReadOnlyList<string> WebFiles,
    IReadOnlyList<string> ConfigurationFiles);

public sealed record DotNetProjectAssessment(
    string Name,
    string Path,
    string? AssemblyName,
    ProjectLanguage Language,
    DotNetPlatform Platform,
    string? TargetFramework,
    ProjectKind Kind,
    bool SdkStyle,
    IReadOnlyList<ProjectReferenceInfo> ProjectReferences,
    IReadOnlyList<AssemblyReferenceInfo> AssemblyReferences,
    IReadOnlyList<PackageReferenceInfo> Packages,
    ProjectFileInventory Files);

public sealed record LegacyTechnologyFinding(
    string Technology,
    string Evidence,
    string Location);

public sealed record ComponentDependency(
    string From,
    string To,
    DependencyKind Kind);

public sealed record LegacyDotNetProjectAssessment(
    string RepositoryName,
    string CommitSha,
    DotNetPlatform Platform,
    IReadOnlyList<DotNetProjectAssessment> Projects,
    IReadOnlyList<LegacyTechnologyFinding> Technologies,
    IReadOnlyList<ComponentDependency> Dependencies);
