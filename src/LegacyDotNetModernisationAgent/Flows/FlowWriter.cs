using System.Text;

namespace LegacyDotNetModernisationAgent.Flows;

/// <summary>Renders the flow set as Markdown: one section per entry point, grouped by deployable unit. Ordering and wording are fixed so a run is byte-stable.</summary>
public static class FlowWriter
{
    public static string Markdown(FlowSet set)
    {
        var text = new StringBuilder();

        text.Append("# Flows: ").Append(set.Repository).Append("\n\nCommit ").Append(set.CommitSha).Append(".\n\n");

        foreach (var note in set.Notes)
        {
            text.Append("- ").Append(note).Append('\n');
        }

        foreach (var unit in set.Flows.GroupBy(flow => flow.Unit).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            text.Append("\n## ").Append(unit.Key).Append('\n');

            foreach (var flow in unit)
            {
                text.Append("\n### ").Append(flow.EntryType).Append('.').Append(flow.EntryMethod).Append("\n\n");
                text.Append("- **Starts as:** ").Append(flow.EntryKind).Append(" (").Append(flow.EntryEvidence).Append(")\n");

                if (flow.Inputs.Count > 0)
                {
                    text.Append("- **Reads from the request:** ").Append(string.Join(", ", flow.Inputs)).Append('\n');
                }

                if (flow.Access.Count > 0)
                {
                    text.Append("- **Declared access that mentions it:** ").Append(string.Join("; ", flow.Access)).Append('\n');
                }

                text.Append("- **Path:**\n");

                for (var i = 0; i < flow.Steps.Count; i++)
                {
                    var step = flow.Steps[i];

                    text.Append("  ").Append(i + 1).Append(". `").Append(step.Type).Append('.').Append(step.Method).Append("` (").Append(step.Basis).Append("; ")
                        .Append(step.Location).Append(':').Append(step.Line).Append(')').Append(step.Boundary is null ? "" : $" **{step.Boundary}**").Append('\n');
                }

                if (flow.Terminals.Count > 0)
                {
                    text.Append("- **Ends at:**\n");

                    foreach (var terminal in flow.Terminals)
                    {
                        text.Append("  - ").Append(terminal.Kind).Append(": `").Append(terminal.Target).Append("` - ").Append(terminal.Detail).Append('\n');
                    }
                }

                if (flow.CouldNotFollow.Count > 0)
                {
                    text.Append("- **Could not follow:**\n");

                    foreach (var gap in flow.CouldNotFollow)
                    {
                        text.Append("  - `").Append(gap.Point).Append("` at ").Append(gap.Location).Append(':').Append(gap.Line).Append(": ").Append(gap.Reason).Append('\n');
                    }
                }

                text.Append("- **Coverage:** ").Append(flow.Coverage).Append('\n');
            }
        }

        return text.ToString();
    }
}
