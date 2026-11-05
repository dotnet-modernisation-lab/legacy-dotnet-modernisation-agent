using System.Text.RegularExpressions;

namespace LegacyDotNetModernisationAgent.Strategy;

/// <summary>
/// Checks a returned strategy against the evidence it was given. It reports every violation (it does not stop at
/// the first) and gives each claim a status: Verified (cites real evidence), Unverified (cites none) or Rejected
/// (cites something that is not in the input, which is treated as an invented fact). The strategy has no fields for
/// classification, so the policy cannot be changed by it; this checks the rest.
/// </summary>
public static partial class StrategyValidator
{
    [GeneratedRegex(@"\b(has been|was|is now|already|now)\s+(decided|resolved|approved|settled|closed|agreed)\b|\b(decision|gate)\s+(is|was)\s+(made|closed|resolved)\b", RegexOptions.IgnoreCase)]
    private static partial Regex DecidedClaim();

    public const string Verified = "Verified";
    public const string Unverified = "Unverified";
    public const string Rejected = "Rejected";

    public static StrategyValidationReport Validate(LegacyMigrationAnalysisInput input, string inputSha256, LegacyMigrationStrategy strategy)
    {
        var violations = new List<Violation>();
        var claims = new List<ClaimStatus>();

        var gateIds = input.Gates.Select(gate => gate.Id).ToHashSet(StringComparer.Ordinal);
        var nodeIds = input.Components.Select(component => component.NodeId)
            .Concat(input.Gates.SelectMany(gate => gate.AffectedNodes))
            .ToHashSet(StringComparer.Ordinal);
        var ruleIds = input.Components.SelectMany(component => component.RuleIds).Concat(input.Gates.SelectMany(gate => gate.RuleIds)).ToHashSet(StringComparer.Ordinal);
        var gapKeys = input.CoverageGaps.Select(gap => gap.Key).ToHashSet(StringComparer.Ordinal);
        var aspectIds = input.Aspects.Select(aspect => aspect.Id).ToHashSet();
        var known = gateIds.Concat(nodeIds).Concat(ruleIds).Concat(gapKeys).ToHashSet(StringComparer.Ordinal);

        if (!string.Equals(strategy.InputSha256, inputSha256, StringComparison.OrdinalIgnoreCase))
        {
            violations.Add(new Violation("V-HASH", "InputSha256", "The strategy was not produced from this input (hash mismatch); it cannot be tied to this evidence."));
        }

        if (string.IsNullOrWhiteSpace(strategy.Model))
        {
            violations.Add(new Violation("V-PROVENANCE", "Model", "The strategy does not name the model or generator that produced it."));
        }

        if (string.IsNullOrWhiteSpace(strategy.Summary))
        {
            violations.Add(new Violation("V-EMPTY", "Summary", "The strategy has no summary."));
        }

        void Claim(string claim, string text, IReadOnlyList<string> evidence)
        {
            var unknown = evidence.Where(id => !known.Contains(id)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

            if (unknown.Count > 0)
            {
                violations.Add(new Violation("V-UNKNOWN-ID", claim, $"Cites evidence that is not in the input: {string.Join(", ", unknown)}."));
                claims.Add(new ClaimStatus(claim, Rejected, $"unknown evidence: {string.Join(", ", unknown)}"));
            }
            else if (evidence.Count == 0)
            {
                claims.Add(new ClaimStatus(claim, Unverified, "cites no evidence"));
            }
            else
            {
                claims.Add(new ClaimStatus(claim, Verified, $"cites {evidence.Count} item(s) present in the input"));
            }

            if (DecidedClaim().IsMatch(text))
            {
                violations.Add(new Violation("V-GATE-DECIDED", claim, "The text says a decision has been made. Only a person decides a gate; the strategy may only recommend."));
            }
        }

        Claim("Summary", strategy.Summary, strategy.SummaryEvidence);

        // Every gate is addressed exactly once.
        foreach (var group in strategy.GateNotes.GroupBy(note => note.GateId, StringComparer.Ordinal))
        {
            if (!gateIds.Contains(group.Key))
            {
                violations.Add(new Violation("V-GATE-UNKNOWN", group.Key, "A gate note refers to a gate that is not in the input."));
            }
            else if (group.Count() > 1)
            {
                violations.Add(new Violation("V-GATE-DUPLICATE", group.Key, "More than one note for the same gate."));
            }
        }

        foreach (var missing in gateIds.Except(strategy.GateNotes.Select(note => note.GateId), StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            violations.Add(new Violation("V-GATE-MISSING", missing, "The input has this decision gate but the strategy does not address it."));
        }

        foreach (var note in strategy.GateNotes)
        {
            Claim($"Gate {note.GateId}", note.Recommendation + " " + string.Join(" ", note.Options.Select(option => $"{option.Name} {option.Advantages} {option.Drawbacks}")), note.Evidence);
        }

        // Phases: nodes exist, each appears once, and every gate on a node is a prerequisite of its phase.
        var gatesOfNode = input.Gates
            .SelectMany(gate => gate.AffectedNodes.Select(node => (Node: node, Gate: gate.Id)))
            .ToLookup(pair => pair.Node, pair => pair.Gate, StringComparer.Ordinal);

        var phaseOfNode = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var phase in strategy.Phases.OrderBy(phase => phase.Number))
        {
            var subject = $"Phase {phase.Number}";

            foreach (var gate in phase.PrerequisiteGates.Where(gate => !gateIds.Contains(gate)))
            {
                violations.Add(new Violation("V-GATE-UNKNOWN", subject, $"Lists prerequisite gate {gate}, which is not in the input."));
            }

            foreach (var node in phase.NodeIds)
            {
                if (!nodeIds.Contains(node))
                {
                    violations.Add(new Violation("V-UNKNOWN-ID", subject, $"Places node {node}, which is not in the input."));

                    continue;
                }

                if (phaseOfNode.TryGetValue(node, out var earlier))
                {
                    violations.Add(new Violation("V-PHASE-DUPLICATE", subject, $"Node {node} is already in phase {earlier}."));

                    continue;
                }
                else
                {
                    phaseOfNode[node] = phase.Number;
                }

                foreach (var gate in gatesOfNode[node].Where(gate => !phase.PrerequisiteGates.Contains(gate, StringComparer.Ordinal)))
                {
                    violations.Add(new Violation("V-PHASE-GATE", subject, $"Node {node} is gated by {gate}, which is not a prerequisite of this phase."));
                }

                var component = input.Components.FirstOrDefault(c => c.NodeId == node);

                if (component?.Disposition is { } disposition && disposition.StartsWith("Unassessed", StringComparison.Ordinal)
                    && !phase.PrerequisiteGates.Contains("D-UNASSESSED-TECH", StringComparer.Ordinal))
                {
                    violations.Add(new Violation("V-PHASE-UNASSESSED", subject, $"Node {node} is unassessed; the phase must list D-UNASSESSED-TECH as a prerequisite."));
                }
            }

            Claim(subject, phase.Rationale, phase.Evidence);
        }

        foreach (var risk in strategy.Risks)
        {
            Claim($"Risk: {risk.Title}", risk.Description, risk.Evidence);
        }

        foreach (var recommendation in strategy.Recommendations)
        {
            Claim($"Recommendation: {Shorten(recommendation.Text)}", recommendation.Text, recommendation.Evidence);
        }

        // Nothing the tool could not see may go unasked: every "None" aspect and every coverage gap is an open question.
        var askedAspects = strategy.OpenQuestions.Where(question => question.AspectId is not null).Select(question => question.AspectId!.Value).ToHashSet();
        var askedGaps = strategy.OpenQuestions.Where(question => question.GapKey is not null).Select(question => question.GapKey!).ToHashSet(StringComparer.Ordinal);

        foreach (var question in strategy.OpenQuestions)
        {
            if ((question.AspectId is null) == (question.GapKey is null))
            {
                violations.Add(new Violation("V-QUESTION-SHAPE", Shorten(question.Question), "An open question must name exactly one of AspectId or GapKey."));
            }
            else if (question.AspectId is { } aspect && !aspectIds.Contains(aspect))
            {
                violations.Add(new Violation("V-UNKNOWN-ID", Shorten(question.Question), $"Refers to aspect {aspect}, which is not in the input."));
            }
            else if (question.GapKey is { } gap && !gapKeys.Contains(gap))
            {
                violations.Add(new Violation("V-UNKNOWN-ID", Shorten(question.Question), $"Refers to coverage gap {gap}, which is not in the input."));
            }
        }

        foreach (var aspect in input.Aspects.Where(aspect => aspect.Coverage == AspectCatalogue.None && !askedAspects.Contains(aspect.Id)))
        {
            violations.Add(new Violation("V-QUESTION-MISSING", $"aspect {aspect.Id}", $"'{aspect.Name}' is not detectable from the repository and must be an open question."));
        }

        foreach (var gap in input.CoverageGaps.Where(gap => !askedGaps.Contains(gap.Key)))
        {
            violations.Add(new Violation("V-QUESTION-MISSING", gap.Key, $"Coverage gap is not an open question: {gap.What}"));
        }

        var ordered = violations
            .OrderBy(violation => violation.Code, StringComparer.Ordinal)
            .ThenBy(violation => violation.Subject, StringComparer.Ordinal)
            .ThenBy(violation => violation.Message, StringComparer.Ordinal)
            .ToList();

        return new StrategyValidationReport(ordered.Count == 0, ordered, claims);
    }

    private static string Shorten(string text) => text.Length <= 60 ? text : text[..57] + "...";
}
