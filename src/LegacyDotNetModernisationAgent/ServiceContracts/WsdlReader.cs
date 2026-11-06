using System.Xml;
using System.Xml.Linq;

namespace LegacyDotNetModernisationAgent.ServiceContracts;

/// <summary>
/// Reads a WSDL 1.1 service description (a file or an address, with the schema and WSDL files it imports) and turns it into
/// comparable facts about operations, message shapes, faults, bindings and endpoints. Anything it cannot read or does not model is
/// listed in NotAnalysed, never skipped silently.
/// </summary>
public sealed class WsdlReader
{
    private const int MaxDocuments = 50;
    private const int MaxDepth = 20;

    private static readonly XNamespace Wsdl = "http://schemas.xmlsoap.org/wsdl/";
    private static readonly XNamespace Soap11 = "http://schemas.xmlsoap.org/wsdl/soap/";
    private static readonly XNamespace Soap12 = "http://schemas.xmlsoap.org/wsdl/soap12/";
    private static readonly XNamespace Xs = "http://www.w3.org/2001/XMLSchema";
    private static readonly XNamespace Wsu = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd";

    private readonly List<ContractFact> facts = [];
    private readonly SortedSet<string> notAnalysed = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Ns, string Name), XElement> elements = [];
    private readonly Dictionary<(string Ns, string Name), XElement> complexTypes = [];
    private readonly Dictionary<(string Ns, string Name), XElement> simpleTypes = [];
    private readonly Dictionary<(string Ns, string Name), XElement> messages = [];
    private readonly List<XElement> portTypes = [];
    private readonly List<XElement> bindings = [];
    private readonly List<XElement> services = [];
    private readonly List<XElement> policies = [];
    private readonly Dictionary<string, int> orderCounts = new(StringComparer.Ordinal);
    private bool remoteRoot;
    private bool importedByNamespaceOnly;

    public async Task<ServiceContract> ReadAsync(string location, HttpClient? http, CancellationToken cancellationToken)
    {
        var root = ToUri(location);

        remoteRoot = !root.IsFile;
        var queue = new Queue<Uri>([root]);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Pass 1 reads the description and what it names. Tools such as svcutil write schema imports by namespace only and put the
        // schema files beside the description; pass 2 reads those when the description is a local file.
        for (var pass = 0; pass < 2; pass++)
        {
            if (pass == 1)
            {
                if (!importedByNamespaceOnly || !root.IsFile)
                {
                    break;
                }

                foreach (var sibling in Directory.GetFiles(Path.GetDirectoryName(root.LocalPath)!, "*.xsd").Order(StringComparer.Ordinal))
                {
                    queue.Enqueue(new Uri(sibling));
                }
            }

        while (queue.Count > 0)
        {
            var uri = queue.Dequeue();

            if (!seen.Add(uri.AbsoluteUri))
            {
                continue;
            }

            if (seen.Count > MaxDocuments)
            {
                notAnalysed.Add($"more than {MaxDocuments} documents were imported; the rest were not read");
                break;
            }

            XDocument document;

            try
            {
                document = Parse(await LoadTextAsync(uri, http, cancellationToken));
            }
            catch (Exception exception) when (exception is IOException or HttpRequestException or XmlException or UnauthorizedAccessException or TaskCanceledException or InvalidOperationException)
            {
                if (uri == root)
                {
                    throw new InvalidOperationException($"The service description at {location} could not be read: {exception.Message}");
                }

                notAnalysed.Add($"{uri} could not be read ({exception.GetType().Name}); what it defines is missing from this comparison");
                continue;
            }

            Collect(document, uri, queue);
        }
        }

        if (portTypes.Count == 0)
        {
            notAnalysed.Add("no WSDL 1.1 portType was found (WSDL 2.0 and plain schema files are not supported), so no operations were read");
        }

        Build();

        foreach (var duplicate in facts.GroupBy(fact => fact.Key, StringComparer.Ordinal).Where(group => group.Select(fact => fact.Value).Distinct(StringComparer.Ordinal).Count() > 1))
        {
            notAnalysed.Add($"{duplicate.Key} is declared more than once with different values; only the first is compared");
        }

        return new ServiceContract(location, facts.OrderBy(fact => fact.Key, StringComparer.Ordinal).ToList(), notAnalysed.ToList());
    }

    private static Uri ToUri(string location) =>
        location.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || location.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? new Uri(location)
            : new Uri(Path.GetFullPath(location));

    private static async Task<string> LoadTextAsync(Uri uri, HttpClient? http, CancellationToken cancellationToken)
    {
        if (uri.IsFile)
        {
            return await File.ReadAllTextAsync(uri.LocalPath, cancellationToken);
        }

        if (http is null)
        {
            throw new InvalidOperationException("an address was given but no HTTP client is available");
        }

        return await http.GetStringAsync(uri, cancellationToken);
    }

    private static XDocument Parse(string text)
    {
        using var reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });

        return XDocument.Load(reader, LoadOptions.SetLineInfo);
    }

    // ---- Collecting ---------------------------------------------------------------------------

    private void Collect(XDocument document, Uri baseUri, Queue<Uri> queue)
    {
        var root = document.Root!;

        if (root.Name.LocalName == "description")
        {
            notAnalysed.Add($"{baseUri} is WSDL 2.0, which is not supported");

            return;
        }

        var definitionsNs = root.Attribute("targetNamespace")?.Value ?? "";

        foreach (var import in root.Elements(Wsdl + "import").Select(import => import.Attribute("location")?.Value).Where(value => !string.IsNullOrEmpty(value)))
        {
            Enqueue(queue, baseUri, import!);
        }

        foreach (var message in root.Elements(Wsdl + "message"))
        {
            messages[(definitionsNs, (string?)message.Attribute("name") ?? "")] = message;
        }

        portTypes.AddRange(root.Elements(Wsdl + "portType"));
        bindings.AddRange(root.Elements(Wsdl + "binding"));
        services.AddRange(root.Elements(Wsdl + "service"));
        policies.AddRange(root.DescendantsAndSelf().Where(element => element.Name.LocalName == "Policy" && element.Attribute(Wsu + "Id") is not null));

        foreach (var schema in root.DescendantsAndSelf(Xs + "schema"))
        {
            var schemaNs = schema.Attribute("targetNamespace")?.Value ?? "";

            foreach (var reference in schema.Elements().Where(child => child.Name == Xs + "import" || child.Name == Xs + "include"))
            {
                var schemaLocation = reference.Attribute("schemaLocation")?.Value;

                if (!string.IsNullOrEmpty(schemaLocation))
                {
                    Enqueue(queue, baseUri, schemaLocation);
                }
                else if (reference.Name == Xs + "import")
                {
                    importedByNamespaceOnly = true;
                }
            }

            foreach (var child in schema.Elements())
            {
                var name = (string?)child.Attribute("name") ?? "";

                if (child.Name == Xs + "element")
                {
                    elements[(schemaNs, name)] = child;
                }
                else if (child.Name == Xs + "complexType")
                {
                    complexTypes[(schemaNs, name)] = child;
                }
                else if (child.Name == Xs + "simpleType")
                {
                    simpleTypes[(schemaNs, name)] = child;
                }
            }
        }
    }

    /// <summary>A description fetched from an address may only pull in more files from addresses, never from this machine.</summary>
    private void Enqueue(Queue<Uri> queue, Uri baseUri, string relative)
    {
        var target = new Uri(baseUri, relative);

        if (remoteRoot && !(target.Scheme is "http" or "https"))
        {
            notAnalysed.Add($"an import of {target} was refused because the description came from an address and may not read local files");

            return;
        }

        queue.Enqueue(target);
    }

    // ---- Building facts ---------------------------------------------------------------------

    private void Add(string key, string kind, string direction, string value) => facts.Add(new ContractFact(key, kind, direction, value));

    private void Build()
    {
        foreach (var portType in portTypes)
        {
            var portTypeName = (string?)portType.Attribute("name") ?? "";

            foreach (var operation in portType.Elements(Wsdl + "operation"))
            {
                var operationKey = $"operation/{portTypeName}.{(string?)operation.Attribute("name")}";

                Add(operationKey, "operation", "none", "exists");

                if (operation.Elements().Any(part => part.Attributes().Any(attribute => attribute.Name.LocalName == "Action")))
                {
                    notAnalysed.Add("WS-Addressing actions (wsaw:Action) are declared but not compared");
                }

                foreach (var part in operation.Elements().Where(child => child.Name == Wsdl + "input" || child.Name == Wsdl + "output"))
                {
                    var direction = part.Name.LocalName == "input" ? "in" : "out";

                    AddMessage($"{operationKey}/{direction}", direction, part);
                }

                foreach (var fault in operation.Elements(Wsdl + "fault"))
                {
                    var faultKey = $"{operationKey}/fault/{(string?)fault.Attribute("name")}";

                    Add(faultKey, "fault", "fault", "declared");
                    AddMessage(faultKey, "fault", fault);
                }
            }
        }

        foreach (var binding in bindings)
        {
            AddBinding(binding);
        }

        foreach (var service in services)
        {
            foreach (var port in service.Elements(Wsdl + "port"))
            {
                var key = $"endpoint/{(string?)service.Attribute("name")}/{(string?)port.Attribute("name")}";
                var address = port.Elements().FirstOrDefault(child => child.Name.LocalName == "address")?.Attribute("location")?.Value ?? "(none)";
                var binding = bindings.FirstOrDefault(candidate => (string?)candidate.Attribute("name") == Local((string?)port.Attribute("binding")));

                Add(key, "endpoint", "none", address);
                Add($"{key}/contract", "endpointContract", "none", Local((string?)binding?.Attribute("type")) ?? "(unknown)");
            }
        }
    }

    private void AddMessage(string key, string direction, XElement reference)
    {
        var message = Qualified(reference, (string?)reference.Attribute("message"));

        if (message is null || !messages.TryGetValue(message.Value, out var definition))
        {
            notAnalysed.Add($"message {message?.Name ?? "(unnamed)"} referenced at {key} was not found, so its shape is not compared");

            return;
        }

        foreach (var part in definition.Elements(Wsdl + "part"))
        {
            var partName = (string?)part.Attribute("name") ?? "";
            var elementRef = Qualified(part, (string?)part.Attribute("element"));
            var typeRef = Qualified(part, (string?)part.Attribute("type"));

            if (elementRef is not null)
            {
                if (elements.TryGetValue(elementRef.Value, out var declaration))
                {
                    AddElement(key, direction, declaration, "1", "1", false, [], 0);
                }
                else
                {
                    notAnalysed.Add($"element {elementRef.Value.Name} used at {key} was not found, so its shape is not compared");
                }
            }
            else if (typeRef is not null)
            {
                // RPC style: the part is a name and a type. Flatten it like an element so a changed shape is seen and a renamed type is not.
                var synthetic = new XElement(Xs + "element", new XAttribute("name", partName), new XAttribute("type", (string?)part.Attribute("type") ?? ""));

                foreach (var declaration in part.AncestorsAndSelf().Reverse().SelectMany(ancestor => ancestor.Attributes().Where(attribute => attribute.IsNamespaceDeclaration)))
                {
                    synthetic.SetAttributeValue(declaration.Name, declaration.Value);
                }

                AddElement(key, direction, synthetic, "1", "1", false, [], 0);
            }
        }
    }

    private void AddElement(string path, string direction, XElement declaration, string inheritedMin, string inheritedMax, bool forceOptional, HashSet<string> stack, int depth)
    {
        var source = declaration;
        var reference = Qualified(declaration, (string?)declaration.Attribute("ref"));

        if (reference is not null)
        {
            if (!elements.TryGetValue(reference.Value, out source!))
            {
                notAnalysed.Add($"element reference {reference.Value.Name} at {path} was not found");

                return;
            }
        }

        var name = (string?)source.Attribute("name") ?? "";
        var min = forceOptional ? "0" : (string?)declaration.Attribute("minOccurs") ?? inheritedMin;
        var max = (string?)declaration.Attribute("maxOccurs") ?? inheritedMax;
        var nillable = ((string?)source.Attribute("nillable") ?? "false").ToLowerInvariant();
        var elementPath = $"{path}/{name}";
        var schema = source.Ancestors(Xs + "schema").FirstOrDefault();
        var targetNs = (string?)schema?.Attribute("targetNamespace") ?? "";
        var qualified = source.Parent?.Name == Xs + "schema"
                        || ((string?)source.Attribute("form") ?? (string?)schema?.Attribute("elementFormDefault")) == "qualified";
        var wireNs = qualified ? targetNs : "";
        var typeRef = Qualified(source, (string?)source.Attribute("type"));
        var inlineComplex = source.Element(Xs + "complexType");
        var inlineSimple = source.Element(Xs + "simpleType");

        string display;
        XElement? complex = null;
        XElement? simple = null;

        if (typeRef is not null)
        {
            if (complexTypes.TryGetValue(typeRef.Value, out complex))
            {
                display = "complex";
            }
            else if (simpleTypes.TryGetValue(typeRef.Value, out simple))
            {
                display = SimpleDisplay(simple);
            }
            else
            {
                display = TypeDisplay(typeRef.Value);

                if (typeRef.Value.Ns != Xs.NamespaceName)
                {
                    notAnalysed.Add($"type {typeRef.Value.Name} used at {elementPath} was not found");
                }
            }
        }
        else if (inlineComplex is not null)
        {
            complex = inlineComplex;
            display = "complex";
        }
        else if (inlineSimple is not null)
        {
            simple = inlineSimple;
            display = SimpleDisplay(simple);
        }
        else
        {
            display = "xs:anyType";
        }

        Add(elementPath, "element", direction, $"{display}; min={min}; max={max}; nillable={nillable}; ns={wireNs}");

        if (simple is not null)
        {
            AddEnumeration(elementPath, direction, simple);
        }

        if (complex is not null)
        {
            var typeKey = typeRef is null ? $"inline:{elementPath}" : $"{typeRef.Value.Ns}:{typeRef.Value.Name}";

            if (depth >= MaxDepth || stack.Contains(typeKey))
            {
                Add($"{elementPath}/#recursive", "note", direction, "the type contains itself; deeper levels are not compared");

                return;
            }

            stack.Add(typeKey);
            AddComplex(elementPath, direction, complex, stack, depth + 1);
            stack.Remove(typeKey);
        }
    }

    private void AddComplex(string path, string direction, XElement complex, HashSet<string> stack, int depth)
    {
        foreach (var child in complex.Elements())
        {
            if (child.Name == Xs + "sequence" || child.Name == Xs + "all")
            {
                AddParticles(path, direction, child, false, stack, depth);
            }
            else if (child.Name == Xs + "choice")
            {
                AddParticles(path, direction, child, true, stack, depth);
            }
            else if (child.Name == Xs + "complexContent" || child.Name == Xs + "simpleContent")
            {
                var derivation = child.Elements().FirstOrDefault(item => item.Name == Xs + "extension" || item.Name == Xs + "restriction");

                if (derivation is null)
                {
                    continue;
                }

                var baseRef = Qualified(derivation, (string?)derivation.Attribute("base"));

                if (baseRef is not null && complexTypes.TryGetValue(baseRef.Value, out var baseType))
                {
                    AddComplex(path, direction, baseType, stack, depth + 1);
                }

                AddComplex(path, direction, derivation, stack, depth + 1);
            }
            else if (child.Name == Xs + "attribute")
            {
                var attributeName = (string?)child.Attribute("name") ?? (string?)child.Attribute("ref") ?? "";
                var attributeType = Qualified(child, (string?)child.Attribute("type"));

                Add($"{path}/@{attributeName}", "attribute", direction, $"{(attributeType is null ? "xs:string" : TypeDisplay(attributeType.Value))}; use={(string?)child.Attribute("use") ?? "optional"}");
            }
            else if (child.Name == Xs + "attributeGroup" || child.Name == Xs + "group")
            {
                notAnalysed.Add($"{child.Name.LocalName} reference at {path} is not modelled");
            }
        }
    }

    private void AddParticles(string path, string direction, XElement container, bool isChoice, HashSet<string> stack, int depth)
    {
        if (container.Name == Xs + "sequence")
        {
            var names = container.Elements(Xs + "element").Select(item => (string?)item.Attribute("name") ?? Local((string?)item.Attribute("ref")) ?? "").ToList();

            if (names.Count > 1)
            {
                var seen = orderCounts.GetValueOrDefault(path);

                orderCounts[path] = seen + 1;
                Add($"{path}/#order{(seen == 0 ? "" : (seen + 1).ToString())}", "order", direction, string.Join("|", names));
            }
        }

        foreach (var child in container.Elements())
        {
            if (child.Name == Xs + "element")
            {
                AddElement(path, direction, child, "1", "1", isChoice, stack, depth);
            }
            else if (child.Name == Xs + "sequence" || child.Name == Xs + "all" || child.Name == Xs + "choice")
            {
                AddParticles(path, direction, child, isChoice || child.Name == Xs + "choice", stack, depth);
            }
            else if (child.Name == Xs + "any")
            {
                Add($"{path}/#any", "note", direction, "accepts any element; not analysed");
                notAnalysed.Add($"xs:any at {path} accepts content the tool cannot compare");
            }
            else if (child.Name == Xs + "group")
            {
                notAnalysed.Add($"xs:group reference at {path} is not modelled");
            }
        }
    }

    private void AddEnumeration(string path, string direction, XElement simple)
    {
        var values = simple.Descendants(Xs + "enumeration").Select(item => (string?)item.Attribute("value") ?? "").Order(StringComparer.Ordinal).ToList();

        if (values.Count > 0)
        {
            Add($"{path}/#enum", "enum", direction, string.Join("|", values));
        }
    }

    private string SimpleDisplay(XElement simple)
    {
        var restriction = simple.Element(Xs + "restriction");
        var baseRef = Qualified(restriction ?? simple, (string?)restriction?.Attribute("base"));

        return $"simple:{(baseRef is null ? "xs:string" : TypeDisplay(baseRef.Value))}";
    }

    private static string TypeDisplay((string Ns, string Name) type) =>
        type.Ns == Xs.NamespaceName ? $"xs:{type.Name}" : type.Name;

    private void AddBinding(XElement binding)
    {
        var portTypeName = Local((string?)binding.Attribute("type")) ?? "(unknown)";
        var soap = binding.Elements().FirstOrDefault(child => child.Name == Soap11 + "binding" || child.Name == Soap12 + "binding");

        if (soap is null)
        {
            notAnalysed.Add($"binding {(string?)binding.Attribute("name")} is not a SOAP binding and is not compared");

            return;
        }

        var version = soap.Name.Namespace == Soap12 ? "1.2" : "1.1";
        var transport = (string?)soap.Attribute("transport") ?? "";
        var transportName = transport.Split('/', '#').LastOrDefault(part => part.Length > 0) ?? "(none)";
        var style = (string?)soap.Attribute("style") ?? "document";
        var baseKey = $"binding/{portTypeName}/soap{version}-{transportName}";
        var sharing = bindings.Count(other => BindingBaseKey(other) == baseKey);
        var key = sharing > 1 ? $"{baseKey}[{(string?)binding.Attribute("name")}]" : baseKey;

        Add(key, "binding", "none", $"SOAP {version} over {transportName}");

        var policyNames = PolicyNames(binding);

        if (policyNames.Count > 0)
        {
            Add($"{key}/policy", "policy", "none", string.Join("|", policyNames));
        }

        foreach (var operation in binding.Elements(Wsdl + "operation"))
        {
            var name = (string?)operation.Attribute("name") ?? "";
            var soapOperation = operation.Elements().FirstOrDefault(child => child.Name == Soap11 + "operation" || child.Name == Soap12 + "operation");
            var body = operation.Element(Wsdl + "input")?.Elements().FirstOrDefault(child => child.Name == Soap11 + "body" || child.Name == Soap12 + "body");

            Add($"{key}/operation/{name}/soapAction", "soapAction", "none", (string?)soapOperation?.Attribute("soapAction") ?? "");
            Add($"{key}/operation/{name}/style", "style", "none", (string?)soapOperation?.Attribute("style") ?? style);
            Add($"{key}/operation/{name}/use", "use", "none", (string?)body?.Attribute("use") ?? "literal");

            if ((string?)body?.Attribute("use") == "encoded")
            {
                notAnalysed.Add($"operation {name} uses SOAP encoding; its messages are compared by name only");
            }

            if (body?.Attribute("namespace") is { } bodyNamespace)
            {
                Add($"{key}/operation/{name}/bodyNamespace", "bodyNamespace", "none", bodyNamespace.Value);
            }

            var operationKey = $"operation/{portTypeName}.{name}";

            foreach (var (side, direction) in new[] { ("input", "in"), ("output", "out") })
            {
                foreach (var header in operation.Element(Wsdl + side)?.Elements().Where(child => child.Name.LocalName == "header") ?? [])
                {
                    AddHeader(operationKey, direction, header);
                }

                if (operation.Element(Wsdl + side)?.Elements().Any(child => child.Name.LocalName == "headerfault") == true)
                {
                    notAnalysed.Add($"operation {name} declares SOAP header faults, which are not compared");
                }
            }
        }
    }

    private string BindingBaseKey(XElement binding)
    {
        var soap = binding.Elements().FirstOrDefault(child => child.Name == Soap11 + "binding" || child.Name == Soap12 + "binding");
        var transport = ((string?)soap?.Attribute("transport") ?? "").Split('/', '#').LastOrDefault(part => part.Length > 0) ?? "(none)";

        return $"binding/{Local((string?)binding.Attribute("type")) ?? "(unknown)"}/soap{(soap?.Name.Namespace == Soap12 ? "1.2" : "1.1")}-{transport}";
    }

    /// <summary>A SOAP header is part of the message: custom headers often carry credentials or context that callers must send or read.</summary>
    private void AddHeader(string operationKey, string direction, XElement header)
    {
        var message = Qualified(header, (string?)header.Attribute("message"));
        var partName = (string?)header.Attribute("part");

        if (message is not null && messages.TryGetValue(message.Value, out var definition))
        {
            var part = definition.Elements(Wsdl + "part").FirstOrDefault(candidate => (string?)candidate.Attribute("name") == partName);
            var elementRef = part is null ? null : Qualified(part, (string?)part.Attribute("element"));

            if (elementRef is not null && elements.TryGetValue(elementRef.Value, out var declaration))
            {
                AddElement($"{operationKey}/{direction}-header", direction, declaration, "1", "1", false, [], 0);

                return;
            }
        }

        notAnalysed.Add($"a SOAP header of {operationKey} ({partName ?? "unnamed"}) could not be resolved to an element, so it is not compared");
    }

    private SortedSet<string> PolicyNames(XElement binding)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        var embedded = binding.Descendants().Where(element => element.Name.LocalName is "Policy" or "PolicyReference").ToList();

        foreach (var element in embedded)
        {
            if (element.Name.LocalName == "PolicyReference")
            {
                var id = ((string?)element.Attribute("URI"))?.TrimStart('#');
                var target = policies.FirstOrDefault(policy => (string?)policy.Attribute(Wsu + "Id") == id);

                if (target is not null)
                {
                    foreach (var name in target.Descendants().Select(descendant => descendant.Name.LocalName).Where(local => local != "Policy"))
                    {
                        names.Add(name);
                    }
                }
            }
            else
            {
                foreach (var name in element.Descendants().Select(descendant => descendant.Name.LocalName).Where(local => local != "Policy"))
                {
                    names.Add(name);
                }
            }
        }

        return names;
    }

    // ---- Names --------------------------------------------------------------------------------

    private static string? Local(string? qualified) => qualified is null ? null : qualified[(qualified.IndexOf(':') + 1)..];

    private static (string Ns, string Name)? Qualified(XElement context, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        var colon = value.IndexOf(':');
        var prefix = colon < 0 ? "" : value[..colon];
        var local = colon < 0 ? value : value[(colon + 1)..];
        var ns = prefix.Length == 0 ? context.GetDefaultNamespace().NamespaceName : context.GetNamespaceOfPrefix(prefix)?.NamespaceName ?? "";

        return (ns, local);
    }
}
