using System.Globalization;
using System.Net;
using System.Text;
using LegacyDotNetModernisationAgent.Models;

namespace LegacyDotNetModernisationAgent.Strategy;

/// <summary>
/// Draws the architecture from the deterministic facts only, so a diagram is never an impression: every box and arrow is a
/// unit or a dependency the tool found. The overview (SVG and Mermaid) shows the deployable areas, how big each is, what
/// waits on decisions, and what depends on what. A per-area detail view (Mermaid) shows the code inside one area.
/// Output is byte-identical for the same input.
/// </summary>
public static class ArchitectureDiagram
{
    private const int BoxWidth = 330;
    private const int BoxHeight = 146;
    private const int GapX = 70;
    private const int GapY = 84;
    private const int Margin = 40;
    private const int TitleHeight = 92;
    private const int LegendHeight = 70;

    public const int MaxDetailNodes = 25;

    // ---- Overview -----------------------------------------------------------------------

    public static string OverviewSvg(LegacyMigrationAnalysisInput input)
    {
        var placed = input.WorkAreas.Where(area => area.Kind != DeployableUnits.NotPlacedKind).OrderBy(area => area.Order).ToList();
        var notPlaced = input.WorkAreas.Where(area => area.Kind == DeployableUnits.NotPlacedKind).ToList();
        var levels = Levels(placed);
        var rows = placed.GroupBy(area => levels[area.Name]).OrderByDescending(group => group.Key).Select(group => group.OrderBy(area => area.Order).ToList()).ToList();

        var widest = Math.Max(1, rows.Count == 0 ? 1 : rows.Max(row => row.Count));
        var laneCount = placed.Sum(area => area.DependsOn.Count(dependency => levels.ContainsKey(dependency) && levels[area.Name] - levels[dependency] != 1));
        var gutter = laneCount * 22 + (laneCount > 0 ? 30 : 0);
        var width = Math.Max(Margin * 2 + widest * BoxWidth + (widest - 1) * GapX + gutter, 880);

        var bodyHeight = rows.Count * BoxHeight + Math.Max(0, rows.Count - 1) * GapY;
        var notPlacedHeight = notPlaced.Count > 0 ? BoxHeight + 56 : 0;
        var height = TitleHeight + bodyHeight + notPlacedHeight + LegendHeight + Margin;

        var position = new Dictionary<string, (int X, int Y)>(StringComparer.Ordinal);

        for (var r = 0; r < rows.Count; r++)
        {
            var rowWidth = rows[r].Count * BoxWidth + (rows[r].Count - 1) * GapX;
            var left = (width - gutter - rowWidth) / 2;

            for (var c = 0; c < rows[r].Count; c++)
            {
                position[rows[r][c].Name] = (left + c * (BoxWidth + GapX), TitleHeight + r * (BoxHeight + GapY));
            }
        }

        if (notPlaced.Count > 0)
        {
            position[notPlaced[0].Name] = (Margin, TitleHeight + bodyHeight + 56);
        }

        var svg = new StringBuilder();
        svg.Append(CultureInfo.InvariantCulture, $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\" viewBox=\"0 0 {width} {height}\" font-family=\"Segoe UI, Helvetica, Arial, sans-serif\">\n");
        svg.Append("<defs>\n");
        svg.Append("<linearGradient id=\"page\" x1=\"0\" y1=\"0\" x2=\"0\" y2=\"1\"><stop offset=\"0\" stop-color=\"#f4f7fb\"/><stop offset=\"1\" stop-color=\"#e9eef6\"/></linearGradient>\n");
        svg.Append("<filter id=\"shadow\" x=\"-10%\" y=\"-10%\" width=\"120%\" height=\"130%\"><feDropShadow dx=\"0\" dy=\"3\" stdDeviation=\"4\" flood-color=\"#1f2d3d\" flood-opacity=\"0.16\"/></filter>\n");
        svg.Append("<marker id=\"arrow\" viewBox=\"0 0 10 10\" refX=\"8\" refY=\"5\" markerWidth=\"9\" markerHeight=\"9\" orient=\"auto\"><path d=\"M 0 0 L 10 5 L 0 10 z\" fill=\"#5f7089\"/></marker>\n");
        svg.Append("</defs>\n");
        svg.Append(CultureInfo.InvariantCulture, $"<rect width=\"{width}\" height=\"{height}\" fill=\"url(#page)\"/>\n");
        svg.Append(Text(Margin, 40, "Architecture overview", 26, "700", "#1f2d3d"));
        svg.Append(Text(Margin, 64, $"{input.Repository}  ·  commit {(input.CommitSha.Length > 8 ? input.CommitSha[..8] : input.CommitSha)}", 13, "600", "#44546a"));

        // Arrows first, so cards sit on top.
        var lane = 0;

        foreach (var area in placed)
        {
            var dependencies = area.DependsOn.Where(dependency => position.ContainsKey(dependency)).Order(StringComparer.Ordinal).ToList();

            for (var i = 0; i < dependencies.Count; i++)
            {
                var dependency = dependencies[i];
                var from = position[area.Name];
                var to = position[dependency];

                if (levels.ContainsKey(dependency) && levels[area.Name] - levels[dependency] == 1)
                {
                    var startX = from.X + BoxWidth * (i + 1) / (dependencies.Count + 1);
                    var endX = to.X + BoxWidth / 2 + (i - (dependencies.Count - 1) / 2) * 24;
                    var sy = from.Y + BoxHeight;
                    var ey = to.Y - 2;
                    var mid = (ey - sy) / 2;

                    svg.Append(CultureInfo.InvariantCulture, $"<path d=\"M {startX} {sy} C {startX} {sy + mid}, {endX} {ey - mid}, {endX} {ey}\" stroke=\"#5f7089\" stroke-width=\"2\" fill=\"none\" marker-end=\"url(#arrow)\"/>\n");
                }
                else
                {
                    // An arrow that skips a row, or runs sideways, curves round the outside so it never crosses a card.
                    var bulge = width - gutter + 22 + lane * 22;
                    lane++;
                    var y1 = from.Y + BoxHeight / 2 + i * 8;
                    var y2 = to.Y + BoxHeight / 2 + i * 8;

                    svg.Append(CultureInfo.InvariantCulture, $"<path d=\"M {from.X + BoxWidth} {y1} C {bulge} {y1}, {bulge} {y2}, {to.X + BoxWidth + 3} {y2}\" stroke=\"#5f7089\" stroke-width=\"2\" fill=\"none\" marker-end=\"url(#arrow)\"/>\n");
                }
            }
        }

        foreach (var area in placed)
        {
            Box(svg, area, position[area.Name], dashed: false);
        }

        if (notPlaced.Count > 0)
        {
            var p = position[notPlaced[0].Name];
            svg.Append(Text(Margin, p.Y - 16, "Counted, but the tool could not tie these to any area", 12, "600", "#6b7a8d"));
            Box(svg, notPlaced[0], p, dashed: true);
        }

        var legendY = height - LegendHeight + 18;
        svg.Append(Text(Margin, legendY, "SIZE", 11, "700", "#6b7a8d"));
        Badge(svg, Margin + 46, legendY - 11, "#d9f2e3", "#2e7d4f", "XS S");
        Badge(svg, Margin + 112, legendY - 11, "#fff1c9", "#b8860b", "M");
        Badge(svg, Margin + 158, legendY - 11, "#fbd5d5", "#b03030", "L XL");
        svg.Append(Text(Margin + 230, legendY, "Relative effort, not days. Low confidence means the size rests on unassessed technology or on decisions not yet made. There is no overall size for the estate.", 12, "normal", "#6b7a8d"));
        svg.Append("</svg>\n");

        return svg.ToString();
    }

