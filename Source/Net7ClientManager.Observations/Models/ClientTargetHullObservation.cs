namespace Net7ClientManager.Observations.Models;

public sealed record ClientTargetHullObservation
{
    public bool IsAvailable { get; init; }

    public string Status { get; init; } = "";

    public uint AuxDataLookupAddress { get; init; }

    public uint HullPointsPropertyAddress { get; init; }

    public uint MaximumHullPointsPropertyAddress { get; init; }

    public bool HasHullPoints { get; init; }

    public bool HasMaximumHullPoints { get; init; }

    public float HullPointsRaw { get; init; }

    public float MaximumHullPointsRaw { get; init; }

    public int HullPoints { get; init; }

    public int MaximumHullPoints { get; init; }

    public float HullFraction { get; init; }

    public float HullPercent { get; init; }

    public bool HasCompleteHullData =>
        this.HasHullPoints &&
        this.HasMaximumHullPoints &&
        this.MaximumHullPointsRaw > 0.0f;

    public bool HasHullData =>
        this.HasHullPoints ||
        this.HasMaximumHullPoints;

    public static ClientTargetHullObservation Unavailable(
        string status,
        uint auxDataLookupAddress = 0)
    {
        return new ClientTargetHullObservation
        {
            Status = status,
            AuxDataLookupAddress =
                auxDataLookupAddress,
        };
    }

    public static ClientTargetHullObservation NoHullData(
        uint auxDataLookupAddress)
    {
        return new ClientTargetHullObservation
        {
            IsAvailable = true,
            Status = "Target exposes no hull fields",
            AuxDataLookupAddress =
                auxDataLookupAddress,
        };
    }
}
