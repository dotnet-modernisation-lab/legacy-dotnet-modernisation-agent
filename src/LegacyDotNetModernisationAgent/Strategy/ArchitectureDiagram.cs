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
    private const int LegendHeight = 96;

    public const int MaxDetailNodes = 25;

    // ---- Overview -----------------------------------------------------------------------

    private const int ActorWidth = 220;
    private const int ExternalWidth = 232;
    private const int ColumnGap = 46;
    private const int Pad = 28;

    public static string OverviewSvg(LegacyMigrationAnalysisInput input, EstateContext context)
    {
        var placed = input.WorkAreas.Where(area => area.Kind != DeployableUnits.NotPlacedKind).OrderBy(area => area.Order).ToList();
        var notPlaced = input.WorkAreas.Where(area => area.Kind == DeployableUnits.NotPlacedKind).ToList();
        var levels = Levels(placed);
        var rows = placed.GroupBy(area => levels[area.Name]).OrderByDescending(group => group.Key).Select(group => group.OrderBy(area => area.Order).ToList()).ToList();
        var linkLabel = context.Links.ToDictionary(link => (link.From, link.To), link => link.Label);

        var widest = Math.Max(1, rows.Count == 0 ? 1 : rows.Max(row => row.Count));
        var laneCount = placed.Sum(area => area.DependsOn.Count(dependency => levels.ContainsKey(dependency) && levels[area.Name] - levels[dependency] != 1));
        var gutter = laneCount * 22 + (laneCount > 0 ? 40 : 0);
        var cardsArea = widest * BoxWidth + (widest - 1) * GapX;

        var hasActors = context.Actors.Count > 0;
        var hasExternal = context.ExternalSystems.Count > 0;
        var boundaryX = hasActors ? Margin + ActorWidth + ColumnGap : Margin;
        var boundaryWidth = Pad + cardsArea + gutter + Pad;
        var cardsLeft = boundaryX + Pad;
        var externalX = boundaryX + boundaryWidth + ColumnGap;
        var width = Math.Max((hasExternal ? externalX + ExternalWidth : boundaryX + boundaryWidth) + Margin, 900);

        var boundaryTop = TitleHeight - 4;
        var rowTop0 = TitleHeight + 40;
        var bodyHeight = rows.Count * BoxHeight + Math.Max(0, rows.Count - 1) * GapY;
        var boundaryBottom = rowTop0 + bodyHeight + 28;
        var notPlacedHeight = notPlaced.Count > 0 ? BoxHeight + 60 : 0;
        var height = boundaryBottom + notPlacedHeight + LegendHeight + 50;

        var position = new Dictionary<string, (int X, int Y)>(StringComparer.Ordinal);

        for (var r = 0; r < rows.Count; r++)
        {
            var rowWidth = rows[r].Count * BoxWidth + (rows[r].Count - 1) * GapX;
            var left = cardsLeft + (cardsArea - rowWidth) / 2;

            for (var c = 0; c < rows[r].Count; c++)
            {
                position[rows[r][c].Name] = (left + c * (BoxWidth + GapX), rowTop0 + r * (BoxHeight + GapY));
            }
        }

        if (notPlaced.Count > 0)
        {
            position[notPlaced[0].Name] = (boundaryX, boundaryBottom + 60);
        }

        var svg = new StringBuilder();
        svg.Append(CultureInfo.InvariantCulture, $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\" viewBox=\"0 0 {width} {height}\" font-family=\"Segoe UI, Helvetica, Arial, sans-serif\">\n");
        svg.Append("<defs>\n");
        svg.Append("<linearGradient id=\"page\" x1=\"0\" y1=\"0\" x2=\"0\" y2=\"1\"><stop offset=\"0\" stop-color=\"#f4f7fb\"/><stop offset=\"1\" stop-color=\"#e9eef6\"/></linearGradient>\n");
        svg.Append("<filter id=\"shadow\" x=\"-10%\" y=\"-10%\" width=\"120%\" height=\"130%\"><feDropShadow dx=\"0\" dy=\"3\" stdDeviation=\"4\" flood-color=\"#1f2d3d\" flood-opacity=\"0.16\"/></filter>\n");
        svg.Append("<marker id=\"arrow\" viewBox=\"0 0 10 10\" refX=\"8\" refY=\"5\" markerWidth=\"9\" markerHeight=\"9\" orient=\"auto\"><path d=\"M 0 0 L 10 5 L 0 10 z\" fill=\"#5f7089\"/></marker>\n");
        svg.Append("<marker id=\"arrowUser\" viewBox=\"0 0 10 10\" refX=\"8\" refY=\"5\" markerWidth=\"9\" markerHeight=\"9\" orient=\"auto\"><path d=\"M 0 0 L 10 5 L 0 10 z\" fill=\"#3b6ea5\"/></marker>\n");
        svg.Append("</defs>\n");
        svg.Append(CultureInfo.InvariantCulture, $"<rect width=\"{width}\" height=\"{height}\" fill=\"url(#page)\"/>\n");
        svg.Append(Text(Margin, 40, "Architecture overview", 26, "700", "#1f2d3d"));
        svg.Append(Text(Margin, 64, $"{input.Repository}  ·  commit {(input.CommitSha.Length > 8 ? input.CommitSha[..8] : input.CommitSha)}", 13, "600", "#44546a"));

        // Zones: the estate (found in the repository), and what lies outside it.
        svg.Append(CultureInfo.InvariantCulture, $"<rect x=\"{boundaryX}\" y=\"{boundaryTop}\" width=\"{boundaryWidth}\" height=\"{boundaryBottom - boundaryTop}\" rx=\"20\" fill=\"#ffffff\" fill-opacity=\"0.55\" stroke=\"#7d93b2\" stroke-width=\"2\" stroke-dasharray=\"9 6\"/>\n");
        svg.Append(Text(boundaryX + 20, boundaryTop + 24, "THE ESTATE (FOUND IN THE REPOSITORY)", 10, "700", "#52688a", spacing: 1.3));

        if (hasActors)
        {
            svg.Append(CultureInfo.InvariantCulture, $"<rect x=\"{Margin}\" y=\"{boundaryTop}\" width=\"{ActorWidth}\" height=\"{boundaryBottom - boundaryTop}\" rx=\"20\" fill=\"none\" stroke=\"#b7c2d3\" stroke-width=\"1.6\" stroke-dasharray=\"5 5\"/>\n");
            svg.Append(Text(Margin + 20, boundaryTop + 24, "PEOPLE AND CALLERS", 10, "700", "#6b7a8d", spacing: 1.3));
        }

        if (hasExternal)
        {
            svg.Append(CultureInfo.InvariantCulture, $"<rect x=\"{externalX}\" y=\"{boundaryTop}\" width=\"{ExternalWidth}\" height=\"{boundaryBottom - boundaryTop}\" rx=\"20\" fill=\"none\" stroke=\"#b7c2d3\" stroke-width=\"1.6\" stroke-dasharray=\"5 5\"/>\n");
            svg.Append(Text(externalX + 20, boundaryTop + 24, "OUTSIDE THE ESTATE", 10, "700", "#6b7a8d", spacing: 1.3));
        }

        // Arrows between areas first, so cards sit on top.
        var lane = 0;
        var labels = new List<string>();

        foreach (var area in placed)
        {
            var dependencies = area.DependsOn.Where(dependency => position.ContainsKey(dependency)).Order(StringComparer.Ordinal).ToList();

            for (var i = 0; i < dependencies.Count; i++)
            {
                var dependency = dependencies[i];
                var from = position[area.Name];
                var to = position[dependency];
                var label = linkLabel.GetValueOrDefault((area.Name, dependency));

                if (levels.ContainsKey(dependency) && levels[area.Name] - levels[dependency] == 1)
                {
                    var startX = from.X + BoxWidth * (i + 1) / (dependencies.Count + 1);
                    var endX = to.X + BoxWidth / 2 + (i - (dependencies.Count - 1) / 2) * 24;
                    var sy = from.Y + BoxHeight;
                    var ey = to.Y - 2;
                    var mid = (ey - sy) / 2;

                    svg.Append(CultureInfo.InvariantCulture, $"<path d=\"M {startX} {sy} C {startX} {sy + mid}, {endX} {ey - mid}, {endX} {ey}\" stroke=\"#5f7089\" stroke-width=\"2\" fill=\"none\" marker-end=\"url(#arrow)\"/>\n");

                    if (label is not null)
                    {
                        labels.Add(Halo((startX + endX) / 2, (sy + ey) / 2 + 4, label));
                    }
                }
                else
                {
                    // An arrow that skips a row, or runs sideways, curves round the outside so it never crosses a card.
                    var bulge = cardsLeft + cardsArea + 24 + lane * 22 + 20;
                    lane++;
                    var y1 = from.Y + BoxHeight / 2 + i * 8;
                    var y2 = to.Y + BoxHeight / 2 + i * 8;
                    var reach = (from.X + BoxWidth) + (int)(0.75 * (bulge - (from.X + BoxWidth)));

                    svg.Append(CultureInfo.InvariantCulture, $"<path d=\"M {from.X + BoxWidth} {y1} C {bulge} {y1}, {bulge} {y2}, {to.X + BoxWidth + 3} {y2}\" stroke=\"#5f7089\" stroke-width=\"2\" fill=\"none\" marker-end=\"url(#arrow)\"/>\n");

                    if (label is not null)
                    {
                        labels.Add(Halo(reach, (y1 + y2) / 2, label));
                    }
                }
            }
        }

        // People and callers, each level with the area they use.
        var actorBottom = boundaryTop + 40;

        foreach (var actor in context.Actors.Where(actor => position.ContainsKey(actor.UnitName)).OrderBy(actor => position[actor.UnitName].Y).ThenBy(actor => actor.UnitName, StringComparer.Ordinal))
        {
            var ghost = actor.Label is "Calling applications" or "Callers not visible";
            var label = ghost ? "Other callers?" : actor.Label;
            var tags = actor.Tags.Take(4).ToList();
            var blockHeight = 58 + (tags.Count + 1) * 27;
            var centre = position[actor.UnitName].Y + BoxHeight / 2;
            var top = Math.Max(actorBottom, centre - blockHeight / 2);

            Person(svg, Margin + 22, top + 6, ghost);
            svg.Append(Text(Margin + 62, top + 22, label, 13, "700", ghost ? "#6b7a8d" : "#1f2d3d"));

            var tagY = top + 50;

            foreach (var tag in tags)
            {
                Chip(svg, Margin + 14, tagY, tag, "#e6eef8", "#2f4f7a");
                tagY += 27;
            }

            Chip(svg, Margin + 14, tagY, ghost ? "Not visible in the repository" : "Who and from where: not known", "#fff7e0", "#8a5a08", dashed: true);

            var arrowY = Math.Min(top + blockHeight / 2, position[actor.UnitName].Y + BoxHeight - 20);
            var cardY = position[actor.UnitName].Y + BoxHeight / 2;
            svg.Append(CultureInfo.InvariantCulture, $"<path d=\"M {Margin + ActorWidth} {arrowY} C {boundaryX} {arrowY}, {position[actor.UnitName].X - 40} {cardY}, {position[actor.UnitName].X - 3} {cardY}\" stroke=\"#3b6ea5\" stroke-width=\"2\" fill=\"none\"{(ghost ? " stroke-dasharray=\"6 5\"" : "")} marker-end=\"url(#arrowUser)\"/>\n");

            actorBottom = top + blockHeight + 18;
        }

        foreach (var area in placed)
        {
            Box(svg, area, position[area.Name], dashed: false);
        }

        // External systems.
        var externalBottom = boundaryTop + 40;

        foreach (var system in context.ExternalSystems.OrderBy(system => system.UsedBy.Min(unit => position.TryGetValue(unit, out var p) ? p.Y : int.MaxValue)).ThenBy(system => system.Name, StringComparer.Ordinal))
        {
            var users = system.UsedBy.Where(position.ContainsKey).ToList();

            if (users.Count == 0)
            {
                continue;
            }

            var anchor = position[users[0]];
            var top = Math.Max(externalBottom, anchor.Y + BoxHeight / 2 - 40);
            var cy = top + 38;

            svg.Append(CultureInfo.InvariantCulture, $"<rect x=\"{externalX + 14}\" y=\"{top}\" width=\"{ExternalWidth - 28}\" height=\"76\" rx=\"12\" fill=\"#ffffff\" stroke=\"#8a96a6\" stroke-width=\"1.6\" stroke-dasharray=\"6 5\"/>\n");
            svg.Append(Text(externalX + 28, top + 28, system.Name, 13, "700", "#1f2d3d"));
            svg.Append(Text(externalX + 28, top + 48, $"used by {string.Join(", ", users.Select(Short))}", 11, "normal", "#6b7a8d"));
            svg.Append(Text(externalX + 28, top + 64, Plural(system.Evidence, "place") + " in the code", 11, "normal", "#6b7a8d"));

            foreach (var user in users)
            {
                var from = position[user];
                var y1 = from.Y + BoxHeight - 30;

                svg.Append(CultureInfo.InvariantCulture, $"<path d=\"M {from.X + BoxWidth} {y1} C {externalX - 30} {y1}, {externalX - 50} {cy}, {externalX + 11} {cy}\" stroke=\"#8a96a6\" stroke-width=\"1.8\" fill=\"none\" stroke-dasharray=\"6 5\" marker-end=\"url(#arrow)\"/>\n");
            }

            externalBottom = top + 76 + 18;
        }

        foreach (var text in labels)
        {
            svg.Append(text);
        }

        if (notPlaced.Count > 0)
        {
            var p = position[notPlaced[0].Name];
            svg.Append(Text(boundaryX, p.Y - 16, "Counted, but the tool could not tie these to any area", 12, "600", "#6b7a8d"));
            Box(svg, notPlaced[0], p, dashed: true);
        }

        var legendY = height - LegendHeight - 14;
        svg.Append(Text(Margin, legendY, "SIZE", 11, "700", "#6b7a8d"));
        Badge(svg, Margin + 46, legendY - 11, "#d9f2e3", "#2e7d4f", "XS S");
        Badge(svg, Margin + 112, legendY - 11, "#fff1c9", "#b8860b", "M");
        Badge(svg, Margin + 158, legendY - 11, "#fbd5d5", "#b03030", "L XL");
        svg.Append(Text(Margin + 230, legendY, "Relative effort, not days. Low confidence: the size rests on unassessed technology or on decisions not yet made. No overall estate size is given.", 12, "normal", "#6b7a8d"));
        svg.Append(Text(Margin, legendY + 26, "People and callers are deduced from what each project is (a website has browser users). Who they are and where they connect from cannot be seen in a repository.", 12, "normal", "#6b7a8d"));
        svg.Append(Text(Margin, legendY + 46, "Blue tags show what the code and configuration declare about access, not what is enforced at run time.", 12, "normal", "#6b7a8d"));
        svg.Append("</svg>\n");

        return svg.ToString();
    }

    private static void Person(StringBuilder svg, int x, int y, bool ghost)
    {
        var stroke = ghost ? "#8a96a6" : "#3b6ea5";
        var fill = ghost ? "none" : "#3b6ea5";
        var dash = ghost ? " stroke-dasharray=\"3 3\"" : "";

        svg.Append(CultureInfo.InvariantCulture, $"<circle cx=\"{x + 14}\" cy=\"{y + 9}\" r=\"8\" fill=\"{fill}\" stroke=\"{stroke}\" stroke-width=\"1.8\"{dash}/>\n");
        svg.Append(CultureInfo.InvariantCulture, $"<path d=\"M {x} {y + 34} Q {x} {y + 19} {x + 14} {y + 19} Q {x + 28} {y + 19} {x + 28} {y + 34} z\" fill=\"{fill}\" stroke=\"{stroke}\" stroke-width=\"1.8\"{dash}/>\n");
    }

    /// <summary>A small label with a pale halo, so it stays legible where it crosses an arrow.</summary>
    private static string Halo(int x, int y, string text) =>
        string.Create(CultureInfo.InvariantCulture, $"<text x=\"{x}\" y=\"{y}\" font-size=\"11\" font-weight=\"600\" fill=\"#44546a\" text-anchor=\"middle\" stroke=\"#eef2f8\" stroke-width=\"4\" paint-order=\"stroke\">{WebUtility.HtmlEncode(text)}</text>\n");

    public static string OverviewMermaid(LegacyMigrationAnalysisInput input, EstateContext context)
    {
        var lines = new List<string> { "flowchart LR" };
        var ordered = input.WorkAreas.OrderBy(area => area.Order).ToList();
        var ids = ordered.Select((area, index) => (area.Name, Id: $"a{index}")).ToDictionary(item => item.Name, item => item.Id, StringComparer.Ordinal);
        var linkLabel = context.Links.ToDictionary(link => (link.From, link.To), link => link.Label);

        for (var i = 0; i < context.Actors.Count; i++)
        {
            var actor = context.Actors[i];

            if (!ids.ContainsKey(actor.UnitName))
            {
                continue;
            }

            var ghost = actor.Label is "Calling applications" or "Callers not visible";
            var text = $"<b>{(ghost ? "Other callers?" : Esc(actor.Label))}</b>" + string.Concat(actor.Tags.Take(4).Select(tag => $"<br/>{Esc(tag)}")) + (ghost ? "<br/><i>not visible in the repository</i>" : "<br/><i>who and from where: not known</i>");

            lines.Add($"    u{i}((\"{text}\")):::person");
            lines.Add($"    u{i} {(ghost ? "-.->" : "-->")} {ids[actor.UnitName]}");
        }

        lines.Add("    subgraph Estate[\"The estate (found in the repository)\"]");

        foreach (var area in ordered)
        {
            var label = $"<b>{Esc(area.Name)}</b><br/>{Esc(area.Kind)}<br/>{area.CodeUnitCount} code units, {area.TechnologyCount} technologies<br/>Size {area.Size}, {Esc(area.Confidence)} confidence"
                + (area.BlockedByGates.Count > 0 ? $"<br/>{area.BlockedByGates.Count} decision(s) waiting" : "");

            lines.Add(area.Kind == DeployableUnits.NotPlacedKind
                ? $"        {ids[area.Name]}[\"{label}\"]:::notplaced"
                : $"        {ids[area.Name]}[\"{label}\"]:::{SizeClass(area.Size)}");
        }

        lines.Add("    end");

        foreach (var area in ordered)
        {
            foreach (var dependency in area.DependsOn.Order(StringComparer.Ordinal).Where(ids.ContainsKey))
            {
                var label = linkLabel.GetValueOrDefault((area.Name, dependency));

                lines.Add(label is null ? $"    {ids[area.Name]} --> {ids[dependency]}" : $"    {ids[area.Name]} -->|{Esc(label)}| {ids[dependency]}");
            }
        }

        for (var i = 0; i < context.ExternalSystems.Count; i++)
        {
            var system = context.ExternalSystems[i];

            lines.Add($"    e{i}[(\"{Esc(system.Name)}\")]:::external");

            foreach (var unit in system.UsedBy.Where(ids.ContainsKey))
            {
                lines.Add($"    {ids[unit]} -.-> e{i}");
            }
        }

        lines.Add("    classDef small fill:#d9f2e3,stroke:#2e7d4f,color:#1f2d3d");
        lines.Add("    classDef medium fill:#fff1c9,stroke:#b8860b,color:#1f2d3d");
        lines.Add("    classDef large fill:#fbd5d5,stroke:#b03030,color:#1f2d3d");
        lines.Add("    classDef notplaced fill:#ffffff,stroke:#888888,stroke-dasharray: 5 5,color:#1f2d3d");
        lines.Add("    classDef person fill:#e6eef8,stroke:#3b6ea5,color:#1f2d3d");
        lines.Add("    classDef external fill:#ffffff,stroke:#8a96a6,stroke-dasharray: 5 5,color:#1f2d3d");

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
    private static int Chip(StringBuilder svg, int x, int y, string label, string fill, string ink, bool dashed = false)
    {
        var width = (int)(label.Length * 6.3) + 18;

        svg.Append(CultureInfo.InvariantCulture, $"<rect x=\"{x}\" y=\"{y}\" width=\"{width}\" height=\"22\" rx=\"11\" fill=\"{fill}\"{(dashed ? $" stroke=\"{ink}\" stroke-width=\"1.1\" stroke-dasharray=\"3 3\"" : "")}/>\n");
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
