using LegacyDotNetModernisationAgent.Analysis;
using LegacyDotNetModernisationAgent.Models;

namespace LegacyDotNetModernisationAgent.Behaviour;

public sealed record ProbeTargets(
    string WebSitePath,
    string HostSitePath,
    int HostPort,
    string LoginUrl,
    string DataAccessUrl,
    string ProfileUrl);

/// <summary>
/// Records what the unmodified legacy application does for the representative customer scenarios.
/// It observes; it does not judge. Observations are flat key/value pairs so a later run can be compared exactly.
/// </summary>
public sealed class BehaviourProbe
{
    public const string UnknownCustomer = "9999";

    public static ProbeTargets ResolveTargets(string repositoryRoot, LegacyDotNetProjectAssessment assessment, LegacyDependencyGraph graph)
    {
        var web = assessment.Projects.FirstOrDefault(project => project.Kind == ProjectKind.WebFormsApplication)
            ?? throw new InvalidOperationException("No Web Forms application project was found.");

        var host = assessment.Projects.FirstOrDefault(project => project.Kind == ProjectKind.WcfServiceHost)
            ?? throw new InvalidOperationException("No WCF service host project was found.");

        var client = assessment.Wcf.Clients.FirstOrDefault(candidate => candidate.AddressSettingKey is not null)
            ?? throw new InvalidOperationException("No WCF client with a configured address was found.");

        var address = WebConfigReader.ReadAll(repositoryRoot, assessment.Projects)
            .SelectMany(config => config.AppSettings)
            .FirstOrDefault(setting => setting.Key == client.AddressSettingKey)?.Value
            ?? throw new InvalidOperationException($"appSettings[{client.AddressSettingKey}] was not found.");

        string UrlOf(string pageName)
        {
            var page = graph.Nodes.FirstOrDefault(node => node.Kind == "Page" && node.Name.Equals(pageName, StringComparison.OrdinalIgnoreCase) && node.Project == web.Name)
                ?? throw new InvalidOperationException($"Page {pageName} was not found in the graph.");

            return "/" + Path.GetRelativePath(Path.GetDirectoryName(web.Path)!, page.Location!).Replace('\\', '/');
        }

        return new ProbeTargets(
            Path.Combine(repositoryRoot, Path.GetDirectoryName(web.Path)!),
            Path.Combine(repositoryRoot, Path.GetDirectoryName(host.Path)!),
            new Uri(address).Port,
            UrlOf("Login.aspx"),
            UrlOf("DataAccess.aspx"),
            UrlOf("Profile.aspx"));
    }

