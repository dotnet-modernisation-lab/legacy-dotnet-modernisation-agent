using LegacyDotNetModernisationAgent.Models;

namespace LegacyDotNetModernisationAgent.Behaviour;

/// <summary>
/// Turns probe observations and the analysis into named behaviour contracts, and compares a later run with a saved baseline.
/// Dynamic contracts record what the app does; derived contracts record facts from the graph.
/// </summary>
public static class BehaviourBaselineBuilder
{
    public const string ProbeName = "http-iisexpress/1";

    public static BehaviourBaseline Build(
        LegacyDotNetProjectAssessment assessment,
        LegacyDependencyGraph graph,
        IReadOnlyDictionary<string, string> observed)
    {
        IReadOnlyDictionary<string, string> Pick(params string[] prefixes) =>
            observed.Where(item => prefixes.Any(prefix => item.Key.StartsWith(prefix, StringComparison.Ordinal)))
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);

        IReadOnlyDictionary<string, string> Only(params string[] keys) =>
            keys.ToDictionary(key => key, key => observed.GetValueOrDefault(key, "(not observed)"), StringComparer.Ordinal);

        IReadOnlyList<string> Nodes(params string[] suffixes) =>
            graph.Nodes.Where(node => suffixes.Any(suffix => node.Id.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))).Select(node => node.Id).Order(StringComparer.Ordinal).ToList();

        var rows1042 = observed.GetValueOrDefault("customer.1042.rows", "");
        var customerNodes = Nodes("DataAccess.aspx", "/CustomerInfo", "/ILegacyCustomerService", "/LegacyCustomerService", "/CustomerData", "/LegacyCustomerDal");

        var contracts = new List<BehaviourContract>
        {
            new("B-CUST-001", "Customer 1042 returns \"Northwind Field Services\"", "Customer", "Dynamic",
                Only("customer.1042.rows", "customer.1042.message"), customerNodes,
                "Observed through Web Forms -> CSLA -> WCF -> DAL as an Admin user."),

            new("B-CUST-002", "Customer 1042 status is \"Active\"", "Customer", "Dynamic",
                Only("customer.1042.rows"), customerNodes,
                $"Grid row for 1042: {rows1042}"),

            new("B-CUST-003", "An unknown customer produces the defined not-found behaviour", "Customer", "Dynamic",
                Only($"customer.{BehaviourProbe.UnknownCustomer}.message", $"customer.{BehaviourProbe.UnknownCustomer}.rows"), customerNodes,
                "As observed, CSLA wraps the service's CustomerNotFoundFault, so the UI shows a generic DataPortal failure. That is the legacy behaviour; improving it is a deliberate change, not preservation."),

            new("B-DATA-001", "Customer retrieval preserves CustomerId input semantics", "Data", "Dynamic",
                Pick("customer.-5.", "customer.0.", "customer.abc.", "customer.default."), customerNodes,
                "Positive integers are accepted; zero, negative and non-numeric values are rejected by the page; no id defaults to 1042."),

            DataContract(assessment, graph),

            new("B-PAGE-001", "Initial request behaviour differs correctly from postback", "Page", "Dynamic",
                Pick("login.initial.", "login.postback.", "customer.1042.postback."), Nodes("Login.aspx", "DataAccess.aspx"),
                "Login shows a prompt on the first request and a validation message on postback. DataAccess loads data only when not a postback."),

            new("B-SESSION-001", "Representative Session-backed behaviour is preserved", "Session", "Dynamic",
                Pick("login.admin.", "session."), Nodes("state:Session", "Login.aspx", "Profile.aspx"),
                "Session values written at sign-in (user, role, last page) are shown on the Profile page."),

            new("B-ACCESS-001", "Anonymous users are redirected to sign in; non-Admin users are denied customer data", "Session", "Dynamic",
                Pick("access."), Nodes("DataAccess.aspx", "state:Session"),
                "Role comes from the forms-authentication ticket and Session."),

            Architecture(graph, observed),
            DeclaredAccess(assessment),
        };

        return new BehaviourBaseline(assessment.RepositoryName, assessment.CommitSha, ProbeName, contracts);
    }

    private static BehaviourContract DeclaredAccess(LegacyDotNetProjectAssessment assessment)
    {
        var observed = new SortedDictionary<string, string>(StringComparer.Ordinal);

        foreach (var mechanism in assessment.Authentication.Mechanisms)
        {
            observed[$"mechanism.{mechanism.Kind}"] = $"{mechanism.Summary} ({mechanism.EvidenceCount} use(s))";
        }

        var number = 0;

        foreach (var rule in assessment.Authentication.AccessRules)
        {
            observed[$"rule.{++number:000}"] = $"{rule.Source}: {rule.Target} {rule.Operation} {rule.Principals}".TrimEnd();
        }

        return new BehaviourContract(
            "B-AUTH-001", "Declared authentication mechanisms and access rules keep the same outcomes per role", "Authentication", "Derived",
            observed,
            [],
            "Derived from analysis: what is declared in code and configuration. It does not prove what the running app enforces; B-ACCESS-001 observes that.");
    }

    private static BehaviourContract DataContract(LegacyDotNetProjectAssessment assessment, LegacyDependencyGraph graph)
    {
        var procedure = assessment.DataAccess.StoredProcedures.FirstOrDefault();
        var observed = new SortedDictionary<string, string>(StringComparer.Ordinal);

        if (procedure is not null)
        {
            observed["storedProcedure.name"] = procedure.Name;
            observed["storedProcedure.parameters"] = string.Join(", ", procedure.Parameters.Select(parameter => parameter.Name));
            observed["storedProcedure.implementedBy"] = string.Join(", ", procedure.Implementations.Select(i => $"{i.DalClass}.{i.Method} [{i.Basis}]"));
            observed["storedProcedure.directReferencesInCode"] = procedure.DirectReferenceCount.ToString();
        }

        return new BehaviourContract(
            "B-DATA-002", "Stored-procedure contract remains dbo.CustomerInfo_SelectById unless explicitly redesigned", "Data", "Derived",
            observed,
            graph.Nodes.Where(node => node.Kind is "StoredProcedureContract" or "Dal").Select(node => node.Id).Order(StringComparer.Ordinal).ToList(),
            "Derived from analysis. The DAL link is by naming convention only: no code references the contract constant, so nothing enforces this contract today.");
    }

    private static BehaviourContract Architecture(LegacyDependencyGraph graph, IReadOnlyDictionary<string, string> observed)
    {
        var byId = graph.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var path = graph.Paths.FirstOrDefault();

        var direct = graph.Edges.Count(edge => byId[edge.From].Layer == "Presentation" && byId[edge.To].Layer is "Persistence" or "DataAccess");

        var values = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["path.kinds"] = path is null ? "(none)" : string.Join(" > ", path.Steps.Select(step => step.Kind)),
            ["presentation.directEdgesToDataAccessOrPersistence"] = direct.ToString(),
            ["boundary.hostStopped.message"] = observed.GetValueOrDefault("boundary.hostStopped.message", "(not observed)"),
            ["boundary.hostStopped.rows"] = observed.GetValueOrDefault("boundary.hostStopped.rows", "(not observed)")
        };

        return new BehaviourContract(
            "B-ARCH-001", "Customer retrieval crosses the business/service boundary rather than the UI reading data sources directly", "Architecture", "Dynamic",
            values,
            path?.Steps.Select(step => step.NodeId).ToList() ?? [],
            "Static: no page has an edge to the DAL or persistence. Dynamic: with the WCF host stopped, retrieval returns no customer, so the page cannot read the fixture itself.");
    }

    /// <summary>Dynamic contracts only: a derived contract changes when the source changes, which is not behaviour drift.</summary>
    public static IReadOnlyList<BehaviourDrift> Compare(BehaviourBaseline baseline, BehaviourBaseline current)
    {
        var drift = new List<BehaviourDrift>();
        var now = current.Contracts.ToDictionary(contract => contract.Id, StringComparer.Ordinal);

        foreach (var contract in baseline.Contracts.Where(contract => contract.Kind == "Dynamic"))
        {
            if (!now.TryGetValue(contract.Id, out var other))
            {
                drift.Add(new BehaviourDrift(contract.Id, "(contract)", "present", "missing"));
                continue;
            }

            foreach (var key in contract.Observed.Keys.Union(other.Observed.Keys).Order(StringComparer.Ordinal))
            {
                var before = contract.Observed.GetValueOrDefault(key);
                var after = other.Observed.GetValueOrDefault(key);

                if (before != after)
                {
                    drift.Add(new BehaviourDrift(contract.Id, key, before, after));
                }
            }
        }

        return drift;
    }
}
