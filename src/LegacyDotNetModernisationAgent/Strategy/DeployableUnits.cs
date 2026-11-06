using LegacyDotNetModernisationAgent.Models;

namespace LegacyDotNetModernisationAgent.Strategy;

/// <summary>A deployable part of the estate: a website, a service, a stand-alone application or the data source.</summary>
public sealed record DeployableUnit(string Name, string Kind, IReadOnlyList<string> Projects, IReadOnlyList<string> Notes);

/// <summary>Which unit every graph node belongs to, and the units themselves.</summary>
public sealed record UnitMap(IReadOnlyList<DeployableUnit> Units, IReadOnlyDictionary<string, string> UnitOfNode, IReadOnlyDictionary<string, string> UnitOfProject);

/// <summary>
/// Places every graph node in the part of the estate it belongs to, so work areas follow what people recognise
/// (the website, the service, the data source) instead of technical layers. It is deterministic and uses only facts:
/// project kinds, project references, file locations and graph edges. Where it cannot place something it says so
/// ("Not placed") rather than guessing.
/// </summary>
public static class DeployableUnits
{
    public const string DataSourceKind = "Data source";
    public const string SharedKind = "Shared";
    public const string NotPlacedKind = "Not placed";

    public const string DataSourceName = "Data source";
    public const string SharedName = "Shared libraries and technologies";
    public const string NotPlacedName = "Not placed in any unit";

    /// <summary>Kinds that describe a technology the code relies on, as opposed to a unit of code or data that someone wrote.</summary>
    public static readonly IReadOnlySet<string> TechnologyKinds = new HashSet<string>(StringComparer.Ordinal)
    {
        "AuthenticationMechanism", "FrameworkAssembly", "Assembly", "LibraryType", "CrossCutting", "Integration", "ConfigurationSetting", "SessionState"
    };

    public static readonly IReadOnlyList<string> Rubric =
    [
        "Work areas are the deployable parts of the estate. Each web application, service host, Windows service or other executable project is one area, labelled from what the project is (the set of labels is open, not fixed); class libraries belong to the area that references them; database objects and fixtures are the data source.",
        "A library referenced by more than one area belongs to the service that hosts it if it contains the service implementation, otherwise to a shared area. Technologies (frameworks, authentication, integrations) belong to the area whose code uses them, or to the shared area if several do.",
        "Anything the tool cannot place from project references, file locations or graph edges is shown as Not placed rather than guessed."
    ];

    public static UnitMap Build(LegacyDotNetProjectAssessment assessment, LegacyDependencyGraph graph)
    {
        var projects = assessment.Projects.ToDictionary(project => project.Name, StringComparer.Ordinal);
        var nodes = graph.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);

        // Entry projects are the deployable ones.
        var entries = assessment.Projects
            .Where(project => project.Kind != ProjectKind.ClassLibrary)
            .OrderBy(project => project.Name, StringComparer.Ordinal)
            .ToList();

        var unitKindOfEntry = entries.ToDictionary(
            project => project.Name,
            KindLabel,
            StringComparer.Ordinal);

        string EntryUnitName(string project) => $"{unitKindOfEntry[project]} ({project})";

        // Which entry projects reach each project through project references.
        var reachedBy = projects.Keys.ToDictionary(name => name, _ => new SortedSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal) { entry.Name };
            var queue = new Queue<string>([entry.Name]);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();

