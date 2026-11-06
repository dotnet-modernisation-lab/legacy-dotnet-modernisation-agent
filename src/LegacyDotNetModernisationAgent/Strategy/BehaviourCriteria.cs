using System.Text.RegularExpressions;
using LegacyDotNetModernisationAgent.Flows;
using LegacyDotNetModernisationAgent.Models;

namespace LegacyDotNetModernisationAgent.Strategy;

/// <summary>
/// One candidate statement of behaviour that a replacement must keep, or must consciously change, written against the interface people and
/// systems use and not against the legacy code. Everything in it is derived from the code and configuration, so it is a candidate until
/// a person confirms it. Mechanisms the legacy system happens to use are kept apart because a replacement need not keep them.
/// </summary>
public sealed record Criterion(
    string Id,
    string Capability,
    string Part,
    IReadOnlyList<string> Given,
    string When,
    IReadOnlyList<string> Then,
    IReadOnlyList<string> ToConfirm,
    IReadOnlyList<string> Mechanisms,
    string Evidence,
    bool HasSubstance);

/// <summary>Something found in the current system that a person should decide on before it is carried into a replacement.</summary>
public sealed record DecisionFirst(string Statement, string Why, string Evidence);

public sealed record BehaviourCriteria(
    IReadOnlyList<Criterion> Criteria,
    IReadOnlyList<DecisionFirst> DecideFirst,
    IReadOnlyList<string> Notes);

/// <summary>
/// Turns the flows into implementation-neutral criteria. It keeps the interface (the page address, the service operation), who may use it
/// as declared, the inputs, and the effects on data and outside systems. It leaves out method names, session state, the sign-in mechanism,
/// the data portal and the service hop between parts, which are listed as mechanisms rather than requirements. What cannot be read from
/// code (what a caller is shown, how bad input is handled) is listed under "to confirm" and never guessed.
/// </summary>
public static partial class BehaviourCriteriaBuilder
{
    [GeneratedRegex(@"OperationContract (?<operation>\w+) on (?<contract>\w+)")]
    private static partial Regex OperationEvidence();

    [GeneratedRegex(@"Handles clause: (?<control>\w+)\.(?<event>\w+)")]
    private static partial Regex HandlesEvidence();

    [GeneratedRegex(@"Session[\(\[]\s*""?(?<key>[^""\)\]]+)""?\s*[\)\]]\s+(?<direction>read|write)")]
    private static partial Regex SessionUse();

    [GeneratedRegex(@"(?<verb>reads|writes|executes)\s+(?<name>\S+)")]
    private static partial Regex DataVerb();

    [GeneratedRegex(@"(?<=[a-z])(?=[A-Z])")]
    private static partial Regex CamelBoundary();

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NotId();

    private const int MaxEvidenceFiles = 4;

    public static BehaviourCriteria Build(LegacyDotNetProjectAssessment assessment, FlowSet flows, ComponentMap components, DataTouchMap data)
    {
        var projectDirectories = assessment.Projects.ToDictionary(project => project.Name, project => (Path.GetDirectoryName(project.Path) ?? "").Replace('\\', '/'), StringComparer.Ordinal);
        var criteria = new List<Criterion>();

        foreach (var flow in flows.Flows.Where(flow => flow.EntryKind != "Framework callback"))
        {
            criteria.Add(Describe(flow, projectDirectories));
        }

        // Two entry points can reduce to the same name (overloads, partial classes); a repeat gets a number so none is dropped.
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);

        criteria = criteria.OrderBy(criterion => criterion.Part, StringComparer.Ordinal).ThenBy(criterion => criterion.Id, StringComparer.Ordinal)
            .Select(criterion =>
            {
                var count = seen.GetValueOrDefault(criterion.Id) + 1;

                seen[criterion.Id] = count;

                return count == 1 ? criterion : criterion with { Id = $"{criterion.Id}-{count}" };
            }).ToList();

