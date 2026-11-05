using System.Text.RegularExpressions;
using LegacyDotNetModernisationAgent.Models;

namespace LegacyDotNetModernisationAgent.Analysis;

/// <summary>
/// Turns detected facts into a relationship graph. Every edge carries its basis and evidence;
/// nothing is inferred from a name unless the basis says so (NamingConvention, Convention).
/// </summary>
public sealed partial class ArchitecturalGraphBuilder
{
    public static readonly string[] DomainLayers = ["Presentation", "BusinessObjects", "ServiceBoundary", "DataAccess", "Persistence"];

    // Assemblies that exist only in .NET Framework (or are not part of modern .NET).
    private static readonly string[] LegacyFrameworkAssemblies =
        ["System.Web", "System.Web.Services", "System.Runtime.Remoting", "System.EnterpriseServices",
         "System.Windows.Forms", "System.Design", "System.Transactions"];

    // DataPortal.Fetch(Of T) in VB.NET, DataPortal.Fetch<T> in C#
    [GeneratedRegex(@"^DataPortal\.(?<op>\w+)(?:\(Of (?<type>[\w.]+)\)|<(?<type>[\w.]+)>)$")]
    private static partial Regex DataPortalCall();

    [GeneratedRegex(@"^Inherits (?:[\w.]*\.)?(?<name>\w+)")]
    private static partial Regex InheritsEvidence();

    private sealed class NodeBuilder(string id, string name, string kind, string layer)
    {
        public string Id { get; } = id;
        public string Name { get; } = name;
        public string Kind { get; set; } = kind;
        public string Layer { get; set; } = layer;
        public string? Project { get; init; }
        public string? Location { get; init; }
        public int? Line { get; init; }
        public string? Detail { get; set; }
        public SortedSet<string> Technologies { get; } = new(StringComparer.Ordinal);
    }

    public LegacyDependencyGraph Build(LegacyDotNetProjectAssessment assessment)
    {
        var nodes = new Dictionary<string, NodeBuilder>(StringComparer.Ordinal);
        var edges = new List<GraphEdge>();
        var pageOfType = new Dictionary<string, string>(StringComparer.Ordinal);
        var findings = assessment.Technologies;

        // ---- Presentation nodes from markup directives ------------------------------------
        var directive = findings.Where(f => f.Evidence.StartsWith("<%@", StringComparison.Ordinal)
            && f.Technology is WebFormsAnalyzer.WebForms or WebFormsAnalyzer.MasterPage or WebFormsAnalyzer.GlobalAsax).ToList();

        foreach (var finding in directive)
        {
            var (prefix, kind, layer) = finding.Evidence switch
            {
                "<%@ Page %>" => ("page", "Page", "Presentation"),
                "<%@ Control %>" => ("control", "UserControl", "Presentation"),
                "<%@ Master %>" => ("master", "MasterPage", "Presentation"),
                "<%@ Application %>" => ("global", "GlobalAsax", "Hosting"),
                _ => ("", "", "")
            };

            if (prefix.Length == 0)
            {
                continue;
            }

            var project = assessment.Projects.FirstOrDefault(p => p.Files.WebFiles.Contains(finding.Location, StringComparer.OrdinalIgnoreCase));
            var node = new NodeBuilder($"{prefix}:{finding.Location}", Path.GetFileName(finding.Location), kind, layer)
            {
                Project = project?.Name,
                Location = finding.Location,
                Line = finding.Line
            };

            nodes[node.Id] = node;

            if (finding.Subject is not null && project is not null)
            {
                var simple = finding.Subject.Split('.').Last();
                var codeBehind = assessment.DeclaredTypes.FirstOrDefault(t => t.Project == project.Name && t.Name.Equals(simple, StringComparison.OrdinalIgnoreCase));

                if (codeBehind is not null)
                {
                    pageOfType[codeBehind.Id] = node.Id;
                    node.Detail = $"code-behind {codeBehind.Name} ({codeBehind.Location})";
                    TagFindings(node, findings, codeBehind.Location, codeBehind.Name);
                }
            }

            TagFindings(node, findings, finding.Location, null);
        }

        // ---- Class / interface nodes -----------------------------------------------------
        var definingClasses = assessment.DataAccess.StoredProcedures.Select(sp => sp.DefinedIn).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var nodeOfType = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var type in assessment.DeclaredTypes)
        {
            if (pageOfType.TryGetValue(type.Id, out var pageId))
            {
                nodeOfType[type.Id] = pageId;
                continue;
            }

            if (definingClasses.Contains(type.Name))
            {
                continue; // evidence for the stored-procedure node, not a component of its own
            }

            var (kind, layer) = Classify(assessment, type);
            var node = new NodeBuilder(type.Id, type.Name, kind, layer) { Project = type.Project, Location = type.Location, Line = type.Line };

            TagFindings(node, findings, type.Location, type.Name);
            nodes[node.Id] = node;
            nodeOfType[type.Id] = node.Id;
        }