                foreach (var reference in projects[current].ProjectReferences.Where(reference => projects.ContainsKey(reference.Name)))
                {
                    if (seen.Add(reference.Name))
                    {
                        reachedBy[reference.Name].Add(entry.Name);
                        queue.Enqueue(reference.Name);
                    }
                }
            }
        }

        var unitOfProject = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var project in assessment.Projects.OrderBy(project => project.Name, StringComparer.Ordinal))
        {
            if (unitKindOfEntry.ContainsKey(project.Name))
            {
                unitOfProject[project.Name] = EntryUnitName(project.Name);
                continue;
            }

            var owners = reachedBy[project.Name];
            var hasServiceImplementation = graph.Nodes.Any(node => node.Project == project.Name && node.Kind == "WcfService");
            var serviceOwners = owners.Where(owner => projects[owner].Kind == ProjectKind.WcfServiceHost).ToList();

            unitOfProject[project.Name] =
                owners.Count == 1 ? EntryUnitName(owners.First())
                : owners.Count > 1 && hasServiceImplementation && serviceOwners.Count == 1 ? EntryUnitName(serviceOwners[0])
                : owners.Count > 1 ? SharedName
                : NotPlacedName;
        }

        var unitOfNode = new Dictionary<string, string>(StringComparer.Ordinal);

        // 1. Nodes that carry a project.
        foreach (var node in graph.Nodes.Where(node => node.Project is not null && unitOfProject.ContainsKey(node.Project)))
        {
            unitOfNode[node.Id] = unitOfProject[node.Project!];
        }

        // 2. Database objects and fixtures are the data source.
        foreach (var node in graph.Nodes.Where(node => !unitOfNode.ContainsKey(node.Id)
            && (node.Id.StartsWith("sp:", StringComparison.Ordinal) || node.Id.StartsWith("db:", StringComparison.Ordinal) || node.Id.StartsWith("fixture:", StringComparison.Ordinal))))
        {
            unitOfNode[node.Id] = DataSourceName;
        }

        // 3. Nodes with a file location inside a project's folder.
        var folders = assessment.Projects
            .Select(project => (project.Name, Folder: FolderOf(project.Path)))
            .Where(item => item.Folder.Length > 0)
            .OrderByDescending(item => item.Folder.Length)
            .ToList();

        foreach (var node in graph.Nodes.Where(node => !unitOfNode.ContainsKey(node.Id) && node.Location is not null))
        {
            var location = node.Location!.Replace('\\', '/');
            var match = folders.FirstOrDefault(item => location.StartsWith(item.Folder + "/", StringComparison.Ordinal));

            if (match.Name is not null)
            {
                unitOfNode[node.Id] = unitOfProject[match.Name];
            }
        }

        // 4. The rest follow the code that uses them: first through source-level use, then through assembly metadata.
        //    A node is placed only when every user is already placed; if that stalls, when any user is.
        PlaceByUsers(graph, unitOfNode, includeMetadata: false);
        PlaceByUsers(graph, unitOfNode, includeMetadata: true);

        foreach (var node in graph.Nodes.Where(node => !unitOfNode.ContainsKey(node.Id)))
        {
            unitOfNode[node.Id] = NotPlacedName;
        }

        // Unit descriptions.
        var unitNames = unitOfNode.Values.Distinct(StringComparer.Ordinal).ToList();

        var units = unitNames
            .Select(name =>
            {
                var kind = name == DataSourceName ? DataSourceKind
                    : name == SharedName ? SharedKind
                    : name == NotPlacedName ? NotPlacedKind
                    : name[..name.IndexOf(" (", StringComparison.Ordinal)];

                var memberProjects = unitOfProject.Where(pair => pair.Value == name).Select(pair => pair.Key).Order(StringComparer.Ordinal).ToList();
                var notes = new List<string>();

                // A compile-time reference from one unit straight into a library owned by another is coupling that the runtime call hides.
                foreach (var entry in entries.Where(entry => EntryUnitName(entry.Name) == name))
                {
                    foreach (var reference in projects[entry.Name].ProjectReferences.Where(reference => unitOfProject.ContainsKey(reference.Name)))
                    {
                        var owner = unitOfProject[reference.Name];

                        if (owner != name && owner != SharedName && owner != NotPlacedName)
                        {
                            notes.Add($"References project {reference.Name} directly, which belongs to {owner}; that is a compile-time link in addition to any runtime call.");
                        }
                    }
                }

                if (name == NotPlacedName)
                {
                    notes.Add("These items could not be tied to a project or to code that uses them (for example settings read only from configuration). They are counted but not attributed.");
                }

                return new DeployableUnit(name, kind, memberProjects, notes.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList());
            })
            .OrderBy(unit => unit.Name, StringComparer.Ordinal)
            .ToList();

        return new UnitMap(units, unitOfNode, unitOfProject);
    }

    /// <summary>
    /// A plain-language label for a deployable project, from its own facts. The set of labels is open: anything the tool
    /// cannot tell apart is just an "Application", never forced into another kind.
    /// </summary>
    private static string KindLabel(DotNetProjectAssessment project)
    {
        bool References(params string[] names) => project.AssemblyReferences.Any(reference => names.Contains(reference.Name, StringComparer.OrdinalIgnoreCase));
        bool Package(params string[] names) => project.Packages.Any(package => names.Contains(package.Id, StringComparer.OrdinalIgnoreCase));

        return project.Kind switch
        {
            ProjectKind.WebFormsApplication => "Website",
            ProjectKind.WcfServiceHost => "WCF service",
            _ when References("System.ServiceProcess") || Package("Topshelf") => "Windows service",
            _ when References("System.Windows.Forms", "PresentationFramework") => "Desktop application",
            _ => "Application"
        };
    }

    private static void PlaceByUsers(LegacyDependencyGraph graph, Dictionary<string, string> unitOfNode, bool includeMetadata)
    {
        var usersOf = graph.Nodes.ToDictionary(node => node.Id, _ => new List<string>(), StringComparer.Ordinal);

        foreach (var edge in graph.Edges.Where(edge => edge.Basis != "Convention" && (includeMetadata || edge.Basis != "AssemblyMetadata")))
        {
            if (usersOf.TryGetValue(edge.To, out var users) && edge.From != edge.To)
            {
                users.Add(edge.From);
            }
        }

        while (true)
        {
            var open = graph.Nodes.Where(node => !unitOfNode.ContainsKey(node.Id) && usersOf[node.Id].Count > 0).ToList();
            var ready = open.Where(node => usersOf[node.Id].All(unitOfNode.ContainsKey)).ToList();

            if (ready.Count == 0)
            {
                ready = open.Where(node => usersOf[node.Id].Any(unitOfNode.ContainsKey)).ToList();
            }

            if (ready.Count == 0)
            {
                return;
            }

            // Decide the whole round from the state at its start, so the result does not depend on node order.
            var decided = ready.Select(node =>
            {
                var owners = usersOf[node.Id].Where(unitOfNode.ContainsKey).Select(user => unitOfNode[user]).Distinct(StringComparer.Ordinal).ToList();

                return (node.Id, Unit: owners.Count == 1 ? owners[0] : SharedName);
            }).ToList();

            foreach (var (id, unit) in decided)
            {
                unitOfNode[id] = unit;
            }
        }
    }

    private static string FolderOf(string projectPath)
    {
        var path = projectPath.Replace('\\', '/');
        var index = path.LastIndexOf('/');

        return index < 0 ? "" : path[..index];
    }
}
