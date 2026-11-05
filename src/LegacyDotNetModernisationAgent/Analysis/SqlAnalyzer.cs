using System.Text.RegularExpressions;
using LegacyDotNetModernisationAgent.Analysis.Facts;
using LegacyDotNetModernisationAgent.Models;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace LegacyDotNetModernisationAgent.Analysis;

public sealed record SqlAnalysisResult(DatabaseAssessment Assessment, IReadOnlyList<CoverageGap> Gaps);

/// <summary>
/// Parses T-SQL with the SQL Server ScriptDom parser: the .sql files in the repository (procedures, functions, views,
/// triggers, tables, loose scripts) and string literals in code that are themselves valid T-SQL. It records what each
/// object reads, writes and calls, and the SQL Server specifics a migration must know about (dynamic SQL, cursors,
/// linked servers, CLR, xp_cmdshell). It does not connect to a database and does not run anything. Inline SQL text is
/// never stored; SQL assembled from several string pieces is not seen.
/// </summary>
public sealed partial class SqlAnalyzer
{
    [GeneratedRegex(@"^\s*GO(\s+\d+)?\s*(--.*)?$", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex BatchSeparator();

    [GeneratedRegex(@"^\s*(SELECT|INSERT|UPDATE|DELETE|EXEC|EXECUTE|MERGE|WITH|TRUNCATE)\s", RegexOptions.IgnoreCase)]
    private static partial Regex SqlVerb();

    private static readonly string[] IgnoredDirectories = ["bin", "obj", ".git", ".vs", "node_modules", "packages"];

    public SqlAnalysisResult Analyze(string root, IReadOnlyList<SourceFacts> sources)
    {
        var gaps = new List<CoverageGap>();
        var objects = new List<SqlObjectInfo>();
        var files = EnumerateSqlFiles(root).Order(StringComparer.Ordinal).ToList();

        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            string text;

            try
            {
                text = File.ReadAllText(file);
            }
            catch (IOException exception)
            {
                gaps.Add(new CoverageGap("SqlFileUnreadable", $"SQL file could not be read: {exception.Message}", relative, "Its objects are not part of the assessment."));

                continue;
            }

            foreach (var (batch, firstLine) in SplitBatches(text))
            {
                var script = Parse(batch, out var errors);

                if (errors.Count > 0)
                {
                    gaps.Add(new CoverageGap(
                        "SqlParseError",
                        $"T-SQL could not be fully parsed at line {firstLine + errors[0].Line - 1}: {errors[0].Message}",
                        relative,
                        "Objects in this batch may be missing or incomplete (for example SQL dialects other than SQL Server)."));
                }

                if (script is null)
                {
                    continue;
                }

                foreach (var statement in script.Batches.SelectMany(b => b.Statements))
                {
                    objects.Add(Describe(statement, relative, firstLine, Path.GetFileName(file)));
                }
            }

        }

        var inline = new List<InlineSqlInfo>();

        foreach (var source in sources)
        {
            foreach (var literal in source.Literals)
            {
                var value = literal.Value;

                if (value.Length < 15 || !SqlVerb().IsMatch(value) || !LooksLikeSql(value))
                {
                    continue;
                }

                var script = Parse(value, out var errors);

                if (errors.Count > 0 || script is null)
                {
                    continue;
                }

                foreach (var statement in script.Batches.SelectMany(b => b.Statements).Take(1))
                {
                    var facts = Visit(statement);

                    inline.Add(new InlineSqlInfo(
                        StatementKind(statement), literal.Type, source.Path, literal.Line,
                        facts.Reads, facts.Writes, facts.Calls, facts.Features));
                }
            }
        }

        return new SqlAnalysisResult(
            new DatabaseAssessment(
                files.Select(file => Path.GetRelativePath(root, file).Replace('\\', '/')).ToList(),
                objects
                    .OrderBy(o => o.Location, StringComparer.Ordinal).ThenBy(o => o.Line).ThenBy(o => o.Name, StringComparer.Ordinal)
                    .ToList(),
                inline
                    .OrderBy(i => i.Location, StringComparer.Ordinal).ThenBy(i => i.Line).ThenBy(i => i.Statement, StringComparer.Ordinal)
                    .ToList()),
            gaps);
    }

    /// <summary>
    /// Prose such as "Select a customer from the list" is valid T-SQL. Require an upper-case verb or SQL punctuation
    /// so UI text is not mistaken for a query.
    /// </summary>
    private static bool LooksLikeSql(string value)
    {
        var verb = value.TrimStart().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0];

        return verb == verb.ToUpperInvariant() || value.IndexOfAny(['*', ',', '@', '.', '[', '(', '=', ';']) >= 0;
    }

