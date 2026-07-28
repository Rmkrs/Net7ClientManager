namespace Net7ClientManager.Observations.Models;

public sealed record ClientGutterRadarTargetObservation
{
    public bool IsAvailable { get; init; }

    public string Status { get; init; } = "";

    public uint ActiveSectorNumber { get; init; }

    public uint ObjectId { get; init; }

    public ClientSectorObjectIdentity Identity =>
        new(
            this.ActiveSectorNumber,
            this.ObjectId);

    public ClientNearbyTargetKind Kind { get; init; }

    public string Name { get; init; } = "";

    public string Owner { get; init; } = "";

    public string Title { get; init; } = "";

    public string Rank { get; init; } = "";

    public string DisplayName { get; init; } = "";

    public string NameStatus { get; init; } = "";

    public float NormalizedX { get; init; }

    public float NormalizedY { get; init; }

    public bool IsInsideViewport { get; init; }

    public bool IsHovered { get; init; }

    public ClientTargetHullObservation Hull { get; init; } =
        ClientTargetHullObservation.Unavailable(
            "Nearby target hull state was not observed");

    public ClientTargetShieldObservation Shield { get; init; } =
        ClientTargetShieldObservation.Unavailable(
            "Nearby target shield state was not observed");

    public ClientNearbyMobObservation Mob { get; init; } =
        ClientNearbyMobObservation.Unavailable(
            "Nearby target is not a promoted mob observation");

    public ClientObjectRelationshipObservation Relationship { get; init; } =
        ClientObjectRelationshipObservation.Unknown();

    public ClientSpatialObservation Spatial { get; init; } =
        ClientSpatialObservation.Unavailable(
            "Nearby target world position was not observed");

    public ClientSpatialObservation CorpseSpatial { get; init; } =
        ClientSpatialObservation.Unavailable(
            "Nearby target is not a corpse");

    public ClientCorpseObservation Corpse { get; init; } =
        ClientCorpseObservation.NotApplicable();

    // Native diagnostics retained for host-side validation and the later
    // gesture-gated action implementation. These never enter Lua.
    public uint ClientObjectAddress { get; init; }

    public uint PresentationMapNodeAddress { get; init; }

    public uint PresentationAddress { get; init; }

    public uint AuxDataAddress { get; init; }

    public byte RawObjectType { get; init; }

    public uint IconRenderObjectAddress { get; init; }

    public uint IconSubObjectAddress { get; init; }

    public uint IconPartId { get; init; }

    public uint Unknown1C { get; init; }

    public uint Unknown20 { get; init; }

    public uint GutterIndicatorAddress { get; init; }
}