    public static string OverviewMermaid(LegacyMigrationAnalysisInput input)
    {
        var lines = new List<string> { "flowchart TD" };
        var ids = input.WorkAreas.OrderBy(area => area.Order).Select((area, index) => (area.Name, Id: $"a{index}")).ToDictionary(item => item.Name, item => item.Id, StringComparer.Ordinal);

        foreach (var area in input.WorkAreas.OrderBy(area => area.Order))
        {
            var label = $"<b>{Esc(area.Name)}</b><br/>{Esc(area.Kind)}<br/>{area.CodeUnitCount} code units, {area.TechnologyCount} technologies<br/>Size {area.Size}, {Esc(area.Confidence)} confidence"
                + (area.BlockedByGates.Count > 0 ? $"<br/>{area.BlockedByGates.Count} decision(s) waiting" : "");

            lines.Add(area.Kind == DeployableUnits.NotPlacedKind
                ? $"    {ids[area.Name]}[\"{label}\"]:::notplaced"
                : $"    {ids[area.Name]}[\"{label}\"]:::{SizeClass(area.Size)}");
        }

        foreach (var area in input.WorkAreas.OrderBy(area => area.Order))
        {
            foreach (var dependency in area.DependsOn.Order(StringComparer.Ordinal).Where(ids.ContainsKey))
            {
                lines.Add($"    {ids[area.Name]} --> {ids[dependency]}");
            }
        }

        lines.Add("    classDef small fill:#d9f2e3,stroke:#2e7d4f,color:#1f2d3d");
        lines.Add("    classDef medium fill:#fff1c9,stroke:#b8860b,color:#1f2d3d");
        lines.Add("    classDef large fill:#fbd5d5,stroke:#b03030,color:#1f2d3d");
        lines.Add("    classDef notplaced fill:#ffffff,stroke:#888888,stroke-dasharray: 5 5,color:#1f2d3d");

        return string.Join("\n", lines) + "\n";
    }

