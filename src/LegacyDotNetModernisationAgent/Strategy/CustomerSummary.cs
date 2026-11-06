using LegacyDotNetModernisationAgent.Models;

namespace LegacyDotNetModernisationAgent.Strategy;

public sealed record SummaryArea(string Name, string Kind, string Size, string SizeWords, string Confidence, string What, string Contains, string Uses);

public sealed record SummaryDecision(string Question, string Why, string Areas);

/// <summary>
/// The short document a customer can read: what the estate is made of, what has to be decided, what is risky, and what the
/// analysis could not see. Every sentence is derived from the deterministic facts, in plain wording from <see cref="ClientLanguage"/>.
/// It deliberately contains no identifiers or codes, no prices, no dates and no overall size for the estate.
/// </summary>
public sealed record CustomerSummary(
    string Title,
    string Subtitle,
    string Headline,
    IReadOnlyList<string> AtAGlance,
    IReadOnlyList<SummaryArea> Areas,
    IReadOnlyList<string> HowItFits,
    IReadOnlyList<string> Journeys,
    IReadOnlyList<SummaryDecision> Decisions,
    IReadOnlyList<string> Risks,
    IReadOnlyList<string> NotSeen,
    IReadOnlyList<string> NextSteps,
    string About);

public static class CustomerSummaryBuilder
{
    public static CustomerSummary Build(
        LegacyDotNetProjectAssessment assessment,
        LegacyDependencyGraph graph,
        MigrationAnalysis analysis,
        LegacyMigrationAnalysisInput input,
        UnitMap unitMap,
        EstateContext context,
        Flows.FlowSet? flows = null)
    {
        var areas = input.WorkAreas.OrderBy(area => area.Order).ToList();
        var unitKinds = unitMap.Units.ToDictionary(unit => unit.Name, unit => unit.Kind, StringComparer.Ordinal);

        // ---- Areas ------------------------------------------------------------------------
        var summaryAreas = areas.Select(area =>
        {
            var nodes = graph.Nodes.Where(node => unitMap.UnitOfNode.GetValueOrDefault(node.Id) == area.Name).ToList();
            var code = nodes.Where(node => !DeployableUnits.TechnologyKinds.Contains(node.Kind)).GroupBy(node => node.Kind, StringComparer.Ordinal)
                .OrderByDescending(group => group.Count()).ThenBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => $"{group.Count()} {ClientLanguage.Noun(group.Key, group.Count())}").ToList();
            var tech = nodes.Where(node => DeployableUnits.TechnologyKinds.Contains(node.Kind)).GroupBy(node => node.Kind, StringComparer.Ordinal)
                .OrderByDescending(group => group.Count()).ThenBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => $"{group.Count()} {ClientLanguage.TechnologyNoun(group.Key, group.Count())}").ToList();

            return new SummaryArea(
                DisplayName(area.Name),
                area.Kind,
                area.Size,
                ClientLanguage.SizeWords(area.Size),
                area.Confidence,
                ClientLanguage.WhatIs(area.Kind),
                code.Count == 0 ? "" : string.Join(", ", code),
                tech.Count == 0 ? "" : string.Join(", ", tech));
        }).ToList();

        // ---- Headline and facts at a glance ------------------------------------------------------
        var languages = assessment.Projects.GroupBy(project => project.Language).OrderByDescending(group => group.Count()).ThenBy(group => group.Key)
            .Select(group => (Name: group.Key switch { ProjectLanguage.VisualBasic => "VB.NET", ProjectLanguage.CSharp => "C#", _ => "unknown language" }, Count: group.Count())).ToList();

        var platform = input.Platform == nameof(DotNetPlatform.DotNetFramework) ? ".NET Framework" : input.Platform == nameof(DotNetPlatform.DotNetCore) ? "modern .NET" : "an unidentified .NET platform";
        var frameworks = assessment.Projects.Select(project => project.TargetFramework).Where(framework => framework is not null).Select(framework => FrameworkName(framework!)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

        var deployable = summaryAreas.Where(area => area.Kind is not (DeployableUnits.NotPlacedKind or DeployableUnits.SharedKind or DeployableUnits.DataSourceKind)).ToList();
        var large = areas.Count(area => area.Size is "L" or "XL" && area.Kind != DeployableUnits.NotPlacedKind);
        var decisions = input.Gates.Count;

        var headline =
            $"{assessment.RepositoryName} is a {platform} estate of {Plural(assessment.Projects.Count, "project")} written in {JoinNatural(languages.Select(language => language.Name).ToList())}. " +
            $"We found {Plural(deployable.Count, "deployable part")}: {JoinNatural(deployable.Select(area => $"{DisplayKind(area.Kind)} ({area.Name})").ToList())}, along with the data it reads and some shared code. " +
            (large == 0 ? "None of the parts is large on its own. " : $"{Plural(large, "part")} {(large == 1 ? "is" : "are")} large pieces of work. ") +
            (decisions == 0 ? "No decisions were found that only your team can make." : $"{Plural(decisions, "decision")} only your team can make must be settled before work in those areas can start.");

        var allCode = graph.Nodes.Where(node => !DeployableUnits.TechnologyKinds.Contains(node.Kind)).GroupBy(node => node.Kind, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count()).ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => $"{group.Count()} {ClientLanguage.Noun(group.Key, group.Count())}").ToList();

        var atAGlance = new List<string>
        {
            $"Projects: {assessment.Projects.Count} ({string.Join(", ", languages.Select(language => $"{language.Name} {language.Count}"))}).",
            $"Platform: {(frameworks.Count > 0 ? string.Join(", ", frameworks) : platform)}.",
            $"What it contains: {string.Join(", ", allCode.Take(8))}{(allCode.Count > 8 ? $" and {allCode.Count - 8} other kinds of item" : "")}.",
            $"Decisions needed from your team: {decisions}.",
            "Things the analysis could not see: PLACEHOLDER."
        };

        // ---- How it fits together ----------------------------------------------------------------
        var fits = new List<string>();

        foreach (var actor in context.Actors.Where(actor => actor.Label is not ("Calling applications" or "Callers not visible")))
        {
            fits.Add($"{actor.Label} reach the {DisplayKind(unitKinds[actor.UnitName])} ({DisplayName(actor.UnitName)}). {actor.Basis}");
        }

        foreach (var link in context.Links)
        {
            var verb = link.Label switch
            {
                "WCF call" => "calls",
                "reads and writes data" => "reads and writes data in",
                "uses shared code" => "uses the",
                _ => "uses"
            };

            fits.Add($"The {DisplayKind(unitKinds[link.From])} ({DisplayName(link.From)}) {verb} {(verb == "uses the" ? "" : "the ")}{DisplayKind(unitKinds[link.To])}{(unitKinds[link.To] is DeployableUnits.DataSourceKind or DeployableUnits.SharedKind ? "" : $" ({DisplayName(link.To)})")}.");
        }

        foreach (var system in context.ExternalSystems)
        {
            var users = JoinNatural(system.UsedBy.Select(unit => $"The {DisplayKind(unitKinds[unit])} ({DisplayName(unit)})").ToList());

            fits.Add(system.Category == "ExternalHttp"
                ? $"{users} contains code that can make web calls. The analysis did not find which address it calls, so whether it reaches an outside service is a question for your team."
                : $"{users} reaches {system.Name.ToLowerInvariant()}.");
        }

        foreach (var actor in context.Actors.Where(actor => actor.DeclaredFacts.Count > 0))
        {
            fits.Add($"What the {DisplayKind(unitKinds[actor.UnitName])} ({DisplayName(actor.UnitName)}) declares about access: {string.Join(" ", actor.DeclaredFacts)}");
        }

        // ---- Decisions ---------------------------------------------------------------------------
        var summaryDecisions = input.Gates
            .OrderByDescending(gate => gate.AffectedNodes.Count).ThenBy(gate => gate.Id, StringComparer.Ordinal)
            .Select(gate =>
            {
                var wording = ClientLanguage.GateFor(gate.Id, gate.Title);
                var affected = gate.AffectedNodes.Select(node => unitMap.UnitOfNode.GetValueOrDefault(node)).Where(unit => unit is not null && unitKinds.GetValueOrDefault(unit) != DeployableUnits.NotPlacedKind).Select(unit => DisplayName(unit!)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

                return new SummaryDecision(wording.Question, wording.Why, affected.Count == 0 ? "" : string.Join(", ", affected));
            })
            .ToList();

        // ---- Risks ---------------------------------------------------------------------------------
        var risks = new List<string>();

        var lowAndWaiting = areas.Where(area => area.Confidence == "Low" && area.BlockedByGates.Count > 0 && area.Kind != DeployableUnits.NotPlacedKind).ToList();

        if (lowAndWaiting.Count > 0)
        {
            risks.Add($"Work in {JoinNatural(lowAndWaiting.Select(area => DisplayName(area.Name)).ToList())} cannot be sized firmly until the decisions above are made, so today's sizes are indications, not commitments.");
        }

        var replace = analysis.Components.Where(component => component.Characteristics.Contains(MigrationCharacteristic.RequiresArchitecturalReplacement)).ToList();

        if (replace.Count > 0)
        {
            var byArea = replace.GroupBy(component => unitMap.UnitOfNode.GetValueOrDefault(component.NodeId) ?? DeployableUnits.NotPlacedName).OrderByDescending(group => group.Count()).ThenBy(group => group.Key, StringComparer.Ordinal).Take(2)
                .Select(group => $"{group.Count()} in {DisplayName(group.Key)}").ToList();

            risks.Add($"{Plural(replace.Count, "component")} will have to be redesigned rather than upgraded ({string.Join(", ", byArea)}). These are where most of the effort and risk sit.");
        }

        foreach (var note in unitMap.Units.SelectMany(unit => unit.Notes.Where(note => note.StartsWith("References project", StringComparison.Ordinal)).Select(note => (unit.Name, note))))
        {
            risks.Add($"The {DisplayKind(unitKinds[note.Name])} ({DisplayName(note.Name)}) is linked directly to code that belongs to another part, as well as calling it. The two cannot be migrated independently until that link is understood.");
        }

        var unassessed = analysis.Components.Count(component => component.Disposition?.StartsWith("Unassessed", StringComparison.Ordinal) == true);

        if (unassessed > 0)
        {
            risks.Add($"{Plural(unassessed, "technology")} was not recognised by the analysis and needs to be reviewed by a person.");
        }

        if (context.Actors.Any(actor => actor.Tags.Contains("No authentication configured")))
        {
            risks.Add("A service declares no authentication in its configuration and appears to rely on a shared application secret. How callers are trusted needs to be decided deliberately in the new design.");
        }

        // ---- What we could not see ----------------------------------------------------------------
        var notSeen = new List<string>();

        notSeen.AddRange(input.CoverageGaps.Select(gap => ClientLanguage.GapQuestion(gap.Category, gap.What)).Distinct(StringComparer.Ordinal));
        notSeen.AddRange(input.Aspects.Where(aspect => aspect.Coverage == AspectCatalogue.None).Select(aspect => ClientLanguage.AspectQuestion(aspect.Id, aspect.Name)));

        foreach (var actor in context.Actors)
        {
            notSeen.AddRange(actor.Unknowns.Select(unknown => $"{DisplayName(actor.UnitName)}: {unknown}"));
        }

        var silent = input.Aspects.Count(aspect => aspect.Coverage != AspectCatalogue.None && aspect.SignalCount == 0 && aspect.Id is not (1 or 12 or 21 or 23 or 24));

        if (silent > 0)
        {
            notSeen.Add($"In {Plural(silent, "further area")} nothing was detected, which may mean none exists or that it lives outside the code (for example scheduled tasks or server settings). The technical report lists them.");
        }

        atAGlance[^1] = $"Things the analysis could not see: {notSeen.Count - (silent > 0 ? 1 : 0)}.";

        // ---- Next steps ----------------------------------------------------------------------------
        var nextSteps = new List<string>
        {
            "Answer the questions under \"What we could not see\". They are things only your team can tell us, and several change the size of the work."
        };

        if (summaryDecisions.Count > 0)
        {
            nextSteps.Add($"Hold short decision sessions for the {summaryDecisions.Count} decisions above, starting with: {JoinNatural(summaryDecisions.Take(3).Select(decision => $"\"{decision.Question}\"").ToList())}");
        }

        nextSteps.Add($"Turn the answers into a phased roadmap. The order the analysis suggests is: {JoinNatural(areas.Where(area => area.Kind != DeployableUnits.NotPlacedKind).Select(area => DisplayName(area.Name)).ToList())}, because each part depends on the ones before it.");
        nextSteps.Add("Before anything is changed, capture how the key user journeys behave today, so the migrated version can be checked against them.");

        var about =
            $"This summary comes from an automated analysis of the source code at commit {Short(assessment.CommitSha)}. It reads code and configuration; it does not build or run anything, and it keeps no source code or database text. " +
            "Sizes are relative effort indicators, not days or cost, and no overall size is given for the estate. Statements about access describe what the code and configuration declare, not what is enforced when the system runs. " +
            "A technical report with the evidence behind every statement is available.";

        return new CustomerSummary(
            $"Application estate summary: {assessment.RepositoryName}",
            "A plain-language view of what you have, what has to be decided, and what we could not see",
            headline,
            atAGlance,
            summaryAreas,
            fits,
            Journeys(flows, unitKinds),
            summaryDecisions,
            risks.Take(6).ToList(),
            notSeen,
            nextSteps,
            about);
    }

    /// <summary>The project name inside brackets: "Website (X)" shows as "X"; names without brackets are unchanged.</summary>
    /// <summary>
    /// The paths that start in one running part and lead on into another, in plain words and without identifiers. Shared libraries are
    /// compiled into the parts that use them, so reaching them is not counted as leading on. Nothing is said when no flow crosses.
    /// </summary>
    private static List<string> Journeys(Flows.FlowSet? flows, Dictionary<string, string> unitKinds)
    {
        var lines = new List<string>();

        if (flows is null)
        {
            return lines;
        }

        string Describe(string unit) => unitKinds.TryGetValue(unit, out var kind) ? $"{DisplayKind(kind)} ({DisplayName(unit)})" : DisplayName(unit);

        var groups = flows.Flows
            .Select(flow => (Flow: flow, Parts: flow.Steps.Where(step => step.Boundary is not null)
                .Select(step => step.Boundary!.Replace("crosses into ", "", StringComparison.Ordinal))
                .Where(part => part != DeployableUnits.SharedName && part != flow.Unit).Distinct(StringComparer.Ordinal).ToList()))
            .Where(item => item.Parts.Count > 0)
            .GroupBy(item => (item.Flow.Unit, Route: string.Join(">", item.Parts)))
            .OrderBy(group => group.Key.Unit, StringComparer.Ordinal).ThenBy(group => group.Key.Route, StringComparer.Ordinal);

        foreach (var group in groups)
        {
            var members = group.Select(item => item.Flow).ToList();
            var parts = group.First().Parts;
            var touches = new List<string>();

            void Touch(string kind, string words)
            {
                if (members.Any(flow => flow.Terminals.Any(terminal => terminal.Kind == kind)))
                {
                    touches.Add(words);
                }
            }

            Touch("Data", "the data");
            Touch("Outside system", "outside systems");
            Touch("Session", "session state");
            Touch("Authentication", "sign-in or access checks");
            Touch("Configuration", "configuration settings");

            var count = members.Count;
            var line = $"{(count == 1 ? "One entry point" : $"{count} entry points")} in the {Describe(group.Key.Unit)} lead{(count == 1 ? "s" : "")} on into the {JoinNatural(parts.Select(Describe).ToList())}.";

            if (touches.Count > 0)
            {
                line += $" Along the way they touch {JoinNatural(touches)}.";
            }

            var unfinished = members.Count(flow => flow.CouldNotFollow.Count > 0);

            if (unfinished > 0)
            {
                line += $" For {(unfinished == 1 ? "one of them" : $"{unfinished} of them")} the analysis could not follow every call, so a person should check the path.";
            }

            lines.Add(line);
        }

        if (lines.Count > 0)
        {
            lines.Add("These are the paths that have to keep working while the parts are migrated one at a time.");
        }

        return lines;
    }

    internal static string DisplayName(string name)
    {
        if (name == DeployableUnits.SharedName)
        {
            return "Shared code";
        }

        if (name == DeployableUnits.NotPlacedName)
        {
            return "Items not placed";
        }

        var open = name.IndexOf(" (", StringComparison.Ordinal);

        return open > 0 && name.EndsWith(')') ? name[(open + 2)..^1] : name;
    }

    internal static string DisplayKind(string kind) => kind switch
    {
        DeployableUnits.DataSourceKind => "data source",
        DeployableUnits.SharedKind => "shared code",
        _ => kind.StartsWith("WCF", StringComparison.Ordinal) || kind.StartsWith("Windows", StringComparison.Ordinal) ? kind : kind.ToLowerInvariant()
    };

    private static string FrameworkName(string framework)
    {
        if (framework.StartsWith("net4", StringComparison.OrdinalIgnoreCase) && framework.Length is 5 or 6 && framework[3..].All(char.IsDigit))
        {
            return $".NET Framework {string.Join('.', framework[3..].ToCharArray())}";
        }

        return framework;
    }

    private static string Plural(int count, string noun) => $"{count} {(count == 1 ? noun : noun + "s")}";

    private static string Short(string commit) => commit.Length > 8 ? commit[..8] : commit;

    private static string JoinNatural(IReadOnlyList<string> items) => items.Count switch
    {
        0 => "",
        1 => items[0],
        2 => $"{items[0]} and {items[1]}",
        _ => string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1]
    };
}
