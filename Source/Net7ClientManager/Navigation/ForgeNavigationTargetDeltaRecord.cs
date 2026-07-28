namespace Net7ClientManager.Navigation;

internal sealed record ForgeNavigationTargetDeltaRecord
{
    public required string SectorId { get; init; }

    public required ForgeNavigationTargetDocument Target { get; init; }
}