    private static IEnumerable<string> EnumerateSqlFiles(string root) =>
        Directory.EnumerateFiles(root, "*.sql", SearchOption.AllDirectories)
            .Where(file => !Path.GetRelativePath(root, file).Replace('\\', '/').Split('/')
                .Any(part => IgnoredDirectories.Contains(part, StringComparer.OrdinalIgnoreCase)));

    private static IEnumerable<(string Batch, int FirstLine)> SplitBatches(string text)
    {
        var line = 1;
        var start = 0;

        foreach (Match separator in BatchSeparator().Matches(text))
        {
            yield return (text[start..separator.Index], line);

            line += text[start..(separator.Index + separator.Length)].Count(c => c == '\n');
            start = separator.Index + separator.Length;
        }

        yield return (text[start..], line);
    }

    private static TSqlScript? Parse(string text, out IList<ParseError> errors)
    {
        var parser = new TSql160Parser(initialQuotedIdentifiers: true);
        using var reader = new StringReader(text);

        return parser.Parse(reader, out errors) as TSqlScript;
    }

    private static SqlObjectInfo Describe(TSqlStatement statement, string location, int firstLine, string fileName)
    {
        var facts = Visit(statement);
        var line = firstLine + statement.StartLine - 1;

        (string Kind, string Name, IReadOnlyList<string> Parameters)? definition = statement switch
        {
            ProcedureStatementBody p => ("Procedure", Name(p.ProcedureReference.Name), p.Parameters.Select(x => x.VariableName.Value).ToList()),
            FunctionStatementBody f => ("Function", Name(f.Name), f.Parameters.Select(x => x.VariableName.Value).ToList()),
            ViewStatementBody v => ("View", Name(v.SchemaObjectName), []),
            TriggerStatementBody t => ("Trigger", Name(t.Name), []),
            CreateTableStatement c => ("Table", Name(c.SchemaObjectName), c.Definition.ColumnDefinitions.Select(x => x.ColumnIdentifier.Value).ToList()),
            _ => null
        };

        var reads = facts.Reads;

        // A trigger fires on a table: record the table as read so the link is visible.
        if (statement is TriggerStatementBody trigger && trigger.TriggerObject?.Name is { } target)
        {
            reads = reads.Append(Name(target)).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
        }

        if (definition is { } d)
        {
            // A table never reads or writes itself.
            return new SqlObjectInfo(
                d.Kind, d.Name, location, line, d.Parameters,
                d.Kind == "Table" ? [] : reads,
                d.Kind == "Table" ? [] : facts.Writes,
                facts.Calls, facts.Features);
        }

        return new SqlObjectInfo("Statement", $"{fileName}: {StatementKind(statement)}", location, line, [], facts.Reads, facts.Writes, facts.Calls, facts.Features);
    }

    private static string StatementKind(TSqlStatement statement) =>
        statement.GetType().Name.Replace("Statement", "", StringComparison.Ordinal);

    internal static string Name(SchemaObjectName name)
    {
        var identifiers = name.Identifiers.Select(identifier => identifier.Value).ToList();

        if (identifiers.Count == 1)
        {
            identifiers.Insert(0, "dbo");
        }

        return string.Join('.', identifiers);
    }

    private static SqlFacts Visit(TSqlFragment fragment)
    {
        var visitor = new FactVisitor();
        fragment.Accept(visitor);

        return visitor.Result();
    }

    private sealed record SqlFacts(
        IReadOnlyList<string> Reads,
        IReadOnlyList<string> Writes,
        IReadOnlyList<string> Calls,
        IReadOnlyList<string> Features);

    private sealed class FactVisitor : TSqlFragmentVisitor
    {
        private readonly List<NamedTableReference> tables = [];
        private readonly HashSet<NamedTableReference> writeTargets = [];
        private readonly List<SchemaObjectName> truncated = [];
        private readonly HashSet<string> commonTableExpressions = new(StringComparer.OrdinalIgnoreCase);
        private readonly SortedSet<string> calls = new(StringComparer.OrdinalIgnoreCase);
        private readonly SortedSet<string> features = new(StringComparer.Ordinal);

        public override void Visit(NamedTableReference node) => tables.Add(node);