    /// <summary>IIS Express can exit at start-up if a previous instance is still shutting down; retry a few times.</summary>
    private static async Task<IisExpressHost> StartWithRetryAsync(string name, string sitePath, int port, string readyPath, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var host = IisExpressHost.Start(name, sitePath, port);

            try
            {
                await host.WaitUntilReadyAsync(readyPath, TimeSpan.FromSeconds(60), cancellationToken);

                return host;
            }
            catch (InvalidOperationException) when (attempt < 3)
            {
                host.Dispose();
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            }
        }
    }

    public async Task<SortedDictionary<string, string>> RunAsync(ProbeTargets targets, Action<string> log, CancellationToken cancellationToken)
    {
        var observed = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var webPort = IisExpressHost.FindFreePort(60171 == targets.HostPort ? 60181 : 60171);

        using var host = await StartWithRetryAsync("WCF host", targets.HostSitePath, targets.HostPort, "LegacyCustomerService.svc?wsdl", cancellationToken);
        using var web = await StartWithRetryAsync("Web Forms app", targets.WebSitePath, webPort, targets.LoginUrl, cancellationToken);

        log($"started WCF host on {targets.HostPort} and Web Forms app on {webPort}");

        using (var wsdl = new ProbeSession(host.BaseAddress))
        {
            var result = await wsdl.GetAsync("LegacyCustomerService.svc?wsdl", cancellationToken);

            observed["wcf.wsdl.status"] = result.Status.ToString();
            observed["wcf.wsdl.exposesContract"] = result.Body.Contains("ILegacyCustomerService", StringComparison.Ordinal).ToString();
        }

        // ---- Anonymous visitor ----------------------------------------------------------
        using (var anonymous = new ProbeSession(web.BaseAddress))
        {
            var result = await anonymous.GetAsync($"{targets.DataAccessUrl}?customerId=1042", cancellationToken);

            observed["access.anonymous.status"] = result.Status.ToString();
            observed["access.anonymous.redirect"] = result.RedirectPath ?? "(none)";
        }

        // ---- Admin: login (initial request vs postback), customers, session ----------------
        using var admin = new ProbeSession(web.BaseAddress);

        var loginPage = await admin.GetAsync(targets.LoginUrl, cancellationToken);
        var userField = ProbeSession.FieldNameEndingWith(loginPage.Body, "UserNameTextBox") ?? throw new InvalidOperationException("Login user-name field not found.");
        var loginButton = ProbeSession.FieldNameEndingWith(loginPage.Body, "LoginButton") ?? throw new InvalidOperationException("Login button not found.");

        observed["login.initial.message"] = ProbeSession.TextOfId(loginPage.Body, "MessageLabel");

        var emptyPostback = await admin.PostBackAsync(targets.LoginUrl, loginPage.Body, [new(userField, ""), new(loginButton, "Login")], cancellationToken);

        observed["login.postback.emptyUser.message"] = ProbeSession.TextOfId(emptyPostback.Body, "MessageLabel");

        var signedIn = await admin.PostBackAsync(targets.LoginUrl, loginPage.Body, [new(userField, "admin"), new(loginButton, "Login")], cancellationToken);

        observed["login.admin.status"] = signedIn.Status.ToString();
        observed["login.admin.redirect"] = signedIn.RedirectPath ?? "(none)";

        PageResult? customerPage = null;

        foreach (var (key, query) in new[]
                 {
                     ("1042", "?customerId=1042"), ("default", ""), (UnknownCustomer, $"?customerId={UnknownCustomer}"),
                     ("-5", "?customerId=-5"), ("0", "?customerId=0"), ("abc", "?customerId=abc")
                 })
        {
            var page = await admin.GetAsync(targets.DataAccessUrl + query, cancellationToken);

            observed[$"customer.{key}.status"] = page.Status.ToString();
            observed[$"customer.{key}.message"] = ProbeSession.TextOfId(page.Body, "StatusLabel");
            observed[$"customer.{key}.rows"] = string.Join(" ; ", ProbeSession.TableRows(page.Body, "CustomerGrid"));

            if (key == "1042")
            {
                customerPage = page;
            }
        }

        // Postback to the page that loaded customer 1042: the page only loads data when not a postback.
        var postback = await admin.PostBackAsync($"{targets.DataAccessUrl}?customerId=1042", customerPage!.Body, [], cancellationToken);

        observed["customer.1042.postback.status"] = postback.Status.ToString();
        observed["customer.1042.postback.message"] = ProbeSession.TextOfId(postback.Body, "StatusLabel");
        observed["customer.1042.postback.rows"] = string.Join(" ; ", ProbeSession.TableRows(postback.Body, "CustomerGrid"));

        var profile = await admin.GetAsync(targets.ProfileUrl, cancellationToken);

        observed["session.profile.status"] = profile.Status.ToString();
        observed["session.profile.message"] = ProbeSession.TextOfId(profile.Body, "SessionLabel");

        // ---- Signed-in but not an Admin ------------------------------------------------------
        using (var user = new ProbeSession(web.BaseAddress))
        {
            var page = await user.GetAsync(targets.LoginUrl, cancellationToken);
            var field = ProbeSession.FieldNameEndingWith(page.Body, "UserNameTextBox")!;
            var button = ProbeSession.FieldNameEndingWith(page.Body, "LoginButton")!;

            await user.PostBackAsync(targets.LoginUrl, page.Body, [new(field, "alice"), new(button, "Login")], cancellationToken);

            var denied = await user.GetAsync($"{targets.DataAccessUrl}?customerId=1042", cancellationToken);

            observed["access.nonAdmin.status"] = denied.Status.ToString();
            observed["access.nonAdmin.message"] = ProbeSession.TextOfId(denied.Body, "StatusLabel");
            observed["access.nonAdmin.rows"] = string.Join(" ; ", ProbeSession.TableRows(denied.Body, "CustomerGrid"));
        }

        // ---- Boundary: stop the WCF host; if retrieval now fails, the UI does not read data itself ----
        host.Stop();
        log("stopped WCF host to test the service boundary");

        var withoutHost = await admin.GetAsync($"{targets.DataAccessUrl}?customerId=1042", cancellationToken);

        observed["boundary.hostStopped.status"] = withoutHost.Status.ToString();
        observed["boundary.hostStopped.message"] = ProbeSession.TextOfId(withoutHost.Body, "StatusLabel");
        observed["boundary.hostStopped.rows"] = string.Join(" ; ", ProbeSession.TableRows(withoutHost.Body, "CustomerGrid"));

        return observed;
    }
}
