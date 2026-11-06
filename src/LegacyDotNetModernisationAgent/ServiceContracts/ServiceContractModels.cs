namespace LegacyDotNetModernisationAgent.ServiceContracts;

/// <summary>
/// One comparable statement about a SOAP service, such as "operation GetOrder exists" or "GetOrder input /GetOrder/orderId is an
/// xs:int, required". Kind says what sort of statement it is, and Direction says whether a caller sends it (in), receives it (out or fault) or neither.
/// Facts describe what is on the wire, so type names that do not appear in messages are not compared.
/// </summary>
public sealed record ContractFact(string Key, string Kind, string Direction, string Value);

public sealed record ServiceContract(string Source, IReadOnlyList<ContractFact> Facts, IReadOnlyList<string> NotAnalysed);

public enum ChangeClass
{
    Breaking,
    Review,
    Compatible,
    Informational
}

public sealed record ContractChange(ChangeClass Class, string Key, string? Old, string? New, string Reason);

public sealed record ContractDiff(string OldSource, string NewSource, IReadOnlyList<ContractChange> Changes, IReadOnlyList<string> NotAnalysed);