        public override void Visit(CommonTableExpression node) => commonTableExpressions.Add(node.ExpressionName.Value);

        public override void Visit(InsertSpecification node)
        {
            if (node.Target is NamedTableReference table)
            {
                writeTargets.Add(table);
            }
        }

        public override void Visit(UpdateSpecification node)
        {
            if (node.Target is NamedTableReference table)
            {
                writeTargets.Add(table);
            }
        }

        public override void Visit(DeleteSpecification node)
        {
            if (node.Target is NamedTableReference table)
            {
                writeTargets.Add(table);
            }
        }

        public override void Visit(MergeSpecification node)
        {
            if (node.Target is NamedTableReference table)
            {
                writeTargets.Add(table);
            }
        }

        public override void Visit(TruncateTableStatement node) => truncated.Add(node.TableName);

        public override void Visit(SelectStatement node)
        {
            if (node.Into is not null)
            {
                truncated.Add(node.Into);
            }
        }

        public override void Visit(ExecuteSpecification node)
        {
            switch (node.ExecutableEntity)
            {
                case ExecutableProcedureReference { ProcedureReference.ProcedureReference.Name: { } name }:
                    var simple = name.BaseIdentifier.Value;

                    if (simple.Equals("sp_executesql", StringComparison.OrdinalIgnoreCase))
                    {
                        features.Add("dynamic SQL (sp_executesql)");
                    }
                    else if (simple.Equals("xp_cmdshell", StringComparison.OrdinalIgnoreCase))
                    {
                        features.Add("xp_cmdshell (runs operating-system commands)");
                    }
                    else if (simple.StartsWith("xp_", StringComparison.OrdinalIgnoreCase))
                    {
                        features.Add("extended stored procedure (xp_)");
                    }
                    else
                    {
                        calls.Add(Name(name));
                    }

                    if (name.ServerIdentifier is not null)
                    {
                        features.Add("linked server");
                    }

                    break;
                case ExecutableProcedureReference { ProcedureReference.ProcedureVariable: not null }:
                    features.Add("dynamic SQL (procedure name in a variable)");
                    break;
                case ExecutableStringList:
                    features.Add("dynamic SQL (EXEC of a string)");
                    break;
            }
        }

        public override void Visit(DeclareCursorStatement node) => features.Add("cursor");

        public override void Visit(BeginTransactionStatement node) => features.Add("explicit transaction");

        public override void Visit(OpenRowsetTableReference node) => features.Add("OPENROWSET (external data source)");

        public override void Visit(OpenQueryTableReference node) => features.Add("OPENQUERY (linked server)");

        public override void Visit(AdHocTableReference node) => features.Add("OPENROWSET/OPENDATASOURCE (external data source)");

        public override void Visit(CreateAssemblyStatement node) => features.Add("SQL CLR assembly");

        public override void Visit(MethodSpecifier node) => features.Add("SQL CLR method");

        public SqlFacts Result()
        {
            var aliases = tables
                .Where(table => table.Alias is not null)
                .GroupBy(table => table.Alias.Value, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            var reads = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            var writes = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var table in tables)
            {
                var schemaObject = table.SchemaObject;
                var single = schemaObject.Identifiers.Count == 1;
                var first = schemaObject.BaseIdentifier.Value;

                if (first.StartsWith('#'))
                {
                    features.Add("temporary table");

                    continue;
                }

                if (single && commonTableExpressions.Contains(first))
                {
                    continue;
                }

                if (schemaObject.ServerIdentifier is not null)
                {
                    features.Add("linked server");
                }
                else if (schemaObject.DatabaseIdentifier is not null)
                {
                    features.Add("cross-database reference");
                }

                if (writeTargets.Contains(table))
                {
                    // UPDATE a SET ... FROM t a: the target is an alias of a real table.
                    if (single && aliases.TryGetValue(first, out var aliased) && !ReferenceEquals(aliased, table))
                    {
                        writes.Add(Name(aliased.SchemaObject));
                    }
                    else
                    {
                        writes.Add(Name(schemaObject));
                    }
                }
                else
                {
                    reads.Add(Name(schemaObject));
                }
            }

            foreach (var name in truncated)
            {
                if (name.BaseIdentifier.Value.StartsWith('#'))
                {
                    features.Add("temporary table");
                }
                else
                {
                    writes.Add(Name(name));
                }
            }

            return new SqlFacts(reads.ToList(), writes.ToList(), calls.ToList(), features.ToList());
        }
    }
}
