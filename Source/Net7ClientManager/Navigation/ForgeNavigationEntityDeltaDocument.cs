namespace Net7ClientManager.Navigation;

internal sealed record ForgeNavigationEntityDeltaDocument
{
    public int ContractVersion { get; init; } =
        ForgeNavigationEntitySnapshotDocument.CurrentContractVersion;

    public long BaseRevision { get; init; }

    public long TargetRevision { get; init; }

    public DateTimeOffset GeneratedAt { get; init; }

    public IReadOnlyList<ForgeNavigationEntityChangeDocument> Changes { get; init; } = [];

    public required string ResultSnapshotSha256 { get; init; }
}
