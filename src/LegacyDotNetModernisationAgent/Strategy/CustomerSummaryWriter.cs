using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace LegacyDotNetModernisationAgent.Strategy;

/// <summary>Renders the customer summary as Markdown and as a styled Word document. A .docx is a zip with timestamps, so only word/document.xml is byte-stable.</summary>
public static class CustomerSummaryWriter
{
    private static readonly DateTime FixedDate = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private const string Ink = "1F2D3D";
    private const string Muted = "5B6770";
    private const string Navy = "1F3A5F";

    public static string Markdown(CustomerSummary summary, string? diagramFile = null)
    {
        var text = new StringBuilder();

        text.Append("# ").Append(summary.Title).Append("\n\n_").Append(summary.Subtitle).Append("_\n\n");
        text.Append("> ").Append(summary.Headline).Append("\n\n");

        foreach (var line in summary.AtAGlance)
        {
            text.Append("- ").Append(line).Append('\n');
        }

        text.Append("\n## The parts of your estate\n\n");

        if (diagramFile is not null)
        {
            text.Append("![Architecture overview](").Append(diagramFile).Append(")\n\n");
        }

        text.Append("| Part | What it is | What it contains | Size | Confidence |\n|---|---|---|---|---|\n");

        foreach (var area in summary.Areas)
        {
            text.Append("| **").Append(area.Name).Append("**").Append(ShowKind(area) ? $" ({area.Kind})" : "").Append(" | ").Append(area.What).Append(" | ")
                .Append(Detail(area, ". ")).Append(" | ")
                .Append(area.SizeWords).Append(" (").Append(area.Size).Append(") | ").Append(area.Confidence).Append(" |\n");
        }

        text.Append("\n## How the parts fit together\n\n");

        foreach (var line in summary.HowItFits)
        {
            text.Append("- ").Append(line).Append('\n');
        }

        text.Append("\n## Decisions only your team can make\n\n| Decision | Why it matters | Affects |\n|---|---|---|\n");

        foreach (var decision in summary.Decisions)
        {
            text.Append("| ").Append(decision.Question).Append(" | ").Append(decision.Why).Append(" | ").Append(decision.Areas).Append(" |\n");
        }

        text.Append("\n## Main risks\n\n");

        foreach (var risk in summary.Risks)
        {
            text.Append("- ").Append(risk).Append('\n');
        }

        text.Append("\n## What we could not see\n\nThese are not assumed to be fine. Each is a question for your team.\n\n");

        foreach (var item in summary.NotSeen)
        {
            text.Append("- ").Append(item).Append('\n');
        }

        text.Append("\n## Suggested next steps\n\n");

        for (var i = 0; i < summary.NextSteps.Count; i++)
        {
            text.Append(i + 1).Append(". ").Append(summary.NextSteps[i]).Append('\n');
        }

        text.Append("\n---\n\n_").Append(summary.About).Append("_\n");

        return text.ToString();
    }

    public static void Word(string path, CustomerSummary summary)
    {
        using var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);

        var main = document.AddMainDocumentPart();
        main.Document = new Document(new Body());
        var body = main.Document.Body!;

        document.PackageProperties.Creator = "legacy-dotnet-modernisation-agent";
        document.PackageProperties.Title = summary.Title;
        document.PackageProperties.Created = FixedDate;
        document.PackageProperties.Modified = FixedDate;
        document.PackageProperties.LastModifiedBy = "legacy-dotnet-modernisation-agent";

        body.Append(Para(summary.Title, size: 44, bold: true, colour: Navy, after: 40));
        body.Append(Para(summary.Subtitle, size: 22, colour: Muted, after: 200));
        body.Append(Callout(summary.Headline));

        foreach (var line in summary.AtAGlance)
        {
            body.Append(Bullet(line));
        }

        body.Append(Heading("The parts of your estate"));
        body.Append(Table(
            ["Part", "What it is", "What it contains", "Size", "Confidence"],
            [18, 24, 34, 12, 12],
            summary.Areas.Select(area => new[]
            {
                ShowKind(area) ? $"{area.Name}\n{area.Kind}" : area.Name,
                area.What,
                Detail(area, "\n"),
                $"{area.SizeWords} ({area.Size})",
                area.Confidence
            }).ToList(),
            shadeColumn: 3,
            shade: row => SizeShade(summary.Areas[row].Size)));

        body.Append(Heading("How the parts fit together"));

        foreach (var line in summary.HowItFits)
        {
            body.Append(Bullet(line));
        }

        body.Append(Heading("Decisions only your team can make"));
        body.Append(Table(
            ["Decision", "Why it matters", "Affects"],
            [38, 42, 20],
            summary.Decisions.Select(decision => new[] { decision.Question, decision.Why, decision.Areas }).ToList()));

        body.Append(Heading("Main risks"));

        foreach (var risk in summary.Risks)
        {
            body.Append(Bullet(risk));
        }

        body.Append(Heading("What we could not see"));
        body.Append(Para("These are not assumed to be fine. Each is a question for your team.", size: 20, colour: Muted, after: 80));

        foreach (var item in summary.NotSeen)
        {
            body.Append(Bullet(item));
        }

        body.Append(Heading("Suggested next steps"));

        for (var i = 0; i < summary.NextSteps.Count; i++)
        {
            body.Append(Para($"{i + 1}.  {summary.NextSteps[i]}", size: 21, after: 80, indent: 280, hanging: 280));
        }

        body.Append(Para(summary.About, size: 17, colour: Muted, after: 0, before: 240, italic: true));

        body.Append(new SectionProperties(
            new PageSize { Width = 11906u, Height = 16838u },
            new PageMargin { Top = 907, Bottom = 907, Left = 1021u, Right = 1021u, Header = 454u, Footer = 454u }));

