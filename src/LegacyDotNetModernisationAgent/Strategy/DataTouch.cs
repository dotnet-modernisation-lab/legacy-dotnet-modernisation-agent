using LegacyDotNetModernisationAgent.Flows;
using LegacyDotNetModernisationAgent.Models;

namespace LegacyDotNetModernisationAgent.Strategy;

/// <summary>How one part touches one data object: R read, W written, X executed (a procedure). ViaProcedure is set when every touch happens inside a stored procedure the code runs.</summary>
public sealed record DataAccessCell(string Access, bool ViaProcedure);

public sealed record DataObjectRow(
    string Name,
    string Kind,
    IReadOnlyDictionary<string, DataAccessCell> ByPart,
    IReadOnlyList<string> WrittenBy,
    IReadOnlyList<string> ReadBy,
    string? Note);

public sealed record DataTouchMap(
    IReadOnlyList<string> Parts,
    IReadOnlyList<DataObjectRow> Rows,
    IReadOnlyList<ComponentFact> SharedWrites,
    IReadOnlyList<string> Notes);

/// <summary>
/// Which parts and components read and write which tables, procedures and data files. A part is the running part whose entry points
/// reach the code that touches the data (so code in a shared library is attributed to every website or service that calls it, not to
/// "shared code"). A component is where the touching code lives. Tables are rarely touched by application code directly: most are
/// reached through stored procedures, so the code is followed to the procedure it runs and then to the tables in that procedure's
/// definition. A procedure whose definition is not in the repository is shown as executed, with its tables unknown.
/// Touching code that no recognised entry point reaches is shown under its own heading, never dropped.
/// </summary>
public static class DataTouchBuilder
{
    public const string NotReached = "Not reached from an entry point";

    private const string ProcedureBody = "db:code:";

    private sealed record Touch(string ObjectId, string Access, bool ViaProcedure);

    private sealed record Site(string FromNode, string File, int Line, Touch Touch);

