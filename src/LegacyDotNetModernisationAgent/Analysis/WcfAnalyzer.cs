using System.Text.RegularExpressions;
using LegacyDotNetModernisationAgent.Analysis.Facts;
using LegacyDotNetModernisationAgent.Models;

namespace LegacyDotNetModernisationAgent.Analysis;

public sealed record WcfAnalysisResult(
    WcfAssessment Assessment,
    IReadOnlyList<LegacyTechnologyFinding> Findings);

/// <summary>
/// Detects WCF contracts, service implementations, .svc hosts, configured endpoints and
/// code-created clients (ChannelFactory). Connects contract -> implementation -> host.
/// </summary>
public sealed partial class WcfAnalyzer
{
    public const string Wcf = "Wcf";

    [GeneratedRegex(@"<%@\s*ServiceHost\b[^%]*\bService\s*=\s*""(?<service>[^""]*)""", RegexOptions.IgnoreCase)]
    private static partial Regex ServiceHostDirective();

    // VB writes GetType(X); C# writes typeof(X).
    [GeneratedRegex(@"^(?:GetType|typeof)\((?<type>.+)\)$", RegexOptions.IgnoreCase)]
    private static partial Regex TypeOfText();

    public WcfAnalysisResult Analyze(
        string repositoryRoot,
        IReadOnlyList<DotNetProjectAssessment> projects,
        IReadOnlyList<SourceFacts> sources,
        IReadOnlyList<WebConfigModel> configs)
    {
        var findings = new List<LegacyTechnologyFinding>();
        var contracts = new List<WcfContractInfo>();
        var services = new List<WcfServiceInfo>();
        var dataContracts = new List<string>();
        var clients = new List<WcfClientUsage>();

        foreach (var source in sources)
        {
            foreach (var contract in source.Types.Where(type => type.Kind == "Interface" && HasAttribute(type.Attributes, "ServiceContract")))
            {
                var operations = contract.Methods
                    .Where(method => HasAttribute(method.Attributes, "OperationContract"))
                    .Select(method => new WcfOperationInfo(method.Name, FaultTypes(method.Attributes)))
                    .OrderBy(operation => operation.Name, StringComparer.Ordinal)
                    .ToList();

                contracts.Add(new WcfContractInfo(contract.Name, operations, source.Path, contract.Line));
                findings.Add(new LegacyTechnologyFinding(Wcf, "ServiceContract", source.Path, contract.Line, contract.Name));

                foreach (var operation in operations)
                {
                    findings.Add(new LegacyTechnologyFinding(Wcf, $"OperationContract {operation.Name}", source.Path, contract.Line, contract.Name));
                }
            }

            dataContracts.AddRange(source.Types
                .Where(type => type.Kind == "Class" && HasAttribute(type.Attributes, "DataContract"))
                .Select(type => type.Name));
        }

        var contractNames = contracts.Select(contract => contract.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var source in sources)
        {
            foreach (var type in source.Types.Where(type => type.Kind == "Class"))
            {
                var implemented = type.Bases
                    .Where(baseType => baseType.Kind == "Implements" && contractNames.Contains(baseType.SimpleName))
                    .Select(baseType => baseType.SimpleName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Order(StringComparer.Ordinal)
                    .ToList();

                if (implemented.Count == 0)
                {
                    continue;
                }

                services.Add(new WcfServiceInfo(type.Name, implemented, source.Path, type.Line));
                findings.Add(new LegacyTechnologyFinding(Wcf, $"Implements {string.Join(", ", implemented)}", source.Path, type.Line, type.Name));
            }

            foreach (var creation in source.Creations.Where(creation => string.Equals(creation.TypeName, "ChannelFactory", StringComparison.OrdinalIgnoreCase)))
            {
                // Same method if there is one; otherwise just the creation's own line.
                bool InScope(int line, string? type, string? method) =>
                    creation.Method is not null ? type == creation.Type && method == creation.Method : line == creation.Line;

                var binding = source.Creations
                    .Where(other => InScope(other.Line, other.Type, other.Method))
                    .Select(other => other.TypeName)
                    .FirstOrDefault(typeName => typeName.EndsWith("Binding", StringComparison.OrdinalIgnoreCase));

                var addressKey = source.Invocations
                    .Where(call => InScope(call.Line, call.Type, call.Method)
                                   && string.Equals(call.CalleeText, "ConfigurationManager.AppSettings", StringComparison.OrdinalIgnoreCase)
                                   && call.FirstString is not null)
                    .Select(call => call.FirstString)
                    .FirstOrDefault();

                var contract = creation.TypeArgument ?? "?";
                var className = creation.Type ?? "?";

                clients.Add(new WcfClientUsage(contract, binding, addressKey, className, source.Path, creation.Line));
                findings.Add(new LegacyTechnologyFinding(
                    Wcf,
                    $"ChannelFactory{(creation.TypeArgumentDisplay.Length > 0 ? creation.TypeArgumentDisplay : "(Of ?)")} binding={binding ?? "?"} address=appSettings[{addressKey ?? "?"}]",
                    source.Path,
                    creation.Line,
                    className));
            }
        }

        var svcFiles = new List<WcfSvcFileInfo>();

        foreach (var file in projects.SelectMany(project => project.Files.WebFiles)
                     .Where(file => file.EndsWith(".svc", StringComparison.OrdinalIgnoreCase))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .Order(StringComparer.Ordinal))
        {
            var match = ServiceHostDirective().Match(File.ReadAllText(Path.Combine(repositoryRoot, file)));
            var service = match.Success ? match.Groups["service"].Value : null;

            svcFiles.Add(new WcfSvcFileInfo(file, service));
            findings.Add(new LegacyTechnologyFinding(Wcf, $"ServiceHost Service={service ?? "?"}", file, 1));
        }

        var endpoints = configs
            .SelectMany(config => config.Endpoints)
            .OrderBy(endpoint => endpoint.Source, StringComparer.Ordinal)
            .ThenBy(endpoint => endpoint.Contract, StringComparer.Ordinal)
            .ToList();

        foreach (var endpoint in endpoints)
        {
            findings.Add(new LegacyTechnologyFinding(
                Wcf,
                $"{endpoint.Role} endpoint binding={endpoint.Binding} contract={endpoint.Contract}",
                endpoint.Source,
                null,
                endpoint.ServiceName));
        }

        var assessment = new WcfAssessment(
            contracts.OrderBy(contract => contract.Name, StringComparer.Ordinal).ToList(),
            services.OrderBy(service => service.Name, StringComparer.Ordinal).ToList(),
            svcFiles,
            endpoints,
            clients.OrderBy(client => client.Location, StringComparer.Ordinal).ToList(),
            dataContracts.Order(StringComparer.Ordinal).ToList());

        return new WcfAnalysisResult(assessment, findings);
    }

    private static bool HasAttribute(IReadOnlyList<AttributeFact> attributes, string name) =>
        attributes.Any(attribute => string.Equals(attribute.Name, name, StringComparison.OrdinalIgnoreCase)
                                 || string.Equals(attribute.Name, name + "Attribute", StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<string> FaultTypes(IReadOnlyList<AttributeFact> attributes) =>
        attributes
            .Where(attribute => string.Equals(attribute.Name, "FaultContract", StringComparison.OrdinalIgnoreCase))
            .Select(attribute => attribute.Arguments.FirstOrDefault())
            .Where(argument => argument is not null)
            .Select(argument => TypeOfText().Match(argument!))
            .Where(match => match.Success)
            .Select(match => match.Groups["type"].Value)
            .Order(StringComparer.Ordinal)
            .ToList();
}
