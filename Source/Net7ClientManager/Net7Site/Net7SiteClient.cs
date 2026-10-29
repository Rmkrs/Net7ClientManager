namespace Net7ClientManager.Net7Site;

using System.Globalization;
using System.Net;

// Signs in to www.net-7.org the way its login form does, and reads the account pages.
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

        var html = await this.GetPageAsync("cpanel.php", cancellationToken)
            .ConfigureAwait(false);

        if (!Net7SitePageParser.IsSignedIn(html))
        {
            throw new Net7SiteException(
                "Net-7.org did not accept that username and password.",
                isSignedOut: true);
        }
    }

    public async Task<IReadOnlyList<Net7SiteGameAccount>> GetRosterAsync(
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report("Reading game accounts from Net-7.org...");

        var html = await this.GetPageAsync("cpanel.php", cancellationToken)
            .ConfigureAwait(false);

        if (!Net7SitePageParser.IsSignedIn(html))
        {
            throw new Net7SiteException(
                "Not signed in to Net-7.org.",
                isSignedOut: true);
        }

        var accounts = Net7SitePageParser.ParseTransferRoster(html);
        var total = accounts.Sum(account => account.Avatars.Count);
        var done = 0;
        var result = new List<Net7SiteGameAccount>(accounts.Count);

        // One page per avatar for race and profession. A sync is rare, so read them one at a time.
        foreach (var account in accounts)
        {
            var avatars = new List<Net7SiteAvatar>(account.Avatars.Count);

            foreach (var avatar in account.Avatars)
            {
                done++;
                progress?.Report(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Reading characters from Net-7.org ({done}/{total})..."));

                var avatarHtml = await this.GetPageAsync(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"apanel.php?avatar_id={avatar.AvatarId}"),
                        cancellationToken)
                    .ConfigureAwait(false);
                var (race, profession) = Net7SitePageParser.ParseAvatarClass(avatarHtml);

                avatars.Add(avatar with
                {
                    Race = race,
                    Profession = profession,
                });
            }

            result.Add(account with { Avatars = avatars });
        }

        return result;
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
}
