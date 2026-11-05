using LegacyDotNetModernisationAgent.Models;
using static LegacyDotNetModernisationAgent.Models.MigrationCharacteristic;

namespace LegacyDotNetModernisationAgent.Analysis;

/// <summary>
/// Deterministic migration policy. Each rule fires on detected facts and records its ID, reason and evidence.
/// It never says a technology is "bad"; it says what kind of change the component needs and which decisions
/// belong to a person. The Strategy Agent explains this output; it cannot change it.
/// </summary>
public sealed class MigrationCharacteristicPolicy
{
    private static readonly MigrationCharacteristic[] BoundCharacteristics =
        [RequiresFrameworkReplacement, RequiresArchitecturalReplacement, RequiresHumanDecision];

    private static readonly string[] ArchitecturalFrameworkAssemblies = ["System.Runtime.Remoting", "System.EnterpriseServices"];

    private sealed class Collector
    {
        public Dictionary<string, List<(MigrationCharacteristic[] Set, PolicyRuleHit Hit)>> Hits { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> Dispositions { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, (string Title, SortedSet<string> Nodes, SortedSet<string> Rules)> Gates { get; } = new(StringComparer.Ordinal);

        public void Hit(string nodeId, string ruleId, string reason, string evidence, string? gateId, string? gateTitle, params MigrationCharacteristic[] set)
        {
            if (!Hits.TryGetValue(nodeId, out var list))
            {
                Hits[nodeId] = list = [];
            }

            if (list.Any(item => item.Hit.RuleId == ruleId))
            {
                return;
            }

            list.Add((set, new PolicyRuleHit(ruleId, reason, evidence)));

            if (gateId is not null)
            {
                if (!Gates.TryGetValue(gateId, out var gate))
                {
                    Gates[gateId] = gate = (gateTitle!, new SortedSet<string>(StringComparer.Ordinal), new SortedSet<string>(StringComparer.Ordinal));
                }

                gate.Nodes.Add(nodeId);
                gate.Rules.Add(ruleId);
            }
        }
    }

    public MigrationAnalysis Evaluate(LegacyDotNetProjectAssessment assessment, LegacyDependencyGraph graph)
    {
        var collector = new Collector();
        var edgesFrom = graph.Edges.ToLookup(edge => edge.From, StringComparer.Ordinal);
        var nodes = graph.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var declared = assessment.DeclaredTypes.ToDictionary(type => type.Id, StringComparer.Ordinal);

        foreach (var node in graph.Nodes)
        {
            switch (node.Kind)
            {
                case "Page" or "UserControl" or "MasterPage":
                    {
                        collector.Hit(node.Id, "R-WF-PAGE",
                            "Web Forms page model (server-side controls and page lifecycle) has no equivalent in modern .NET",
                            node.Detail ?? node.Location ?? node.Name,
                            "D-UI-TARGET", "Decide the UI target for the Web Forms pages",
                            RequiresFrameworkReplacement);

                        var controls = assessment.Technologies
                            .Where(f => f.Technology == WebFormsAnalyzer.ServerControl && f.Location == node.Location)
                            .Select(f => f.Evidence)
                            .ToList();

                        if (controls.Count > 0)
                        {
                            collector.Hit(node.Id, "R-WF-CONTROLS", "Server controls render and bind on the server and need UI replacement",
                                string.Join(", ", controls), null, null, RequiresFrameworkReplacement);
                        }

                        if (node.Technologies.Contains(WebFormsAnalyzer.PageLifecycle))
                        {
                            var lifecycle = assessment.Technologies
                                .Where(f => f.Technology == WebFormsAnalyzer.PageLifecycle && node.Detail is not null && node.Detail.Contains(f.Location, StringComparison.OrdinalIgnoreCase))
                                .Select(f => f.Evidence).Distinct().Order(StringComparer.Ordinal).ToList();

                            collector.Hit(node.Id, "R-WF-LIFECYCLE", "Initial-request versus postback behaviour must be kept (B-PAGE-001)",
                                string.Join(", ", lifecycle), null, null, RequiresFrameworkReplacement, RequiresBehaviourPreservation);
                        }

                        break;
                    }

                case "GlobalAsax":
                    collector.Hit(node.Id, "R-WF-GLOBAL", "Global.asax application events belong to the System.Web host", node.Location ?? node.Name, null, null, RequiresFrameworkReplacement);
                    break;

                case "SessionState":
                    collector.Hit(node.Id, "R-SESSION", "Server-side Session state needs a state-management redesign",
                        node.Detail ?? "Session usage detected", "D-STATE-MANAGEMENT", "Decide how session state is handled after the UI replatform",
                        RequiresArchitecturalReplacement, RequiresBehaviourPreservation);
                    break;

                case "FrameworkAssembly":
                    collector.Hit(node.Id, "R-FRAMEWORK-ONLY", "Assembly exists only in .NET Framework; remove or replace after boundaries move",
                        node.Detail ?? node.Name, null, null,
                        ArchitecturalFrameworkAssemblies.Contains(node.Name, StringComparer.OrdinalIgnoreCase)
                            ? RequiresArchitecturalReplacement : RequiresFrameworkReplacement);
                    break;

                case "Assembly" when node.Name.StartsWith("Csla", StringComparison.OrdinalIgnoreCase):
                    collector.Hit(node.Id, "R-CSLA-LIB", "Supplied CSLA binary is a legacy line; target version is a decision, not a mechanical upgrade",
                        node.Detail ?? node.Name, "D-CSLA-TARGET", "Decide the CSLA target (retain, upgrade or replace)",
                        RequiresLibraryUpgrade, RequiresHumanDecision);
                    break;

                case "CslaBusinessObject":
                    collector.Hit(node.Id, "R-CSLA-BO", "Business object derives from the legacy CSLA base class",
                        "Inherits Csla.ReadOnlyBase", "D-CSLA-TARGET", "Decide the CSLA target (retain, upgrade or replace)",
                        RequiresLibraryUpgrade, RequiresHumanDecision);
                    break;

                case "WcfContract":
                    collector.Hit(node.Id, "R-WCF-CONTRACT", "Service-contract behaviour must be separated from the WCF implementation technology",
                        node.Location ?? node.Name, "D-WCF-TARGET", "Decide the service boundary technology",
                        RequiresHumanDecision, RequiresBehaviourPreservation);
                    break;

                case "WcfService" or "WcfClient" or "WcfHost":
                    collector.Hit(node.Id, "R-WCF-BOUNDARY", "Part of the WCF service boundary; its migration depends on the service decision",
                        node.Location ?? node.Name, "D-WCF-TARGET", "Decide the service boundary technology",
                        RequiresHumanDecision);
                    break;

                case "CrossCutting":
                    CrossCuttingRule(collector, node);
                    break;

                case "Integration":
                    IntegrationRule(collector, node);
                    break;

                case "AuthenticationMechanism":
                    AuthenticationRule(collector, node);
                    break;

                case "HttpModule" or "HttpHandler" or "MessageHandler":
                    collector.Hit(node.Id, "R-HTTP-PIPELINE", "A custom HTTP module, handler or message handler is a Web Forms-era pipeline extension; it needs its own decision (middleware or endpoint handler)",
                        node.Detail ?? node.Location ?? node.Name, "D-HTTP-PIPELINE", "Decide how custom HTTP modules and handlers move to the target pipeline",
                        RequiresArchitecturalReplacement, RequiresHumanDecision);
                    break;

                case "StoredProcedureContract":
                    collector.Hit(node.Id, "R-SP-CONTRACT", "Stored-procedure contract is preserved unless deliberately redesigned",
                        node.Detail ?? node.Name, "D-DATA-CONTRACT", "Agree whether any data-contract change is acceptable",
                        RequiresBehaviourPreservation);
                    collector.Dispositions[node.Id] = "Preserve unless deliberately redesigned";
                    break;

                case "Fixture":
                    collector.Dispositions[node.Id] = "Test infrastructure only";
                    break;

                case "Dal":
                    collector.Hit(node.Id, "R-DAL-CONTRACT", "DAL implements the stored-procedure contract; its behaviour is what must survive",
                        node.Location ?? node.Name, null, null, RequiresBehaviourPreservation);
                    break;

                case "DataContract":
                    collector.Hit(node.Id, "R-DATA-CONTRACT-TYPE", "Data-contract semantics (CustomerId, CustomerRecord) must be preserved",
                        node.Location ?? node.Name, null, null, RequiresBehaviourPreservation, RequiresRecompile);
                    break;
            }

            // Cross-cutting rules, driven by edges and detected technologies.
            if (node.Kind != "Page" && edgesFrom[node.Id].Any(edge => edge.Type == EdgeType.UsesSession))
            {
                collector.Hit(node.Id, "R-SESSION-USER", "Code reads or writes Session",
                    string.Join("; ", edgesFrom[node.Id].Where(e => e.Type == EdgeType.UsesSession).Select(e => e.Evidence)),
                    "D-STATE-MANAGEMENT", "Decide how session state is handled after the UI replatform",
                    RequiresArchitecturalReplacement, RequiresBehaviourPreservation);
            }
            else if (node.Kind == "Page" && edgesFrom[node.Id].Any(edge => edge.Type == EdgeType.UsesSession))
            {
                collector.Hit(node.Id, "R-SESSION-USER", "Page reads or writes Session",
                    string.Join("; ", edgesFrom[node.Id].Where(e => e.Type == EdgeType.UsesSession).Select(e => e.Evidence)),
                    "D-STATE-MANAGEMENT", "Decide how session state is handled after the UI replatform",
                    RequiresArchitecturalReplacement, RequiresBehaviourPreservation);
            }

            if (node.Kind == "Assembly")
            {
                var frameworkOnly = edgesFrom[node.Id].Where(edge => edge.To.StartsWith("fw:", StringComparison.Ordinal)).Select(edge => edge.To[3..]).Order(StringComparer.Ordinal).ToList();

                if (frameworkOnly.Count > 0)
                {
                    collector.Hit(node.Id, "R-ASSEMBLY-FRAMEWORK-DEPS", "Binary references assemblies that exist only in .NET Framework",
                        string.Join(", ", frameworkOnly), null, null, RequiresFrameworkReplacement);
                }
            }
            else if (node.Layer != "Presentation" && node.Kind is not ("FrameworkAssembly" or "LibraryType")
                && edgesFrom[node.Id].Any(edge => edge.To == "fw:System.Web"))
            {
                collector.Hit(node.Id, "R-SYSTEMWEB-IN-LOGIC", "Non-UI code depends on System.Web (HttpContext); it cannot move until decoupled",
                    string.Join("; ", edgesFrom[node.Id].Where(e => e.To == "fw:System.Web").Select(e => e.Evidence)),
                    null, null, RequiresFrameworkReplacement);
            }

            var usedAuthentication = edgesFrom[node.Id].Where(edge => edge.Type == EdgeType.UsesAuthentication).Select(edge => edge.To[5..]).Distinct().Order(StringComparer.Ordinal).ToList();

            if (usedAuthentication.Count > 0)
            {
                collector.Hit(node.Id, "R-AUTH-USER", "Code depends on authentication or authorization; its outcomes per role must be preserved and its target decided",
                    "uses " + string.Join(", ", usedAuthentication), "D-AUTH-TARGET", AuthGateTitle, RequiresBehaviourPreservation, RequiresHumanDecision);
            }

            if (node.Technologies.Contains(CslaAnalyzer.CslaDataPortal) || node.Id == "lib:Csla.DataPortal")
            {
                collector.Hit(node.Id, "R-DATAPORTAL", "Distributed business-object semantics (DataPortal) need an architectural assessment",
                    string.Join("; ", edgesFrom[node.Id].Where(e => e.To == "lib:Csla.DataPortal").Select(e => e.Evidence).DefaultIfEmpty("Csla.DataPortal")),
                    "D-DATAPORTAL-TARGET", "Decide what replaces or retains the DataPortal semantics",
                    RequiresArchitecturalReplacement, RequiresHumanDecision);
            }

            if (declared.TryGetValue(node.Id, out var type) && type.Imports.Contains("System.Configuration", StringComparer.OrdinalIgnoreCase))
            {
                collector.Hit(node.Id, "R-CONFIG-API", "System.Configuration needs the ConfigurationManager package in modern .NET",
                    "Imports System.Configuration", null, null, RequiresLibraryUpgrade);
            }
        }

        // A technology the tool cannot analyse is never "portable": it goes to a person.
        var unassessed = assessment.Coverage.Gaps.Where(gap => gap.Category == "UnanalysedTechnology").ToList();

        if (unassessed.Count > 0)
        {
            collector.Gates["D-UNASSESSED-TECH"] = (
                "Assess technologies the tool cannot yet analyse",
                new SortedSet<string>(unassessed.Select(gap => $"project:{gap.Location} ({gap.What})"), StringComparer.Ordinal),
                new SortedSet<string>(["R-UNASSESSED-TECH"], StringComparer.Ordinal));
        }

        // Anything left without a bound characteristic is plain VB source that at least needs a recompile.
        foreach (var node in graph.Nodes.Where(n => n.Kind is "Class" or "Interface" or "Module" or "DataContract" or "Dal"))
        {
            if (!collector.Hits.TryGetValue(node.Id, out var hits) || hits.All(h => !h.Set.Intersect(BoundCharacteristics).Any()))
            {
                collector.Hit(node.Id, "R-RECOMPILE", "VB source with no detected framework coupling; retargeting needs at least a recompile",
                    node.Location ?? node.Name, null, null, RequiresRecompile);
            }
        }

        var bound = collector.Hits
            .Where(item => item.Value.Any(hit => hit.Set.Intersect(BoundCharacteristics).Any()))
            .Select(item => item.Key)
            .ToHashSet(StringComparer.Ordinal);

        var (portable, coupled) = Portability(graph, nodes, bound);

        // Code in a project that uses technologies we cannot analyse is not "potentially portable": we cannot know.
        var unassessedProjects = assessment.Projects
            .Where(project => unassessed.Any(gap => gap.Location == project.Path))
            .Select(project => project.Name)
            .ToHashSet(StringComparer.Ordinal);

        portable.RemoveWhere(id => nodes[id].Project is not null && unassessedProjects.Contains(nodes[id].Project!));
        coupled.RemoveWhere(id => nodes[id].Project is not null && unassessedProjects.Contains(nodes[id].Project!));

        var components = graph.Nodes
            .Where(node => node.Kind != "ConfigurationSetting" && (collector.Hits.ContainsKey(node.Id) || collector.Dispositions.ContainsKey(node.Id)))
            .Select(node =>
            {
                var hits = collector.Hits.GetValueOrDefault(node.Id, []);
                var set = hits.SelectMany(hit => hit.Set).Distinct().Order().ToList();

                var disposition = collector.Dispositions.GetValueOrDefault(node.Id)
                    ?? (portable.Contains(node.Id) ? "PotentiallyPortable"
                        : coupled.Contains(node.Id) ? "CoupledToFrameworkBoundComponent"
                        : node.Project is not null && unassessedProjects.Contains(node.Project) && !bound.Contains(node.Id)
                            ? "Unassessed: project uses technologies the tool cannot analyse"
                        : null);

                return new ComponentMigrationAssessment(node.Id, node.Kind, set, disposition, hits.Select(hit => hit.Hit).ToList());
            })
            .ToList();

        return new MigrationAnalysis(
            components,
            collector.Gates
                .OrderBy(gate => gate.Key, StringComparer.Ordinal)
                .Select(gate => new DecisionGate(gate.Key, gate.Value.Title, gate.Value.Nodes.ToList(), gate.Value.Rules.ToList()))
                .ToList(),
            portable.Order(StringComparer.Ordinal).ToList(),
            coupled.Order(StringComparer.Ordinal).ToList());
    }

    private static void CrossCuttingRule(Collector collector, GraphNode node)
    {
        var detail = node.Detail ?? node.Name;

        switch (node.Name)
        {
            case CrossCuttingAnalyzer.Logging:
                collector.Hit(node.Id, "R-XCUT-LOGGING", "Logging and monitoring must keep reaching the same places (files, event log, APM); the target logging approach is a decision",
                    detail, "D-OBSERVABILITY", "Decide the logging and monitoring approach", RequiresLibraryUpgrade, RequiresBehaviourPreservation, RequiresHumanDecision);
                break;
            case CrossCuttingAnalyzer.ErrorHandling:
                collector.Hit(node.Id, "R-XCUT-ERRORS", "Application-level error handling hooks into the System.Web pipeline; what users and operators see on failure must be preserved",
                    detail, "D-ERROR-HANDLING", "Decide how errors are handled, reported and shown", RequiresFrameworkReplacement, RequiresBehaviourPreservation, RequiresHumanDecision);
                break;
            default:
                collector.Hit(node.Id, "R-XCUT-CACHING", "In-process cache or application state is lost or duplicated when the app runs on more than one instance or restarts; a shared-state decision is needed",
                    detail, "D-CACHING", "Decide how cached and shared in-process state is handled", RequiresArchitecturalReplacement, RequiresBehaviourPreservation, RequiresHumanDecision);
                break;
        }
    }

    private static void IntegrationRule(Collector collector, GraphNode node)
    {
        var detail = node.Detail ?? node.Name;

        switch (node.Name)
        {
            case IntegrationAnalyzer.ScheduledWork:
                collector.Hit(node.Id, "R-INTEGRATION-SCHEDULED", "Background, timer or Windows-service work depends on how the application is hosted; where it runs after migration is a decision",
                    detail, "D-INTEGRATION-HOSTING", "Decide where background and scheduled work runs", RequiresArchitecturalReplacement, RequiresHumanDecision);
                break;
            case IntegrationAnalyzer.Messaging:
                collector.Hit(node.Id, "R-INTEGRATION-MESSAGING", "MSMQ is Windows-only and System.Messaging is not in modern .NET; the messaging technology is a decision",
                    detail, "D-INTEGRATION-MESSAGING", "Decide the messaging technology", RequiresArchitecturalReplacement, RequiresHumanDecision);
                break;
            case IntegrationAnalyzer.ComInterop:
                collector.Hit(node.Id, "R-INTEGRATION-NATIVE", "COM and native calls tie the code to Windows and to installed components; availability and bitness must be established",
                    detail, "D-INTEGRATION-NATIVE", "Decide how COM and native dependencies are provided or replaced", RequiresFrameworkReplacement, RequiresHumanDecision);
                break;
            case IntegrationAnalyzer.FileSystem:
                collector.Hit(node.Id, "R-INTEGRATION-FILES", "File access assumes the host file system (paths, shares, permissions); the target storage must be decided",
                    detail, "D-INTEGRATION-FILES", "Decide where files live after migration", RequiresBehaviourPreservation, RequiresHumanDecision);
                break;
            case IntegrationAnalyzer.Transactions:
                collector.Hit(node.Id, "R-INTEGRATION-TRANSACTIONS", "Transaction boundaries (and any distributed transaction) must keep their semantics",
                    detail, "D-TRANSACTION-BOUNDARIES", "Agree the transaction boundaries to preserve", RequiresBehaviourPreservation, RequiresHumanDecision);
                break;
            case IntegrationAnalyzer.Email or IntegrationAnalyzer.ExternalHttp:
                collector.Hit(node.Id, "R-INTEGRATION-OUTBOUND", "Outbound email or HTTP calls exist in modern .NET; endpoints, credentials and network access still need confirming in the target environment",
                    detail, null, null, RequiresBehaviourPreservation);
                break;
            default:
                collector.Hit(node.Id, "R-INTEGRATION-EXTERNAL", "Calls to other systems (FTP, child processes) need their endpoints, credentials and network access confirmed in the target environment",
                    detail, "D-INTEGRATION-EXTERNAL", "Confirm external integrations and how they are reached", RequiresBehaviourPreservation, RequiresHumanDecision);
                break;
        }
    }

    private const string AuthGateTitle = "Decide the authentication and authorization target";

    private static void AuthenticationRule(Collector collector, GraphNode node)
    {
        var detail = node.Detail ?? node.Name;

        switch (node.Name)
        {
            case AuthenticationAnalyzer.Forms:
                collector.Hit(node.Id, "R-AUTH-FORMS", "Forms authentication (System.Web.Security) has no direct equivalent in modern .NET; tickets, cookies and the login flow must be replaced",
                    detail, "D-AUTH-TARGET", AuthGateTitle, RequiresFrameworkReplacement, RequiresBehaviourPreservation, RequiresHumanDecision);
                break;

            case AuthenticationAnalyzer.Membership:
                collector.Hit(node.Id, "R-AUTH-MEMBERSHIP", "Membership and role providers are not part of modern .NET; the provider model needs an architectural replacement",
                    detail, "D-AUTH-TARGET", AuthGateTitle, RequiresArchitecturalReplacement, RequiresBehaviourPreservation, RequiresHumanDecision);
                break;

            case AuthenticationAnalyzer.Windows or AuthenticationAnalyzer.IisAuthentication or AuthenticationAnalyzer.Impersonation:
                collector.Hit(node.Id, "R-AUTH-WINDOWS", "Windows or IIS-hosted identity ties authentication to the hosting platform; the target hosting and identity flow need a decision",
                    detail, "D-AUTH-TARGET", AuthGateTitle, RequiresBehaviourPreservation, RequiresHumanDecision);
                break;

            case AuthenticationAnalyzer.AspNetIdentity or AuthenticationAnalyzer.Claims or AuthenticationAnalyzer.ExternalIdentity or AuthenticationAnalyzer.DirectoryServices:
                collector.Hit(node.Id, "R-AUTH-IDENTITY-LIB", "Identity library or directory integration; the target version or provider is a decision",
                    detail, "D-AUTH-TARGET", AuthGateTitle, RequiresLibraryUpgrade, RequiresHumanDecision);
                break;

            case AuthenticationAnalyzer.CustomPrincipal:
                collector.Hit(node.Id, "R-AUTH-CUSTOM-PRINCIPAL", "Identity is constructed or propagated by hand (custom principal); this needs an architectural decision, especially across a service boundary",
                    detail, "D-AUTH-TARGET", AuthGateTitle, RequiresArchitecturalReplacement, RequiresHumanDecision);
                break;

            case AuthenticationAnalyzer.SharedSecret:
                collector.Hit(node.Id, "R-AUTH-SHARED-SECRET", "Credential-like settings are read in code (shared secret or trusted-subsystem pattern); service-to-service trust needs a security design decision",
                    detail, "D-AUTH-TARGET", AuthGateTitle, RequiresHumanDecision);
                break;

            case AuthenticationAnalyzer.WcfSecurity:
                collector.Hit(node.Id, "R-AUTH-WCF-SECURITY", "WCF binding or credential security follows the service-boundary decision",
                    detail, "D-WCF-TARGET", "Decide the service boundary technology", RequiresHumanDecision);
                break;

            case AuthenticationAnalyzer.MachineKey:
                collector.Hit(node.Id, "R-AUTH-MACHINEKEY", "A configured machine key lets applications decrypt each other tickets; ticket compatibility must be considered when authentication moves",
                    detail, "D-AUTH-TARGET", AuthGateTitle, RequiresHumanDecision);
                break;

            case AuthenticationAnalyzer.CustomPipeline:
                collector.Hit(node.Id, "R-AUTH-PIPELINE", "Custom code authenticates or authorizes requests inside the HTTP pipeline; it needs an architectural decision (middleware or the target identity system)",
                    detail, "D-AUTH-TARGET", AuthGateTitle, RequiresArchitecturalReplacement, RequiresHumanDecision);
                break;

            case AuthenticationAnalyzer.NoAuthentication:
                collector.Hit(node.Id, "R-AUTH-NONE", "Configured with no authentication; confirm this relies on a trusted caller and decide how the target secures it",
                    detail, "D-AUTH-TARGET", AuthGateTitle, RequiresHumanDecision);
                break;

            default:
                collector.Hit(node.Id, "R-AUTH-RULES", "Declared access rules or identity checks; outcomes per role must be preserved",
                    detail, null, null, RequiresBehaviourPreservation);
                break;
        }
    }

    /// <summary>
    /// A component is potentially portable when neither it nor anything it depends on carries a
    /// framework-replacement, architectural-replacement or human-decision characteristic.
    /// </summary>
    private static (HashSet<string> Portable, HashSet<string> Coupled) Portability(
        LegacyDependencyGraph graph,
        Dictionary<string, GraphNode> nodes,
        HashSet<string> bound)
    {
        var dependsOn = graph.Edges
            .Where(edge => edge.Basis != "Convention")
            .GroupBy(edge => edge.From, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(edge => edge.To).ToList(), StringComparer.Ordinal);

        var portable = new HashSet<string>(StringComparer.Ordinal);
        var coupled = new HashSet<string>(StringComparer.Ordinal);

        foreach (var node in graph.Nodes.Where(n => n.Kind is "Class" or "Interface" or "Module" or "DataContract" or "Dal"))
        {
            var seen = new HashSet<string>(StringComparer.Ordinal) { node.Id };
            var queue = new Queue<string>([node.Id]);
            var reachesBound = false;

            while (queue.Count > 0 && !reachesBound)
            {
                foreach (var next in dependsOn.GetValueOrDefault(queue.Dequeue(), []))
                {
                    if (!seen.Add(next))
                    {
                        continue;
                    }

                    reachesBound |= bound.Contains(next);
                    queue.Enqueue(next);
                }
            }

            if (bound.Contains(node.Id))
            {
                continue;
            }

            (reachesBound ? coupled : portable).Add(node.Id);
        }

        return (portable, coupled);
    }
}
