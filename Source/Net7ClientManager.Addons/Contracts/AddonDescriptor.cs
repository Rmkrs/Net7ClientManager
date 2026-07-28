namespace Net7ClientManager.Addons.Contracts;

public sealed record AddonDescriptor
{
    public required string DirectoryName { get; init; }

    public required string DirectoryPath { get; init; }

    public AddonManifest? Manifest { get; init; }

    public bool IsValid { get; init; }

    public string Error { get; init; } = "";

    public string Notice { get; init; } = "";

    public bool IsDevelopment { get; init; }

    public string PackageSha256 { get; init; } = "";

    public string PublisherId { get; init; } = "";

    public string PublisherName { get; init; } = "";

    public string? AvailableVersion { get; init; }

    public bool IsPinned { get; init; }

    public string Id => this.Manifest?.Id ?? this.DirectoryName;

    public string Name => this.Manifest?.Name ?? this.DirectoryName;

    public string Version => this.Manifest?.Version ?? "";

    public bool HasUpdate =>
        !string.IsNullOrWhiteSpace(this.AvailableVersion) &&
        !string.Equals(
            this.Version,
            this.AvailableVersion,
            StringComparison.Ordinal);
}
