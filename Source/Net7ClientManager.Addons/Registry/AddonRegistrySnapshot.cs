namespace Net7ClientManager.Addons.Registry;

internal sealed record AddonRegistrySnapshot
{
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; init; } = CurrentFormatVersion;

    public required DateTimeOffset FetchedAt { get; init; }

    public IReadOnlyList<AddonRegistrySummary> Addons { get; init; } = [];
}
