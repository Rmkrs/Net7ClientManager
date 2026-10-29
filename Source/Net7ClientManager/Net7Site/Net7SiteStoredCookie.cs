namespace Net7ClientManager.Net7Site;

internal sealed record Net7SiteStoredCookie
{
    public string Name { get; init; } = "";

    public string Value { get; init; } = "";

    public string Domain { get; init; } = "";

    public string Path { get; init; } = "/";

    public DateTime Expires { get; init; }

    public bool Secure { get; init; }

    public bool HttpOnly { get; init; }
}
