namespace LegacyDotNetModernisationAgent.Strategy;

/// <summary>
/// Plain-language wording for the customer summary, in one place so it can be reviewed by someone who writes for clients.
/// Nothing here decides anything: each entry only rewords a fact the tool already established. Anything without an entry
/// falls back to the technical wording, never to a guess.
/// </summary>
public static class ClientLanguage
{
    public sealed record GateWording(string Question, string Why);

    public static readonly IReadOnlyDictionary<string, GateWording> Gates = new Dictionary<string, GateWording>(StringComparer.Ordinal)
    {
        ["D-AUTH-TARGET"] = new("How will users and services sign in after migration?", "Sign-in is built on technology that does not carry over directly, and most parts of the estate touch it."),
        ["D-CSLA-TARGET"] = new("Will you keep the CSLA framework, upgrade it, or replace it?", "CSLA shapes how the business logic is written, so this choice sets the pattern for that whole layer."),
        ["D-DATAPORTAL-TARGET"] = new("What should replace the way the application reaches its business objects (the CSLA data portal)?", "It decides whether the website still talks to the service through business objects or through something simpler."),
        ["D-DATA-CONTRACT"] = new("Is any change to the data the service sends and receives acceptable?", "Other applications may depend on its exact shape, so any change needs agreement first."),
        ["D-INTEGRATION-FILES"] = new("Where will files live after migration (local disk, a shared drive or cloud storage)?", "The code reads and writes files directly, and the answer depends on where the new system is hosted."),
        ["D-STATE-MANAGEMENT"] = new("How should information remembered between page visits (session state) be handled?", "The old approach depends on the web server remembering each user; modern hosting usually works differently."),
        ["D-UI-TARGET"] = new("What should the web pages be rebuilt with?", "Web Forms has no direct equivalent in modern .NET, so this is the largest single choice for the website."),
        ["D-WCF-TARGET"] = new("What should replace the WCF service technology?", "WCF is not carried forward into modern .NET in the same form, so the service and its callers need a new way to talk."),
        ["D-HTTP-PIPELINE"] = new("How should custom handling of web requests be replaced?", "Custom modules and handlers are tied to the old web server model."),
        ["D-INTEGRATION-EXTERNAL"] = new("How should calls to outside web services be handled?", "These calls depend on networks, credentials and agreements outside the code."),
        ["D-INTEGRATION-HOSTING"] = new("How should background and scheduled work be hosted after migration?", "Scheduled work needs a home in the new environment, and that is an operations choice."),
        ["D-OBSERVABILITY"] = new("How should logging and monitoring work after migration?", "The existing logging approach may not carry over, and teams usually want to keep the same visibility."),
        ["D-ERROR-HANDLING"] = new("How should errors be handled and reported after migration?", "Error handling is spread through the code and should be kept consistent."),
        ["D-CACHING"] = new("How should caching be handled after migration?", "Caching behaviour affects speed and correctness, and the old mechanism may not exist in the new platform."),
        ["D-REPORTING"] = new("What will replace the reporting tool?", "Reports are tied to a reporting technology that may not move across."),
        ["D-DATABASE-TARGET"] = new("Which database platform and access approach will be used going forward?", "It affects how the data layer is rewritten and how data is moved."),
        ["D-UNASSESSED-TECH"] = new("Some technology was not recognised by the analysis. Who will review it?", "Until it is reviewed, the size of the work around it cannot be firmed up.")
    };

    /// <summary>The question for a decision gate, falling back to the gate's own title when no plain wording exists.</summary>
    public static GateWording GateFor(string gateId, string title) =>
        Gates.TryGetValue(gateId, out var wording) ? wording : new GateWording(title.EndsWith('?') ? title : title + ".", "The analysis found a choice here that only a person can make.");

