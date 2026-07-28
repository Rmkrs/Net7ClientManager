namespace Net7ClientManager.Navigation;

internal sealed record ForgeNavigationDataPackageManifest
{
    public const int CurrentPackageFormatVersion = 1;

    public int PackageFormatVersion { get; init; } = CurrentPackageFormatVersion;

    public int ContractVersion { get; init; } =
        ForgeNavigationEntitySnapshotDocument.CurrentContractVersion;

    public required string Component { get; init; }

    public required string Mode { get; init; }

    public long DataRevision { get; init; }

    public long SourceAuthorityRevision { get; init; }

    public long? BaseRevision { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public required string Origin { get; init; }

    public required string PayloadEntry { get; init; }

    public required string PayloadSha256 { get; init; }

    public required string ResultSnapshotSha256 { get; init; }

    public IReadOnlyDictionary<string, int> EntityCounts { get; init; } =
        new SortedDictionary<string, int>(StringComparer.Ordinal);
}
