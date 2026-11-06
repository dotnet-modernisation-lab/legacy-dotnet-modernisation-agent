using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using LegacyDotNetModernisationAgent.Flows;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace LegacyDotNetModernisationAgent.Strategy;

/// <summary>
/// The architecture document: the overview diagram, the parts of the estate, who uses them and what they reach outside, and for each
/// part the entry points and what each one does, read from the code. It states what the tool could not see. It carries no overall size.
/// Only word/document.xml is byte-stable; the zip has timestamps and the PNG depends on installed fonts.
/// </summary>
public static class ArchitectureDocumentWriter
{
    private static readonly DateTime FixedDate = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private const string Muted = "5B6770";
    private const string Navy = "1F3A5F";

    public static void Word(string path, string repository, CustomerSummary summary, EstateContext context, FlowSet flows, (byte[] Png, int Width, int Height)? diagram)
    {
        using var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);

        var main = document.AddMainDocumentPart();
        main.Document = new Document(new Body());
        var body = main.Document.Body!;
        var title = $"Architecture: {repository}";

        document.PackageProperties.Creator = "legacy-dotnet-modernisation-agent";
        document.PackageProperties.Title = title;
        document.PackageProperties.Created = FixedDate;
        document.PackageProperties.Modified = FixedDate;
        document.PackageProperties.LastModifiedBy = "legacy-dotnet-modernisation-agent";

        var entries = new List<(string Title, int Level, string Name)>();

        Paragraph Mark(Paragraph paragraph, string heading, int level)
        {
            var id = entries.Count + 1;
            var name = $"section{id}";

            paragraph.InsertAt(new BookmarkStart { Id = id.ToString(), Name = name }, 1);
            paragraph.Append(new BookmarkEnd { Id = id.ToString() });

            if (level > 1 && paragraph.ParagraphProperties is { } properties)
            {
                properties.Append(new OutlineLevel { Val = level - 1 });
            }

            entries.Add((heading, level, name));

            return paragraph;
        }

        body.Append(CustomerSummaryWriter.Para(title, size: 44, bold: true, colour: Navy, after: 40));
        body.Append(CustomerSummaryWriter.Para("How the estate is put together, read from the code", size: 22, colour: Muted, after: 200));
        body.Append(CustomerSummaryWriter.Callout("This document shows the deployable parts of the estate, who uses them as far as the code shows, what they connect to outside the estate, and what each entry point does. Everything in it is read from the code and configuration. Nothing was run, and where the tool could not follow or could not see, it says so."));

        var contentsPlaceholder = new Paragraph();

        body.Append(contentsPlaceholder);

        // ---- Overview ---------------------------------------------------------------------------------
        body.Append(Mark(CustomerSummaryWriter.Heading("Overview"), "Overview", 1));

        if (diagram is { } picture)
        {
            body.Append(Picture(main, picture.Png, picture.Width, picture.Height, 17.0, "Architecture overview diagram. It shows the people and callers, the parts of the estate inside a dashed boundary, the systems outside it, and labelled arrows between them."));
        }

        body.Append(CustomerSummaryWriter.Para("Cards are the deployable parts. The bar and badge show relative size, and the chips show confidence and open decisions. People and callers are deduced from what each project is, and who they are is not visible in a repository. The dashed boundary is the estate found in the repository. Arrows point from a part to the part it depends on.", size: 19, colour: Muted, after: 120, before: 80));

        body.Append(Mark(CustomerSummaryWriter.Heading("The parts"), "The parts", 1));
        body.Append(CustomerSummaryWriter.Table(
            ["Part", "What it is", "What it contains", "Size", "Confidence"],
            [18, 24, 34, 12, 12],
            summary.Areas.Select(area => new[]
            {
                CustomerSummaryWriter.ShowKind(area) ? $"{area.Name}\n{area.Kind}" : area.Name,
                area.What,
                CustomerSummaryWriter.Detail(area, "\n"),
                $"{area.SizeWords} ({area.Size})",
                area.Confidence
            }).ToList()));

        foreach (var line in summary.HowItFits.Where(line => !line.StartsWith("What the ", StringComparison.Ordinal)))
        {
            body.Append(CustomerSummaryWriter.Bullet(line));
        }

        // ---- People and outside systems -----------------------------------------------------------------
        body.Append(Mark(CustomerSummaryWriter.Heading("Who uses it, and what it reaches"), "Who uses it, and what it reaches", 1));

