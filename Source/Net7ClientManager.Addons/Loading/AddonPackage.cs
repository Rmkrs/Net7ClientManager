namespace Net7ClientManager.Addons.Loading;

using Net7ClientManager.Addons.Contracts;

internal sealed record AddonPackage
{
    public required AddonDescriptor Descriptor { get; init; }

    public required AddonManifest Manifest { get; init; }

    public required string EntryPointSourceName { get; init; }

    public required string EntryPointSource { get; init; }

    public required string PackageSha256 { get; init; }

    public AddonPackageReleaseDocument? Release { get; init; }

    public bool IsDevelopment { get; init; }

    public IReadOnlyDictionary<string, string> Modules { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