        // ---- Persistence, hosting, configuration, framework and library nodes -------------
        foreach (var procedure in assessment.DataAccess.StoredProcedures)
        {
            nodes[$"sp:{procedure.Name}"] = new NodeBuilder($"sp:{procedure.Name}", procedure.Name, "StoredProcedureContract", "Persistence")
            {
                Location = procedure.Location,
                Line = procedure.Line,
                Detail = "parameters: " + string.Join(", ", procedure.Parameters.Select(p => p.Name))
            };
        }

        foreach (var file in assessment.DataAccess.Fixtures.SelectMany(f => f.FixtureFiles).Distinct(StringComparer.Ordinal))
        {
            nodes[$"fixture:{file}"] = new NodeBuilder($"fixture:{file}", Path.GetFileName(file), "Fixture", "Persistence")
            {
                Location = file,
                Detail = "stand-in for the physical database; not a persistence architecture"
            };
        }

        foreach (var svc in assessment.Wcf.SvcFiles)
        {
            nodes[$"svc:{svc.Path}"] = new NodeBuilder($"svc:{svc.Path}", Path.GetFileName(svc.Path), "WcfHost", "ServiceBoundary")
            {
                Location = svc.Path,
                Line = 1,
                Detail = svc.Service is null ? null : $"Service={svc.Service}"
            };
        }

        foreach (var key in findings.Where(f => f.Technology == ConfigurationReadAnalyzer.ConfigurationRead)
                     .Select(f => Regex.Match(f.Evidence, @"\[(?<key>[^\]]+)\]").Groups["key"].Value).Distinct(StringComparer.Ordinal))
        {
            nodes[$"config:{key}"] = new NodeBuilder($"config:{key}", key, "ConfigurationSetting", "Infrastructure");
        }

        var sessionFinding = findings.FirstOrDefault(f => f.Technology == WebFormsAnalyzer.Session && f.Evidence.StartsWith("sessionState", StringComparison.Ordinal));

        if (findings.Any(f => f.Technology == WebFormsAnalyzer.Session))
        {
            nodes["state:Session"] = new NodeBuilder("state:Session", "ASP.NET Session", "SessionState", "Presentation")
            {
                Location = sessionFinding?.Location,
                Detail = sessionFinding?.Evidence
            };
        }

        if (findings.Any(f => f.Technology == WebFormsAnalyzer.SystemWeb && f.Subject is not null))
        {
            nodes["fw:System.Web"] = new NodeBuilder("fw:System.Web", "System.Web", "FrameworkAssembly", "Infrastructure")
            {
                Detail = ".NET Framework only; not available in modern .NET"
            };
        }

        foreach (var mechanism in assessment.Authentication.Mechanisms)
        {
            var mechanismId = $"auth:{mechanism.Kind}";

            nodes[mechanismId] = new NodeBuilder(mechanismId, mechanism.Kind, "AuthenticationMechanism", "Infrastructure")
            {
                Detail = $"{mechanism.Summary} ({mechanism.EvidenceCount} use{(mechanism.EvidenceCount == 1 ? "" : "s")})"
            };
        }