    public static DataTouchMap Build(LegacyDependencyGraph graph, UnitMap unitMap, ComponentMap components, FlowSet flows)
    {
        var nodeById = graph.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var edges = graph.SiteEdges.Count > 0 ? graph.SiteEdges : graph.Edges;

        // procedure contract -> the definition found in the repository (by name match)
        var bodyOf = edges.Where(edge => edge.Type == EdgeType.BackedBy && edge.From.StartsWith("sp:", StringComparison.Ordinal) && edge.To.StartsWith(ProcedureBody, StringComparison.Ordinal))
            .GroupBy(edge => edge.From, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().To, StringComparer.Ordinal);

        // what a definition does to tables
        var tablesOf = edges.Where(edge => edge.From.StartsWith(ProcedureBody, StringComparison.Ordinal) && edge.To.StartsWith("db:table:", StringComparison.Ordinal) && edge.Type is EdgeType.ReadsData or EdgeType.WritesData)
            .GroupBy(edge => edge.From, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(edge => (edge.To, Access: edge.Type == EdgeType.WritesData ? "W" : "R")).Distinct().ToList(), StringComparer.Ordinal);

        bool IsUndefined(string id) => nodeById.TryGetValue(id, out var node) && node.Detail is not null && node.Detail.Contains("definition not in repository", StringComparison.Ordinal);

        var sites = new List<Site>();
        var unknownProcedures = new SortedSet<string>(StringComparer.Ordinal);

        void AddProcedure(GraphEdge edge, string procedureId, string definitionId)
        {
            sites.Add(new Site(edge.From, Normalise(edge.Location!), edge.Line!.Value, new Touch(procedureId, "X", false)));

            if (tablesOf.TryGetValue(definitionId, out var tables))
            {
                foreach (var (table, access) in tables)
                {
                    sites.Add(new Site(edge.From, Normalise(edge.Location!), edge.Line!.Value, new Touch(table, access, true)));
                }
            }
            else if (procedureId.StartsWith("sp:", StringComparison.Ordinal) ? !bodyOf.ContainsKey(procedureId) : IsUndefined(procedureId))
            {
                unknownProcedures.Add(procedureId);
            }
        }

        foreach (var edge in edges.Where(edge => edge.Location is not null && edge.Line is not null && components.OfNode.ContainsKey(edge.From)))
        {
            switch (edge.Type)
            {
                case EdgeType.ReadsData or EdgeType.WritesData when edge.To.StartsWith("db:table:", StringComparison.Ordinal):
                    sites.Add(new Site(edge.From, Normalise(edge.Location!), edge.Line!.Value, new Touch(edge.To, edge.Type == EdgeType.WritesData ? "W" : "R", false)));
                    break;

                case EdgeType.BackedBy when edge.To.StartsWith("fixture:", StringComparison.Ordinal):
                    sites.Add(new Site(edge.From, Normalise(edge.Location!), edge.Line!.Value, new Touch(edge.To, "R", false)));
                    break;

                case EdgeType.Invokes when edge.To.StartsWith(ProcedureBody, StringComparison.Ordinal):
                    AddProcedure(edge, edge.To, edge.To);
                    break;

                case EdgeType.ImplementsContract when edge.To.StartsWith("sp:", StringComparison.Ordinal):
                    AddProcedure(edge, edge.To, bodyOf.GetValueOrDefault(edge.To, ""));
                    break;
            }
        }

        // The running parts whose flows pass through the method that contains each touch.
        var stepsByFile = flows.Flows
            .SelectMany(flow => flow.Steps.Select(step => (flow.Unit, Step: step)))
            .GroupBy(item => Normalise(item.Step.Location), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        string Display(string id) => id.StartsWith("fixture:", StringComparison.Ordinal) ? id["fixture:".Length..] : nodeById.TryGetValue(id, out var node) ? node.Name : id[(id.LastIndexOf(':') + 1)..];

        string KindOf(string id) => id.StartsWith("db:table:", StringComparison.Ordinal) ? "Table" : id.StartsWith("fixture:", StringComparison.Ordinal) ? "Data file" : "Procedure";

        var rows = new List<(string Id, string Part, string Component, Touch Touch)>();

        foreach (var site in sites.Distinct())
        {
            var component = components.OfNode[site.FromNode];
            var label = $"{CustomerSummaryBuilder.DisplayName(component.Unit)}: {component.Name}";
            var parts = stepsByFile.TryGetValue(site.File, out var steps)
                ? steps.Where(item => site.Line >= item.Step.Line && site.Line <= item.Step.EndLine).Select(item => item.Unit).Distinct(StringComparer.Ordinal).ToList()
                : [];

            foreach (var part in parts.Count == 0 ? [NotReached] : parts)
            {
                rows.Add((site.Touch.ObjectId, part, label, site.Touch));
            }
        }

        var partNames = rows.Select(row => row.Part).Distinct(StringComparer.Ordinal).OrderBy(part => part == NotReached).ThenBy(part => part, StringComparer.Ordinal).ToList();

        var objects = rows.GroupBy(row => row.Id, StringComparer.Ordinal).Select(group =>
        {
            var byPart = group.GroupBy(row => row.Part, StringComparer.Ordinal).ToDictionary(
                partGroup => partGroup.Key,
                partGroup => new DataAccessCell(
                    string.Concat(partGroup.Select(row => row.Touch.Access).Distinct().Order(StringComparer.Ordinal)),
                    partGroup.All(row => row.Touch.ViaProcedure)),
                StringComparer.Ordinal);

            var writers = group.Where(row => row.Touch.Access == "W").Select(row => row.Component).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
            var readers = group.Where(row => row.Touch.Access is "R" or "X").Select(row => row.Component).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

            return new DataObjectRow(
                Display(group.Key),
                KindOf(group.Key),
                byPart,
                writers,
                readers,
                unknownProcedures.Contains(group.Key) ? "the procedure's definition is not in the repository, so the tables it touches are not known" : null);
        }).ToList();

        // Shared objects first: written from more than one component, then touched by more than one part, then by the most components.
        static List<string> WriterParts(DataObjectRow row) =>
            row.ByPart.Where(item => item.Key != NotReached && item.Value.Access.Contains('W')).Select(item => item.Key).Order(StringComparer.Ordinal).ToList();

        var ordered = objects
            .OrderByDescending(row => WriterParts(row).Count > 1 || row.WrittenBy.Count > 1)
            .ThenByDescending(row => row.ByPart.Count > 1)
            .ThenByDescending(row => row.WrittenBy.Union(row.ReadBy).Count())
            .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Writes from several parts, or from several components, are the strongest coupling signal in a shared database.
        var sharedWrites = ordered.Where(row => WriterParts(row).Count > 1 || row.WrittenBy.Count > 1)
            .Select(row =>
            {
                var parts = WriterParts(row).Select(CustomerSummaryBuilder.DisplayName).ToList();
                var byParts = parts.Count > 1 ? $" by {parts.Count} parts ({string.Join(", ", parts.Take(4))}{(parts.Count > 4 ? " and others" : "")})" : "";
                var byComponents = row.WrittenBy.Count > 1 ? $" from {row.WrittenBy.Count} components ({string.Join(", ", row.WrittenBy.Take(3))}{(row.WrittenBy.Count > 3 ? " and others" : "")})" : $" through {row.WrittenBy[0]}";

                return new ComponentFact($"{row.Name} ({row.Kind.ToLowerInvariant()}) is written{byParts}{byComponents}.", row.Name);
            })
            .ToList();

        var notes = new List<string>
        {
            "A part is the running part (website, service) whose entry points reach the code that touches the data; code in a shared library counts for every part that calls it. R means read, W written, X executed (a procedure). (p) means the touch happens inside a stored procedure the code runs, found by following the code to the procedure and then to the tables in its definition.",
            "Procedure definitions are read from SQL files in the repository. Where a procedure is linked to code only by its name, the link is a convention, not a call the tool saw. Tables touched by code the tool cannot read (dynamic SQL, definitions outside the repository, scheduled jobs) are not shown."
        };

        if (partNames.Contains(NotReached))
        {
            notes.Add($"The column \"{NotReached}\" holds touches by code that no recognised entry point reaches, so the tool cannot say which part uses it.");
        }

        if (objects.Count == 0)
        {
            notes.Add("No data objects were found being touched by code. Either the data layer is reached in a way the tool does not recognise, or it lives outside the repository.");
        }

        return new DataTouchMap(partNames, ordered, sharedWrites, notes);
    }

    private static string Normalise(string path) => path.Replace('\\', '/');
}
