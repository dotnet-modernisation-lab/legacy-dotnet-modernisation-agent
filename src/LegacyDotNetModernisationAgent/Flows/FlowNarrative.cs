using System.Text.RegularExpressions;

namespace LegacyDotNetModernisationAgent.Flows;

/// <summary>A flow told as Given (who and what must hold), When (what starts it) and Then (what the code does and what it ends up touching), with what the tool could not see.</summary>
public sealed record FlowStory(IReadOnlyList<string> Given, IReadOnlyList<string> When, IReadOnlyList<string> Then, IReadOnlyList<string> Caveats);

/// <summary>
/// Tells a flow in words, from the facts the flow already holds. It states what the code and configuration declare and what the code
/// does in the order it is written; it never infers what a method is for from its name. The calls are in source order, not a run:
/// conditions are not read, so a call listed may not happen every time. Every story says so.
/// </summary>
public static partial class FlowNarrative
{
    private const int MaxCallSentences = 8;
    private const int MaxEffectSentences = 8;

    [GeneratedRegex(@"Session[\(\[]\s*""?(?<key>[^""\)\]]+)""?\s*[\)\]]\s+(?<direction>read|write)")]
    private static partial Regex SessionUse();

    [GeneratedRegex(@"(?<verb>reads|writes|executes)\s+(?<name>\S+)")]
    private static partial Regex DataVerb();

    [GeneratedRegex(@"(?<=[a-z])(?=[A-Z])")]
    private static partial Regex CamelBoundary();

    /// <param name="moreIn">Where the steps beyond the cap can be read, for example "flows.md" or "the technical detail below".</param>
    public static FlowStory Tell(Flow flow, Func<string, string> partName, string moreIn, int maxCalls = MaxCallSentences, int maxEffects = MaxEffectSentences)
    {
        return new FlowStory(Given(flow), When(flow), Then(flow, partName, moreIn, maxCalls, maxEffects), Caveats(flow));
    }

    // ---- Given ---------------------------------------------------------------------------------------

