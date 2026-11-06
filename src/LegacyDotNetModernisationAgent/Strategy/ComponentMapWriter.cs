using System.Text;

namespace LegacyDotNetModernisationAgent.Strategy;

/// <summary>Markdown form of the component map: every component of every part, with no cap. The Word document shows the most connected ones.</summary>
public static class ComponentMapWriter
{
    public const string Framing = "A component is a folder of code inside a part (the first folder below the project, or one level deeper when a project keeps everything in one folder). Dependencies are those found in the code; references between assemblies and links made only by naming convention are left out. \"Not classified\" means the tool could not place the code in a layer. Whether a dependency is wanted depends on the intended design, which the tool cannot see.";

    public static string Markdown(ComponentMap map, string repository)
    {
        var text = new StringBuilder();

        text.Append("# Components: ").Append(repository).Append("\n\n").Append(Framing).Append('\n');

        foreach (var unit in map.Components.GroupBy(component => component.Unit).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            text.Append("\n## ").Append(CustomerSummaryBuilder.DisplayName(unit.Key)).Append("\n\n| Component | Layer | Main items | Depends on | Used by |\n|---|---|---|---|---|\n");

            foreach (var component in unit)
            {
                text.Append("| ").Append(component.Name).Append(" | ").Append(string.Join(", ", component.Layers)).Append(" | ")
                    .Append(Join(component.MainItems, 6)).Append(" | ").Append(Join(component.DependsOn, 8)).Append(" | ").Append(Join(component.UsedBy, 8)).Append(" |\n");
            }
        }

        AppendFacts(text, "Loops between components", map.Loops, "No components depend on each other in a loop.");
        AppendFacts(text, "Presentation code that skips layers", map.DirectReaches, "No presentation code was found depending directly on data access or persistence.");
        AppendFacts(text, "Most depended-on components", map.MostUsed, "No component is used by another.");

        return text.ToString();
    }

    public static string Join(IReadOnlyList<string> items, int limit) =>
        items.Count == 0 ? "-" : string.Join(", ", items.Take(limit)) + (items.Count > limit ? $" and {items.Count - limit} more" : "");

    private static void AppendFacts(StringBuilder text, string heading, IReadOnlyList<ComponentFact> facts, string none)
    {
        text.Append("\n## ").Append(heading).Append("\n\n");

        if (facts.Count == 0)
        {
            text.Append(none).Append('\n');

            return;
        }

        foreach (var fact in facts)
        {
            text.Append("- ").Append(fact.Statement).Append('\n');
        }
    }
}