    /// <summary>Plain nouns for kinds of code, singular and plural.</summary>
    private static readonly Dictionary<string, (string One, string Many)> KindNouns = new(StringComparer.Ordinal)
    {
        ["Page"] = ("web page", "web pages"),
        ["MasterPage"] = ("page template", "page templates"),
        ["UserControl"] = ("reusable page component", "reusable page components"),
        ["GlobalAsax"] = ("application start-up file", "application start-up files"),
        ["CslaBusinessObject"] = ("business object", "business objects"),
        ["WcfService"] = ("service implementation", "service implementations"),
        ["WcfContract"] = ("service contract", "service contracts"),
        ["WcfHost"] = ("service host file", "service host files"),
        ["WcfClient"] = ("service client", "service clients"),
        ["DataContract"] = ("data contract", "data contracts"),
        ["Dal"] = ("data access class", "data access classes"),
        ["StoredProcedureContract"] = ("stored procedure", "stored procedures"),
        ["Fixture"] = ("data file", "data files"),
        ["Class"] = ("other class", "other classes")
    };

    public static string Noun(string kind, int count)
    {
        var (one, many) = KindNouns.TryGetValue(kind, out var nouns) ? nouns : (kind.ToLowerInvariant(), kind.ToLowerInvariant() + "s");

        return count == 1 ? one : many;
    }

    private static readonly Dictionary<string, (string One, string Many)> TechnologyNouns = new(StringComparer.Ordinal)
    {
        ["AuthenticationMechanism"] = ("sign-in or access mechanism", "sign-in and access mechanisms"),
        ["FrameworkAssembly"] = ("framework library", "framework libraries"),
        ["Assembly"] = ("third-party library", "third-party libraries"),
        ["LibraryType"] = ("third-party library feature", "third-party library features"),
        ["SessionState"] = ("use of session state", "uses of session state"),
        ["Integration"] = ("link to an outside system", "links to outside systems"),
        ["CrossCutting"] = ("shared concern (such as time handling)", "shared concerns (such as time handling)"),
        ["ConfigurationSetting"] = ("configuration setting", "configuration settings")
    };

    public static string TechnologyNoun(string kind, int count)
    {
        var (one, many) = TechnologyNouns.TryGetValue(kind, out var nouns) ? nouns : (kind.ToLowerInvariant(), kind.ToLowerInvariant() + "s");

        return count == 1 ? one : many;
    }

    /// <summary>What each kind of area is, in one plain sentence.</summary>
    public static string WhatIs(string areaKind) => areaKind switch
    {
        "Website" => "The user-facing web application. People reach it through a browser.",
        "WCF service" => "A service that other applications call to get things done and to reach the data.",
        "Data source" => "The database objects and data files the application reads.",
        "Shared" => "Code and libraries used by more than one part of the estate.",
        "Windows service" => "A background program that runs unattended on a server.",
        "Desktop application" => "A program installed on users' Windows computers.",
        "Application" => "A program that is run directly rather than hosted as a website or service.",
        "Not placed" => "Items the analysis counted but could not tie to any one part.",
        _ => "A deployable part of the estate."
    };

    public static string SizeWords(string size) => size switch
    {
        "XS" => "Very small",
        "S" => "Small",
        "M" => "Medium",
        "L" => "Large",
        "XL" => "Very large",
        _ => size
    };

    public static string GapQuestion(string category, string what) => category switch
    {
        "ProjectNotInSolution" => "A project exists in the repository that is not part of any solution, so it was not analysed. Is it still used?",
        "StoredProcedureDefinitionMissing" => "The code calls database stored procedures whose definitions are not in the repository. The definitions are needed from the database to assess the data layer.",
        _ => what.EndsWith('.') ? what : what + "."
    };

    public static string AspectQuestion(int id, string name) => id switch
    {
        7 => "How will existing data be migrated, and how will the cutover be managed?",
        22 => "How is the application built, packaged and deployed today?",
        25 => "How much automated testing exists today?",
        26 => "Are there compliance or audit requirements the migration must meet?",
        27 => "Are there non-technical constraints, such as budget, deadlines, skills or contracts?",
        _ => $"{name}: the repository does not show this. What is the position?"
    };
}