    // ---- Detail of one area ---------------------------------------------------------------

    /// <summary>
    /// The code inside one area: at most <see cref="MaxDetailNodes"/> components (those with a decision gate or more than a
    /// recompile first), grouped by layer, with the links between them and a stub for each other area they reach.
    /// Technologies are summarised by count, because listing every framework assembly buries the code.
    /// </summary>
    public static string DetailMermaid(
        string unitName,
        LegacyDependencyGraph graph,
        UnitMap unitMap,
        MigrationAnalysis analysis,
        IReadOnlyDictionary<string, List<string>> gatesOfNode)
    {
        var verdict = analysis.Components.ToDictionary(component => component.NodeId, StringComparer.Ordinal);

        var members = graph.Nodes
            .Where(node => unitMap.UnitOfNode.GetValueOrDefault(node.Id) == unitName)
            .OrderBy(node => node.Id, StringComparer.Ordinal)
            .ToList();

        var code = members.Where(node => !DeployableUnits.TechnologyKinds.Contains(node.Kind)).ToList();
        var technologies = members.Where(node => DeployableUnits.TechnologyKinds.Contains(node.Kind)).ToList();

        int Priority(GraphNode node) =>
            (gatesOfNode.ContainsKey(node.Id) ? 0 : 2) +
            (verdict.TryGetValue(node.Id, out var v) && v.Characteristics.Any(c => c != MigrationCharacteristic.RequiresRecompile) ? 0 : 1);

        var shown = code.OrderBy(Priority).ThenBy(node => node.Id, StringComparer.Ordinal).Take(MaxDetailNodes).OrderBy(node => node.Id, StringComparer.Ordinal).ToList();
        var ids = shown.Select((node, index) => (node.Id, Mid: $"n{index}")).ToDictionary(item => item.Id, item => item.Mid, StringComparer.Ordinal);

        var lines = new List<string> { "flowchart TD" };

        foreach (var layer in shown.GroupBy(node => node.Layer, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            lines.Add($"    subgraph L_{layer.Key.Replace(' ', '_')}[\"{Esc(layer.Key)}\"]");

            foreach (var node in layer)
            {
                var gated = gatesOfNode.TryGetValue(node.Id, out var gates);
                var label = $"<b>{Esc(node.Name)}</b><br/>{Esc(node.Kind)}" + (gated ? $"<br/>decisions: {Esc(string.Join(", ", gates!))}" : "");

                lines.Add($"        {ids[node.Id]}[\"{label}\"]{(gated ? ":::gated" : "")}");
            }

            lines.Add("    end");
        }

        var linked = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var edge in graph.Edges.Where(edge => edge.Basis != "Convention" && ids.ContainsKey(edge.From)))
        {
            if (ids.ContainsKey(edge.To) && edge.From != edge.To)
            {
                linked.Add($"    {ids[edge.From]} --> {ids[edge.To]}");
            }
        }

        lines.AddRange(linked);

        // Other areas this one reaches, as one stub each.
        var stubs = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);

        foreach (var edge in graph.Edges.Where(edge => edge.Basis is not ("Convention" or "AssemblyMetadata") && ids.ContainsKey(edge.From)))
        {
            var target = unitMap.UnitOfNode.GetValueOrDefault(edge.To);

            if (target is not null && target != unitName && !ids.ContainsKey(edge.To))
            {
                if (!stubs.TryGetValue(target, out var from))
                {
                    stubs[target] = from = new SortedSet<string>(StringComparer.Ordinal);
                }

                from.Add(ids[edge.From]);
            }
        }

        var stubIndex = 0;

        foreach (var (target, from) in stubs)
        {
            var stub = $"x{stubIndex++}";
            lines.Add($"    {stub}([\"{Esc(target)}\"]):::other");

            foreach (var source in from)
            {
                lines.Add($"    {source} -.-> {stub}");
            }
        }

