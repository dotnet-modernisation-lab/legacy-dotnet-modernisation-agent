using System.Text;

namespace LegacyDotNetModernisationAgent.Strategy;

public static class BehaviourCriteriaWriter
{
    public static string Markdown(BehaviourCriteria criteria, string repository)
    {
        var text = new StringBuilder();

        text.Append("# Behaviour criteria: ").Append(repository).Append("\n\n");

        foreach (var note in criteria.Notes)
        {
            text.Append("- ").Append(note).Append('\n');
        }

        text.Append("\n## Decide before carrying forward\n\nThese are things found in the current system that a replacement would copy by default. Each needs a decision: keep, change or drop.\n\n");

        if (criteria.DecideFirst.Count == 0)
        {
            text.Append("Nothing was found that needs a decision before it is carried forward.\n");
        }

        for (var i = 0; i < criteria.DecideFirst.Count; i++)
        {
            var item = criteria.DecideFirst[i];

            text.Append(i + 1).Append(". **").Append(item.Statement).Append("** ").Append(item.Why).Append(" _(").Append(item.Evidence).Append(")_\n");
        }

        var substantial = criteria.Criteria.Where(criterion => criterion.HasSubstance).ToList();

        foreach (var part in substantial.GroupBy(criterion => criterion.Part).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            text.Append("\n## ").Append(CustomerSummaryBuilder.DisplayName(part.Key)).Append('\n');

            foreach (var criterion in part)
            {
                text.Append("\n### ").Append(criterion.Id).Append(": ").Append(criterion.Capability).Append("\n\n");
                text.Append("_Status: candidate, derived from the code; not confirmed._\n\n");

                for (var i = 0; i < criterion.Given.Count; i++)
                {
                    text.Append(i == 0 ? "**Given** " : "**And** ").Append(criterion.Given[i]).Append("  \n");
                }

                text.Append("**When** ").Append(criterion.When).Append("  \n");

                if (criterion.Then.Count == 0)
                {
                    text.Append("**Then** no effect on data, outside systems or remembered information was found, and what the caller receives is to be confirmed  \n");
                }

                for (var i = 0; i < criterion.Then.Count; i++)
                {
                    text.Append(i == 0 ? "**Then** " : "**And** ").Append("the system ").Append(criterion.Then[i]).Append("  \n");
                }

                text.Append("\n**To confirm:**\n\n");

                foreach (var item in criterion.ToConfirm)
                {
                    text.Append("- ").Append(item).Append('\n');
                }

                if (criterion.Mechanisms.Count > 0)
                {
                    text.Append("\n**Used today, not required of a replacement:** ").Append(string.Join("; ", criterion.Mechanisms)).Append('\n');
                }

                text.Append("\n_Evidence: ").Append(criterion.Evidence).Append("_\n");
            }
        }

        var empty = criteria.Criteria.Where(criterion => !criterion.HasSubstance).ToList();

        if (empty.Count > 0)
        {
            text.Append("\n## Nothing declared or found\n\nFor these, no access rule, input or effect was found. That says what the tool could see, not that nothing happens.\n\n| Capability | Part |\n|---|---|\n");

            foreach (var criterion in empty)
            {
                text.Append("| ").Append(criterion.Capability).Append(" | ").Append(CustomerSummaryBuilder.DisplayName(criterion.Part)).Append(" |\n");
            }
        }

        return text.ToString();
    }
}
