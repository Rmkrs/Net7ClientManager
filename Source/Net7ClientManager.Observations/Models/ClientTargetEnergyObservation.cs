namespace Net7ClientManager.Observations.Models;

public sealed record ClientTargetEnergyObservation
{
    public bool IsAvailable { get; init; }

    public string Status { get; init; } = "";

    public uint AuxDataLookupAddress { get; init; }

    public uint EnergyPercentPropertyAddress { get; init; }

    public uint EnergyChangePerTickPropertyAddress { get; init; }

    public uint MaximumEnergyPowerPropertyAddress { get; init; }

    public bool HasEnergyPercent { get; init; }

    public bool HasEnergyChangePerTick { get; init; }

    public bool HasMaximumEnergyPower { get; init; }

    public float EnergyFraction { get; init; }

    public float EnergyFractionChangePerTick { get; init; }

    public int EnergyPercent { get; init; }

    public float MaximumEnergyPowerRaw { get; init; }

    public int MaximumEnergyPower { get; init; }

    public float DerivedCurrentEnergyPower { get; init; }

    public bool HasCompleteEnergyData =>
        this.HasEnergyPercent &&
        this.HasMaximumEnergyPower;

    public bool HasEnergyData =>
        this.HasEnergyPercent ||
        this.HasEnergyChangePerTick ||
        this.HasMaximumEnergyPower;

    public float EnergyPercentChangePerTick =>
        this.EnergyFractionChangePerTick *
        100.0f;

    public bool IsEnergyDraining =>
        this.HasEnergyChangePerTick &&
        this.EnergyFractionChangePerTick < 0.0f;

    public bool IsEnergyRecovering =>
        this.HasEnergyChangePerTick &&
        this.EnergyFractionChangePerTick > 0.0f;

    public static ClientTargetEnergyObservation Unavailable(
        string status,
        uint auxDataLookupAddress = 0)
    {
        return new ClientTargetEnergyObservation
        {
            Status = status,
            AuxDataLookupAddress =
                auxDataLookupAddress,
        };
    }

    public static ClientTargetEnergyObservation NoEnergyData(
        uint auxDataLookupAddress)
    {
        return new ClientTargetEnergyObservation
        {
            IsAvailable = true,
            Status = "Target exposes no energy fields",
            AuxDataLookupAddress =
                auxDataLookupAddress,
        };
    }
}