        foreach (var binary in assessment.Assemblies)
        {
            var assemblyId = $"assembly:{Path.GetFileName(binary.Path)}";

            nodes[assemblyId] = new NodeBuilder(assemblyId, Path.GetFileName(binary.Path), "Assembly", "Infrastructure")
            {
                Location = binary.Path,
                Detail = $"{binary.Name} {binary.Version}, CLR {binary.ClrMetadataVersion}, token {binary.PublicKeyToken ?? "none"}"
            };

            foreach (var type in binary.TypesUsedBySource)
            {
                var libId = $"lib:{LibName(type.FullName)}";

                nodes[libId] = new NodeBuilder(libId, LibName(type.FullName), "LibraryType", "Infrastructure")
                {
                    Detail = $"public type of {binary.Name} {binary.Version}"
                };

                edges.Add(new GraphEdge(libId, assemblyId, EdgeType.References, "AssemblyMetadata", $"public type in {binary.Name} {binary.Version}", binary.Path, null));
            }

            foreach (var reference in binary.ReferencedAssemblies.Where(r => LegacyFrameworkAssemblies.Contains(r.Name, StringComparer.OrdinalIgnoreCase)))
            {
                var frameworkId = $"fw:{reference.Name}";

                if (!nodes.ContainsKey(frameworkId))
                {
                    nodes[frameworkId] = new NodeBuilder(frameworkId, reference.Name, "FrameworkAssembly", "Infrastructure")
                    {
                        Detail = ".NET Framework only; not available in modern .NET"
                    };
                }

                edges.Add(new GraphEdge(assemblyId, frameworkId, EdgeType.References, "AssemblyMetadata", $"{binary.Name} references {reference.Name} {reference.Version}", binary.Path, null));
            }
        }

