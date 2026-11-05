using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
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

    public DataAccessAnalysisResult Analyze(
        string repositoryRoot,
        IReadOnlyList<DotNetProjectAssessment> projects,
        IReadOnlyList<VbSource> sources,
        IReadOnlyList<WebConfigModel> configs)
    {
        var findings = new List<LegacyTechnologyFinding>();

        // 1. Stored-procedure definitions: schema-qualified string constants.
        var definitions = new List<(string Name, string Class, string? Field, string Path, int Line, ClassBlockSyntax Block)>();

        foreach (var source in sources)
        {
            foreach (var literal in source.Root.DescendantNodes().OfType<LiteralExpressionSyntax>()
                         .Where(node => node.Kind() == SyntaxKind.StringLiteralExpression))
            {
                var value = literal.Token.ValueText;
                var block = literal.Ancestors().OfType<ClassBlockSyntax>().FirstOrDefault();

                if (block is null || !StoredProcedureName().IsMatch(value))
                {
                    continue;
                }

                var field = literal.Ancestors().OfType<VariableDeclaratorSyntax>().FirstOrDefault()?.Names.FirstOrDefault()?.Identifier.Text;

                if (!definitions.Any(definition => definition.Name == value))
                {
                    definitions.Add((value, block.ClassStatement.Identifier.Text, field, source.Path, VbSyntax.Line(literal), block));
                }
            }
        }

        // 2. DAL classes and their public methods.
        var dalMethods = new List<(string Class, MethodBlockSyntax Method, string Path)>();
        var dalClasses = new List<DalClassInfo>();
        var definitionClasses = definitions.Select(definition => definition.Class).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var source in sources)
        {
            foreach (var type in source.Root.DescendantNodes().OfType<ClassBlockSyntax>())
            {
                var name = type.ClassStatement.Identifier.Text;
                var inDataAccessFolder = source.Path.Contains("/DataAccess/", StringComparison.OrdinalIgnoreCase);

                if (definitionClasses.Contains(name) || !(DalClassName().IsMatch(name) || inDataAccessFolder))
                {
                    continue;
                }

                dalClasses.Add(new DalClassInfo(name, source.Path, VbSyntax.Line(type)));
                findings.Add(new LegacyTechnologyFinding(DataAccess, "DAL class", source.Path, VbSyntax.Line(type), name));

                dalMethods.AddRange(type.Members.OfType<MethodBlockSyntax>()
                    .Where(method => method.SubOrFunctionStatement.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PublicKeyword)))
                    .Select(method => (name, method, source.Path)));
            }
        }

        // 3. Build each contract with parameters, implementations and call sites.
        var contracts = new List<StoredProcedureContractInfo>();

        foreach (var definition in definitions.OrderBy(definition => definition.Name, StringComparer.Ordinal))
        {
            var parameters = definition.Block.DescendantNodes().OfType<LiteralExpressionSyntax>()
                .Where(node => node.Kind() == SyntaxKind.StringLiteralExpression && ParameterName().IsMatch(node.Token.ValueText))
                .Select(node => new StoredProcedureParameter(node.Token.ValueText, definition.Path, VbSyntax.Line(node)))
                .ToList();

            var normalizedObject = Normalize(definition.Name[(definition.Name.LastIndexOf('.') + 1)..]);

            var directReferences = sources
                .SelectMany(source => source.Root.DescendantNodes().OfType<MemberAccessExpressionSyntax>()
                    .Where(member => definition.Field is not null
                        && VbSyntax.Is(VbSyntax.SimpleName(member.Expression), definition.Class)
                        && VbSyntax.Is(member.Name.Identifier.Text, definition.Field))
                    .Select(member => (Source: source.Path, Member: member)))
                .ToList();

            var implementations = new List<StoredProcedureImplementation>();

            foreach (var (className, method, path) in dalMethods)
            {
                var methodName = method.SubOrFunctionStatement.Identifier.Text;
                var usesContract = directReferences.Any(reference => reference.Member.Ancestors().Contains(method));
                var matchesName = Normalize(methodName).Contains(normalizedObject, StringComparison.Ordinal);

                if (!usesContract && !matchesName)
                {
                    continue;
                }

                implementations.Add(new StoredProcedureImplementation(
                    className,
                    methodName,
                    ReturnType(method),
                    usesContract ? "DirectReference" : "NamingConvention",
                    path,
                    VbSyntax.Line(method)));
            }

            var callSites = CallSites(sources, dalMethods.Select(method => method.Method.SubOrFunctionStatement.Identifier.Text).ToHashSet(StringComparer.OrdinalIgnoreCase),
                dalMethods.Select(method => method.Method).ToHashSet());

            contracts.Add(new StoredProcedureContractInfo(
                definition.Name,
                definition.Class,
                definition.Path,
                definition.Line,
                parameters,
                implementations,
                callSites,
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

    private static string ReturnType(MethodBlockSyntax method) =>
        (method.SubOrFunctionStatement.AsClause as SimpleAsClauseSyntax)?.Type.ToString().Trim() ?? "(none)";

    private static List<DalCallSite> CallSites(
        IReadOnlyList<VbSource> sources,
        IReadOnlySet<string> dalMethodNames,
        IReadOnlySet<MethodBlockSyntax> dalMethodNodes)
    {
        var callSites = new List<DalCallSite>();

        foreach (var source in sources)
        {
            foreach (var invocation in source.Root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var callee = VbSyntax.SimpleName(invocation.Expression);
                var caller = invocation.Ancestors().OfType<MethodBlockSyntax>().FirstOrDefault();

                if (!dalMethodNames.Contains(callee) || caller is null)
                {
                    continue;
                }

                callSites.Add(new DalCallSite(
                    VbSyntax.EnclosingClass(invocation) ?? "?",
                    caller.SubOrFunctionStatement.Identifier.Text,
                    callee,
                    source.Path,
                    VbSyntax.Line(invocation)));
            }
        }

        return callSites
            .OrderBy(site => site.Location, StringComparer.Ordinal)
            .ThenBy(site => site.Line)
            .ToList();
    }

    private static List<FixtureBackingInfo> DetectFixtures(
        string repositoryRoot,
        IReadOnlyList<DotNetProjectAssessment> projects,
        IReadOnlyList<VbSource> sources,
        IReadOnlyList<WebConfigModel> configs,
        IReadOnlyList<DalClassInfo> dalClasses,
        List<LegacyTechnologyFinding> findings)
    {
        var fixtures = new List<FixtureBackingInfo>();

        foreach (var dal in dalClasses)
        {
            var source = sources.First(candidate => candidate.Path == dal.Location);
            var block = source.Root.DescendantNodes().OfType<ClassBlockSyntax>()
                .First(type => type.ClassStatement.Identifier.Text == dal.Name);

            var mechanism = block.DescendantNodes().OfType<ObjectCreationExpressionSyntax>()
                .Select(creation => VbSyntax.SimpleName(creation.Type))
                .FirstOrDefault(name => JsonMechanisms.Contains(name, StringComparer.OrdinalIgnoreCase));

            if (mechanism is null)
            {
                continue;
            }

            var keys = block.DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Where(invocation => VbSyntax.Is(VbSyntax.Compact(invocation.Expression), "ConfigurationManager.AppSettings"))
                .Select(VbSyntax.FirstStringArgument)
                .Where(key => key is not null && key.Contains("Fixture", StringComparison.OrdinalIgnoreCase))
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
