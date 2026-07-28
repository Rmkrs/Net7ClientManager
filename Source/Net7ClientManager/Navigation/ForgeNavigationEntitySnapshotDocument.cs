namespace Net7ClientManager.Navigation;

internal sealed record ForgeNavigationEntitySnapshotDocument
{
    public const int CurrentContractVersion = 1;

    public int ContractVersion { get; init; } = CurrentContractVersion;

    public long Revision { get; init; }

    public DateTimeOffset GeneratedAt { get; init; }

    public IReadOnlyList<ForgeNavigationEntityRecordDocument> Entities { get; init; } = [];
}