    private static List<string> Given(Flow flow)
    {
        var given = new List<string>();

        var configuration = flow.Access.Where(rule => rule.Source == "WebConfig").ToList();
        var others = flow.Access.Where(rule => rule.Source != "WebConfig").ToList();

        if (configuration.Count > 0)
        {
            var steps = configuration.Select(rule => $"{rule.Operation} {Subject(rule.Principals)}").ToList();
            var target = configuration[0].Target.StartsWith("location:", StringComparison.Ordinal) ? configuration[0].Target["location:".Length..] : configuration[0].Target;

            var consequence = configuration[0].Operation.Equals("deny", StringComparison.OrdinalIgnoreCase) && configuration[0].Principals is "users=?"
                ? ", so a user who is not signed in is denied before any later rule is reached"
                : configuration[0].Operation.Equals("allow", StringComparison.OrdinalIgnoreCase) && configuration[0].Principals is "users=*" && configuration.Count > 1
                    ? ", but the first rule allows everyone, so the rules after it do not take effect"
                    : "";

            given.Add($"the configuration for {target} declares, in the order the tool read them, these rules: {string.Join("; ", steps)}. ASP.NET applies the first rule that matches{consequence}");
        }

        foreach (var group in others.GroupBy(rule => (rule.Source, rule.Operation, rule.Principals)).OrderBy(group => group.Key.Source, StringComparer.Ordinal).ThenBy(group => group.Key.Operation, StringComparer.Ordinal))
        {
            var properties = group.Select(rule => rule.Target.Contains('.') ? rule.Target[(rule.Target.LastIndexOf('.') + 1)..] : rule.Target).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
            var who = group.Key.Source.Contains("Csla", StringComparison.OrdinalIgnoreCase) ? $"{(group.Key.Principals.Contains(',') ? "roles" : "role")} {group.Key.Principals}" : group.Key.Principals;

            given.Add($"the business object declares that {Join(properties)} can be {Verb(group.Key.Operation)} only by {who}");
        }

        if (flow.Access.Count == 0)
        {
            given.Add("no declared access rule mentions this entry point, so the tool cannot say who may use it");
        }

        var mechanisms = flow.Terminals.Where(terminal => terminal.Kind == "Authentication").Select(terminal => Words(Plain(terminal.Target))).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

        if (mechanisms.Count > 0)
        {
            given.Add($"the code on this path uses {Join(mechanisms)}");
        }

        var sessionReads = SessionKeys(flow, "read");

        if (sessionReads.Count > 0)
        {
            given.Add($"session state already holds {Join(sessionReads.Select(key => $"\"{key}\"").ToList())}, which the code reads");
        }

        var settings = flow.Terminals.Where(terminal => terminal.Kind == "Configuration").Select(terminal => Plain(terminal.Target)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

        if (settings.Count > 0)
        {
            given.Add($"the configuration setting{(settings.Count == 1 ? "" : "s")} {Join(settings)} {(settings.Count == 1 ? "is" : "are")} set");
        }

        if (flow.Inputs.Count > 0)
        {
            given.Add($"the request supplies {Join(flow.Inputs.ToList())}");
        }

        return given;
    }

    private static string Subject(string principals) => principals switch
    {
        "users=?" => "anonymous users",
        "users=*" => "all users",
        _ when principals.StartsWith("users=", StringComparison.Ordinal) => $"users {principals["users=".Length..]}",
        _ when principals.StartsWith("roles=", StringComparison.Ordinal) => $"role {principals["roles=".Length..]}",
        _ => principals
    };

    private static string Verb(string operation) => operation.StartsWith("Allow", StringComparison.OrdinalIgnoreCase)
        ? operation[5..].ToLowerInvariant() switch
        {
            "read" or "get" => "read",
            "write" or "set" => "written",
            "execute" => "run",
            "create" => "created",
            "delete" => "deleted",
            var other => other
        }
        : operation;

    private static List<string> SessionKeys(Flow flow, string direction) =>
        flow.Terminals.Where(terminal => terminal.Kind == "Session")
            .SelectMany(terminal => SessionUse().Matches(terminal.Detail).Where(match => match.Groups["direction"].Value == direction).Select(match => match.Groups["key"].Value))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

    // ---- When ----------------------------------------------------------------------------------------

    private static List<string> When(Flow flow)
    {
        var entry = $"{flow.EntryType}.{flow.EntryMethod}";

        var start = flow.EntryKind switch
        {
            "Page or control event" => $"the user triggers {entry}",
            "Service operation" => $"a caller invokes the service operation {flow.EntryMethod} ({flow.EntryEvidence})",
            "Application event" => $"the application raises {entry}",
            "Web pipeline" => $"the web server runs {entry} for a request",
            "Background work" => $"the host starts {entry}",
            "Program entry" => $"the program starts at {entry}",
            "Framework callback" => $"the framework calls {entry}, for example when business-object data is fetched through the data portal",
            _ => $"{entry} is started ({flow.EntryKind})"
        };

        var evidence = flow.EntryKind == "Page or control event"
            ? flow.EntryEvidence.StartsWith("naming convention", StringComparison.Ordinal)
                ? "the tool found this handler by its name only; no markup or Handles clause confirms it is wired to a control"
                : flow.EntryEvidence
            : null;

        return [evidence is null ? start : $"{start} ({evidence})"];
    }

    // ---- Then ----------------------------------------------------------------------------------------

    private static List<string> Then(Flow flow, Func<string, string> partName, string moreIn, int maxCalls, int maxEffects)
    {
        var then = new List<string>();

        // children of each step, from the depth of the steps in the order they were followed
        var children = new Dictionary<int, List<int>>();
        var parents = new Stack<int>();

        for (var i = 0; i < flow.Steps.Count; i++)
        {
            while (parents.Count > 0 && flow.Steps[parents.Peek()].Depth >= flow.Steps[i].Depth)
            {
                parents.Pop();
            }

            if (parents.Count > 0)
            {
                if (!children.TryGetValue(parents.Peek(), out var list))
                {
                    children[parents.Peek()] = list = [];
                }

                list.Add(i);
            }

            parents.Push(i);
        }

        string Name(int index) => $"{flow.Steps[index].Type}.{flow.Steps[index].Method}";

        var sentences = new List<string>();

        foreach (var (parent, list) in children.OrderBy(item => item.Key))
        {
            var called = list.Select(child => $"{Name(child)}{BasisNote(flow.Steps[child].Basis)}").ToList();
            var crossings = list.Where(child => flow.Steps[child].Boundary is not null && !flow.Steps[child].Boundary!.EndsWith(Strategy.DeployableUnits.SharedName, StringComparison.Ordinal))
                .Select(child => $" At {Name(child)} the path leaves the {partName(flow.Unit)} and continues in the {partName(flow.Steps[child].Boundary!.Replace("crosses into ", "", StringComparison.Ordinal))}.");

            sentences.Add($"{Name(parent)} calls {Join(called)}.{string.Concat(crossings)}");
        }

        if (sentences.Count == 0)
        {
            then.Add($"{Name(0)} makes no call the tool could follow into the code it analysed");
        }

        then.AddRange(sentences.Take(maxCalls));

        if (sentences.Count > maxCalls)
        {
            then.Add($"{sentences.Count - maxCalls} further steps are in {moreIn}");
        }

        var effects = new List<string>();

        for (var i = 0; i < flow.Steps.Count; i++)
        {
            var actions = Actions(flow.Terminals.Where(terminal => terminal.Step == i).ToList());

            if (actions.Count > 0)
            {
                effects.Add($"{Name(i)} {Join(actions)}");
            }
        }

        then.AddRange(effects.Take(maxEffects).Select(effect => $"And {effect}"));

        if (effects.Count > maxEffects)
        {
            then.Add($"And {effects.Count - maxEffects} further steps touch data, session, configuration or outside systems (see {moreIn})");
        }

        return then;
    }

    private static string BasisNote(string basis) => basis switch
    {
        _ when basis.StartsWith("by name, narrowed", StringComparison.Ordinal) => " (matched by method name among the types this code refers to)",
        "by name" => " (matched by method name)",
        _ when basis.StartsWith("implementation of", StringComparison.Ordinal) => $" ({basis})",
        _ when basis.StartsWith("service contract", StringComparison.Ordinal) => " (through the service contract this class opens a channel to)",
        _ when basis.StartsWith("framework convention", StringComparison.Ordinal) => " (called by the framework, not by name)",
        "inherited" => " (inherited)",
        _ => ""
    };

    private static List<string> Actions(List<FlowTerminal> terminals)
    {
        var actions = new List<string>();

        foreach (var terminal in terminals.Where(terminal => terminal.Kind == "Data").OrderBy(terminal => terminal.Target, StringComparer.Ordinal))
        {
            var verb = DataVerb().Match(terminal.Detail);
            var name = verb.Success ? verb.Groups["name"].Value : Plain(terminal.Target);

            if (terminal.Target.StartsWith("fixture:", StringComparison.Ordinal))
            {
                actions.Add($"reads the data file {terminal.Target["fixture:".Length..]}");
            }
            else if (terminal.Target.StartsWith("sp:", StringComparison.Ordinal) || terminal.Target.StartsWith("db:code:", StringComparison.Ordinal))
            {
                actions.Add($"runs {Plain(terminal.Target)}{(terminal.Detail.Contains("NamingConvention", StringComparison.Ordinal) ? " (linked to this code by name only)" : "")}");
            }
            else
            {
                actions.Add($"{(verb.Success && verb.Groups["verb"].Value == "writes" ? "writes" : "reads")} the table {name}{(verb.Success ? "" : "")}");
            }
        }

        var reads = SessionKeysOf(terminals, "read");
        var writes = SessionKeysOf(terminals, "write");

        if (reads.Count > 0)
        {
            actions.Add($"reads the session value{(reads.Count == 1 ? "" : "s")} {Join(reads.Select(key => $"\"{key}\"").ToList())}");
        }

        if (writes.Count > 0)
        {
            actions.Add($"writes the session value{(writes.Count == 1 ? "" : "s")} {Join(writes.Select(key => $"\"{key}\"").ToList())}");
        }

        var settings = terminals.Where(terminal => terminal.Kind == "Configuration").Select(terminal => Plain(terminal.Target)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

        if (settings.Count > 0)
        {
            actions.Add($"reads the configuration setting{(settings.Count == 1 ? "" : "s")} {Join(settings)}");
        }

        foreach (var outside in terminals.Where(terminal => terminal.Kind == "Outside system").Select(terminal => terminal.Target).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            actions.Add($"uses an outside system of kind {Words(Plain(outside))}");
        }

        return actions;
    }

    private static List<string> SessionKeysOf(List<FlowTerminal> terminals, string direction) =>
        terminals.Where(terminal => terminal.Kind == "Session")
            .SelectMany(terminal => SessionUse().Matches(terminal.Detail).Where(match => match.Groups["direction"].Value == direction).Select(match => match.Groups["key"].Value))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

    // ---- Caveats -------------------------------------------------------------------------------------

    private static List<string> Caveats(Flow flow)
    {
        var caveats = new List<string>
        {
            "This follows the order of calls as written in the source, not a run: conditions are not read, so a call listed here may not happen every time."
        };

        foreach (var gap in flow.CouldNotFollow)
        {
            var at = gap.Step >= 0 && gap.Step < flow.Steps.Count ? $" in {flow.Steps[gap.Step].Type}.{flow.Steps[gap.Step].Method}" : "";

            caveats.Add($"The tool could not follow {gap.Point}{at}: {gap.Reason}. What happens after it is not shown.");
        }

        if (flow.UnfollowedCalls > 0)
        {
            caveats.Add($"{flow.UnfollowedCalls} further call(s) go to the framework, to code outside the repository, or to constructors, properties or lambdas the tool does not index.");
        }

        return caveats;
    }

    // ---- Wording helpers -------------------------------------------------------------------------------

    private static string Plain(string target) => target[(target.LastIndexOf(':') + 1)..];

    /// <summary>CustomPrincipal becomes "custom principal": an identifier split into words, without deciding what it means.</summary>
    private static string Words(string identifier) => CamelBoundary().Replace(identifier, " ").ToLowerInvariant();

    private static string Join(List<string> items) => items.Count switch
    {
        0 => "",
        1 => items[0],
        2 => $"{items[0]} and {items[1]}",
        _ => $"{string.Join(", ", items.Take(items.Count - 1))} and {items[^1]}"
    };
}
