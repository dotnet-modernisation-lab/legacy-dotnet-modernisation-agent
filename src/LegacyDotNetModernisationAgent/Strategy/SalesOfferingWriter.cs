using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace LegacyDotNetModernisationAgent.Strategy;

/// <summary>
/// Renders the sales offering Markdown (docs/sales-offering.md) as a Word document in the same style as the customer summary.
/// It understands only the small Markdown subset that file uses: #, ##, paragraphs, bullets, numbered lines and pipe tables.
/// The Markdown stays the single source of the wording.
/// </summary>
public static class SalesOfferingWriter
{
    private static readonly DateTime FixedDate = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static void Word(string markdownPath, string docxPath)
    {
        var lines = File.ReadAllLines(markdownPath);

        using var document = WordprocessingDocument.Create(docxPath, WordprocessingDocumentType.Document);
        var main = document.AddMainDocumentPart();
        main.Document = new Document(new Body());
        var body = main.Document.Body!;

        document.PackageProperties.Creator = "legacy-dotnet-modernisation-agent";
        document.PackageProperties.Created = FixedDate;
        document.PackageProperties.Modified = FixedDate;
        document.PackageProperties.LastModifiedBy = "legacy-dotnet-modernisation-agent";

        var firstParagraph = true;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd();

            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                document.PackageProperties.Title = Plain(line[2..]);
                body.Append(CustomerSummaryWriter.Para(Plain(line[2..]), size: 44, bold: true, colour: "1F3A5F", after: 80));
            }
            else if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                body.Append(CustomerSummaryWriter.Heading(Plain(line[3..])));
            }
            else if (line.StartsWith("|", StringComparison.Ordinal))
            {
                var rows = new List<string[]>();

                while (i < lines.Length && lines[i].TrimStart().StartsWith('|'))
                {
                    var cells = lines[i].Trim().Trim('|').Split('|').Select(cell => Plain(cell.Trim())).ToArray();

                    if (!cells.All(cell => cell.Length > 0 && cell.All(c => c is '-' or ':')))
                    {
                        rows.Add(cells);
                    }

                    i++;
                }

                i--;
                var columns = rows[0].Length;
                var widths = columns == 3 ? new[] { 22, 55, 23 } : Enumerable.Repeat(100 / columns, columns).ToArray();
                body.Append(CustomerSummaryWriter.Table(rows[0], widths, rows.Skip(1).ToList()));
                body.Append(CustomerSummaryWriter.Para("", size: 8, after: 40));
            }
            else if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                body.Append(CustomerSummaryWriter.Bullet(Plain(line[2..])));
            }
            else if (char.IsDigit(line[0]) && line.Contains(". ", StringComparison.Ordinal))
            {
                body.Append(CustomerSummaryWriter.Para(Plain(line), size: 21, after: 80, indent: 280, hanging: 280));
            }
            else if (firstParagraph)
            {
                body.Append(CustomerSummaryWriter.Callout(Plain(line)));
            }
            else
            {
                body.Append(CustomerSummaryWriter.Para(Plain(line)));
            }

            firstParagraph = false;
        }

        body.Append(new SectionProperties(
            new PageSize { Width = 11906u, Height = 16838u },
            new PageMargin { Top = 907, Bottom = 907, Left = 1021u, Right = 1021u, Header = 454u, Footer = 454u }));

        main.Document.Save();
    }

    private static string Plain(string text) => text.Replace("**", "").Replace("`", "");
}
