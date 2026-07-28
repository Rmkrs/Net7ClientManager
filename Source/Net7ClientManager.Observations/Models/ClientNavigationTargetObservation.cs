namespace Net7ClientManager.Observations.Models;

public sealed record ClientNavigationTargetObservation
{
    public bool IsAvailable { get; init; }

    public string Status { get; init; } = "";

    public uint ActiveSectorNumber { get; init; }

    public uint ObjectId { get; init; }

    public ClientSectorObjectIdentity Identity =>
        new(
            this.ActiveSectorNumber,
            this.ObjectId);

    public uint ClientObjectAddress { get; init; }

    public uint NavigationMapNodeAddress { get; init; }

    public uint NavigationDataAddress { get; init; }

    public uint AuxDataAddress { get; init; }

    public byte RawObjectType { get; init; }

    public string Name { get; init; } = "";

    public string Owner { get; init; } = "";

    public string Title { get; init; } = "";

    public string Rank { get; init; } = "";

    public string MapDisplayName { get; init; } = "";

    public string MapDisplayNameSource { get; init; } = "";

    public string StockMapDisplayName =>
        this.PlayerHasVisited
            ? this.MapDisplayName
            : "?";

    public string NameStatus { get; init; } = "";

    public float Signature { get; init; }

    public bool PlayerHasVisited { get; init; }

    public int NavType { get; init; }

    public bool IsHuge { get; init; }

    public bool IsRouteCandidate =>
        this.NavType == 1;

    public ClientSpatialObservation Spatial { get; init; } =
        ClientSpatialObservation.Unavailable(
            "Navigation-object spatial state was not observed");
}
