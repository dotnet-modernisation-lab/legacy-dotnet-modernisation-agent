using System.Text;
using LegacyDotNetModernisationAgent.Analysis;
using LegacyDotNetModernisationAgent.Strategy;

namespace LegacyDotNetModernisationAgent.Commands;

/// <summary>
/// Draws the architecture from the deterministic facts: an overview of the deployable areas (SVG and Mermaid) and a detail
/// view per area (Mermaid). No model is involved, so the diagrams are the same on every run.
/// </summary>
public sealed class DiagramCommand
{
    public async Task<int> ExecuteAsync(string repositoryPath, string artifactsDirectory, CancellationToken cancellationToken)
    {
        var assessment = new LegacyDotNetRepositoryAnalyzer().Analyze(repositoryPath);
        var graph = new ArchitecturalGraphBuilder().Build(assessment);
        var analysis = new MigrationCharacteristicPolicy().Evaluate(assessment, graph);
        var input = LegacyMigrationAnalysisInputBuilder.Build(assessment, graph, analysis);
        var unitMap = DeployableUnits.Build(assessment, graph);

        var gatesOfNode = analysis.DecisionGates
            .SelectMany(gate => gate.AffectedNodes.Select(node => (Node: node, Gate: gate.Id)))
            .GroupBy(pair => pair.Node, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(pair => pair.Gate).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList(), StringComparer.Ordinal);

        Directory.CreateDirectory(artifactsDirectory);

        var context = EstateContextBuilder.Build(assessment, graph, unitMap);
        var overviewMermaid = ArchitectureDiagram.OverviewMermaid(input, context);

        await File.WriteAllTextAsync(Path.Combine(artifactsDirectory, "architecture-overview.svg"), ArchitectureDiagram.OverviewSvg(input, context), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(artifactsDirectory, "architecture-overview.mmd"), overviewMermaid, cancellationToken);

        // One Markdown page with every diagram, which GitHub and most wikis render directly.
        var page = new StringBuilder();
        page.Append("# Architecture: ").Append(input.Repository).Append("\n\n");
        page.Append("Commit ").Append(input.CommitSha).Append(". Drawn from the deterministic analysis; every box and arrow is something the tool found.\n\n");
        page.Append("## Overview\n\n![Architecture overview](architecture-overview.svg)\n\n");
        page.Append("### How to read this\n\n");
        page.Append("- **Cards** are the deployable parts of the estate. The coloured bar and badge show relative size, and the chips show confidence and open decisions. There is no overall size for the estate.\n");
        page.Append("- **People and callers** (left) are deduced from what each project is. Who they are and where they connect from is not visible in a repository, so the diagram marks it as not known.\n");
        page.Append("- **The dashed boundary** is the estate found in the repository. **Outside the estate** (right) holds the external systems the code reaches.\n");
        page.Append("- **Arrows** point from a part to the part it depends on, labelled with the kind of link.\n\n");
        page.Append("### Drill-down (Mermaid)\n\nThe same overview as a Mermaid diagram, then one diagram per part below.\n\n```mermaid\n").Append(overviewMermaid).Append("```\n");

        foreach (var area in input.WorkAreas.Where(area => area.Kind != DeployableUnits.NotPlacedKind).OrderBy(area => area.Order))
        {
            var detail = ArchitectureDiagram.DetailMermaid(area.Name, graph, unitMap, analysis, gatesOfNode);

            await File.WriteAllTextAsync(Path.Combine(artifactsDirectory, $"architecture-detail-{ArchitectureDiagram.Slug(area.Name)}.mmd"), detail, cancellationToken);

            page.Append("\n## ").Append(area.Name).Append("\n\nRed boxes wait on a decision. Dashed arrows go to other areas. Up to ")
                .Append(ArchitectureDiagram.MaxDetailNodes).Append(" components are drawn, those needing the most attention first.\n\n```mermaid\n").Append(detail).Append("```\n");
        }

        await File.WriteAllTextAsync(Path.Combine(artifactsDirectory, "architecture.md"), page.ToString().Replace("\r\n", "\n", StringComparison.Ordinal), cancellationToken);

        Console.WriteLine($"{input.Repository}: {input.WorkAreas.Count} area(s) drawn");
        Console.WriteLine($"Wrote architecture-overview.svg, architecture-overview.mmd, architecture.md and one architecture-detail-*.mmd per area to {artifactsDirectory}");

        return 0;
    }
}
