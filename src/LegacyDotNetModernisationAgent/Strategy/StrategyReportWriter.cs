using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace LegacyDotNetModernisationAgent.Strategy;

/// <summary>
/// Renders the report. The deterministic sections (gates, coverage, open questions, components needing attention) come
/// straight from the input, so the report is useful with no model. The strategy sections are marked with the validation
/// status of each claim. A .docx is a zip with timestamps, so the file is not byte-stable; its word/document.xml is.
/// </summary>
public static class StrategyReportWriter
{
    private static readonly DateTime FixedDate = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private sealed record Block(string Kind, string Text, string[]? Row = null);

    public static string Markdown(LegacyMigrationAnalysisInput input, string inputSha256, LegacyMigrationStrategy strategy, StrategyValidationReport report) =>
        string.Join("\n", Blocks(input, inputSha256, strategy, report).Select(block => block.Kind switch
        {
            "title" => $"# {block.Text}\n",
            "h1" => $"\n## {block.Text}\n",
            "h2" => $"\n### {block.Text}\n",
            "bullet" => $"- {block.Text}",
            "row" => "| " + string.Join(" | ", block.Row!.Select(cell => cell.Replace("|", "/", StringComparison.Ordinal))) + " |",
            "header" => "| " + string.Join(" | ", block.Row!) + " |\n|" + string.Concat(Enumerable.Repeat("---|", block.Row!.Length)),
            _ => block.Text
        })) + "\n";

    public static void Word(string path, LegacyMigrationAnalysisInput input, string inputSha256, LegacyMigrationStrategy strategy, StrategyValidationReport report)
    {
        using var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);

        var main = document.AddMainDocumentPart();
        main.Document = new Document(new Body());
        var body = main.Document.Body!;

        document.PackageProperties.Creator = "legacy-dotnet-modernisation-agent";
        document.PackageProperties.Title = $"Migration strategy: {input.Repository}";
        document.PackageProperties.Created = FixedDate;
        document.PackageProperties.Modified = FixedDate;
        document.PackageProperties.LastModifiedBy = "legacy-dotnet-modernisation-agent";

        var blocks = Blocks(input, inputSha256, strategy, report);

        for (var index = 0; index < blocks.Count; index++)
        {
            var block = blocks[index];

            switch (block.Kind)
            {
                case "header" or "row":
                    {
                        var rows = new List<Block>();

                        while (index < blocks.Count && blocks[index].Kind is "header" or "row")
                        {
                            rows.Add(blocks[index]);
                            index++;
                        }

                        index--;
                        body.Append(Table(rows));
                        break;
                    }
                case "title":
                    body.Append(Paragraph(block.Text, bold: true, size: "40"));
                    break;
                case "h1":
                    body.Append(Paragraph(block.Text, bold: true, size: "30"));
                    break;
                case "h2":
                    body.Append(Paragraph(block.Text, bold: true, size: "24"));
                    break;
                case "bullet":
                    body.Append(Paragraph("• " + block.Text));
                    break;
                default:
                    body.Append(Paragraph(block.Text));
                    break;
            }
        }

