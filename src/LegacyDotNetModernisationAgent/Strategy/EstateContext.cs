using LegacyDotNetModernisationAgent.Models;

namespace LegacyDotNetModernisationAgent.Strategy;

/// <summary>Who or what uses a deployable area, what the repository declares about how they get in, and what it cannot say.</summary>
public sealed record UnitActor(
    string UnitName,
    string Label,
    string Basis,
    IReadOnlyList<string> DeclaredFacts,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Unknowns);

/// <summary>Something outside the estate that the code reaches (a web service, a file share, a mail server).</summary>
public sealed record ExternalSystem(string Name, string Category, IReadOnlyList<string> UsedBy, int Evidence);

/// <summary>A dependency between two areas, with a plain label for what kind of link it is.</summary>
public sealed record UnitLink(string From, string To, string Label);

public sealed record EstateContext(
    IReadOnlyList<UnitActor> Actors,
    IReadOnlyList<ExternalSystem> ExternalSystems,
    IReadOnlyList<UnitLink> Links);

/// <summary>
/// Deduces, from facts only, who uses each deployable area, what sits outside the estate, and how areas link. Nothing here is
/// guessed: a website implies browser users because it serves pages, but whether they are staff, customers or the public
/// (and from which network) cannot be seen in a repository, so the actor carries that as an explicit unknown.
/// </summary>
public static class EstateContextBuilder
{
    private static readonly Dictionary<string, string> ExternalNames = new(StringComparer.Ordinal)
    {
        ["ExternalHttp"] = "External web services",
        ["FileSystem"] = "File shares or local files",
        ["Email"] = "Mail server",
        ["Ftp"] = "FTP server",
        ["Messaging"] = "Message queue",
        ["ComInterop"] = "COM components on the server",
        ["ChildProcess"] = "Other programs started by the code"
    };

    public static EstateContext Build(LegacyDotNetProjectAssessment assessment, LegacyDependencyGraph graph, UnitMap unitMap)
    {
        var unitKind = unitMap.Units.ToDictionary(unit => unit.Name, unit => unit.Kind, StringComparer.Ordinal);
        var nodes = graph.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);

        // Which unit does a repository path belong to? The project whose folder contains it.
        var folders = assessment.Projects
            .Where(project => unitMap.UnitOfProject.ContainsKey(project.Name))
            .Select(project => (Folder: FolderOf(project.Path), Unit: unitMap.UnitOfProject[project.Name]))
            .Where(item => item.Folder.Length > 0)
            .OrderByDescending(item => item.Folder.Length)
            .ToList();

        string? UnitOfPath(string path)
        {
            var normal = path.Replace('\\', '/');
            var match = folders.FirstOrDefault(item => normal.StartsWith(item.Folder + "/", StringComparison.Ordinal));

            return match.Unit;
        }

        // Dependencies between areas, with a label from the kind of link.
        var links = new SortedDictionary<(string From, string To), string>();

        foreach (var edge in graph.Edges.Where(edge => edge.Basis is not ("Convention" or "AssemblyMetadata")))
        {
            if (!unitMap.UnitOfNode.TryGetValue(edge.From, out var from) || !unitMap.UnitOfNode.TryGetValue(edge.To, out var to) || from == to)
            {
                continue;
            }

            if (unitKind[from] == DeployableUnits.NotPlacedKind || unitKind[to] == DeployableUnits.NotPlacedKind)
            {
                continue;
            }

            var label = nodes[edge.From].Kind == "WcfClient" && nodes[edge.To].Kind is "WcfContract" or "WcfService" or "WcfHost" or "DataContract" ? "WCF call"
                : unitKind[to] == DeployableUnits.DataSourceKind ? "reads and writes data"
                : unitKind[to] == DeployableUnits.SharedKind ? "uses shared code"
                : "uses";

            // A specific label wins over the generic one when several links join the same pair.
            if (!links.TryGetValue((from, to), out var existing) || existing == "uses" || (existing == "uses shared code" && label != "uses"))
            {
                links[(from, to)] = label;
            }
        }

        var callers = links
            .Where(link => unitKind[link.Key.From] is not (DeployableUnits.SharedKind or DeployableUnits.DataSourceKind))
            .GroupBy(link => link.Key.To, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(link => link.Key.From).Order(StringComparer.Ordinal).ToList(), StringComparer.Ordinal);

        var actors = new List<UnitActor>();