        var notes = new List<string>
        {
            "These are candidates, derived from the code and configuration, and none is confirmed. They are written against the interface people and systems use, so a replacement can be checked against them without copying how the legacy system works.",
            "What a caller is shown (content, messages, status), how bad or missing input is handled, and what happens for a user who is not allowed cannot be read from code. They are listed under \"to confirm\" and need a person, a scenario file or a recorded session.",
            "Mechanisms the legacy system uses (sign-in technology, session state, the data portal, the service call between parts) are listed with each criterion as not required: a replacement may do these differently.",
            "A criterion with nothing declared and nothing found is listed in a short table, not as a full criterion; that is a statement about what the tool could see, not proof that nothing happens."
        };

        return new BehaviourCriteria(criteria, DecideFirstList(assessment, flows, components, data), notes);
    }

    // ---- One flow as a criterion ---------------------------------------------------------------------

    private static Criterion Describe(Flow flow, Dictionary<string, string> projectDirectories)
    {
        var inputNames = flow.Inputs.Select(InputName).ToList();
        var (capability, key, when) = Capability(flow, projectDirectories, inputNames);
        var given = new List<string>();
        var confirm = new List<string>();

        // who may use it, as declared
        var configuration = flow.Access.Where(rule => rule.Source == "WebConfig").ToList();
        var others = flow.Access.Where(rule => rule.Source != "WebConfig").ToList();

        if (configuration.Count > 0)
        {
            var first = configuration[0];

            given.Add(first.Operation.Equals("deny", StringComparison.OrdinalIgnoreCase) && first.Principals == "users=?"
                ? "the user is signed in (declared: anonymous users are denied)"
                : first.Operation.Equals("allow", StringComparison.OrdinalIgnoreCase) && first.Principals == "users=*"
                    ? "any user may reach it (declared: all users are allowed)"
                    : $"declared access: {string.Join("; ", configuration.Select(rule => $"{rule.Operation} {Subject(rule.Principals)}"))}");
        }

        foreach (var group in others.GroupBy(rule => (rule.Source, rule.Operation, rule.Principals)).OrderBy(group => group.Key.Source, StringComparer.Ordinal).ThenBy(group => group.Key.Operation, StringComparer.Ordinal))
        {
            var properties = group.Select(rule => rule.Target.Contains('.') ? rule.Target[(rule.Target.LastIndexOf('.') + 1)..] : rule.Target).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

            given.Add($"declared: {Join(properties)} can be {Verb(group.Key.Operation)} only by {(group.Key.Principals.Contains(',') ? "roles" : "role")} {group.Key.Principals}");
        }

        if (flow.Access.Count == 0)
        {
            given.Add("no access restriction is declared for it, so who may use it must be confirmed");
            confirm.Add("who may use this capability");
        }
        else
        {
            confirm.Add("what a user who is not allowed is told or shown");
        }

        var remembered = SessionKeys(flow, "read");

        if (remembered.Count > 0)
        {
            given.Add($"information remembered from an earlier request is available: {Join(remembered)}");
        }

        var settings = flow.Terminals.Where(terminal => terminal.Kind == "Configuration").Select(terminal => Plain(terminal.Target)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

        if (settings.Count > 0)
        {
            given.Add($"these environment settings have values: {Join(settings)}");
        }

        if (flow.Inputs.Count > 0)
        {
            given.Add($"the caller supplies {Join(inputNames)}");
            confirm.Add($"what happens when {Join(inputNames)} is missing or invalid");
        }

        // observable effects
        var then = new List<string>();

        foreach (var terminal in flow.Terminals.Where(terminal => terminal.Kind == "Data").OrderBy(terminal => terminal.Target, StringComparer.Ordinal).DistinctBy(terminal => terminal.Target))
        {
            var verb = DataVerb().Match(flow.Terminals.Where(item => item.Target == terminal.Target).Select(item => item.Detail).FirstOrDefault(string.Empty));
            var name = verb.Success ? verb.Groups["name"].Value : Plain(terminal.Target);

            then.Add(terminal.Target.StartsWith("fixture:", StringComparison.Ordinal) ? $"reads the data file {terminal.Target["fixture:".Length..]}"
                : terminal.Target.StartsWith("sp:", StringComparison.Ordinal) || terminal.Target.StartsWith("db:code:", StringComparison.Ordinal) ? $"runs the database procedure {Plain(terminal.Target)} (the tables inside it are in data-touch.md)"
                : verb.Success && verb.Groups["verb"].Value == "writes" ? $"writes to {name}"
                : $"reads from {name}");
        }

        var writes = SessionKeys(flow, "write");

        if (writes.Count > 0)
        {
            then.Add($"remembers for later requests: {Join(writes)}");
        }

        foreach (var outside in flow.Terminals.Where(terminal => terminal.Kind == "Outside system").Select(terminal => Words(Plain(terminal.Target))).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            then.Add($"uses an outside system of kind {outside}");
        }

        var otherParts = flow.Steps.Where(step => step.Boundary is not null).Select(step => step.Boundary!.Replace("crosses into ", "", StringComparison.Ordinal))
            .Where(part => !part.EndsWith(DeployableUnits.SharedName, StringComparison.Ordinal)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

        if (otherParts.Count > 0)
        {
            then.Add($"obtains part of its result from {Join(otherParts.Select(CustomerSummaryBuilder.DisplayName).ToList())}");
        }

        confirm.Add("what is returned to the caller (content, messages, status)");

        foreach (var gap in flow.CouldNotFollow)
        {
            confirm.Add($"what happens beyond {gap.Point} ({gap.Reason})");
        }

        var mechanisms = new List<string>();
        var authentication = flow.Terminals.Where(terminal => terminal.Kind == "Authentication").Select(terminal => Words(Plain(terminal.Target))).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

        mechanisms.AddRange(authentication);

        if (SessionKeys(flow, "read").Count + SessionKeys(flow, "write").Count > 0)
        {
            mechanisms.Add("session state to remember information between requests");
        }

        if (flow.Steps.Any(step => step.Basis.StartsWith("framework convention", StringComparison.Ordinal)))
        {
            mechanisms.Add("the business-object framework's data portal");
        }

        if (otherParts.Count > 0)
        {
            mechanisms.Add("a call between parts of the estate");
        }

        if (flow.Access.Any(rule => rule.Source != "WebConfig"))
        {
            mechanisms.Add($"authorisation rules declared on business objects ({string.Join(", ", flow.Access.Where(rule => rule.Source != "WebConfig").Select(rule => Words(rule.Source)).Distinct(StringComparer.Ordinal))})");
        }

        var evidence = string.Join(", ", flow.Steps.Select(step => step.Location).Concat(flow.Access.Select(rule => rule.Location)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Take(MaxEvidenceFiles))
                       + (flow.Steps.Select(step => step.Location).Concat(flow.Access.Select(rule => rule.Location)).Distinct(StringComparer.Ordinal).Count() > MaxEvidenceFiles ? " and others" : "");

        var substance = flow.Access.Count > 0 || flow.Inputs.Count > 0 || then.Count > 0;

        return new Criterion($"BC-{NotId().Replace((CustomerSummaryBuilder.DisplayName(flow.Unit) + " " + key).ToLowerInvariant(), "-").Trim('-')}", capability, flow.Unit, given, when, then, confirm, mechanisms, evidence, substance);
    }

    private static (string Capability, string Key, string When) Capability(Flow flow, Dictionary<string, string> projectDirectories, List<string> inputNames)
    {
        var inputs = inputNames.Count == 0 ? "" : $" with {Join(inputNames)}";

        switch (flow.EntryKind)
        {
            case "Page or control event":
                var page = PagePath(flow, projectDirectories);
                var handles = HandlesEvidence().Match(flow.EntryEvidence);
                var loads = flow.EntryMethod.EndsWith("_Load", StringComparison.OrdinalIgnoreCase) || (handles.Success && handles.Groups["event"].Value == "Load");

                if (loads)
                {
                    return ($"Request the page {page}", $"page {page} load", $"a user requests the page {page}{inputs}");
                }

                var action = handles.Success ? $"the {handles.Groups["control"].Value} control ({handles.Groups["event"].Value.ToLowerInvariant()})" : "a control on the page";

                return ($"Use {action} on the page {page}", $"page {page} {flow.EntryMethod}", $"a user uses {action} on the page {page}{inputs}");

            case "Service operation":
                var operation = OperationEvidence().Match(flow.EntryEvidence);
                var name = operation.Success ? $"{operation.Groups["operation"].Value} of {operation.Groups["contract"].Value}" : flow.EntryMethod;

                return ($"Service operation {name}", $"operation {name}", $"a caller invokes the service operation {name}{inputs}");

            case "Application event":
                return ($"Application event {flow.EntryMethod}", $"application event {flow.EntryType} {flow.EntryMethod}", $"the application raises {flow.EntryMethod}");

            case "Web pipeline":
                return ($"Handling of web requests by {flow.EntryType}", $"pipeline {flow.EntryType}", $"the web server passes a request through {flow.EntryType}");

            case "Background work":
                return ($"Background work in {flow.EntryType}", $"background {flow.EntryType} {flow.EntryMethod}", $"the host starts {flow.EntryType}");

            default:
                return ($"{flow.EntryKind}: {flow.EntryType}", $"{flow.EntryKind} {flow.EntryType} {flow.EntryMethod}", $"{flow.EntryType} is started");
        }
    }

    /// <summary>The address of the page the code-behind belongs to, relative to its project: the file name minus the code-behind extension.</summary>
    private static string PagePath(Flow flow, Dictionary<string, string> projectDirectories)
    {
        var location = (flow.Steps.Count > 0 ? flow.Steps[0].Location : flow.EntryType).Replace('\\', '/');
        var markup = Regex.Replace(location, @"\.(vb|cs)$", "", RegexOptions.IgnoreCase);
        var directory = projectDirectories.GetValueOrDefault(flow.Project, "");

        return directory.Length > 0 && markup.StartsWith(directory + "/", StringComparison.OrdinalIgnoreCase) ? markup[(directory.Length + 1)..] : markup;
    }

    // ---- Things to decide first --------------------------------------------------------------------------

    private static List<DecisionFirst> DecideFirstList(LegacyDotNetProjectAssessment assessment, FlowSet flows, ComponentMap components, DataTouchMap data)
    {
        var decisions = new List<DecisionFirst>();

        // access rules that do not do what they appear to
        foreach (var group in flows.Flows.SelectMany(flow => flow.Access).Where(rule => rule.Source == "WebConfig")
                     .GroupBy(rule => (rule.Location, rule.Target)).OrderBy(group => group.Key.Target, StringComparer.Ordinal))
        {
            var rules = group.ToList();

            if (rules.Count > 1 && rules[0].Operation.Equals("allow", StringComparison.OrdinalIgnoreCase) && rules[0].Principals == "users=*")
            {
                decisions.Add(new DecisionFirst(
                    $"For {Target(group.Key.Target)}, the first access rule allows everyone, so the rules after it do not take effect.",
                    "A replacement that enforces the later rules would block users the legacy system lets in; one that copies the first would keep an unintended opening. Decide which behaviour is wanted.",
                    group.Key.Location));
            }
        }

        // secrets
        foreach (var mechanism in assessment.Authentication.Mechanisms.Where(mechanism => mechanism.Kind.Contains("Secret", StringComparison.OrdinalIgnoreCase)))
        {
            decisions.Add(new DecisionFirst(
                $"A shared application secret is used to vouch for calls between applications ({mechanism.Summary}).",
                "How callers prove who they are is a design decision for the replacement, as is where the secret is held. Copying this pattern would carry the weakness forward.",
                "authentication analysis"));
        }

        foreach (var fact in components.Loops.Concat(components.DirectReaches))
        {
            decisions.Add(new DecisionFirst(fact.Statement, "A replacement that reproduces this structure reproduces the coupling. Decide whether the dependency is intended behaviour or an accident of how the legacy code grew.", fact.Where));
        }

        foreach (var flow in flows.Flows.Where(flow => flow.EntryKind == "Page or control event" && flow.EntryEvidence.StartsWith("naming convention", StringComparison.Ordinal)))
        {
            decisions.Add(new DecisionFirst($"{flow.EntryType}.{flow.EntryMethod} looks like an event handler but nothing confirms it is connected to a control.", "It may be dead code, or behaviour the tool cannot see wired. Confirm before deciding whether a replacement needs it.", flow.Steps.Count > 0 ? flow.Steps[0].Location : flow.EntryType));
        }

        foreach (var row in data.Rows.Where(row => row.Note is not null))
        {
            decisions.Add(new DecisionFirst($"{row.Name} is run by the code, but its definition is not in the repository.", "What it reads and writes cannot be checked against a replacement until the definition is obtained from the database.", row.Name));
        }

        var sharedWrites = data.Rows.Where(row => row.ByPart.Count(item => item.Key != DataTouchBuilder.NotReached && item.Value.Access.Contains('W')) > 1).ToList();

        if (sharedWrites.Count > 0 && sharedWrites.Count <= 3)
        {
            foreach (var row in sharedWrites)
            {
                decisions.Add(new DecisionFirst($"{row.Name} is written by more than one part of the estate.", "If the parts are migrated separately, they must agree on who owns this data and how changes are shared meanwhile.", row.Name));
            }
        }
        else if (sharedWrites.Count > 3)
        {
            decisions.Add(new DecisionFirst(
                $"{sharedWrites.Count} data objects are each written by more than one part of the estate, for example {string.Join(", ", sharedWrites.Take(6).Select(row => row.Name))}.",
                "If the parts are migrated separately, they must agree on who owns this data and how changes are shared meanwhile. The full list is in data-touch.md.",
                "data-touch.md"));
        }

        return decisions.DistinctBy(decision => decision.Statement).ToList();
    }

    // ---- Helpers -------------------------------------------------------------------------------------------

    /// <summary>QueryString["id"] becomes "the query parameter id": what the caller supplies, not the framework call that reads it.</summary>
    private static string InputName(string input)
    {
        var open = input.IndexOf('[');

        if (open < 0 || !input.EndsWith(']'))
        {
            return input;
        }

        var name = input[(open + 1)..^1].Trim('"');

        return input[..open] switch
        {
            "QueryString" => $"the query parameter {name}",
            "Form" => $"the form field {name}",
            "Cookies" => $"the cookie {name}",
            "Headers" => $"the header {name}",
            "Params" or "ServerVariables" => $"the request value {name}",
            _ => input
        };
    }

    private static string Target(string target) => target.StartsWith("location:", StringComparison.Ordinal) ? target["location:".Length..] : target;

    private static string Subject(string principals) => principals switch
    {
        "users=?" => "anonymous users",
        "users=*" => "all users",
        _ when principals.StartsWith("users=", StringComparison.Ordinal) => $"users {principals["users=".Length..]}",
        _ when principals.StartsWith("roles=", StringComparison.Ordinal) => $"role {principals["roles=".Length..]}",
        _ => principals
    };

    private static string Verb(string operation) => operation.StartsWith("Allow", StringComparison.OrdinalIgnoreCase)
        ? operation[5..].ToLowerInvariant() switch { "read" or "get" => "read", "write" or "set" => "written", "execute" => "run", "create" => "created", "delete" => "deleted", var other => other }
        : operation;

    private static List<string> SessionKeys(Flow flow, string direction) =>
        flow.Terminals.Where(terminal => terminal.Kind == "Session")
            .SelectMany(terminal => SessionUse().Matches(terminal.Detail).Where(match => match.Groups["direction"].Value == direction).Select(match => match.Groups["key"].Value))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

    private static string Plain(string target) => target[(target.LastIndexOf(':') + 1)..];

    private static string Words(string identifier) => CamelBoundary().Replace(identifier, " ").ToLowerInvariant();

    private static string Join(List<string> items) => items.Count switch
    {
        0 => "",
        1 => items[0],
        2 => $"{items[0]} and {items[1]}",
        _ => $"{string.Join(", ", items.Take(items.Count - 1))} and {items[^1]}"
    };
}