        main.Document.Save();
    }

    private static Paragraph Paragraph(string text, bool bold = false, string? size = null)
    {
        var properties = new RunProperties();

        if (bold)
        {
            properties.Append(new Bold());
        }

        if (size is not null)
        {
            properties.Append(new FontSize { Val = size });
        }

        return new Paragraph(new Run(properties, new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
    }

    private static Table Table(List<Block> rows)
    {
        var table = new Table(new TableProperties(new TableBorders(
            new TopBorder { Val = BorderValues.Single, Size = 4 },
            new BottomBorder { Val = BorderValues.Single, Size = 4 },
            new LeftBorder { Val = BorderValues.Single, Size = 4 },
            new RightBorder { Val = BorderValues.Single, Size = 4 },
            new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4 },
            new InsideVerticalBorder { Val = BorderValues.Single, Size = 4 })));

        foreach (var row in rows)
        {
            var tableRow = new TableRow();

            foreach (var cell in row.Row!)
            {
                tableRow.Append(new TableCell(Paragraph(cell, bold: row.Kind == "header")));
            }

            table.Append(tableRow);
        }

        return table;
    }

    private static List<Block> Blocks(LegacyMigrationAnalysisInput input, string inputSha256, LegacyMigrationStrategy strategy, StrategyValidationReport report)
    {
        var blocks = new List<Block>();
        var status = report.Claims.GroupBy(claim => claim.Claim, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First().Status, StringComparer.Ordinal);

        void Text(string text) => blocks.Add(new Block("p", text));
        void H1(string text) => blocks.Add(new Block("h1", text));
        void H2(string text) => blocks.Add(new Block("h2", text));
        void Bullet(string text) => blocks.Add(new Block("bullet", text));
        void Header(params string[] cells) => blocks.Add(new Block("header", "", cells));
        void Row(params string[] cells) => blocks.Add(new Block("row", "", cells));
        string Mark(string claim) => status.TryGetValue(claim, out var value) ? $"[{value}] " : "";

        blocks.Add(new Block("title", $"Migration strategy: {input.Repository}"));
        Text($"Commit {input.CommitSha}. Platform {input.Platform}. {input.ProjectCount} project(s), {input.NodeCount} graph node(s), {input.ComponentsAssessed} component(s) assessed.");
        Text($"Evidence hash (SHA-256 of the input bundle): {inputSha256}. Strategy produced by: {strategy.Model}.");
        Text(report.Accepted
            ? "Validation: accepted. Every gate and every unknown is addressed, and every cited ID exists in the evidence."
            : $"Validation: NOT accepted. {report.Violations.Count} violation(s) are listed at the end of this report.");
        Text("This is an assessment aid. The classification below is fixed by deterministic analysis; the strategy only describes and recommends. Decisions belong to people.");

        H1("Summary");
        Text(Mark("Summary") + strategy.Summary);

        H1("Decisions only a person can make");

        if (input.Gates.Count == 0)
        {
            Text("No decision gates were raised.");
        }
        else
        {
            Header("Gate", "Decision", "Affects", "Rules");

            foreach (var gate in input.Gates)
            {
                Row(gate.Id, gate.Title, $"{gate.AffectedNodes.Count} component(s)", string.Join(", ", gate.RuleIds));
            }

            foreach (var note in strategy.GateNotes.OrderBy(note => note.GateId, StringComparer.Ordinal))
            {
                H2($"{note.GateId} (confidence {note.Confidence})");
                Text(Mark($"Gate {note.GateId}") + note.Recommendation);

                foreach (var option in note.Options)
                {
                    Bullet($"{option.Name}: {option.Advantages} Drawbacks: {option.Drawbacks}");
                }
            }
        }

        H1("Work areas: size and order");
        Text("Relative T-shirt sizes and a dependency-aware order, calculated from the deterministic classification (not estimated by a model). A size is an effort indicator, not a number of days. Confidence is Low where the size rests on unassessed technology or undecided gates.");
        Header("Order", "Area", "Size", "Points", "Confidence", "Code units", "Technologies", "Waits on decisions", "Drivers");

        foreach (var area in input.WorkAreas.OrderBy(area => area.Order))
        {
            Row(area.Order.ToString(), area.Name, area.Size, area.Points.ToString(), area.Confidence, area.CodeUnitCount.ToString(), area.TechnologyCount.ToString(), string.Join(", ", area.BlockedByGates), string.Join("; ", area.Drivers));
        }

        var lowConfidence = input.WorkAreas.Count(area => area.Confidence == "Low");
        Text($"Areas with Low confidence: {lowConfidence} of {input.WorkAreas.Count}. Sizes are per area and are not added up into one number for the estate.");

        foreach (var area in input.WorkAreas.Where(area => area.Note is not null))
        {
            Bullet($"{area.Name}: {area.Note}");
        }

        H2("How sizes are calculated");

        foreach (var line in input.SizingRubric)
        {
            Bullet(line);
        }

        H1("Proposed phases");

        foreach (var phase in strategy.Phases.OrderBy(phase => phase.Number))
        {
            H2($"Phase {phase.Number}: {phase.Title}");
            Text(Mark($"Phase {phase.Number}") + phase.Rationale);
            Text($"Waits on gates: {(phase.PrerequisiteGates.Count == 0 ? "none" : string.Join(", ", phase.PrerequisiteGates))}. Components: {phase.NodeIds.Count}.");
        }

        if (strategy.Risks.Count > 0)
        {
            H1("Risks");

            foreach (var risk in strategy.Risks)
            {
                Bullet($"{Mark($"Risk: {risk.Title}")}{risk.Title}: {risk.Description}");
            }
        }

        if (strategy.Recommendations.Count > 0)
        {
            H1("Recommendations");

            foreach (var recommendation in strategy.Recommendations)
            {
                Bullet(recommendation.Text);
            }
        }

        H1("Open questions for people");
        Text("Things the tool could not see, and gaps in what it analysed. None of these is assumed to be fine.");

        foreach (var question in strategy.OpenQuestions)
        {
            Bullet($"{(question.AspectId is { } aspect ? $"Aspect {aspect}" : question.GapKey)} ({question.Audience}): {question.Question}");
        }

        H1("Components needing attention");
        Header("Component", "Kind", "Area", "Layer", "Characteristics", "Gates");

        foreach (var component in input.Components)
        {
            Row(component.Name, component.Kind, component.Unit, component.Layer, string.Join(", ", component.Characteristics), string.Join(", ", component.GateIds));
        }

        foreach (var omission in input.OmittedComponents)
        {
            Bullet($"Not listed individually: {omission.Count} {omission.Kind} component(s) with no bound characteristic or beyond the listing limit.");
        }

        if (input.ReviewHints.Count > 0)
        {
            H1("Security review hints");
            Text("Hints for a security reviewer only. They are not findings about enforcement and do not affect the classification.");

            foreach (var hint in input.ReviewHints)
            {
                Bullet(hint);
            }
        }

        if (!report.Accepted)
        {
            H1("Validation violations");
            Header("Code", "Subject", "Message");

            foreach (var violation in report.Violations)
            {
                Row(violation.Code, violation.Subject, violation.Message);
            }
        }

        return blocks;
    }
}
