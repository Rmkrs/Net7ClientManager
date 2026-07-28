namespace Net7ClientManager.Addons.Registry;

public sealed record AddonRegistryRelease
{
    public required string AddonId { get; init; }

    public required string Name { get; init; }

    public required string Version { get; init; }

    public required int ApiVersion { get; init; }

    public string? Description { get; init; }

    public string? Author { get; init; }

    public required string PublisherId { get; init; }

    public required string PublisherName { get; init; }

    public required DateTimeOffset PublishedAt { get; init; }

    public string? Summary { get; init; }

    public required string PackageSha256 { get; init; }

    public required long PackageSize { get; init; }

    public required string DownloadUrl { get; init; }
}
