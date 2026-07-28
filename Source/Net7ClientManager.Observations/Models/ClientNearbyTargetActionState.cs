namespace Net7ClientManager.Observations.Models;

/// <summary>
/// Narrow, freshly validated native state used only by the host-side
/// gesture-gated nearby-target selection action. This type is never projected
/// into the Lua game API.
/// </summary>
public sealed record ClientNearbyTargetActionState
{
    public bool IsAvailable { get; init; }

    public string Status { get; init; } = "";

    public uint ActiveSectorNumber { get; init; }

    public uint ObjectId { get; init; }

    public uint ClientObjectAddress { get; init; }

    public uint RadarSystemAddress { get; init; }

    public uint HoveredClientObjectAddress { get; init; }

    public uint CurrentTargetObjectId { get; init; }

    public float NormalizedX { get; init; }

    public float NormalizedY { get; init; }

    public bool IsInsideViewport { get; init; }

    public bool IsHovered =>
        this.ClientObjectAddress != 0 &&
        this.HoveredClientObjectAddress ==
            this.ClientObjectAddress;

    public bool IsSelected =>
        this.ObjectId != 0 &&
        this.CurrentTargetObjectId == this.ObjectId;

    public static ClientNearbyTargetActionState Unavailable(
        string status,
        uint objectId = 0,
        uint activeSectorNumber = 0)
    {
        return new ClientNearbyTargetActionState
        {
            Status = status,
            ObjectId = objectId,
            ActiveSectorNumber = activeSectorNumber,
        };
    }
}