        // ---- Edges from detected facts ----------------------------------------------------
        string? NodeOfDeclared(string? name, string? location)
        {
            var exact = assessment.DeclaredTypes
                .Where(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && t.Location.Equals(location, StringComparison.OrdinalIgnoreCase))
                .Select(t => nodeOfType.GetValueOrDefault(t.Id))
                .FirstOrDefault(id => id is not null);

            if (exact is not null || location is null)
            {
                return exact;
            }

            // The evidence may sit in another part of a partial type (a code-behind and its designer, or two partial files).
            var project = assessment.Projects.FirstOrDefault(p => p.Files.SourceFiles.Contains(location, StringComparer.OrdinalIgnoreCase))?.Name;

            return assessment.DeclaredTypes
                .Where(t => t.Project == project && t.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                .Select(t => nodeOfType.GetValueOrDefault(t.Id))
                .FirstOrDefault(id => id is not null);
        }

        string? ResolveByName(string name, string? fromProject, params string[] kinds)
        {
            var matches = nodes.Values
                .Where(n => n.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && kinds.Contains(n.Kind))
                .ToList();

            return matches.FirstOrDefault(n => n.Project == fromProject)?.Id ?? (matches.Count == 1 ? matches[0].Id : null);
        }

        foreach (var dependency in assessment.TypeDependencies)
        {
            if (!nodeOfType.TryGetValue(dependency.From, out var from) || !nodeOfType.TryGetValue(dependency.To, out var to) || from == to)
            {
                continue;
            }

            var (fromNode, toNode) = (nodes[from], nodes[to]);

            var type = dependency.Kind switch
            {
                "Implements" => toNode.Kind == "WcfContract" ? EdgeType.ImplementsContract : EdgeType.Inherits,
                "Inherits" => EdgeType.Inherits,
                _ when fromNode.Kind == "WcfClient" && toNode.Kind == "WcfContract" => EdgeType.Invokes,
                _ when fromNode.Kind == "WcfService" && toNode.Kind is "CslaBusinessObject" or "Dal" => EdgeType.DelegatesTo,
                _ when toNode.Kind == "Dal" && fromNode.Kind != "Dal" => EdgeType.DelegatesTo,
                _ => EdgeType.Uses
            };

            edges.Add(new GraphEdge(from, to, type, "TypeReference", $"{fromNode.Name} refers to {toNode.Name}", dependency.Location, dependency.Line));
        }

        foreach (var finding in findings.Where(f => f.Technology == CslaAnalyzer.Csla && f.Evidence.StartsWith("Inherits ", StringComparison.Ordinal)))
        {
            var match = InheritsEvidence().Match(finding.Evidence);
            var libId = $"lib:Csla.{match.Groups["name"].Value}";
            var from = NodeOfDeclared(finding.Subject, finding.Location);

            if (from is not null && match.Success && nodes.ContainsKey(libId))
            {
                edges.Add(new GraphEdge(from, libId, EdgeType.Inherits, "SourceDeclaration", finding.Evidence, finding.Location, finding.Line));
            }
        }

        foreach (var finding in findings.Where(f => f.Technology == CslaAnalyzer.CslaDataPortal))
        {
            var from = NodeOfDeclared(finding.Subject, finding.Location);
            var call = DataPortalCall().Match(finding.Evidence);

            if (from is null || !call.Success || !nodes.ContainsKey("lib:Csla.DataPortal"))
            {
                continue;
            }

            edges.Add(new GraphEdge(from, "lib:Csla.DataPortal", EdgeType.Uses, "SourceUsage", finding.Evidence, finding.Location, finding.Line));

            var target = ResolveByName(call.Groups["type"].Value, nodes[from].Project, "CslaBusinessObject");
            var handler = $"DataPortal_{call.Groups["op"].Value}";

            if (target is not null && findings.Any(f => f.Technology == CslaAnalyzer.CslaDataPortal && f.Evidence == handler && f.Subject == nodes[target].Name))
            {
                edges.Add(new GraphEdge("lib:Csla.DataPortal", target, EdgeType.Invokes, "Convention",
                    $"{finding.Evidence} dispatches to {nodes[target].Name}.{handler}", nodes[target].Location, nodes[target].Line));
            }
        }

        foreach (var client in assessment.Wcf.Clients)
        {
            var from = NodeOfDeclared(client.Class, client.Location);
            var contract = ResolveByName(client.Contract, from is null ? null : nodes[from].Project, "WcfContract");

            if (from is not null && contract is not null)
            {
                edges.Add(new GraphEdge(from, contract, EdgeType.Invokes, "ChannelFactory",
                    $"ChannelFactory(Of {client.Contract}) binding={client.Binding ?? "?"} address=appSettings[{client.AddressSettingKey ?? "?"}]", client.Location, client.Line));
            }
        }

        foreach (var service in assessment.Wcf.Services)
        {
            var from = NodeOfDeclared(service.Name, service.Location);

            foreach (var contractName in service.ImplementedContracts)
            {
                var contract = ResolveByName(contractName, from is null ? null : nodes[from].Project, "WcfContract");

                if (from is not null && contract is not null)
                {
                    edges.Add(new GraphEdge(from, contract, EdgeType.ImplementsContract, "Declared", $"{service.Name} Implements {contractName}", service.Location, service.Line));
                }
            }
        }

        foreach (var svc in assessment.Wcf.SvcFiles.Where(s => s.Service is not null))
        {
            var service = ResolveByName(svc.Service!.Split('.').Last(), null, "WcfService");

            if (service is not null)
            {
                edges.Add(new GraphEdge($"svc:{svc.Path}", service, EdgeType.Hosts, "SvcDirective", $"<%@ ServiceHost Service=\"{svc.Service}\" %>", svc.Path, 1));
            }
        }

        foreach (var finding in findings.Where(f => f.Technology == ConfigurationReadAnalyzer.ConfigurationRead))
        {
            var from = NodeOfDeclared(finding.Subject, finding.Location);
            var key = Regex.Match(finding.Evidence, @"\[(?<key>[^\]]+)\]").Groups["key"].Value;

            if (from is not null)
            {
                edges.Add(new GraphEdge(from, $"config:{key}", EdgeType.ReadsConfiguration, "SourceUsage", finding.Evidence, finding.Location, finding.Line));
            }
        }

        foreach (var finding in findings.Where(f => f.Technology == WebFormsAnalyzer.Session && f.Subject is not null))
        {
            var from = NodeOfDeclared(finding.Subject, finding.Location);

            if (from is not null)
            {
                edges.Add(new GraphEdge(from, "state:Session", EdgeType.UsesSession, "SourceUsage", finding.Evidence, finding.Location, finding.Line));
            }
        }

        foreach (var finding in findings.Where(f => f.Technology is AuthenticationAnalyzer.Authentication or AuthenticationAnalyzer.Authorization && f.Subject is not null))
        {
            var from = NodeOfDeclared(finding.Subject, finding.Location);
            var mechanismId = $"auth:{finding.Evidence.Split(':')[0]}";

            if (from is not null && nodes.ContainsKey(mechanismId))
            {
                edges.Add(new GraphEdge(from, mechanismId, EdgeType.UsesAuthentication, "SourceUsage", finding.Evidence, finding.Location, finding.Line));
            }
        }

        foreach (var finding in findings.Where(f => f.Technology == WebFormsAnalyzer.SystemWeb && f.Subject is not null))
        {
            var from = NodeOfDeclared(finding.Subject, finding.Location);

            if (from is not null)
            {
                edges.Add(new GraphEdge(from, "fw:System.Web", EdgeType.Uses, "SourceUsage", finding.Evidence, finding.Location, finding.Line));
            }
        }

        foreach (var finding in findings.Where(f => f.Technology == WebFormsAnalyzer.MasterPage && f.Evidence.StartsWith("MasterPageFile=", StringComparison.Ordinal)))
        {
            var page = nodes.Values.FirstOrDefault(n => n.Location == finding.Location && n.Kind is "Page" or "UserControl");
            var masterFile = Path.GetFileName(finding.Evidence["MasterPageFile=".Length..]);
            var master = nodes.Values.FirstOrDefault(n => n.Kind == "MasterPage" && n.Project == page?.Project && n.Name.Equals(masterFile, StringComparison.OrdinalIgnoreCase));

            if (page is not null && master is not null)
            {
                edges.Add(new GraphEdge(page.Id, master.Id, EdgeType.Uses, "MasterPageFile", finding.Evidence, finding.Location, finding.Line));
            }
        }

        foreach (var procedure in assessment.DataAccess.StoredProcedures)
        {
            foreach (var implementation in procedure.Implementations)
            {
                var from = NodeOfDeclared(implementation.DalClass, implementation.Location);

                if (from is not null)
                {
                    edges.Add(new GraphEdge(from, $"sp:{procedure.Name}", EdgeType.ImplementsContract, implementation.Basis,
                        $"{implementation.DalClass}.{implementation.Method} -> {procedure.Name} [{implementation.Basis}]", implementation.Location, implementation.Line));
                }
            }
        }

        foreach (var fixture in assessment.DataAccess.Fixtures)
        {
            var dal = assessment.DataAccess.DalClasses.FirstOrDefault(d => d.Location == fixture.Location);
            var from = dal is null ? null : NodeOfDeclared(dal.Name, dal.Location);

            foreach (var file in fixture.FixtureFiles.Where(file => from is not null))
            {
                edges.Add(new GraphEdge(from!, $"fixture:{file}", EdgeType.BackedBy, "FixtureFile", $"{fixture.Mechanism} reads a JSON fixture", fixture.Location, null));
            }
        }

        var finalEdges = Consolidate(edges.Where(e => nodes.ContainsKey(e.From) && nodes.ContainsKey(e.To)).ToList());

        var finalNodes = nodes.Values
            .OrderBy(n => n.Id, StringComparer.Ordinal)
            .Select(n => new GraphNode(n.Id, n.Name, n.Kind, n.Layer, n.Project, n.Location, n.Line, n.Detail, n.Technologies.ToList()))
            .ToList();

        var byId = finalNodes.ToDictionary(n => n.Id, StringComparer.Ordinal);

        return new LegacyDependencyGraph(
            finalNodes,
            finalEdges,
            Boundaries(finalEdges, byId),
            Impact(finalNodes, finalEdges),
            Paths(finalNodes, finalEdges, byId));
    }

