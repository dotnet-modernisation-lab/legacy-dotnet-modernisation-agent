using System.Text.RegularExpressions;
using LegacyDotNetModernisationAgent.Analysis.Facts;
using LegacyDotNetModernisationAgent.Models;
using LegacyDotNetModernisationAgent.Strategy;

namespace LegacyDotNetModernisationAgent.Flows;

/// <summary>
/// Builds flows from the facts the analysis already extracts: entry points found from evidence, then the calls followed by name
/// through the method index, with the graph's data, integration, session, configuration and authentication edges read off each
/// step as terminals. Nothing is run. Facts are syntax-only, so a call is followed only when exactly one target is plausible;
/// otherwise the flow says why it stopped.
/// </summary>
public sealed partial class FlowBuilder
{
    private const int MaxDepth = 12;
    private const int MaxListedCandidates = 5;

    private sealed record TypeDef(SourceFacts Source, TypeFact Type)
    {
        public string Key => $"{Source.Project}|{Type.Name}";
    }

    private sealed record Entry(TypeDef Type, MethodFact Method, string Kind, string Evidence, int Rank);

    private sealed record Target(TypeDef Type, MethodFact Method, string Basis);

    /// <summary>Framework conventions that dispatch to a method by naming rule rather than by a visible call. Each entry only restates a documented convention.</summary>
    private static readonly (string Qualifier, string[] Callees, string TargetPrefix, string Label)[] Dispatch =
    [
        ("DataPortal", ["Fetch", "Create", "Update", "Delete", "Execute"], "DataPortal_", "business-object framework data portal")
    ];

    private static readonly string[] ReflectionCallees = ["InvokeMember", "CreateInstance", "DynamicInvoke", "GetMethod", "GetConstructor", "CreateInstanceFrom"];

    [GeneratedRegex(@"^[A-Za-z]\w*_[A-Za-z]\w*$")]
    private static partial Regex ControlEventName();

    [GeneratedRegex(@"^(Application|Session)_\w+$", RegexOptions.IgnoreCase)]
    private static partial Regex GlobalEventName();

    [GeneratedRegex(@"^(DataPortal_|Child_)\w+$")]
    private static partial Regex FrameworkCallbackName();

    [GeneratedRegex(@"(^|\.)(QueryString|Form|Params|Cookies|Headers|ServerVariables|RouteData\.Values)$", RegexOptions.IgnoreCase)]
    private static partial Regex InputAccessor();

    /// <summary>Background and hosted work: a base type and the method the host starts. Each pair only restates how that host works.</summary>
    private static readonly (string Base, string[] Methods)[] HostedBases =
    [
        ("ServiceBase", ["OnStart", "OnStop"]),
        ("BackgroundService", ["ExecuteAsync"]),
        ("IHostedService", ["StartAsync"]),
        ("IJob", ["Execute"])
    ];

    private static readonly string[] HandlerBases = ["IHttpHandler", "IHttpAsyncHandler", "IHttpHandlerFactory", "HttpTaskAsyncHandler", "IHttpModule"];

