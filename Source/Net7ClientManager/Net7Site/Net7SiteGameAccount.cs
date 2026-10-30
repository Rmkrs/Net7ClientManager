namespace Net7ClientManager.Net7Site;

// One game account as listed by the Net-7.org account API.
internal sealed record Net7SiteGameAccount
{
    public required int AccountId { get; init; }

    public required string LoginName { get; init; }

    public required IReadOnlyList<Net7SiteAvatar> Avatars { get; init; }
}