    private static string LibName(string fullName)
    {
        var generic = fullName.IndexOf('<');

        return generic < 0 ? fullName : fullName[..generic];
    }

    private static void TagFindings(NodeBuilder node, IReadOnlyList<LegacyTechnologyFinding> findings, string location, string? subject)
    {
        foreach (var finding in findings.Where(f => f.Location.Equals(location, StringComparison.OrdinalIgnoreCase)
                     && (subject is null || string.Equals(f.Subject, subject, StringComparison.OrdinalIgnoreCase))
                     && f.Technology != ConfigurationReadAnalyzer.ConfigurationRead))
        {
            node.Technologies.Add(finding.Technology);
        }
    }

    private static (string Kind, string Layer) Classify(LegacyDotNetProjectAssessment a, DeclaredTypeInfo type)
    {
        if (type.Kind == "Interface" && a.Wcf.Contracts.Any(c => c.Name == type.Name && c.Location == type.Location))
        {
            return ("WcfContract", "ServiceBoundary");
        }

        if (a.Wcf.Services.Any(s => s.Name == type.Name && s.Location == type.Location))
        {
            return ("WcfService", "ServiceBoundary");
        }

        if (a.Wcf.Clients.Any(c => c.Class == type.Name && c.Location == type.Location))
        {
            return ("WcfClient", "ServiceBoundary");
        }

        if (a.Technologies.Any(f => f.Technology == CslaAnalyzer.Csla && f.Evidence.StartsWith("Inherits Csla.", StringComparison.Ordinal)
            && f.Subject == type.Name && f.Location == type.Location))
        {
            return ("CslaBusinessObject", "BusinessObjects");
        }

        if (a.DataAccess.DalClasses.Any(d => d.Name == type.Name && d.Location == type.Location))
        {
            return ("Dal", "DataAccess");
        }

        var pipeline = a.Technologies.FirstOrDefault(f => f.Technology == HttpPipelineAnalyzer.HttpPipeline && f.Subject == type.Name && f.Location == type.Location);

        if (pipeline is not null)
        {
            return (pipeline.Evidence.Split(':')[0], "Hosting");
        }

        if (a.Wcf.DataContracts.Contains(type.Name, StringComparer.OrdinalIgnoreCase))
        {
            return ("DataContract", "Unclassified");
        }

        return (type.Kind, "Unclassified");
    }

