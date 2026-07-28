namespace Net7ClientManager.Addons.Loading;

internal sealed record AddonPackageReleaseDocument
{
    public const int CurrentFormatVersion = 1;

    public required int FormatVersion { get; init; }

    public required string AddonId { get; init; }

    public required string Version { get; init; }

    public required string PublisherId { get; init; }

    public required string PublisherName { get; init; }

    public required DateTimeOffset PublishedAt { get; init; }

    public string? Summary { get; init; }
}
