namespace Net7ClientManager.Addons.Loading;

internal sealed record AddonInstallationState
{
    public const int CurrentFormatVersion = 2;

    public int FormatVersion { get; init; } = CurrentFormatVersion;

    public bool BaselineInitialized { get; set; }

    public Dictionary<string, InstalledAddonReference> Addons { get; init; } =
        new(StringComparer.Ordinal);
}

internal sealed record InstalledAddonReference
{
    public required string Version { get; init; }

    public required string PackageSha256 { get; init; }

    public bool IsPinned { get; init; }
}
