namespace Net7ClientManager.Net7Site;

internal sealed record Net7SiteSession(
    string UserName,
    IReadOnlyList<Net7SiteStoredCookie> Cookies);