    private readonly List<TypeDef> types = [];
    private readonly Dictionary<string, List<TypeDef>> typesByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<TypeDef>> typesByKey = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Path, string Type, string Method), List<InvocationFact>> invocations = [];
    private readonly Dictionary<string, List<GraphEdge>> edgesByFile = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> identifiersByType = new(StringComparer.OrdinalIgnoreCase);
    private LegacyDotNetProjectAssessment assessment = null!;
    private UnitMap unitMap = null!;

    public FlowSet Build(string repositoryRoot, LegacyDotNetProjectAssessment assessment, LegacyDependencyGraph graph, UnitMap unitMap)
    {
        this.assessment = assessment;
        this.unitMap = unitMap;

        var sources = SourceFactLoader.Load(repositoryRoot, assessment.Projects);
        var (bindings, markupHandlers) = MarkupWiring.Read(repositoryRoot, assessment.Projects);

        Index(sources, graph);

        var entries = FindEntries(sources, bindings, markupHandlers)
            .GroupBy(entry => (entry.Type.Key, entry.Method.Name.ToLowerInvariant()))
            .Select(group => group.OrderBy(entry => entry.Rank).ThenBy(entry => entry.Kind, StringComparer.Ordinal).First())
            .OrderBy(entry => UnitOf(entry.Type), StringComparer.Ordinal)
            .ThenBy(entry => entry.Type.Source.Project, StringComparer.Ordinal)
            .ThenBy(entry => entry.Type.Type.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Method.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var flows = entries.Select(BuildFlow).ToList();

        var notes = new List<string>
        {
            "Flows are read from the code and followed by name, not run. A call is followed only when one target is plausible; otherwise the flow says why it stopped.",
            "Handlers are found from markup wiring and VB Handles clauses where present, and otherwise by naming convention, which each flow labels.",
            "A flow shows what the code can do, not how often or with which data."
        };

        if (flows.Count == 0)
        {
            notes.Add("No entry points were found. Either the code has none the tool recognises, or the project kinds are not yet covered.");
        }

        return new FlowSet(assessment.RepositoryName, assessment.CommitSha, flows, notes);
    }

    private void Index(IReadOnlyList<SourceFacts> sources, LegacyDependencyGraph graph)
    {
        foreach (var source in sources.OrderBy(source => source.Path, StringComparer.Ordinal))
        {
            foreach (var type in source.Types)
            {
                var definition = new TypeDef(source, type);

                types.Add(definition);
                Add(typesByName, type.Name, definition);
                Add(typesByKey, definition.Key, definition);
            }

            foreach (var call in source.Invocations.Where(call => call.Type is not null && call.Method is not null))
            {
                var key = (source.Path, call.Type!, call.Method!.ToLowerInvariant());

                if (!invocations.TryGetValue(key, out var list))
                {
                    invocations[key] = list = [];
                }

                list.Add(call);
            }

            foreach (var identifier in source.Identifiers.Where(identifier => identifier.Type is not null))
            {
                var key = $"{source.Project}|{identifier.Type}";

                if (!identifiersByType.TryGetValue(key, out var names))
                {
                    identifiersByType[key] = names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }

                names.Add(identifier.Name);
            }
        }

        foreach (var edge in graph.SiteEdges.Where(edge => edge.Location is not null && edge.Line is not null && IsTerminalEdge(edge)))
        {
            Add(edgesByFile, Normalise(edge.Location!), edge);
        }

        static void Add<T>(Dictionary<string, List<T>> dictionary, string key, T value)
        {
            if (!dictionary.TryGetValue(key, out var list))
            {
                dictionary[key] = list = [];
            }

            list.Add(value);
        }
    }

    private static bool IsTerminalEdge(GraphEdge edge) => edge.Type switch
    {
        EdgeType.ReadsData or EdgeType.WritesData or EdgeType.UsesIntegration or EdgeType.UsesSession or EdgeType.ReadsConfiguration or EdgeType.UsesAuthentication or EdgeType.BackedBy => true,
        EdgeType.Invokes => edge.To.StartsWith("db:code:", StringComparison.Ordinal) || edge.To.StartsWith("sp:", StringComparison.Ordinal),
        EdgeType.ImplementsContract => edge.To.StartsWith("sp:", StringComparison.Ordinal),
        _ => false
    };

    private static string Normalise(string path) => path.Replace('\\', '/');

    private string UnitOf(TypeDef type) => unitMap.UnitOfProject.GetValueOrDefault(type.Source.Project, "(no unit)");

    // ---- Entry points -------------------------------------------------------------------------

    private IEnumerable<Entry> FindEntries(IReadOnlyList<SourceFacts> sources, IReadOnlyList<MarkupBinding> bindings, IReadOnlyList<MarkupHandler> markupHandlers)
    {
        static string Directory(string path) => Normalise(Path.GetDirectoryName(path) ?? "");

        bool Beside(TypeDef type, string markupFile) =>
            Directory(type.Source.Path).Equals(Directory(markupFile), StringComparison.OrdinalIgnoreCase);

        foreach (var type in types)
        {
            var bound = bindings.Any(binding => binding.TypeName.Equals(type.Type.Name, StringComparison.OrdinalIgnoreCase) && Beside(type, binding.MarkupFile));
            var isHandlerType = type.Type.Bases.Any(@base => HandlerBases.Contains(@base.SimpleName, StringComparer.Ordinal));
            var isGlobal = type.Source.IsCodeBehind("asax");

            foreach (var method in type.Type.Methods.Where(method => type.Type.Kind != "Interface"))
            {
                if (method.Handles is { Count: > 0 })
                {
                    yield return new Entry(type, method, "Page or control event", $"Handles clause: {string.Join(", ", method.Handles)}", 1);
                }

                foreach (var handler in markupHandlers.Where(handler => handler.Handler.Equals(method.Name, StringComparison.OrdinalIgnoreCase)
                             && handler.TypeName.Equals(type.Type.Name, StringComparison.OrdinalIgnoreCase) && Beside(type, handler.MarkupFile)))
                {
                    yield return new Entry(type, method, "Page or control event", $"wired in markup {handler.MarkupFile} line {handler.Line}", 1);
                }

                if (bound && ControlEventName().IsMatch(method.Name))
                {
                    yield return new Entry(type, method, "Page or control event", "naming convention (ControlId_Event) in a page's code-behind; not confirmed by markup or a Handles clause", 3);
                }

                if (isGlobal && GlobalEventName().IsMatch(method.Name))
                {
                    yield return new Entry(type, method, "Application event", "event method in the application's global file", 2);
                }

                if (isHandlerType && method.Name is "ProcessRequest" or "ProcessRequestAsync" or "Init")
                {
                    yield return new Entry(type, method, "Web pipeline", $"type implements {string.Join(", ", type.Type.Bases.Select(@base => @base.SimpleName).Where(name => HandlerBases.Contains(name, StringComparer.Ordinal)))}", 2);
                }

                if (type.Type.Bases.Any(@base => @base.SimpleName.Equals("WebService", StringComparison.Ordinal))
                    && method.Attributes.Any(attribute => attribute.Name is "WebMethod" or "WebMethodAttribute"))
                {
                    yield return new Entry(type, method, "Service operation", "WebMethod on a WebService class (SOAP over ASMX)", 1);
                }

                foreach (var (hostedBase, methods) in HostedBases)
                {
                    if (methods.Contains(method.Name, StringComparer.Ordinal) && type.Type.Bases.Any(@base => @base.SimpleName.Equals(hostedBase, StringComparison.Ordinal)))
                    {
                        yield return new Entry(type, method, "Background work", $"type derives from or implements {hostedBase}; the host starts {method.Name}", 2);
                    }
                }

                if (method.Name.Equals("Main", StringComparison.OrdinalIgnoreCase) && type.Type.Kind is "Module" or "Class")
                {
                    yield return new Entry(type, method, "Program entry", "method named Main", 3);
                }

                if (FrameworkCallbackName().IsMatch(method.Name))
                {
                    yield return new Entry(type, method, "Framework callback", "naming convention of the business-object framework; called by the framework, not by application code", 4);
                }
            }
        }

        foreach (var service in assessment.Wcf.Services)
        {
            foreach (var contractName in service.ImplementedContracts)
            {
                var contract = assessment.Wcf.Contracts.FirstOrDefault(candidate => candidate.Name.Equals(contractName, StringComparison.OrdinalIgnoreCase));

                if (contract is null)
                {
                    continue;
                }

                foreach (var operation in contract.Operations)
                {
                    foreach (var type in typesByName.GetValueOrDefault(service.Name) ?? [])
                    {
                        var method = type.Type.Methods.FirstOrDefault(candidate => candidate.Name.Equals(operation.Name, StringComparison.OrdinalIgnoreCase));

                        if (method is not null)
                        {
                            yield return new Entry(type, method, "Service operation", $"OperationContract {operation.Name} on {contract.Name}, implemented by {service.Name}", 1);
                        }
                    }
                }
            }
        }
    }

    // ---- Following calls ------------------------------------------------------------------------

    private sealed class State
    {
        public List<FlowStep> Steps { get; } = [];
        public SortedDictionary<string, FlowTerminal> Terminals { get; } = new(StringComparer.Ordinal);
        public SortedDictionary<string, FlowGap> Gaps { get; } = new(StringComparer.Ordinal);
        public SortedSet<string> Inputs { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Visited { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> OnStack { get; } = new(StringComparer.OrdinalIgnoreCase);
        public int Unfollowed { get; set; }
    }

    private Flow BuildFlow(Entry entry)
    {
        var state = new State();

        Follow(entry.Type, entry.Method, "entry point", 0, state, previousUnit: null);

        var rules = assessment.Authentication.AccessRules
            .Where(rule => rule.Location.Equals(entry.Type.Source.Path, StringComparison.OrdinalIgnoreCase)
                           || TargetSegments(rule.Target).Any(segment => segment.Equals(entry.Type.Type.Name, StringComparison.OrdinalIgnoreCase)
                               || segment.Equals(Path.GetFileNameWithoutExtension(entry.Type.Source.Path), StringComparison.OrdinalIgnoreCase)
                               || segment.Equals(Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(entry.Type.Source.Path)), StringComparison.OrdinalIgnoreCase)))
            .Select(rule => $"{rule.Source}: {rule.Target} {rule.Operation} {rule.Principals}".TrimEnd())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        var unit = UnitOf(entry.Type);

        return new Flow(
            $"flow:{entry.Type.Source.Project}:{entry.Type.Type.Name}.{entry.Method.Name}",
            unit,
            entry.Type.Source.Project,
            entry.Type.Type.Name,
            entry.Method.Name,
            entry.Kind,
            entry.Evidence,
            state.Inputs.ToList(),
            rules,
            state.Steps,
            state.Terminals.Values.ToList(),
            state.Gaps.Values.ToList(),
            state.Unfollowed,
            $"{state.Steps.Count} step(s) followed; {state.Unfollowed} call(s) not followed because they go to the framework, to code outside the repository, or to members the tool does not index (constructors, properties, lambdas); {state.Gaps.Count} point(s) where the tool stopped for a reason listed.");
    }

    private void Follow(TypeDef type, MethodFact method, string basis, int depth, State state, string? previousUnit)
    {
        var id = $"{type.Key}.{method.Name}";

        if (state.OnStack.Contains(id))
        {
            AddGap(state, $"{type.Type.Name}.{method.Name}", "recursive call back to a method already being followed", type.Source.Path, method.StartLine);

            return;
        }

        if (!state.Visited.Add(id))
        {
            return;
        }

        var unit = UnitOf(type);

        state.Steps.Add(new FlowStep(depth, type.Type.Name, method.Name, basis, type.Source.Path, method.StartLine,
            previousUnit is not null && previousUnit != unit ? $"crosses into {unit}" : null));

        CollectTerminals(type, method, state);

        if (depth >= MaxDepth)
        {
            AddGap(state, $"{type.Type.Name}.{method.Name}", $"depth limit of {MaxDepth} steps reached; calls from here were not followed", type.Source.Path, method.StartLine);

            return;
        }

        state.OnStack.Add(id);

        foreach (var call in CallsOf(type, method))
        {
            if (InputAccessor().IsMatch(call.CalleeText) && call.FirstString is not null)
            {
                state.Inputs.Add($"{Accessor(call.CalleeText)}[\"{call.FirstString}\"]");
                continue;
            }

            if (call.IsIndexer)
            {
                continue;
            }

            if (ReflectionCallees.Contains(call.CalleeName, StringComparer.OrdinalIgnoreCase))
            {
                AddGap(state, call.CalleeText, $"reflection ({call.CalleeName}); the target is chosen at run time", type.Source.Path, call.Line);
                continue;
            }

            var resolution = Resolve(call, type);

            if (resolution.Reason is not null)
            {
                AddGap(state, call.CalleeText, resolution.Reason, type.Source.Path, call.Line);
                continue;
            }

            if (resolution.Targets.Count == 0)
            {
                state.Unfollowed++;
                continue;
            }

            Follow(resolution.Targets[0].Type, resolution.Targets[0].Method, resolution.Targets[0].Basis, depth + 1, state, unit);
        }

        state.OnStack.Remove(id);
    }

    private IEnumerable<InvocationFact> CallsOf(TypeDef type, MethodFact method) =>
        (invocations.GetValueOrDefault((type.Source.Path, type.Type.Name, method.Name.ToLowerInvariant())) ?? [])
        .DistinctBy(call => (call.Line, call.CalleeText))
        .OrderBy(call => call.Line)
        .ThenBy(call => call.CalleeText, StringComparer.Ordinal);

    /// <summary>Whole name segments of a rule target ("Pages/DataAccess.aspx", "Order.Total"), so a rule for OrderLine is not attributed to Order.</summary>
    private static string[] TargetSegments(string target) =>
        target.Split(['.', '/', '\\', ':', ' ', '='], StringSplitOptions.RemoveEmptyEntries);

    private static string Accessor(string calleeText) =>
        calleeText.Contains('.', StringComparison.Ordinal) ? calleeText[(calleeText.LastIndexOf('.') + 1)..] : calleeText;

    private static void AddGap(State state, string point, string reason, string location, int line)
    {
        var gap = new FlowGap(point, reason, Normalise(location), line);

        state.Gaps.TryAdd($"{gap.Location}:{gap.Line:00000}:{gap.Point}:{gap.Reason}", gap);
    }

    private void CollectTerminals(TypeDef type, MethodFact method, State state)
    {
        foreach (var edge in edgesByFile.GetValueOrDefault(Normalise(type.Source.Path)) ?? [])
        {
            if (edge.Line < method.StartLine || edge.Line > method.EndLine)
            {
                continue;
            }

            var (kind, target) = edge.Type switch
            {
                EdgeType.ReadsData or EdgeType.WritesData or EdgeType.Invokes or EdgeType.ImplementsContract or EdgeType.BackedBy => ("Data", edge.To),
                EdgeType.UsesIntegration => ("Outside system", edge.To),
                EdgeType.UsesSession => ("Session", edge.To),
                EdgeType.ReadsConfiguration => ("Configuration", edge.To),
                _ => ("Authentication", edge.To)
            };

            var detail = edge.Basis is "Convention" or "NameMatch" ? $"{edge.Evidence} (basis: {edge.Basis})" : edge.Evidence;

            state.Terminals.TryAdd($"{kind}|{target}|{detail}", new FlowTerminal(kind, target, detail, Normalise(type.Source.Path), edge.Line ?? 0));
        }
    }

    // ---- Resolving one call -----------------------------------------------------------------

    private sealed record Resolution(IReadOnlyList<Target> Targets, string? Reason);

    private static readonly Resolution NotFound = new([], null);

    private Resolution Resolve(InvocationFact call, TypeDef caller)
    {
        var callee = call.CalleeName;
        var qualifier = call.Qualifier ?? "";

        foreach (var (dispatchQualifier, callees, prefix, label) in Dispatch)
        {
            if (qualifier.Equals(dispatchQualifier, StringComparison.OrdinalIgnoreCase) && callees.Contains(callee, StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(call.TypeArgument))
                {
                    return new Resolution([], $"{label}: the type fetched is not named in the call, so the target is not known");
                }

                var found = MethodsNamed(typesByName.GetValueOrDefault(call.TypeArgument) ?? [], prefix + callee, $"framework convention ({label})");

                return found.Count == 0 ? NotFound : Pick(found, call);
            }
        }

        if (qualifier.Length == 0 || qualifier is "Me" or "this" or "MyClass")
        {
            var own = MethodsNamed(typesByKey.GetValueOrDefault(caller.Key) ?? [], callee, "same type");

            if (own.Count > 0)
            {
                return Pick(own, call);
            }

            var inherited = FromBases(caller, callee);

            if (inherited.Count > 0)
            {
                return Pick(inherited, call);
            }

            if (qualifier.Length == 0)
            {
                var modules = MethodsNamed(types.Where(type => type.Type.Kind == "Module").ToList(), callee, "module function");

                return modules.Count == 0 ? NotFound : Pick(modules, call);
            }

            return NotFound;
        }

        if (qualifier is "MyBase" or "base")
        {
            var inherited = FromBases(caller, callee);

            return inherited.Count == 0 ? NotFound : Pick(inherited, call);
        }

        if (typesByName.TryGetValue(qualifier, out var named))
        {
            var direct = MethodsNamed(named, callee, "named type");

            if (direct.Count > 0)
            {
                return Pick(Expand(direct, callee), call);
            }
        }

        var candidates = MethodsNamed(types.Where(type => type.Type.Methods.Any(method => method.Name.Equals(callee, StringComparison.OrdinalIgnoreCase))).ToList(), callee, "by name");

        if (candidates.Count == 0)
        {
            return NotFound;
        }

        // A call through a variable is followed only into a type this code can reach: one it refers to by name, or a service
        // contract it opens a channel to. Method names such as Add or Save are common to framework types, so a name match alone is a guess.
        var contracts = assessment.Wcf.Clients.Where(client => client.Class.Equals(caller.Type.Name, StringComparison.OrdinalIgnoreCase)).Select(client => client.Contract).ToList();
        var used = identifiersByType.GetValueOrDefault(caller.Key);

        candidates = candidates.Where(candidate => (used?.Contains(candidate.Type.Type.Name) ?? false) || contracts.Contains(candidate.Type.Type.Name, StringComparer.OrdinalIgnoreCase)).ToList();

        if (candidates.Count == 0)
        {
            return NotFound;
        }

        if (candidates.DistinctBy(candidate => candidate.Type.Key).Count() > 1)
        {
            // A class that opens a channel to a service contract calls through that contract: the analysis already found the link.
            var viaContract = candidates.Where(candidate => contracts.Contains(candidate.Type.Type.Name, StringComparer.OrdinalIgnoreCase)).ToList();

            if (viaContract.Count > 0)
            {
                candidates = viaContract.Select(candidate => candidate with { Basis = "service contract this class opens a channel to" }).ToList();
            }
        }

        if (candidates.DistinctBy(candidate => candidate.Type.Key).Count() > 1 && used is not null)
        {
            // Narrow by the types this code refers to, before interfaces are replaced by their implementations,
            // because code names the interface it calls through, not the class behind it.
            var narrowed = candidates.Where(candidate => used.Contains(candidate.Type.Type.Name)).ToList();

            if (narrowed.Count > 0)
            {
                candidates = narrowed.Select(candidate => candidate with { Basis = "by name, narrowed to the types this code refers to" }).ToList();
            }
        }

        candidates = Expand(candidates, callee);

        return Pick(candidates, call);
    }

    private List<Target> MethodsNamed(IReadOnlyList<TypeDef> candidates, string name, string basis)
    {
        var result = new List<Target>();

        foreach (var group in candidates.GroupBy(candidate => candidate.Key).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            foreach (var part in group)
            {
                var method = part.Type.Methods.FirstOrDefault(candidate => candidate.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

                if (method is not null)
                {
                    result.Add(new Target(part, method, basis));
                    break;
                }
            }
        }

        return result;
    }

    private List<Target> FromBases(TypeDef type, string callee)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal) { type.Key };
        var queue = new Queue<TypeDef>([type]);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            foreach (var @base in current.Type.Bases.Where(@base => @base.Kind != "Implements"))
            {
                var parents = (typesByName.GetValueOrDefault(@base.SimpleName) ?? []).Where(parent => seen.Add(parent.Key)).ToList();
                var found = MethodsNamed(parents, callee, "inherited");

                if (found.Count > 0)
                {
                    return found;
                }

                parents.ForEach(queue.Enqueue);
            }
        }

        return [];
    }

    /// <summary>An interface has no body; replace it by the classes that implement it.</summary>
    private List<Target> Expand(List<Target> targets, string callee)
    {
        var result = new List<Target>();

        foreach (var target in targets)
        {
            if (target.Type.Type.Kind != "Interface")
            {
                result.Add(target);
                continue;
            }

            var implementers = types
                .Where(type => type.Type.Kind != "Interface" && type.Type.Bases.Any(@base => @base.SimpleName.Equals(target.Type.Type.Name, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            result.AddRange(MethodsNamed(implementers, callee, $"implementation of {target.Type.Type.Name}"));
        }

        return result.DistinctBy(target => target.Type.Key).ToList();
    }

    private static Resolution Pick(List<Target> targets, InvocationFact call)
    {
        var distinct = targets.DistinctBy(target => target.Type.Key).ToList();

        if (distinct.Count == 1)
        {
            return new Resolution([distinct[0]], null);
        }

        if (distinct.Count == 0)
        {
            return NotFound;
        }

        var names = distinct.Select(target => $"{target.Type.Type.Name}.{target.Method.Name}").Order(StringComparer.OrdinalIgnoreCase).ToList();
        var listed = string.Join(", ", names.Take(MaxListedCandidates)) + (names.Count > MaxListedCandidates ? $" and {names.Count - MaxListedCandidates} more" : "");

        return new Resolution([], $"ambiguous: {distinct.Count} methods could be the target ({listed}); the tool does not guess");
    }
}