        main.Document.Save();
    }

    /// <summary>The kind is shown under the name, except for the special areas whose name already says what they are.</summary>
    private static bool ShowKind(SummaryArea area) => area.Kind is not (DeployableUnits.DataSourceKind or DeployableUnits.SharedKind or DeployableUnits.NotPlacedKind);

    private static string Detail(SummaryArea area, string separator)
    {
        var parts = new List<string>();

        if (area.Contains.Length > 0)
        {
            parts.Add(area.Contains + (separator == "\n" ? "" : ""));
        }

        if (area.Uses.Length > 0)
        {
            parts.Add($"Technologies: {area.Uses}");
        }

        return parts.Count == 0 ? "Nothing further was assessed here." : string.Join(separator, parts) + (separator == "\n" ? "" : ".");
    }

    private static string SizeShade(string size) => size switch
    {
        "XS" or "S" => "D9F2E3",
        "M" => "FFF1C9",
        _ => "FBD5D5"
    };

    private static Paragraph Heading(string text) =>
        new(
            new ParagraphProperties(
                new KeepNext(),
                new ParagraphBorders(new BottomBorder { Val = BorderValues.Single, Size = 8, Color = "9FB3CC", Space = 2 }),
                new SpacingBetweenLines { Before = "280", After = "100" }),
            Run(text, 28, true, Navy));

    private static Paragraph Bullet(string text) => Para("•  " + text, size: 21, after: 60, indent: 300, hanging: 300);

    private static Paragraph Para(string text, int size = 21, bool bold = false, string colour = Ink, int after = 80, int before = 0, int indent = 0, int hanging = 0, bool italic = false)
    {
        var properties = new ParagraphProperties(new SpacingBetweenLines { Before = before.ToString(), After = after.ToString(), Line = "264", LineRule = LineSpacingRuleValues.Auto });

        if (indent > 0)
        {
            properties.Append(new Indentation { Left = indent.ToString(), Hanging = hanging > 0 ? hanging.ToString() : null });
        }

        return new Paragraph(properties, Run(text, size, bold, colour, italic));
    }

    private static Paragraph Callout(string text) =>
        new(
            new ParagraphProperties(
                new ParagraphBorders(
                    new LeftBorder { Val = BorderValues.Single, Size = 36, Color = "3B6EA5", Space = 8 }),
                new Shading { Val = ShadingPatternValues.Clear, Fill = "EAF1F9" },
                new SpacingBetweenLines { Before = "60", After = "160", Line = "276", LineRule = LineSpacingRuleValues.Auto },
                new Indentation { Left = "160", Right = "100" }),
            Run(text, 23, false, Ink));

    private static Run Run(string text, int size, bool bold, string colour, bool italic = false)
    {
        var properties = new RunProperties(new RunFonts { Ascii = "Calibri", HighAnsi = "Calibri" }, new Color { Val = colour }, new FontSize { Val = size.ToString() });

        if (bold)
        {
            properties.Append(new Bold());
        }

        if (italic)
        {
            properties.Append(new Italic());
        }

        var run = new Run(properties);
        var lines = text.Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                run.Append(new Break());
            }

            run.Append(new Text(lines[i]) { Space = SpaceProcessingModeValues.Preserve });
        }

        return run;
    }

    private static Table Table(string[] headers, int[] widths, List<string[]> rows, int shadeColumn = -1, Func<int, string>? shade = null)
    {
        var table = new Table(new TableProperties(
            new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
            new TableLayout { Type = TableLayoutValues.Fixed },
            new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 4, Color = "C8D3E0" },
                new BottomBorder { Val = BorderValues.Single, Size = 4, Color = "C8D3E0" },
                new LeftBorder { Val = BorderValues.Single, Size = 4, Color = "C8D3E0" },
                new RightBorder { Val = BorderValues.Single, Size = 4, Color = "C8D3E0" },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4, Color = "C8D3E0" },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = 4, Color = "C8D3E0" }),
            new TableCellMarginDefault(
                new TopMargin { Width = "50", Type = TableWidthUnitValues.Dxa },
                new LeftMargin { Width = "90", Type = TableWidthUnitValues.Dxa },
                new BottomMargin { Width = "50", Type = TableWidthUnitValues.Dxa },
                new RightMargin { Width = "90", Type = TableWidthUnitValues.Dxa })));

        var header = new TableRow(new TableRowProperties(new TableHeader(), new CantSplit()));

        for (var c = 0; c < headers.Length; c++)
        {
            header.Append(Cell(headers[c], widths[c], fill: Navy, bold: true, colour: "FFFFFF"));
        }

        table.Append(header);

        for (var r = 0; r < rows.Count; r++)
        {
            var row = new TableRow(new TableRowProperties(new CantSplit()));

            for (var c = 0; c < rows[r].Length; c++)
            {
                var first = c == 0;
                var fill = c == shadeColumn && shade is not null ? shade(r) : r % 2 == 1 ? "F6F8FB" : null;

                row.Append(Cell(rows[r][c], widths[c], fill, bold: first || c == shadeColumn, colour: Ink));
            }

            table.Append(row);
        }

        return table;
    }

    private static TableCell Cell(string text, int widthPercent, string? fill, bool bold, string colour)
    {
        var properties = new TableCellProperties(new TableCellWidth { Width = (widthPercent * 50).ToString(), Type = TableWidthUnitValues.Pct });

        if (fill is not null)
        {
            properties.Append(new Shading { Val = ShadingPatternValues.Clear, Fill = fill });
        }

        return new TableCell(properties, new Paragraph(new ParagraphProperties(new SpacingBetweenLines { After = "0" }), Run(text, 18, bold, colour)));
    }
}
