namespace Net7ClientManager.Observations.Models;

/// <summary>
/// Native gutter-radar observation backing the public nearby-targets domain.
/// Durable identity is ActiveSectorNumber + ObjectId. Native addresses remain
/// observation diagnostics and are never projected into the addon API.
/// </summary>
public sealed record ClientGutterRadarObservation
{
    public bool IsAvailable { get; init; }

    public string Status { get; init; } = "";

    public uint ActiveSectorNumber { get; init; }

    public uint MainViewAddress { get; init; }

    public uint RadarSystemAddress { get; init; }

    public uint HoveredClientObjectAddress { get; init; }

    public uint? HoveredObjectId { get; init; }

    public int RadarReportedActiveCount { get; init; }

    public bool ActiveCountMatchesBinderEntryCount { get; init; }

    public uint BinderRegistryAddress { get; init; }

    public uint BinderEntryAddress { get; init; }

    public uint RuntimeTypeNameAddress { get; init; }

    public string RuntimeTypeName { get; init; } = "";

    public uint BinderKeyAddress { get; init; }

    public uint PresentationMapAddress { get; init; }

    public int BucketCount { get; init; }

    public int EntryCount { get; init; }

    public IReadOnlyList<ClientGutterRadarTargetObservation> Targets
    {
        get;
        init;
    } = [];

    public int InsideViewportCount =>
        this.Targets.Count(
            target =>
                target is { IsAvailable: true, IsInsideViewport: true });

    public int GutterCount =>
        this.Targets.Count(
            target =>
                target is { IsAvailable: true, IsInsideViewport: false });

    public static ClientGutterRadarObservation Unavailable(
        string status,
        uint activeSectorNumber = 0)
    {
        return new ClientGutterRadarObservation
        {
            Status = status,
            ActiveSectorNumber = activeSectorNumber,
        };
    }
}