    /// <summary>One edge per (from, to, type) with merged evidence; a specific edge replaces a plain Uses between the same nodes.</summary>
    private static List<GraphEdge> Consolidate(List<GraphEdge> edges)
    {
        var merged = edges
            .GroupBy(e => (e.From, e.To, e.Type))
            .Select(group =>
            {
                var first = group.OrderBy(e => e.Line ?? int.MaxValue).First();
                var evidence = string.Join("; ", group.Select(e => e.Evidence).Distinct(StringComparer.Ordinal).Take(6));

                return first with { Evidence = evidence };
            })
            .ToList();

        var specific = merged.Where(e => e.Type != EdgeType.Uses).Select(e => (e.From, e.To)).ToHashSet();

        return merged
            .Where(e => e.Type != EdgeType.Uses || !specific.Contains((e.From, e.To)))
            .OrderBy(e => e.From, StringComparer.Ordinal)
            .ThenBy(e => e.To, StringComparer.Ordinal)
            .ThenBy(e => e.Type)
            .ToList();
    }

    private static List<ArchitecturalBoundary> Boundaries(List<GraphEdge> edges, Dictionary<string, GraphNode> nodes)
    {
        return edges
            .Where(e => DomainLayers.Contains(nodes[e.From].Layer) && DomainLayers.Contains(nodes[e.To].Layer)
                && nodes[e.From].Layer != nodes[e.To].Layer)
            .Select(e => new ArchitecturalBoundary(e.From, e.To, e.Type, nodes[e.From].Layer, nodes[e.To].Layer))
            .ToList();
    }

