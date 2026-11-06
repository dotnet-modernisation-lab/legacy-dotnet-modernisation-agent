using LegacyDotNetModernisationAgent.Models;

namespace LegacyDotNetModernisationAgent.ServiceContracts;

public sealed record CrossCheckFinding(string Kind, string Item, string Detail);

public sealed record CrossCheck(string Description, string Repository, IReadOnlyList<CrossCheckFinding> Findings, IReadOnlyList<string> NotAnalysed);

/// <summary>
/// Checks a published service description against the service contracts the analysis found in the code. A difference means the
/// description is not the description of this code (an older build, another service, or a renamed contract), which matters before
/// it is used as the baseline for comparing a migrated service.
/// </summary>
public static class ContractCrossCheck
{
    public static CrossCheck Check(ServiceContract description, LegacyDotNetProjectAssessment assessment)
    {
        var findings = new List<CrossCheckFinding>();

        var published = description.Facts
            .Where(fact => fact.Kind == "operation")
            .Select(fact => fact.Key["operation/".Length..])
            .Select(name => (Contract: name[..name.IndexOf('.')], Operation: name[(name.IndexOf('.') + 1)..]))
            .ToList();

        var contracts = assessment.Wcf.Contracts;

        foreach (var contract in contracts.OrderBy(contract => contract.Name, StringComparer.OrdinalIgnoreCase))
        {
            var inDescription = published.Where(item => item.Contract.Equals(contract.Name, StringComparison.OrdinalIgnoreCase)).ToList();

            if (inDescription.Count == 0)
            {
                findings.Add(new CrossCheckFinding("In code only", contract.Name, $"the code declares this contract ({contract.Location}:{contract.Line}) but the description does not publish it. It may not be hosted, or the description may be older, or the contract may be published under another name."));
                continue;
            }

            foreach (var operation in contract.Operations)
            {
                var match = inDescription.FirstOrDefault(item => item.Operation.Equals(operation.Name, StringComparison.OrdinalIgnoreCase));

                if (match == default)
                {
                    findings.Add(new CrossCheckFinding("In code only", $"{contract.Name}.{operation.Name}", "the code declares this operation but the description does not publish it"));
                    continue;
                }

                var faultKey = $"operation/{match.Contract}.{match.Operation}/fault/";
                var publishedFaults = description.Facts
                    .Where(fact => fact.Key.StartsWith(faultKey, StringComparison.Ordinal) && fact.Kind == "element" && fact.Key[faultKey.Length..].Split('/').Length == 2)
                    .Select(fact => fact.Key[faultKey.Length..].Split('/')[1])
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var fault in operation.FaultContracts.Select(FaultName).Where(name => !publishedFaults.Contains(name)))
                {
                    findings.Add(new CrossCheckFinding("In code only", $"{contract.Name}.{operation.Name} fault {fault}", "the code declares this fault but the description does not"));
                }

                foreach (var fault in publishedFaults.Where(name => !operation.FaultContracts.Select(FaultName).Contains(name, StringComparer.OrdinalIgnoreCase)).Order(StringComparer.OrdinalIgnoreCase))
                {
                    findings.Add(new CrossCheckFinding("In description only", $"{contract.Name}.{operation.Name} fault {fault}", "the description declares this fault but the code does not"));
                }
            }

            foreach (var item in inDescription.Where(item => !contract.Operations.Any(operation => operation.Name.Equals(item.Operation, StringComparison.OrdinalIgnoreCase))))
            {
                findings.Add(new CrossCheckFinding("In description only", $"{contract.Name}.{item.Operation}", "the description publishes this operation but the code does not declare it"));
            }
        }

        foreach (var contractName in published.Select(item => item.Contract).Distinct(StringComparer.OrdinalIgnoreCase).Where(name => !contracts.Any(contract => contract.Name.Equals(name, StringComparison.OrdinalIgnoreCase))).Order(StringComparer.OrdinalIgnoreCase))
        {
            findings.Add(new CrossCheckFinding("In description only", contractName, "the description publishes this contract but no contract of that name was found in the code. It may belong to another service, or the code names the contract differently."));
        }

        var notes = description.NotAnalysed.ToList();

        notes.Add("Only operation names and declared faults are checked. Message shapes are not compared with the code's data contracts, and a contract renamed with a Name argument shows up as an 'in code only' and an 'in description only' pair.");

        return new CrossCheck(description.Source, assessment.RepositoryName, findings.OrderBy(finding => finding.Kind, StringComparer.Ordinal).ThenBy(finding => finding.Item, StringComparer.OrdinalIgnoreCase).ToList(), notes);
    }

    /// <summary>A fault contract in code is a type, often written FaultException(Of X) or X; the description names the element after the type.</summary>
    private static string FaultName(string fault)
    {
        var open = fault.IndexOf('<');
        var name = open >= 0 ? fault[(open + 1)..].TrimEnd('>') : fault;
        var dot = name.LastIndexOf('.');

        return dot >= 0 ? name[(dot + 1)..] : name;
    }
}