        if (technologies.Count > 0)
        {
            var summary = string.Join("<br/>", technologies.GroupBy(node => node.Kind, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal).Select(group => $"{group.Count()} {Esc(group.Key)}"));

            lines.Add($"    tech[\"<b>Technologies used</b><br/>{summary}\"]:::tech");
        }

        if (code.Count > shown.Count)
        {
            lines.Add($"    more[\"+{code.Count - shown.Count} more components not drawn\"]:::tech");
        }

        lines.Add("    classDef gated fill:#fbd5d5,stroke:#b03030,color:#1f2d3d");
        lines.Add("    classDef other fill:#eef2f7,stroke:#44546a,color:#1f2d3d");
        lines.Add("    classDef tech fill:#ffffff,stroke:#888888,stroke-dasharray: 5 5,color:#1f2d3d");

        return string.Join("\n", lines) + "\n";
    }

    public static string Slug(string name)
    {
        var chars = name.ToLowerInvariant().Select(character => char.IsAsciiLetterOrDigit(character) ? character : '-').ToArray();

        return string.Join("-", new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
    }

    // ---- Helpers -------------------------------------------------------------------------

    /// <summary>Row of each area: 0 for an area that depends on nothing else, otherwise one more than what it depends on.</summary>
    private static Dictionary<string, int> Levels(IReadOnlyList<InputWorkArea> areas)
    {
        var byName = areas.ToDictionary(area => area.Name, StringComparer.Ordinal);
        var levels = new Dictionary<string, int>(StringComparer.Ordinal);

        int Level(string name, HashSet<string> visiting)
        {
            if (levels.TryGetValue(name, out var known))
            {
                return known;
            }

            if (!visiting.Add(name))
            {
                return 0; // a cycle: break it here
            }

            var dependencies = byName[name].DependsOn.Where(byName.ContainsKey).ToList();
            var level = dependencies.Count == 0 ? 0 : 1 + dependencies.Max(dependency => Level(dependency, visiting));

            visiting.Remove(name);

            return levels[name] = level;
        }

        foreach (var area in areas.OrderBy(area => area.Order))
        {
            Level(area.Name, new HashSet<string>(StringComparer.Ordinal));
        }

        return levels;
    }

    private static void Box(StringBuilder svg, InputWorkArea area, (int X, int Y) at, bool dashed)
    {
        var accent = dashed ? "#9aa5b4" : SizeStroke(area.Size);
        var badgeFill = dashed ? "#ffffff" : SizeFill(area.Size);
        var x = at.X;
        var y = at.Y;

        svg.Append(CultureInfo.InvariantCulture, $"<g{(dashed ? "" : " filter=\"url(#shadow)\"")}>\n");
        svg.Append(CultureInfo.InvariantCulture, $"<rect x=\"{x}\" y=\"{y}\" width=\"{BoxWidth}\" height=\"{BoxHeight}\" rx=\"12\" fill=\"#ffffff\" stroke=\"{(dashed ? "#9aa5b4" : "#d5dde8")}\" stroke-width=\"{(dashed ? "1.6" : "1.2")}\"{(dashed ? " stroke-dasharray=\"6 5\"" : "")}/>\n");

        if (!dashed)
        {
            // Accent bar down the left edge, rounded to follow the card corners.
            svg.Append(CultureInfo.InvariantCulture, $"<path d=\"M {x + 12} {y} A 12 12 0 0 0 {x} {y + 12} L {x} {y + BoxHeight - 12} A 12 12 0 0 0 {x + 12} {y + BoxHeight} L {x + 12} {y + BoxHeight} L {x + 9} {y + BoxHeight} L {x + 9} {y} z\" fill=\"{accent}\"/>\n");
        }

        svg.Append("</g>\n");

        // Kind label, name, and the size badge.
        svg.Append(Text(x + 26, y + 26, area.Kind.ToUpperInvariant(), 10, "700", "#6b7a8d", spacing: 1.2));

        var name = Wrap(Short(area.Name), 24);
        var ty = y + 50;

        foreach (var line in name)
        {
            svg.Append(Text(x + 26, ty, line, 17, "700", "#1f2d3d"));
            ty += 21;
        }

        svg.Append(CultureInfo.InvariantCulture, $"<circle cx=\"{x + BoxWidth - 40}\" cy=\"{y + 44}\" r=\"24\" fill=\"{badgeFill}\" stroke=\"{accent}\" stroke-width=\"2.2\"/>\n");
        svg.Append(CultureInfo.InvariantCulture, $"<text x=\"{x + BoxWidth - 40}\" y=\"{y + 50}\" font-size=\"16\" font-weight=\"800\" fill=\"{(dashed ? "#44546a" : accent)}\" text-anchor=\"middle\">{WebWire(area.Size)}</text>\n");

        // What is in it.
        svg.Append(Text(x + 26, y + 96, $"{Plural(area.CodeUnitCount, "code unit")}  ·  {Plural(area.TechnologyCount, "technology", "technologies")}  ·  {Plural(area.Points, "pt")}", 12, "500", "#44546a"));

        // Chips.
        var cx = x + 26;
        var cy = y + 108;
        var confidence = area.Confidence switch { "High" => ("#e3f4ea", "#256b44"), "Medium" => ("#fff1c9", "#8a6508"), _ => ("#fdecc8", "#8a5a08") };
        cx += Chip(svg, cx, cy, $"{area.Confidence} confidence", confidence.Item1, confidence.Item2);

        if (area.BlockedByGates.Count > 0)
        {
            Chip(svg, cx + 8, cy, $"{Plural(area.BlockedByGates.Count, "decision")} waiting", "#fbe1e1", "#9b2c2c");
        }
    }

    /// <summary>Draws a pill and returns its width.</summary>
    private static int Chip(StringBuilder svg, int x, int y, string label, string fill, string ink)
    {
        var width = (int)(label.Length * 6.3) + 18;

        svg.Append(CultureInfo.InvariantCulture, $"<rect x=\"{x}\" y=\"{y}\" width=\"{width}\" height=\"22\" rx=\"11\" fill=\"{fill}\"/>\n");
        svg.Append(CultureInfo.InvariantCulture, $"<text x=\"{x + width / 2}\" y=\"{y + 15}\" font-size=\"11\" font-weight=\"700\" fill=\"{ink}\" text-anchor=\"middle\">{WebWire(label)}</text>\n");

        return width;
    }

    private static void Badge(StringBuilder svg, int x, int y, string fill, string stroke, string label)
    {
        var width = label.Length * 8 + 16;

        svg.Append(CultureInfo.InvariantCulture, $"<rect x=\"{x}\" y=\"{y}\" width=\"{width}\" height=\"20\" rx=\"10\" fill=\"{fill}\" stroke=\"{stroke}\" stroke-width=\"1.4\"/>\n");
        svg.Append(CultureInfo.InvariantCulture, $"<text x=\"{x + width / 2}\" y=\"{y + 14}\" font-size=\"11\" font-weight=\"700\" fill=\"{stroke}\" text-anchor=\"middle\">{WebWire(label)}</text>\n");
    }

    private static string WebWire(string text) => WebUtility.HtmlEncode(text);

    private static string Plural(int count, string singular, string? plural = null) => $"{count} {(count == 1 ? singular : plural ?? singular + "s")}";

    /// <summary>The kind is already shown above the name, so "Website (X)" becomes "X"; a name with no brackets is left alone.</summary>
    private static string Short(string name)
    {
        var open = name.IndexOf(" (", StringComparison.Ordinal);

        return open > 0 && name.EndsWith(')') ? name[(open + 2)..^1] : name;
    }

    private static string Text(int x, int y, string text, int size, string weight, string colour, double spacing = 0) =>
        string.Create(CultureInfo.InvariantCulture, $"<text x=\"{x}\" y=\"{y}\" font-size=\"{size}\" font-weight=\"{weight}\" fill=\"{colour}\"{(spacing > 0 ? $" letter-spacing=\"{spacing}\"" : "")}>{WebUtility.HtmlEncode(text)}</text>\n");

    private static List<string> Wrap(string text, int max)
    {
        var lines = new List<string>();
        var current = "";

        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current.Length > 0 && current.Length + 1 + word.Length > max)
            {
                lines.Add(current);
                current = word;
            }
            else
            {
                current = current.Length == 0 ? word : current + " " + word;
            }
        }

        if (current.Length > 0)
        {
            lines.Add(current);
        }

        return lines.Count > 2 ? [lines[0], string.Join(" ", lines.Skip(1))[..Math.Min(string.Join(" ", lines.Skip(1)).Length, max)] + "…"] : lines;
    }

    private static string SizeClass(string size) => size is "XS" or "S" ? "small" : size == "M" ? "medium" : "large";

    private static string SizeFill(string size) => SizeClass(size) switch { "small" => "#d9f2e3", "medium" => "#fff1c9", _ => "#fbd5d5" };

    private static string SizeStroke(string size) => SizeClass(size) switch { "small" => "#2e7d4f", "medium" => "#b8860b", _ => "#b03030" };

    private static string Esc(string text) => text.Replace("\"", "'", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal);
}