    private static List<ImpactEntry> Impact(List<GraphNode> nodes, List<GraphEdge> edges)
    {
        // From depends on To. Convention edges (DataPortal dispatch) are runtime routing, not static dependency.
        var dependents = edges
            .Where(e => e.Basis != "Convention")
            .GroupBy(e => e.To)
            .ToDictionary(group => group.Key, group => group.Select(e => e.From).ToList(), StringComparer.Ordinal);

        // A contract's behaviour is its implementation's behaviour: callers of the contract depend on the service at runtime.
        var kindOf = nodes.ToDictionary(n => n.Id, n => n.Kind, StringComparer.Ordinal);

        foreach (var implementation in edges.Where(e => e.Type == EdgeType.ImplementsContract && kindOf[e.From] == "WcfService"))
        {
            if (!dependents.TryGetValue(implementation.From, out var list))
            {
                dependents[implementation.From] = list = [];
            }

            list.Add(implementation.To);
        }

        var subjects = new[] { "Assembly", "LibraryType", "FrameworkAssembly", "SessionState", "AuthenticationMechanism", "WcfContract", "StoredProcedureContract", "Fixture", "ConfigurationSetting" };
        var kinds = nodes.ToDictionary(n => n.Id, n => n.Kind, StringComparer.Ordinal);

        return nodes
            .Where(n => subjects.Contains(n.Kind))
            .Select(n =>
            {
                var seen = new HashSet<string>(StringComparer.Ordinal) { n.Id };
                var queue = new Queue<string>([n.Id]);

                while (queue.Count > 0)
                {
                    foreach (var dependent in dependents.GetValueOrDefault(queue.Dequeue(), []))
                    {
                        if (seen.Add(dependent))
                        {
                            queue.Enqueue(dependent);
                        }
                    }
                }

                seen.Remove(n.Id);
                var affected = seen.Order(StringComparer.Ordinal).ToList();

                return new ImpactEntry(n.Id, affected, affected.Where(id => kinds[id] == "Page").ToList());
            })
            .ToList();
    }

    private static List<RepresentativePath> Paths(List<GraphNode> nodes, List<GraphEdge> edges, Dictionary<string, GraphNode> byId)
    {
        // Flow follows runtime direction: a service implementing a contract is entered *from* the contract.
        var flow = new Dictionary<string, List<(string To, GraphEdge Edge, bool Forward)>>(StringComparer.Ordinal);

        void Add(string from, string to, GraphEdge edge, bool forward)
        {
            if (!flow.TryGetValue(from, out var list))
            {
                flow[from] = list = [];
            }

            list.Add((to, edge, forward));
        }

        foreach (var edge in edges.Where(e => DomainLayers.Contains(byId[e.From].Layer) && DomainLayers.Contains(byId[e.To].Layer)))
        {
            if (edge.Type == EdgeType.ImplementsContract && byId[edge.From].Kind == "WcfService")
            {
                Add(edge.To, edge.From, edge, false);
            }
            else
            {
                Add(edge.From, edge.To, edge, true);
            }
        }

        var paths = new List<RepresentativePath>();

        foreach (var page in nodes.Where(n => n.Kind == "Page"))
        {
            foreach (var target in nodes.Where(n => n.Kind == "StoredProcedureContract"))
            {
                var previous = new Dictionary<string, (string From, GraphEdge Edge, bool Forward)>(StringComparer.Ordinal);
                var queue = new Queue<string>([page.Id]);
                var seen = new HashSet<string>(StringComparer.Ordinal) { page.Id };

                while (queue.Count > 0 && !seen.Contains(target.Id))
                {
                    var current = queue.Dequeue();

                    foreach (var (to, edge, forward) in flow.GetValueOrDefault(current, []).OrderBy(step => step.To, StringComparer.Ordinal))
                    {
                        if (seen.Add(to))
                        {
                            previous[to] = (current, edge, forward);
                            queue.Enqueue(to);
                        }
                    }
                }

                if (!seen.Contains(target.Id))
                {
                    continue;
                }

                var chain = new List<(string Node, GraphEdge? Edge, bool Forward)>();

                for (var node = target.Id; node != page.Id; node = previous[node].From)
                {
                    chain.Add((node, previous[node].Edge, previous[node].Forward));
                }

                chain.Reverse();

                var steps = new List<GraphPathStep>();
                var ids = new[] { page.Id }.Concat(chain.Select(c => c.Node)).ToList();

                for (var index = 0; index < ids.Count; index++)
                {
                    var link = index < chain.Count ? chain[index] : default;

                    steps.Add(new GraphPathStep(
                        ids[index],
                        byId[ids[index]].Kind,
                        index < chain.Count ? link.Edge!.Type : null,
                        index >= chain.Count || link.Forward,
                        byId[ids[index]].Technologies));
                }

                paths.Add(new RepresentativePath(page.Id, target.Id, steps));
            }
        }

        return paths;
    }
}
