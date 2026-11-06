namespace LegacyDotNetModernisationAgent.ServiceContracts;

/// <summary>
/// Compares two service contracts fact by fact and classifies each difference from the point of view of a caller built against
/// the old contract. The rules are deliberately conservative and fixed, so the same two descriptions always give the same answer;
/// a person decides what to do about each one.
/// </summary>
public static class ContractComparer
{
    public static ContractDiff Compare(ServiceContract old, ServiceContract current)
    {
        var before = old.Facts.GroupBy(fact => fact.Key, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var after = current.Facts.GroupBy(fact => fact.Key, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var changes = new List<ContractChange>();

        // Everything inside an operation that was added or removed is already said by that one change; listing it again is noise.
        var wholeOperations = before.Values.Concat(after.Values)
            .Where(fact => fact.Kind == "operation" && before.ContainsKey(fact.Key) != after.ContainsKey(fact.Key))
            .Select(fact => fact.Key["operation/".Length..])
            .ToList();

        foreach (var key in before.Keys.Union(after.Keys).Order(StringComparer.Ordinal))
        {
            if (wholeOperations.Any(operation => IsInside(key, operation)))
            {
                continue;
            }

            before.TryGetValue(key, out var was);
            after.TryGetValue(key, out var now);

            if (was is not null && now is not null && was.Value == now.Value)
            {
                continue;
            }

            changes.Add(Classify(key, was, now));
        }

        return new ContractDiff(
            old.Source,
            current.Source,
            changes.OrderBy(change => change.Class).ThenBy(change => change.Key, StringComparer.Ordinal).ToList(),
            old.NotAnalysed.Select(note => $"old: {note}").Concat(current.NotAnalysed.Select(note => $"new: {note}")).ToList());
    }

    /// <summary>True when the key belongs to the operation ("PortType.Operation"): its shapes, or its message framing in a binding.</summary>
    private static bool IsInside(string key, string operation)
    {
        if (key.StartsWith($"operation/{operation}/", StringComparison.Ordinal))
        {
            return true;
        }

        var dot = operation.IndexOf('.');
        var portType = operation[..dot];
        var name = operation[(dot + 1)..];

        return key.StartsWith($"binding/{portType}/", StringComparison.Ordinal) && key.Contains($"/operation/{name}/", StringComparison.Ordinal);
    }

    private static ContractChange Classify(string key, ContractFact? was, ContractFact? now)
    {
        var fact = (was ?? now)!;
        var direction = fact.Direction;

        ContractChange Make(ChangeClass changeClass, string reason) => new(changeClass, key, was?.Value, now?.Value, reason);

        if (was is null)
        {
            return fact.Kind switch
            {
                "operation" => Make(ChangeClass.Compatible, "a new operation; existing callers do not use it"),
                "binding" => Make(ChangeClass.Compatible, "a new way to reach the service; existing callers are unaffected"),
                "fault" => Make(ChangeClass.Compatible, "a new fault the service may return"),
                "element" or "attribute" when direction == "in" => RequiredIn(fact.Value)
                    ? Make(ChangeClass.Breaking, "a new required input; existing callers do not send it")
                    : Make(ChangeClass.Compatible, "a new optional input"),
                "element" or "attribute" => Make(ChangeClass.Compatible, "a new item in what callers receive; callers that validate strictly against the old shape may reject it"),
                "endpoint" or "endpointContract" => Make(ChangeClass.Informational, "a new endpoint"),
                "policy" => Make(ChangeClass.Review, "a security or messaging policy is now declared"),
                "note" => Make(ChangeClass.Review, "part of the contract that the tool cannot compare was added"),
                _ => Make(ChangeClass.Informational, "added")
            };
        }

        if (now is null)
        {
            return fact.Kind switch
            {
                "operation" => Make(ChangeClass.Breaking, "the operation is gone; existing callers that use it will fail"),
                "binding" => Make(ChangeClass.Breaking, "the way existing callers reach the service (protocol version or transport) is gone"),
                "fault" => Make(ChangeClass.Breaking, "a fault callers may handle is no longer declared"),
                "element" or "attribute" => direction == "in"
                    ? Make(ChangeClass.Breaking, "an input that existing callers send is no longer in the contract; the new service may reject or ignore it")
                    : Make(ChangeClass.Breaking, "an item that existing callers read is no longer returned"),
                "soapAction" or "style" or "use" => Make(ChangeClass.Breaking, "the message framing that existing callers use is gone"),
                "endpoint" => Make(ChangeClass.Review, "an endpoint is gone"),
                "endpointContract" => Make(ChangeClass.Review, "an endpoint no longer names its contract"),
                "policy" => Make(ChangeClass.Review, "a declared security or messaging policy is gone"),
                "enum" => Make(ChangeClass.Review, "the set of allowed values is no longer declared"),
                "note" => Make(ChangeClass.Review, "part of the contract that the tool could not compare is gone"),
                _ => Make(ChangeClass.Informational, "removed")
            };
        }

        return fact.Kind switch
        {
            "operation" or "binding" => Make(ChangeClass.Informational, "changed"),
            "soapAction" => Make(ChangeClass.Breaking, "the SOAP action existing callers send no longer matches"),
            "style" or "use" => Make(ChangeClass.Breaking, "the message encoding differs from what existing callers produce and expect"),
            "endpoint" => Make(ChangeClass.Informational, "the service address differs; that is a deployment detail, not a contract change"),
            "endpointContract" => Make(ChangeClass.Review, "the endpoint now serves a different contract"),
            "policy" => Make(ChangeClass.Review, "the declared security or messaging policy differs"),
            "enum" => ClassifyEnum(direction, was.Value, now.Value, Make),
            "element" or "attribute" => ClassifyShape(direction, was.Value, now.Value, Make),
            _ => Make(ChangeClass.Review, "changed")
        };
    }

    private static bool RequiredIn(string value) =>
        !value.Contains("min=0", StringComparison.Ordinal) && !value.Contains("use=optional", StringComparison.Ordinal);

    private static ContractChange ClassifyEnum(string direction, string old, string current, Func<ChangeClass, string, ContractChange> make)
    {
        var was = old.Split('|').ToHashSet(StringComparer.Ordinal);
        var now = current.Split('|').ToHashSet(StringComparer.Ordinal);
        var added = now.Except(was).Order(StringComparer.Ordinal).ToList();
        var removed = was.Except(now).Order(StringComparer.Ordinal).ToList();
        var detail = $"{(added.Count > 0 ? $"added {string.Join(", ", added)}" : "")}{(added.Count > 0 && removed.Count > 0 ? "; " : "")}{(removed.Count > 0 ? $"removed {string.Join(", ", removed)}" : "")}";

        if (direction == "in")
        {
            return removed.Count > 0
                ? make(ChangeClass.Breaking, $"values callers may send are no longer accepted ({detail})")
                : make(ChangeClass.Compatible, $"more values are accepted ({detail})");
        }

        return added.Count > 0
            ? make(ChangeClass.Review, $"callers may now receive values they were not built for ({detail})")
            : make(ChangeClass.Compatible, $"fewer values are returned ({detail})");
    }

    /// <summary>Compares "type; min=; max=; nillable=" (or "type; use=") between two versions of the same item.</summary>
    private static ContractChange ClassifyShape(string direction, string old, string current, Func<ChangeClass, string, ContractChange> make)
    {
        var was = Parse(old);
        var now = Parse(current);
        var results = new List<(ChangeClass Class, string Reason)>();

        if (was.GetValueOrDefault("type") != now.GetValueOrDefault("type"))
        {
            results.Add((ChangeClass.Breaking, $"the type changed from {was.GetValueOrDefault("type")} to {now.GetValueOrDefault("type")}"));
        }

        var inbound = direction == "in";

        if (was.GetValueOrDefault("min") != now.GetValueOrDefault("min"))
        {
            var becameRequired = was.GetValueOrDefault("min") == "0" && now.GetValueOrDefault("min") != "0";

            results.Add(inbound == becameRequired
                ? (ChangeClass.Breaking, becameRequired ? "now required; existing callers may not send it" : "now optional; existing callers may receive nothing")
                : (ChangeClass.Compatible, becameRequired ? "now always present" : "now optional for callers to send"));
        }

        if (was.GetValueOrDefault("max") != now.GetValueOrDefault("max"))
        {
            var more = Order(now.GetValueOrDefault("max")) > Order(was.GetValueOrDefault("max"));

            results.Add(inbound != more
                ? (ChangeClass.Breaking, more ? "may now repeat more often than callers expect" : "may now be sent fewer times than callers send it")
                : (ChangeClass.Compatible, more ? "more occurrences are accepted" : "fewer occurrences are returned"));
        }

        if (was.GetValueOrDefault("nillable") != now.GetValueOrDefault("nillable"))
        {
            var becameNillable = now.GetValueOrDefault("nillable") == "true";

            results.Add(inbound != becameNillable
                ? (ChangeClass.Breaking, becameNillable ? "may now be returned as nil" : "no longer accepts nil")
                : (ChangeClass.Compatible, becameNillable ? "nil is now accepted" : "no longer returned as nil"));
        }

        if (was.GetValueOrDefault("use") != now.GetValueOrDefault("use"))
        {
            results.Add((ChangeClass.Breaking, $"use changed from {was.GetValueOrDefault("use")} to {now.GetValueOrDefault("use")}"));
        }

        if (results.Count == 0)
        {
            return make(ChangeClass.Review, "changed in a way the tool does not classify");
        }

        return make(results.Min(result => result.Class), string.Join("; ", results.Select(result => result.Reason)));
    }

    private static int Order(string? max) => max == "unbounded" ? int.MaxValue : int.TryParse(max, out var value) ? value : 1;

    private static Dictionary<string, string> Parse(string value)
    {
        var parts = value.Split("; ");
        var result = new Dictionary<string, string>(StringComparer.Ordinal) { ["type"] = parts[0] };

        foreach (var part in parts.Skip(1))
        {
            var equals = part.IndexOf('=');

            if (equals > 0)
            {
                result[part[..equals]] = part[(equals + 1)..];
            }
        }

        return result;
    }
}
