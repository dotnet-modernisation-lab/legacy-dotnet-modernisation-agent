using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
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

    [GeneratedRegex(@"^GetType\((?<type>.+)\)$", RegexOptions.IgnoreCase)]
    private static partial Regex GetTypeText();

    public WcfAnalysisResult Analyze(
        string repositoryRoot,
        IReadOnlyList<DotNetProjectAssessment> projects,
        IReadOnlyList<VbSource> sources,
        IReadOnlyList<WebConfigModel> configs)
    {
        var findings = new List<LegacyTechnologyFinding>();
        var contracts = new List<WcfContractInfo>();
        var services = new List<WcfServiceInfo>();
        var dataContracts = new List<string>();
        var clients = new List<WcfClientUsage>();

        foreach (var source in sources)
        {
            foreach (var contract in source.Root.DescendantNodes().OfType<InterfaceBlockSyntax>()
                         .Where(node => HasAttribute(node.InterfaceStatement.AttributeLists, "ServiceContract")))
            {
                var name = contract.InterfaceStatement.Identifier.Text;

                var operations = contract.Members.OfType<MethodStatementSyntax>()
                    .Where(method => HasAttribute(method.AttributeLists, "OperationContract"))
                    .Select(method => new WcfOperationInfo(method.Identifier.Text, FaultTypes(method.AttributeLists)))
                    .OrderBy(operation => operation.Name, StringComparer.Ordinal)
                    .ToList();

                contracts.Add(new WcfContractInfo(name, operations, source.Path, VbSyntax.Line(contract)));
                findings.Add(new LegacyTechnologyFinding(Wcf, "ServiceContract", source.Path, VbSyntax.Line(contract), name));

                foreach (var operation in operations)
                {
                    findings.Add(new LegacyTechnologyFinding(Wcf, $"OperationContract {operation.Name}", source.Path, VbSyntax.Line(contract), name));
                }
            }

            foreach (var type in source.Root.DescendantNodes().OfType<ClassBlockSyntax>())
            {
                if (HasAttribute(type.ClassStatement.AttributeLists, "DataContract"))
                {
                    dataContracts.Add(type.ClassStatement.Identifier.Text);
                }
            }
        }

        var contractNames = contracts.Select(contract => contract.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var source in sources)
        {
            foreach (var type in source.Root.DescendantNodes().OfType<ClassBlockSyntax>())
            {
                var implemented = type.Implements
                    .SelectMany(statement => statement.Types)
                    .Select(VbSyntax.SimpleName)
                    .Where(contractNames.Contains)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Order(StringComparer.Ordinal)
                    .ToList();

                if (implemented.Count == 0)
                {
                    continue;
                }

                var name = type.ClassStatement.Identifier.Text;

                services.Add(new WcfServiceInfo(name, implemented, source.Path, VbSyntax.Line(type)));
                findings.Add(new LegacyTechnologyFinding(Wcf, $"Implements {string.Join(", ", implemented)}", source.Path, VbSyntax.Line(type), name));
            }

            foreach (var creation in source.Root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
            {
                if (!VbSyntax.Is(VbSyntax.SimpleName(creation.Type), "ChannelFactory"))
                {
                    continue;
                }

                var method = creation.Ancestors().OfType<MethodBlockSyntax>().FirstOrDefault();
                var scope = (SyntaxNode?)method ?? creation;

                var binding = scope.DescendantNodes().OfType<ObjectCreationExpressionSyntax>()
                    .Select(node => VbSyntax.SimpleName(node.Type))
                    .FirstOrDefault(typeName => typeName.EndsWith("Binding", StringComparison.OrdinalIgnoreCase));

                var addressKey = scope.DescendantNodes().OfType<InvocationExpressionSyntax>()
                    .Where(invocation => VbSyntax.Is(VbSyntax.Compact(invocation.Expression), "ConfigurationManager.AppSettings"))
                    .Select(VbSyntax.FirstStringArgument)
                    .FirstOrDefault(key => key is not null);

                var contract = VbSyntax.FirstTypeArgument(creation.Type) ?? "?";
                var className = VbSyntax.EnclosingClass(creation) ?? "?";

                clients.Add(new WcfClientUsage(contract, binding, addressKey, className, source.Path, VbSyntax.Line(creation)));
                findings.Add(new LegacyTechnologyFinding(
                    Wcf,
                    $"ChannelFactory(Of {contract}) binding={binding ?? "?"} address=appSettings[{addressKey ?? "?"}]",
                    source.Path,
                    VbSyntax.Line(creation),
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

    private static bool HasAttribute(SyntaxList<AttributeListSyntax> lists, string name) =>
        lists.SelectMany(list => list.Attributes).Any(attribute => VbSyntax.Is(VbSyntax.SimpleName(attribute.Name), name)
            || VbSyntax.Is(VbSyntax.SimpleName(attribute.Name), name + "Attribute"));

    private static IReadOnlyList<string> FaultTypes(SyntaxList<AttributeListSyntax> lists) =>
        lists.SelectMany(list => list.Attributes)
            .Where(attribute => VbSyntax.Is(VbSyntax.SimpleName(attribute.Name), "FaultContract"))
            .Select(attribute => attribute.ArgumentList?.Arguments.FirstOrDefault())
            .OfType<SimpleArgumentSyntax>()
            .Select(argument => GetTypeText().Match(VbSyntax.Compact(argument.Expression)))
            .Where(match => match.Success)
            .Select(match => match.Groups["type"].Value)
            .Order(StringComparer.Ordinal)
            .ToList();
}
