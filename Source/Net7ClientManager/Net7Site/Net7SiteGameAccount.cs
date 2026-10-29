namespace Net7ClientManager.Net7Site;

// One game account as listed on the Net-7.org Avatar Transfer page.
internal sealed record Net7SiteGameAccount
{
    public required int AccountId { get; init; }

    public required string LoginName { get; init; }

    public required IReadOnlyList<Net7SiteAvatar> Avatars { get; init; }
}
