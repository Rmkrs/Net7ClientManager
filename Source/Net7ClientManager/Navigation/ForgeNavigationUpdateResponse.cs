namespace Net7ClientManager.Navigation;

internal sealed record ForgeNavigationUpdateResponse
{
    public required string DatasetEpoch { get; init; }

    public long FromRevision { get; init; }

    public long ToRevision { get; init; }

    public int ContractVersion { get; init; }

    public required string Mode { get; init; }

    public required string Reason { get; init; }

    public required string PackageSha256 { get; init; }

    public long PackageSize { get; init; }

    public required string DownloadUrl { get; init; }

    public required string ResultSnapshotSha256 { get; init; }
}
