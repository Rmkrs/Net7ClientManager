namespace Net7ClientManager.Addons.Contracts;

public sealed record AddonInstallationInfo
{
    public required string AddonId { get; init; }

    public required string Version { get; init; }

    public required string PackageSha256 { get; init; }

    public bool IsPinned { get; init; }
}
