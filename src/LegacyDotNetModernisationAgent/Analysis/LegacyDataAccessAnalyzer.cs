using System.Text.RegularExpressions;
using LegacyDotNetModernisationAgent.Analysis.Facts;
using LegacyDotNetModernisationAgent.Models;

namespace LegacyDotNetModernisationAgent.Analysis;

public sealed record DataAccessAnalysisResult(
    DataAccessAssessment Assessment,
    IReadOnlyList<LegacyTechnologyFinding> Findings);

/// <summary>
/// Detects DAL classes and stored-procedure contracts, and says how strongly each DAL method is tied to a
/// procedure: DirectReference (the code uses the contract constant) or NamingConvention (name match only).
/// A JSON fixture is recorded as a stand-in for the database, never as a persistence architecture.
/// </summary>
public sealed partial class LegacyDataAccessAnalyzer
{
    public const string DataAccess = "DataAccess";

    private static readonly string[] JsonMechanisms = ["DataContractJsonSerializer", "JavaScriptSerializer", "JsonConvert"];

    // schema.object_name (underscore in the object part) or usp_/sp_ prefixed names
    [GeneratedRegex(@"^(?:\[?\w+\]?\.\[?\w*_\w*\]?|(?:usp|sp)_\w+)$")]
    private static partial Regex StoredProcedureName();

    [GeneratedRegex(@"^@\w+$")]
    private static partial Regex ParameterName();

    [GeneratedRegex(@"(Dal|Dao|Repository|DataAccess)$", RegexOptions.IgnoreCase)]
    private static partial Regex DalClassName();

    private sealed record Definition(string Name, string Class, string? Field, string Path, int Line);

    private sealed record DalMethod(string Class, MethodFact Method, string Path);

    public DataAccessAnalysisResult Analyze(
        string repositoryRoot,
        IReadOnlyList<DotNetProjectAssessment> projects,
        IReadOnlyList<SourceFacts> sources,
        IReadOnlyList<WebConfigModel> configs)
    {
        var findings = new List<LegacyTechnologyFinding>();

        // 1. Stored-procedure definitions: schema-qualified string constants inside a class.
        var definitions = new List<Definition>();

        foreach (var source in sources)
        {
            foreach (var literal in source.Literals.Where(l => l.Type is not null && StoredProcedureName().IsMatch(l.Value)))
            {
                if (!definitions.Any(definition => definition.Name == literal.Value))
                {
                    definitions.Add(new Definition(literal.Value, literal.Type!, literal.Declarator, source.Path, literal.Line));
                }
            }
        }

        // 2. DAL classes and their public methods.
        var dalMethods = new List<DalMethod>();
        var dalClasses = new List<DalClassInfo>();
        var definitionClasses = definitions.Select(definition => definition.Class).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var source in sources)
        {
            foreach (var type in source.Types.Where(type => type.Kind == "Class"))
            {
                var inDataAccessFolder = source.Path.Contains("/DataAccess/", StringComparison.OrdinalIgnoreCase);

                if (definitionClasses.Contains(type.Name) || !(DalClassName().IsMatch(type.Name) || inDataAccessFolder))
                {
                    continue;
                }

                dalClasses.Add(new DalClassInfo(type.Name, source.Path, type.Line));
                findings.Add(new LegacyTechnologyFinding(DataAccess, "DAL class", source.Path, type.Line, type.Name));

                dalMethods.AddRange(type.Methods.Where(method => method.IsPublic).Select(method => new DalMethod(type.Name, method, source.Path)));
            }
        }

