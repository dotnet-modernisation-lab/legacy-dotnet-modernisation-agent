using System.Net;
using System.Text.RegularExpressions;

namespace LegacyDotNetModernisationAgent.Behaviour;

public sealed record PageResult(int Status, string? RedirectPath, string Body);

/// <summary>A browser-like session: cookies kept, redirects not followed so they can be observed.</summary>
public sealed partial class ProbeSession : IDisposable
{
    private readonly HttpClient client;

    public ProbeSession(Uri baseAddress)
    {
        client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = true, CookieContainer = new CookieContainer() })
        {
            BaseAddress = baseAddress,
            Timeout = TimeSpan.FromSeconds(60)
        };
    }

    public async Task<PageResult> GetAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(path, cancellationToken);

        return await ReadAsync(response, cancellationToken);
    }

    /// <summary>Posts the page's own hidden fields (a true postback) plus the given fields.</summary>
    public async Task<PageResult> PostBackAsync(string path, string pageHtml, IEnumerable<KeyValuePair<string, string>> fields, CancellationToken cancellationToken)
    {
        var form = HiddenFields(pageHtml).Concat(fields).ToList();

        using var response = await client.PostAsync(path, new FormUrlEncodedContent(form), cancellationToken);

        return await ReadAsync(response, cancellationToken);
    }

    private static async Task<PageResult> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var location = response.Headers.Location;
        var redirect = location is null ? null : location.IsAbsoluteUri ? location.AbsolutePath : location.OriginalString.Split('?')[0];

        return new PageResult((int)response.StatusCode, redirect, await response.Content.ReadAsStringAsync(cancellationToken));
    }

    public static IEnumerable<KeyValuePair<string, string>> HiddenFields(string html)
    {
        foreach (Match tag in InputTag().Matches(html))
        {
            var name = Attribute(tag.Value, "name");

            if (name is "__VIEWSTATE" or "__VIEWSTATEGENERATOR" or "__EVENTVALIDATION" or "__EVENTTARGET" or "__EVENTARGUMENT")
            {
                yield return new KeyValuePair<string, string>(name, WebUtility.HtmlDecode(Attribute(tag.Value, "value") ?? ""));
            }
        }
    }

    /// <summary>Name of the first input whose name ends with the suffix (ASP.NET prefixes names with ctl00$MainContent$).</summary>
    public static string? FieldNameEndingWith(string html, string suffix) =>
        InputTag().Matches(html).Select(tag => Attribute(tag.Value, "name")).FirstOrDefault(name => name is not null && name.EndsWith(suffix, StringComparison.Ordinal));

    /// <summary>Visible text of the element whose id ends with the suffix, or "(absent)".</summary>
    public static string TextOfId(string html, string idSuffix)
    {
        var match = Regex.Match(html, $"<(?<tag>span|div|p|h1|h2|label)[^>]*\\bid=\"[^\"]*{Regex.Escape(idSuffix)}\"[^>]*>(?<text>.*?)</\\k<tag>>", RegexOptions.Singleline | RegexOptions.IgnoreCase);

        return match.Success ? Clean(match.Groups["text"].Value) : "(absent)";
    }

    /// <summary>Rows of the table whose id ends with the suffix, each as "cell | cell"; header row excluded.</summary>
    public static IReadOnlyList<string> TableRows(string html, string idSuffix)
    {
        var table = Regex.Match(html, $"<table[^>]*\\bid=\"[^\"]*{Regex.Escape(idSuffix)}\"[^>]*>(?<body>.*?)</table>", RegexOptions.Singleline | RegexOptions.IgnoreCase);

        if (!table.Success)
        {
            return [];
        }

        return Regex.Matches(table.Groups["body"].Value, "<tr[^>]*>(?<row>.*?)</tr>", RegexOptions.Singleline | RegexOptions.IgnoreCase)
            .Select(row => Regex.Matches(row.Groups["row"].Value, "<td[^>]*>(?<cell>.*?)</td>", RegexOptions.Singleline | RegexOptions.IgnoreCase)
                .Select(cell => Clean(cell.Groups["cell"].Value)).ToList())
            .Where(cells => cells.Count > 0)
            .Select(cells => string.Join(" | ", cells))
            .ToList();
    }

    private static string Clean(string html) =>
        Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", " ")), @"\s+", " ").Trim();

    private static string? Attribute(string tag, string name)
    {
        var match = Regex.Match(tag, $"\\b{name}=\"(?<value>[^\"]*)\"", RegexOptions.IgnoreCase);

        return match.Success ? match.Groups["value"].Value : null;
    }

    [GeneratedRegex("<input\\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex InputTag();

    public void Dispose() => client.Dispose();
}
