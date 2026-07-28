namespace Net7ClientManager.Addons.Registry;

public sealed record AddonRegistrySummary
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    public string? Author { get; init; }

    public required string PublisherId { get; init; }

    public required string PublisherName { get; init; }

    public bool IsOfficial { get; init; }

    public IReadOnlyList<string> Categories { get; init; } = [];

    public IReadOnlyList<string> Tags { get; init; } = [];

    public required AddonRegistryRelease LatestRelease { get; init; }

    public IReadOnlyList<AddonRegistryRelease> Releases { get; init; } = [];
}
