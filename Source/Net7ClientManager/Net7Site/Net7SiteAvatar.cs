namespace Net7ClientManager.Net7Site;

internal sealed record Net7SiteAvatar
{
    public required int AvatarId { get; init; }

    public required string Name { get; init; }

    // The server's slot, 0 to 4. Client Manager's character slot is this plus one.
    public required int Slot { get; init; }

    public string? Race { get; init; }

    public string? Profession { get; init; }
}
