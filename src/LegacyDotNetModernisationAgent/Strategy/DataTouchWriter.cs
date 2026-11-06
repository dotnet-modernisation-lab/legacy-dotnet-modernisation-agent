using System.Text;

namespace LegacyDotNetModernisationAgent.Strategy;

public static class DataTouchWriter
{
    /// <summary>With more parts than this the matrix would not fit a page, so each data object lists its parts instead.</summary>
    public const int MaxMatrixColumns = 5;

    public static string Cell(DataObjectRow row, string part) =>
        row.ByPart.TryGetValue(part, out var cell) ? cell.Access + (cell.ViaProcedure ? "(p)" : "") : "";

    public static string Touchers(DataObjectRow row) =>
        string.Join("; ", row.ByPart.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => $"{(item.Key == DataTouchBuilder.NotReached ? "no entry point" : CustomerSummaryBuilder.DisplayName(item.Key))} {item.Value.Access}{(item.Value.ViaProcedure ? "(p)" : "")}"));

    public static string Markdown(DataTouchMap map, string repository)
    {
        var text = new StringBuilder();

        text.Append("# Data touched: ").Append(repository).Append("\n\n");

        foreach (var note in map.Notes)
        {
            text.Append("- ").Append(note).Append('\n');
        }

        text.Append("\n## Written from more than one part or component\n\n");

        if (map.SharedWrites.Count == 0)
        {
            text.Append("No data object is written from more than one part or component.\n");
        }

        foreach (var fact in map.SharedWrites)
        {
            text.Append("- ").Append(fact.Statement).Append('\n');
        }

        text.Append("\n## Every data object\n\n| Data object | Kind | Touched by | Written by | Read or executed by | Note |\n|---|---|---|---|---|---|\n");

        foreach (var row in map.Rows)
        {
            text.Append("| ").Append(row.Name).Append(" | ").Append(row.Kind).Append(" | ").Append(Touchers(row)).Append(" | ")
                .Append(ComponentMapWriter.Join(row.WrittenBy, 6)).Append(" | ").Append(ComponentMapWriter.Join(row.ReadBy, 6)).Append(" | ").Append(row.Note ?? "").Append(" |\n");
        }

        return text.ToString();
    }
}
