namespace Net7ClientManager.Observations.Models;

public sealed record ClientNavigationObservation
{
    public bool IsAvailable { get; init; }

    public string Status { get; init; } = "";

    public uint ActiveSectorNumber { get; init; }

    public uint BinderRegistryAddress { get; init; }

    public uint BinderEntryAddress { get; init; }

    public uint NavigationDataMapAddress { get; init; }

    public uint RuntimeTypeNameAddress { get; init; }

    public string RuntimeTypeName { get; init; } = "";

    public int BucketCount { get; init; }

    public int EntryCount { get; init; }

    public IReadOnlyList<ClientNavigationTargetObservation> Targets
    {
        get;
        init;
    } = [];

    public int VisitedCount =>
        this.Targets.Count(
            target =>
                target is { IsAvailable: true, PlayerHasVisited: true });

    public int UndiscoveredCount =>
        this.Targets.Count(
            target =>
                target is { IsAvailable: true, PlayerHasVisited: false });

    public int RouteCandidateCount =>
        this.Targets.Count(
            target =>
                target is { IsAvailable: true, NavType: 1 });

    public int HugeCount =>
        this.Targets.Count(
            target =>
                target is { IsAvailable: true, IsHuge: true });

    public static ClientNavigationObservation Unavailable(
        string status,
        uint activeSectorNumber = 0)
    {
        return new ClientNavigationObservation
        {
            Status = status,
            ActiveSectorNumber = activeSectorNumber,
        };
    }
}