        foreach (var actor in context.Actors)
        {
            body.Append(CustomerSummaryWriter.Para($"{actor.Label}: {CustomerSummary_DisplayName(actor.UnitName)}", size: 21, bold: true, after: 20, before: 80));
            body.Append(CustomerSummaryWriter.Para(actor.Basis, size: 19, colour: Muted, after: 20));

            foreach (var fact in actor.DeclaredFacts)
            {
                body.Append(CustomerSummaryWriter.Bullet(fact));
            }

            foreach (var unknown in actor.Unknowns)
            {
                body.Append(CustomerSummaryWriter.Bullet("Not known: " + unknown));
            }
        }

        if (context.ExternalSystems.Count > 0)
        {
            body.Append(CustomerSummaryWriter.Para("Outside the estate", size: 21, bold: true, after: 20, before: 120));

            foreach (var system in context.ExternalSystems)
            {
                body.Append(CustomerSummaryWriter.Bullet($"{system.Name}, used by {string.Join(", ", system.UsedBy.Select(CustomerSummary_DisplayName))}"));
            }
        }

        // ---- Entry points per part ----------------------------------------------------------------------------
        body.Append(Mark(CustomerSummaryWriter.Heading("What each part does"), "What each part does", 1));
        body.Append(CustomerSummaryWriter.Para("Each row is an entry point: something outside the code starts it. The path is followed by name through the code, only where one target is plausible.", size: 19, colour: Muted, after: 80));

        if (flows.Flows.Count == 0)
        {
            body.Append(CustomerSummaryWriter.Para("No entry points were recognised, so no flows are shown. Either the code has none the tool knows about, or the project kinds are not yet covered.", size: 21, after: 80));
        }

        foreach (var unit in flows.Flows.GroupBy(flow => flow.Unit).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            body.Append(Mark(CustomerSummaryWriter.Para(CustomerSummary_DisplayName(unit.Key), size: 24, bold: true, colour: Navy, after: 60, before: 160), CustomerSummary_DisplayName(unit.Key), 2));

            body.Append(CustomerSummaryWriter.Table(
                ["Entry point", "Starts as", "Reads from the request", "Ends at", "Not followed"],
                [22, 16, 16, 38, 8],
                unit.Select(flow => new[]
                {
                    $"{flow.EntryType}.{flow.EntryMethod}",
                    flow.EntryKind == "Page or control event" && flow.EntryEvidence.StartsWith("naming convention", StringComparison.Ordinal) ? $"{flow.EntryKind} (by naming convention, unconfirmed)" : flow.EntryKind,
                    flow.Inputs.Count == 0 ? "-" : string.Join(", ", flow.Inputs),
                    EndsAt(flow),
                    flow.CouldNotFollow.Count == 0 ? "-" : flow.CouldNotFollow.Count.ToString()
                }).ToList()));
        }

        // ---- Journeys that cross a boundary ---------------------------------------------------------------------
        // Shared libraries are compiled into the parts that use them, so reaching them is not a runtime link between parts.
        string[] RunningParts(Flow flow) => flow.Steps.Where(step => step.Boundary is not null)
            .Select(step => CustomerSummary_DisplayName(step.Boundary!.Replace("crosses into ", "", StringComparison.Ordinal)))
            .Where(part => part != "Shared code").Distinct(StringComparer.Ordinal).ToArray();

        var crossing = flows.Flows.Where(flow => RunningParts(flow).Length > 0).ToList();

        if (crossing.Count > 0)
        {
            body.Append(Mark(CustomerSummaryWriter.Heading("Journeys that cross from one part to another"), "Journeys that cross from one part to another", 1));
            body.Append(CustomerSummaryWriter.Para("These are the paths that matter most when parts are migrated separately, because the link between the parts has to keep working.", size: 19, colour: Muted, after: 80));

            foreach (var flow in crossing)
            {
                var route = new List<string> { CustomerSummary_DisplayName(flow.Unit) };

                foreach (var part in RunningParts(flow))
                {
                    if (route[^1] != part)
                    {
                        route.Add(part);
                    }
                }

                body.Append(CustomerSummaryWriter.Bullet($"{flow.EntryType}.{flow.EntryMethod}: {string.Join(" to ", route)}. Ends at {EndsAt(flow)}"));
            }
        }

        // ---- What could not be seen -------------------------------------------------------------------------------
        body.Append(Mark(CustomerSummaryWriter.Heading("What the tool could not follow or see"), "What the tool could not follow or see", 1));

        var gaps = flows.Flows.SelectMany(flow => flow.CouldNotFollow.Select(gap => (flow, gap)))
            .GroupBy(item => (item.gap.Point, item.gap.Reason, item.gap.Location, item.gap.Line))
            .OrderBy(group => group.Key.Location, StringComparer.Ordinal).ThenBy(group => group.Key.Line)
            .ToList();

