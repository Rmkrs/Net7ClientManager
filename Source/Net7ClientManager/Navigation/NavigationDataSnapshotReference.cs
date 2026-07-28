namespace Net7ClientManager.Navigation;

internal sealed record NavigationDataSnapshotReference
{
    public required string DatasetEpoch { get; init; }

    public long Revision { get; init; }

    public long AuthorityRevision { get; init; }

    public int ContractVersion { get; init; }

    public required string SnapshotSha256 { get; init; }

    public required string PackageSha256 { get; init; }

    public required string Source { get; init; }

    public DateTimeOffset ActivatedAt { get; init; }
}
