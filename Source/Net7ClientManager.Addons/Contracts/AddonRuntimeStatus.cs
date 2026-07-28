namespace Net7ClientManager.Addons.Contracts;

public sealed record AddonRuntimeStatus
{
    public required string AddonId { get; init; }

    public required string Name { get; init; }

    public required string Version { get; init; }

    public string? Description { get; init; }

    public string Author { get; init; } = "";

    public string PublisherName { get; init; } = "";

    public bool IsOfficial { get; init; }

    public bool IsPinned { get; init; }

    public required string DirectoryPath { get; init; }

    public bool IsDevelopment { get; init; }

    public string Source { get; init; } = "Installed package";

    public string? AvailableVersion { get; init; }

    public bool HasUpdate =>
        !string.IsNullOrWhiteSpace(this.AvailableVersion) &&
        !string.Equals(
            this.Version,
            this.AvailableVersion,
            StringComparison.Ordinal);

    public bool IsValid { get; init; }

    public bool IsEnabled { get; init; }

    public AddonRuntimeState State { get; init; }

    public string Error { get; init; } = "";

    public string Detail { get; init; } = "";

    public IReadOnlyList<string> ActivationContexts { get; init; } = [];

    public string CurrentContext { get; init; } = "unknown";

    public DateTimeOffset? LoadedAt { get; init; }

    public DateTimeOffset? LastActivityAt { get; init; }
}
