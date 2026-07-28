namespace Net7ClientManager.Observations.Models;

public sealed record ClientShipControlFlagsObservation
{
    public bool? LockSpeed { get; init; }

    public bool? LockOrient { get; init; }

    public bool? AutoLevel { get; init; }

    public bool? IsCloaked { get; init; }

    public bool? IsCountermeasureActive { get; init; }

    public bool? IsIncapacitated { get; init; }

    public bool? IsOrganic { get; init; }

    public bool? IsInPvp { get; init; }

    public bool? IsAutoFollowing { get; init; }

    public bool? IsRescueBeaconActive { get; init; }
}
