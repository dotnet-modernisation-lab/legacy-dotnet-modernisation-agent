using System.Text.RegularExpressions;
using LegacyDotNetModernisationAgent.Models;

namespace LegacyDotNetModernisationAgent.Flows;

/// <summary>A page, control or master markup file and the code-behind type it is bound to.</summary>
public sealed record MarkupBinding(string MarkupFile, string TypeName);

/// <summary>A handler named in markup (for example OnClick="Save_Click") and the code-behind type the markup is bound to.</summary>
public sealed record MarkupHandler(string MarkupFile, string TypeName, string Handler, int Line);

/// <summary>Reads page, control and master markup for the two things flow analysis needs: which type is the code-behind, and which handlers the markup wires up.</summary>
public static partial class MarkupWiring
{
    [GeneratedRegex(@"<%@\s*(?:Page|Control|Master)\b(?<attrs>[^%]*)%>", RegexOptions.IgnoreCase)]
    private static partial Regex Directive();

    [GeneratedRegex(@"\bInherits\s*=\s*""(?<type>[^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex InheritsAttribute();

    // Server-side event attributes look like OnClick="Handler". OnClientClick and the like name script, not code-behind.
    [GeneratedRegex(@"\bOn(?!Client)[A-Z]\w*\s*=\s*""(?<handler>[A-Za-z_]\w*)""")]
    private static partial Regex EventAttribute();

    public static (IReadOnlyList<MarkupBinding> Bindings, IReadOnlyList<MarkupHandler> Handlers) Read(string repositoryRoot, IReadOnlyList<DotNetProjectAssessment> projects)
    {
        var handlers = new List<MarkupHandler>();
        var bindings = new List<MarkupBinding>();

        foreach (var file in projects.SelectMany(project => project.Files.WebFiles).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal))
        {
            if (Path.GetExtension(file).ToLowerInvariant() is not (".aspx" or ".ascx" or ".master"))
            {
                continue;
            }

            var path = Path.Combine(repositoryRoot, file);

            if (!File.Exists(path))
            {
                continue;
            }

            var text = File.ReadAllText(path);
            var directive = Directive().Match(text);
            var inherits = directive.Success ? InheritsAttribute().Match(directive.Groups["attrs"].Value) : Match.Empty;

            if (!inherits.Success)
            {
                continue;
            }

            var typeName = inherits.Groups["type"].Value.Split('.').Last();

            bindings.Add(new MarkupBinding(file, typeName));

            foreach (Match match in EventAttribute().Matches(text))
            {
                handlers.Add(new MarkupHandler(file, typeName, match.Groups["handler"].Value, text.Take(match.Index).Count(character => character == '\n') + 1));
            }
        }

        return (bindings, handlers);
    }
}
