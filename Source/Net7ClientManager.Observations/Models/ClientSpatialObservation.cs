namespace Net7ClientManager.Observations.Models;

public sealed record ClientSpatialObservation
{
    public bool IsAvailable { get; init; }

    public string Status { get; init; } = "";

    public uint ClientObjectAddress { get; init; }

    public uint SpatialProviderAddress { get; init; }

    public uint SpatialStateAddress { get; init; }

    public ClientSpatialProviderKind ProviderKind { get; init; }

    public ClientSpatialStateKind StateKind { get; init; }

    public uint ClientTime { get; init; }

    public int TimeOffset { get; init; }

    public uint EffectiveTime { get; init; }

    public uint SampleStartTime { get; init; }

    public uint SampleEndTime { get; init; }

    public ClientSpatialPosition Position { get; init; }

    public float TargetingDistanceRadius { get; init; }

    public static ClientSpatialObservation Unavailable(
        string status,
        uint clientObjectAddress = 0)
    {
        return new ClientSpatialObservation
        {
            Status = status,
            ClientObjectAddress = clientObjectAddress,
        };
    }
}
