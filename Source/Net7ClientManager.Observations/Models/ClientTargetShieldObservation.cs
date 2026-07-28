namespace Net7ClientManager.Observations.Models;

public sealed record ClientTargetShieldObservation
{
    public bool IsAvailable { get; init; }

    public string Status { get; init; } = "";

    public uint AuxDataLookupAddress { get; init; }

    public uint MaximumShieldPowerPropertyAddress { get; init; }

    public uint ShieldPercentPropertyAddress { get; init; }

    public bool HasMaximumShieldPower { get; init; }

    public bool HasShieldPercent { get; init; }

    public float MaximumShieldPowerRaw { get; init; }

    public int MaximumShieldPower { get; init; }

    public float ShieldFraction { get; init; }

    public int ShieldPercent { get; init; }

    public float CurrentShieldPower { get; init; }

    public bool HasCurrentShieldPower =>
        this.HasMaximumShieldPower &&
        this.HasShieldPercent;

    public bool HasShieldData =>
        this.HasMaximumShieldPower ||
        this.HasShieldPercent;

    public static ClientTargetShieldObservation Unavailable(
        string status,
        uint auxDataLookupAddress = 0)
    {
        return new ClientTargetShieldObservation
        {
            Status = status,
            AuxDataLookupAddress =
                auxDataLookupAddress,
        };
    }

    public static ClientTargetShieldObservation NoShieldData(
        uint auxDataLookupAddress)
    {
        return new ClientTargetShieldObservation
        {
            IsAvailable = true,
            Status = "Target exposes no shield fields",
            AuxDataLookupAddress =
                auxDataLookupAddress,
        };
    }
}
