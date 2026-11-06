using System.Text;

namespace LegacyDotNetModernisationAgent.ServiceContracts;

public static class ServiceContractWriter
{
    public static string Markdown(ServiceContract contract)
    {
        var text = new StringBuilder();

        text.Append("# Service contract\n\nSource: ").Append(contract.Source).Append("\n\n");
        text.Append("Read from the service description. Shapes show what is on the wire: element names, types, how often each may appear and whether it may be nil.\n");

        foreach (var operation in contract.Facts.Where(fact => fact.Kind == "operation"))
        {
            text.Append("\n## ").Append(operation.Key["operation/".Length..]).Append('\n');

            var shapes = contract.Facts
                .Where(fact => fact.Key.StartsWith(operation.Key + "/", StringComparison.Ordinal) && fact.Kind is "element" or "attribute" or "enum" or "fault" or "note")
                .GroupBy(fact => fact.Direction)
                .OrderBy(group => group.Key, StringComparer.Ordinal);

            foreach (var group in shapes)
            {
                text.Append("\n**").Append(group.Key switch { "in" => "Callers send", "out" => "Callers receive", _ => "Faults" }).Append(":**\n\n");

                foreach (var fact in group)
                {
                    text.Append("- `").Append(fact.Key[(operation.Key.Length + 1)..]).Append("`: ").Append(fact.Value).Append('\n');
                }
            }
        }

        var framing = contract.Facts.Where(fact => fact.Kind is "binding" or "soapAction" or "style" or "use" or "policy").ToList();

        if (framing.Count > 0)
        {
            text.Append("\n## How messages are framed\n\n");

            foreach (var fact in framing)
            {
                text.Append("- `").Append(fact.Key).Append("`: ").Append(fact.Value.Length == 0 ? "(empty)" : fact.Value).Append('\n');
            }
        }

        var endpoints = contract.Facts.Where(fact => fact.Kind is "endpoint" or "endpointContract").ToList();

        if (endpoints.Count > 0)
        {
            text.Append("\n## Endpoints\n\n");

            foreach (var fact in endpoints)
            {
                text.Append("- `").Append(fact.Key).Append("`: ").Append(fact.Value).Append('\n');
            }
        }

        AppendNotAnalysed(text, contract.NotAnalysed);

        return text.ToString();
    }

    public static string Markdown(ContractDiff diff)
    {
        var text = new StringBuilder();

        text.Append("# Service contract comparison\n\nOld: ").Append(diff.OldSource).Append("\n\nNew: ").Append(diff.NewSource).Append("\n\n");
        text.Append("Each difference is judged from the point of view of a caller built against the old contract. ")
            .Append("**Breaking**: that caller would fail or misbehave. **Review**: a person must judge it. **Compatible**: that caller is unaffected. ")
            .Append("**Informational**: not a contract change. The rules are fixed and conservative; they do not know how your callers use the service.\n\n");

        if (diff.Changes.Count == 0)
        {
            text.Append("No differences were found in what the tool compares.\n");
        }

        foreach (var group in diff.Changes.GroupBy(change => change.Class).OrderBy(group => group.Key))
        {
            text.Append("## ").Append(group.Key).Append(" (").Append(group.Count()).Append(")\n\n| Where | Old | New | Why |\n|---|---|---|---|\n");

            foreach (var change in group)
            {
                text.Append("| `").Append(change.Key).Append("` | ").Append(Cell(change.Old)).Append(" | ").Append(Cell(change.New)).Append(" | ").Append(change.Reason).Append(" |\n");
            }

            text.Append('\n');
        }

        AppendNotAnalysed(text, diff.NotAnalysed);

        return text.ToString();
    }

    private static string Cell(string? value) =>
        value is null ? "(absent)" : value.Length == 0 ? "(empty)" : value.Replace("|", "\\|", StringComparison.Ordinal);

    private static void AppendNotAnalysed(StringBuilder text, IReadOnlyList<string> notes)
    {
        text.Append("\n## What the tool could not read or compare\n\n");

        foreach (var note in notes)
        {
            text.Append("- ").Append(note).Append('\n');
        }

        text.Append(notes.Count == 0 ? "" : "\n");
        text.Append("Security policies are compared only as the names of what they declare, not their settings. Behaviour behind the service is not compared.\n");
    }
}
