namespace Net7ClientManager.Observations.Models;

/// <summary>
/// Stable semantic and spatial facts promoted for nearby non-player ships,
/// capital ships, and drones. Runtime ObjectId remains an instance identity;
/// this record intentionally exposes only the fields needed to derive a
/// semantic mob variant and positive location sightings.
/// </summary>
public sealed record ClientNearbyMobObservation
{
    public bool IsAvailable { get; init; }

    public string Status { get; init; } = "";

    public string Name { get; init; } = "";

    public string FactionIdentifier { get; init; } = "";

    public int? CombatLevel { get; init; }

    public bool? AutoLevel { get; init; }

    public bool? IsOrganic { get; init; }

    public ClientSpatialObservation Spatial { get; init; } =
        ClientSpatialObservation.Unavailable(
            "Nearby mob spatial state was not observed");

    public static ClientNearbyMobObservation Unavailable(
        string status,
        ClientSpatialObservation? spatial = null)
    {
        return new ClientNearbyMobObservation
        {
            Status = status,
            Spatial = spatial ?? ClientSpatialObservation.Unavailable(status),
        };
    }
}