        if (gaps.Count == 0)
        {
            body.Append(CustomerSummaryWriter.Para("No point was found where the tool had to stop following a flow for a reason it could name.", size: 21, after: 80));
        }

        foreach (var gap in gaps)
        {
            body.Append(CustomerSummaryWriter.Bullet($"{gap.Key.Point} ({gap.Key.Location}, line {gap.Key.Line}): {gap.Key.Reason}"));
        }

        foreach (var item in summary.NotSeen)
        {
            body.Append(CustomerSummaryWriter.Bullet(item));
        }

        foreach (var note in flows.Notes)
        {
            body.Append(CustomerSummaryWriter.Bullet(note));
        }

        body.Append(CustomerSummaryWriter.Para("Calls into constructors, properties and lambdas are not indexed. Calls through a variable are followed only into a type the calling code refers to.", size: 17, colour: Muted, after: 0, before: 240, italic: true));

        var contents = new List<Paragraph> { CustomerSummaryWriter.Para("Contents", size: 24, bold: true, colour: Navy, after: 60, before: 120) };

        foreach (var (entry, level, name) in entries)
        {
            contents.Add(new Paragraph(
                new ParagraphProperties(
                    new SpacingBetweenLines { After = "30" },
                    new Indentation { Left = (level == 1 ? 0 : 360).ToString() }),
                new Hyperlink(CustomerSummaryWriter.Run(entry, level == 1 ? 21 : 19, false, level == 1 ? Navy : Muted)) { Anchor = name, History = true }));
        }

        foreach (var paragraph in contents)
        {
            contentsPlaceholder.InsertBeforeSelf(paragraph);
        }

        contentsPlaceholder.Remove();

        body.Append(new SectionProperties(
            new PageSize { Width = 11906u, Height = 16838u },
            new PageMargin { Top = 907, Bottom = 907, Left = 1021u, Right = 1021u, Header = 454u, Footer = 454u }));

        main.Document.Save();
    }

    private static string CustomerSummary_DisplayName(string unitName) => CustomerSummaryBuilder.DisplayName(unitName);

    /// <summary>Terminals as a short list per kind, with the internal id prefixes removed (db:table:dbo.orders becomes dbo.orders).</summary>
    private static string EndsAt(Flow flow)
    {
        if (flow.Terminals.Count == 0)
        {
            return "nothing recognised";
        }

        return string.Join("; ", flow.Terminals
            .GroupBy(terminal => terminal.Kind)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => $"{group.Key}: {string.Join(", ", group.Select(terminal => Plain(terminal.Target)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))}"));
    }

    private static string Plain(string target)
    {
        var colon = target.LastIndexOf(':');

        return colon >= 0 ? target[(colon + 1)..] : target;
    }

    private static Paragraph Picture(MainDocumentPart main, byte[] png, int widthPixels, int heightPixels, double widthCentimetres, string description)
    {
        var part = main.AddImagePart(ImagePartType.Png, "rIdArchitectureOverview");

        using (var stream = new MemoryStream(png))
        {
            part.FeedData(stream);
        }

        var id = main.GetIdOfPart(part);
        var cx = (long)(widthCentimetres * 360000);
        var cy = cx * heightPixels / widthPixels;

        var drawing = new Drawing(
            new DW.Inline(
                new DW.Extent { Cx = cx, Cy = cy },
                new DW.DocProperties { Id = 1U, Name = "Architecture overview", Description = description },
                new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoChangeAspect = true }),
                new A.Graphic(
                    new A.GraphicData(
                        new PIC.Picture(
                            new PIC.NonVisualPictureProperties(
                                new PIC.NonVisualDrawingProperties { Id = 0U, Name = "architecture-overview.png", Description = description },
                                new PIC.NonVisualPictureDrawingProperties()),
                            new PIC.BlipFill(new A.Blip { Embed = id }, new A.Stretch(new A.FillRectangle())),
                            new PIC.ShapeProperties(
                                new A.Transform2D(new A.Offset { X = 0L, Y = 0L }, new A.Extents { Cx = cx, Cy = cy }),
                                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle })))
                    { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }))
            { DistanceFromTop = 0U, DistanceFromBottom = 0U, DistanceFromLeft = 0U, DistanceFromRight = 0U });

        return new Paragraph(new ParagraphProperties(new SpacingBetweenLines { After = "80" }), new Run(drawing));
    }
}
