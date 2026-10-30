namespace Net7ClientManager.Net7Site;

using System.Net;

// Signs in to www.net-7.org the way its login form does, and reads its account API.
// Only the sign-in cookies are ever kept (see Net7SiteSessionStore), never the password.
internal sealed class Net7SiteClient : IDisposable
{
    private static readonly Uri siteBaseUri = new(
        "https://www.net-7.org/",
        UriKind.Absolute);

    private readonly CookieContainer cookies = new();
    private readonly HttpClient httpClient;

    public Net7SiteClient()
    {
        var handler = new HttpClientHandler
        {
            CookieContainer = this.cookies,
            UseCookies = true,
            AllowAutoRedirect = true,
        };

        this.httpClient = new HttpClient(handler, disposeHandler: true)
        {
            BaseAddress = siteBaseUri,
            Timeout = TimeSpan.FromSeconds(30),
        };

        this.httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Net7ClientManager");
    }

    public async Task SignInAsync(
        string userName,
        string password,
        bool rememberLogin,
        CancellationToken cancellationToken)
    {
        // Load the front page first so the site's session cookie exists before the form post.
        _ = await this.GetPageAsync("", cancellationToken)
            .ConfigureAwait(false);

        var fields = new List<KeyValuePair<string, string>>
        {
            new("login", "1"),
            new("uname", userName),
            new("pwd", password),
        };

        if (rememberLogin)
        {
            fields.Add(new KeyValuePair<string, string>("remember", "1"));
        }

        using var content = new FormUrlEncodedContent(fields);
        using var response = await this.httpClient.PostAsync(
                "",
                content,
                cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        _ = await this.GetAccountJsonAsync(
                "Net-7.org did not accept that username and password.",
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Net7SiteGameAccount>> GetRosterAsync(
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report("Reading game accounts from Net-7.org...");

        var json = await this.GetAccountJsonAsync(
                "Not signed in to Net-7.org.",
                cancellationToken)
            .ConfigureAwait(false);

        return Net7SiteApiParser.ParseAccounts(json);
    }

    public void RestoreCookies(IEnumerable<Net7SiteStoredCookie> storedCookies)
    {
        ArgumentNullException.ThrowIfNull(storedCookies);

        foreach (var stored in storedCookies)
        {
            try
            {
                this.cookies.Add(new Cookie(
                    stored.Name,
                    stored.Value,
                    stored.Path,
                    stored.Domain)
                {
                    Expires = stored.Expires,
                    Secure = stored.Secure,
                    HttpOnly = stored.HttpOnly,
                });
            }
            catch (CookieException)
            {
                // A cookie the container will not take is simply not restored.
            }
        }
    }

    public IReadOnlyList<Net7SiteStoredCookie> ExportCookies()
    {
        return [.. this.cookies.GetAllCookies()
            .Where(cookie => !cookie.Expired)
            .Select(cookie => new Net7SiteStoredCookie
            {
                Name = cookie.Name,
                Value = cookie.Value,
                Domain = cookie.Domain,
                Path = cookie.Path,
                Expires = cookie.Expires,
                Secure = cookie.Secure,
                HttpOnly = cookie.HttpOnly,
            })];
    }

    public void Dispose()
    {
        this.httpClient.Dispose();
    }

    private async Task<string> GetPageAsync(
        string path,
        CancellationToken cancellationToken)
    {
        using var response = await this.httpClient.GetAsync(
                path,
                cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    // The site's account API answers 401 when the session is not signed in.
    private async Task<string> GetAccountJsonAsync(
        string signedOutMessage,
        CancellationToken cancellationToken)
    {
        using var response = await this.httpClient.GetAsync(
                "api/account",
                cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new Net7SiteException(
                signedOutMessage,
                isSignedOut: true);
        }

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