        // 3. Build each contract with parameters, implementations and call sites.
        var contracts = new List<StoredProcedureContractInfo>();
        var dalMethodNames = dalMethods.Select(method => method.Method.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var definition in definitions.OrderBy(definition => definition.Name, StringComparer.Ordinal))
        {
            var parameters = sources
                .Where(source => source.Path == definition.Path)
                .SelectMany(source => source.Literals)
                .Where(literal => literal.Type == definition.Class && ParameterName().IsMatch(literal.Value))
                .Select(literal => new StoredProcedureParameter(literal.Value, definition.Path, literal.Line))
                .ToList();

            var normalizedObject = Normalize(definition.Name[(definition.Name.LastIndexOf('.') + 1)..]);

            var directReferences = sources
                .SelectMany(source => source.MemberAccesses
                    .Where(member => definition.Field is not null
                        && string.Equals(member.Qualifier, definition.Class, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(member.Member, definition.Field, StringComparison.OrdinalIgnoreCase))
                    .Select(member => (Source: source.Path, Member: member)))
                .ToList();

            var implementations = new List<StoredProcedureImplementation>();

            foreach (var dal in dalMethods)
            {
                var usesContract = directReferences.Any(reference => reference.Source == dal.Path
                    && reference.Member.Line >= dal.Method.StartLine && reference.Member.Line <= dal.Method.EndLine);

                var matchesName = Normalize(dal.Method.Name).Contains(normalizedObject, StringComparison.Ordinal);

                if (!usesContract && !matchesName)
                {
                    continue;
                }

                implementations.Add(new StoredProcedureImplementation(
                    dal.Class,
                    dal.Method.Name,
                    dal.Method.ReturnType,
                    usesContract ? "DirectReference" : "NamingConvention",
                    dal.Path,
                    dal.Method.StartLine));
            }

            contracts.Add(new StoredProcedureContractInfo(
                definition.Name,
                definition.Class,
                definition.Path,
                definition.Line,
                parameters,
                implementations,
                CallSites(sources, dalMethodNames),
                directReferences.Count(reference => !reference.Source.Equals(definition.Path, StringComparison.OrdinalIgnoreCase))));

            findings.Add(new LegacyTechnologyFinding(DataAccess, $"StoredProcedure {definition.Name}", definition.Path, definition.Line, definition.Class));

            foreach (var parameter in parameters)
            {
                findings.Add(new LegacyTechnologyFinding(DataAccess, $"StoredProcedureParameter {parameter.Name}", parameter.Location, parameter.Line, definition.Class));
            }

            foreach (var implementation in implementations)
            {
                findings.Add(new LegacyTechnologyFinding(
                    DataAccess,
                    $"{definition.Name} implemented by {implementation.DalClass}.{implementation.Method} [{implementation.Basis}]",
                    implementation.Location,
                    implementation.Line,
                    implementation.DalClass));
            }
        }

        var fixtures = DetectFixtures(repositoryRoot, projects, sources, configs, dalClasses, findings);

        return new DataAccessAnalysisResult(
            new DataAccessAssessment(dalClasses, contracts, fixtures),
            findings);
    }

    private static string Normalize(string value) =>
        new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    private static List<DalCallSite> CallSites(IReadOnlyList<SourceFacts> sources, IReadOnlySet<string> dalMethodNames)
    {
        return sources
            .SelectMany(source => source.Invocations
                .Where(call => !call.IsIndexer && dalMethodNames.Contains(call.CalleeName) && call.Method is not null)
                .Select(call => new DalCallSite(call.Type ?? "?", call.Method!, call.CalleeName, source.Path, call.Line)))
            .OrderBy(site => site.Location, StringComparer.Ordinal)
            .ThenBy(site => site.Line)
            .ToList();
    }

    private static List<FixtureBackingInfo> DetectFixtures(
        string repositoryRoot,
        IReadOnlyList<DotNetProjectAssessment> projects,
        IReadOnlyList<SourceFacts> sources,
        IReadOnlyList<WebConfigModel> configs,
        IReadOnlyList<DalClassInfo> dalClasses,
        List<LegacyTechnologyFinding> findings)
    {
        var fixtures = new List<FixtureBackingInfo>();

        foreach (var dal in dalClasses)
        {
            var source = sources.First(candidate => candidate.Path == dal.Location);

            var mechanism = source.Creations
                .Where(creation => creation.Type == dal.Name)
                .Select(creation => creation.TypeName)
                .FirstOrDefault(name => JsonMechanisms.Contains(name, StringComparer.OrdinalIgnoreCase));

            if (mechanism is null)
            {
                continue;
            }

            var keys = source.Invocations
                .Where(call => call.Type == dal.Name
                               && string.Equals(call.CalleeText, "ConfigurationManager.AppSettings", StringComparison.OrdinalIgnoreCase)
                               && call.FirstString is not null
                               && call.FirstString.Contains("Fixture", StringComparison.OrdinalIgnoreCase))
                .Select(call => call.FirstString!)
                .Distinct()
                .ToList();

            var files = new SortedSet<string>(StringComparer.Ordinal);

            foreach (var config in configs)
            {
                foreach (var setting in config.AppSettings.Where(setting => keys.Contains(setting.Key)))
                {
                    var relative = Path.Combine(Path.GetDirectoryName(config.Path) ?? "", setting.Value.Replace('\\', Path.DirectorySeparatorChar));

                    if (File.Exists(Path.Combine(repositoryRoot, relative)))
                    {
                        files.Add(relative.Replace('\\', '/'));
                    }
                }
            }

            var project = projects.First(candidate => candidate.Name == source.Project);
            var projectDirectory = Path.GetDirectoryName(project.Path) ?? "";

            foreach (var json in Directory.EnumerateFiles(Path.Combine(repositoryRoot, projectDirectory), "*.json", SearchOption.AllDirectories)
                         .Select(file => Path.GetRelativePath(repositoryRoot, file).Replace('\\', '/'))
                         .Where(file => !file.Contains("/bin/") && !file.Contains("/obj/") && !file.Contains("/.vs/")))
            {
                files.Add(json);
            }

            fixtures.Add(new FixtureBackingInfo(mechanism, keys.FirstOrDefault(), files.ToList(), dal.Location));
            findings.Add(new LegacyTechnologyFinding(
                DataAccess,
                $"Fixture-backed ({mechanism}); stand-in for a physical database, not a persistence architecture",
                dal.Location,
                dal.Line,
                dal.Name));
        }

        return fixtures;
    }
}