        foreach (var unit in unitMap.Units.OrderBy(unit => unit.Name, StringComparer.Ordinal))
        {
            var (facts, tags) = DeclaredFacts(assessment, unit.Name, UnitOfPath);

            switch (unit.Kind)
            {
                case "Website":
                    actors.Add(new UnitActor(unit.Name, "Browser users", "The project serves web pages.", facts, tags,
                        ["Whether the users are staff, customers or the public, and whether they connect from the internet or an internal network."]));
                    break;
                case "WCF service":
                    {
                        var known = callers.GetValueOrDefault(unit.Name, []);

                        actors.Add(new UnitActor(unit.Name, known.Count > 0 ? "Calling applications" : "Callers not visible", known.Count > 0 ? $"Called by {string.Join(", ", known)}." : "The service is exposed, but no caller was found in the repository.", facts, tags,
                            ["Whether other applications or partners call the service, and from which networks."]));
                        break;
                    }
                case "Desktop application":
                    actors.Add(new UnitActor(unit.Name, "Desktop users", "The project is a Windows desktop application.", facts, tags,
                        ["How many users there are and how the application is installed on their machines."]));
                    break;
                case "Windows service":
                    actors.Add(new UnitActor(unit.Name, "Started by Windows", "The project is a Windows service, so it runs unattended.", facts, tags,
                        ["What starts or schedules its work, and who is responsible for it running."]));
                    break;
                case "Application":
                    actors.Add(new UnitActor(unit.Name, "Operators or scheduled tasks", "The project is an executable with no web or service host.", facts, tags,
                        ["Who or what runs it, and how often."]));
                    break;
            }
        }

        // External systems: integrations the code reaches, with the areas that use them.
        var external = new List<ExternalSystem>();

        foreach (var node in graph.Nodes.Where(node => node.Id.StartsWith("integration:", StringComparison.Ordinal)).OrderBy(node => node.Id, StringComparer.Ordinal))
        {
            var category = node.Id["integration:".Length..];

            if (!ExternalNames.TryGetValue(category, out var name))
            {
                continue; // scheduled work and transactions are not systems outside the estate
            }

            var users = graph.Edges
                .Where(edge => edge.To == node.Id && edge.Type == EdgeType.UsesIntegration)
                .Select(edge => unitMap.UnitOfNode.GetValueOrDefault(edge.From))
                .Where(unit => unit is not null && unitKind[unit] != DeployableUnits.NotPlacedKind)
                .Select(unit => unit!)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToList();

            if (users.Count > 0)
            {
                external.Add(new ExternalSystem(name, category, users, graph.Edges.Count(edge => edge.To == node.Id && edge.Type == EdgeType.UsesIntegration)));
            }
        }

        return new EstateContext(
            actors,
            external,
            links.Select(link => new UnitLink(link.Key.From, link.Key.To, link.Value)).ToList());
    }

    private static (List<string> Facts, List<string> Tags) DeclaredFacts(LegacyDotNetProjectAssessment assessment, string unit, Func<string, string?> unitOfPath)
    {
        var facts = new List<string>();
        var tags = new List<string>();

        foreach (var mechanism in assessment.Authentication.Mechanisms.OrderBy(mechanism => mechanism.Kind, StringComparer.Ordinal))
        {
            if (!mechanism.Locations.Any(location => unitOfPath(location) == unit))
            {
                continue;
            }

            var (text, tag) = mechanism.Kind switch
            {
                "FormsAuthentication" => ("Sign-in uses Forms authentication (declared in configuration).", "Forms sign-in"),
                "NoAuthenticationConfigured" => ("No authentication is configured here.", "No authentication configured"),
                "SharedSecretOrTrustedSubsystem" => ("Callers are trusted through a shared application secret.", "Shared-secret trust"),
                "CslaAuthorizationRules" => ("Field-level read rules are declared in business objects.", "Field-level rules"),
                "IdentityChecksInCode" => ("Code checks whether the user is signed in.", "Sign-in checked in code"),
                "CustomPrincipal" => ("A custom identity is attached to each request.", "Custom identity"),
                "WebConfigAuthorization" => (null, null),
                _ => ((string?)$"Identity or access mechanism: {mechanism.Kind}.", (string?)mechanism.Kind)
            };

            if (text is not null && tag is not null)
            {
                facts.Add(text);
                tags.Add(tag);
            }
        }

        var rules = assessment.Authentication.AccessRules.Where(rule => unitOfPath(rule.Location) == unit).ToList();
        var signedInOnly = rules.Where(rule => rule.Operation == "deny" && rule.Principals == "users=?").Select(rule => rule.Target).Distinct(StringComparer.Ordinal).Count();

        if (signedInOnly > 0)
        {
            facts.Add($"{signedInOnly} location(s) require sign-in; anything else the configuration allows is open to anyone.");
            tags.Add($"{signedInOnly} location(s) need sign-in");
        }

        var roles = rules.Where(rule => rule.Source == "CslaAuthorizationRules").SelectMany(rule => rule.Principals.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

        if (roles.Count > 0)
        {
            facts.Add($"Roles named in code: {string.Join(", ", roles)}.");
            tags.Add($"Roles: {string.Join(", ", roles)}");
        }

        return (facts, tags);
    }

    private static string FolderOf(string projectPath)
    {
        var path = projectPath.Replace('\\', '/');
        var index = path.LastIndexOf('/');

        return index < 0 ? "" : path[..index];
    }
}
