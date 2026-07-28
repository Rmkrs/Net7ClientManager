namespace Net7ClientManager.Observations.Observers;

using Net7ClientManager.Observations.Models;

internal sealed class ClientShipAuxDataObserver
{
    private const string MaximumShieldPowerName =
        "MaxShieldPower";

    private const string ShieldPercentName =
        "ShieldPercent";

    private const string HullPointsName =
        "HullPoints";

    private const string MaximumHullPointsName =
        "MaxHullPoints";

    private const string EnergyPercentName =
        "EnergyPercent";

    private const string EnergyChangePerTickName =
        "EnergyPercent.ChangePerTick";

    private const string MaximumEnergyPowerName =
        "MaxEnergyPower";

    private static readonly string[] vitalPropertyNames =
    [
        MaximumShieldPowerName,
        ShieldPercentName,
        HullPointsName,
        MaximumHullPointsName,
        EnergyPercentName,
        EnergyChangePerTickName,
        MaximumEnergyPowerName,
    ];

    internal static readonly string[] RemoteGroupVitalPropertyNames =
    [
        MaximumShieldPowerName,
        ShieldPercentName,
        HullPointsName,
        MaximumHullPointsName,
    ];

    internal static readonly string[] PropertyNames =
    [
        .. vitalPropertyNames,
        .. ClientShipOperationalObserver.PropertyNames,
    ];

    private readonly ClientAuxDataLookupReader auxDataReader =
        new();

    private readonly ClientShipOperationalObserver operationalObserver =
        new();

    public ClientShipAuxDataObservation Observe(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        uint clientTime,
        uint objectAuxDataAddress)
    {
        if (!this.auxDataReader.TryOpen(
                memory,
                moduleBaseAddress,
                objectAuxDataAddress,
                PropertyNames,
                out var lookup,
                out var openError))
        {
            return ClientShipAuxDataObservation.Unavailable(
                openError);
        }

        return this.Observe(
            memory,
            clientTime,
            lookup);
    }

    internal ClientShipAuxDataObservation Observe(
        ProcessMemoryReader memory,
        uint clientTime,
        ClientAuxDataLookupSnapshot lookup)
    {
        var operational = this.operationalObserver.Observe(
            memory,
            lookup);

        var shield = this.ObserveShield(
            memory,
            lookup,
            clientTime);

        var hull = this.ObserveHull(
            memory,
            lookup);

        var energy = this.ObserveEnergy(
            memory,
            lookup,
            clientTime);

        return new ClientShipAuxDataObservation(
            shield,
            hull,
            energy,
            operational);
    }

    internal (
        ClientTargetShieldObservation Shield,
        ClientTargetHullObservation Hull)
        ObserveRemoteGroupVitals(
            ProcessMemoryReader memory,
            uint clientTime,
            ClientAuxDataLookupSnapshot lookup)
    {
        return (
            this.ObserveShield(
                memory,
                lookup,
                clientTime),
            this.ObserveHull(
                memory,
                lookup));
    }

    private ClientTargetShieldObservation ObserveShield(
        ProcessMemoryReader memory,
        ClientAuxDataLookupSnapshot lookup,
        uint clientTime)
    {
        if (!this.auxDataReader.TryReadFloatProperty(
                memory,
                lookup,
                MaximumShieldPowerName,
                out var maximumShieldPower,
                out var error) ||
            !this.auxDataReader.TryReadDeltaInterpolatedFloatProperty(
                memory,
                lookup,
                ShieldPercentName,
                clientTime,
                out var shieldPercent,
                out error))
        {
            return ClientTargetShieldObservation.Unavailable(
                error,
                lookup.LookupAddress);
        }

        if (maximumShieldPower.PropertyAddress == 0 &&
            shieldPercent.PropertyAddress == 0)
        {
            return ClientTargetShieldObservation.NoShieldData(
                lookup.LookupAddress);
        }

        if (!maximumShieldPower.IsValid &&
            !shieldPercent.IsValid)
        {
            return new ClientTargetShieldObservation
            {
                IsAvailable = true,
                Status =
                    "Target shield properties are present but not valid",
                AuxDataLookupAddress =
                    lookup.LookupAddress,
                MaximumShieldPowerPropertyAddress =
                    maximumShieldPower.PropertyAddress,
                ShieldPercentPropertyAddress =
                    shieldPercent.PropertyAddress,
            };
        }

        var maximumShieldPowerValue =
            maximumShieldPower.IsValid
                ? TruncateToInt32(
                    maximumShieldPower.Value)
                : 0;

        var shieldFraction = shieldPercent.IsValid
            ? Math.Clamp(
                shieldPercent.Value,
                0.0f,
                1.0f)
            : 0.0f;

        var hasCompleteData =
            maximumShieldPower.IsValid &&
            shieldPercent.IsValid;

        var status = hasCompleteData
            ? "Available"
            : maximumShieldPower.IsValid
                ? "Maximum shield power available; ShieldPercent unavailable"
                : "ShieldPercent available; MaxShieldPower unavailable";

        return new ClientTargetShieldObservation
        {
            IsAvailable = true,
            Status = status,
            AuxDataLookupAddress =
                lookup.LookupAddress,
            MaximumShieldPowerPropertyAddress =
                maximumShieldPower.PropertyAddress,
            ShieldPercentPropertyAddress =
                shieldPercent.PropertyAddress,
            HasMaximumShieldPower =
                maximumShieldPower.IsValid,
            HasShieldPercent =
                shieldPercent.IsValid,
            MaximumShieldPowerRaw =
                maximumShieldPower.Value,
            MaximumShieldPower =
                maximumShieldPowerValue,
            ShieldFraction =
                shieldFraction,
            ShieldPercent =
                TruncateToInt32(
                    shieldFraction *
                    100.0f),
            CurrentShieldPower =
                hasCompleteData
                    ? maximumShieldPowerValue *
                      shieldFraction
                    : 0.0f,
        };
    }

    private ClientTargetHullObservation ObserveHull(
        ProcessMemoryReader memory,
        ClientAuxDataLookupSnapshot lookup)
    {
        if (!this.auxDataReader.TryReadFloatProperty(
                memory,
                lookup,
                HullPointsName,
                out var hullPoints,
                out var error) ||
            !this.auxDataReader.TryReadFloatProperty(
                memory,
                lookup,
                MaximumHullPointsName,
                out var maximumHullPoints,
                out error))
        {
            return ClientTargetHullObservation.Unavailable(
                error,
                lookup.LookupAddress);
        }

        if (hullPoints.PropertyAddress == 0 &&
            maximumHullPoints.PropertyAddress == 0)
        {
            return ClientTargetHullObservation.NoHullData(
                lookup.LookupAddress);
        }

        if (!hullPoints.IsValid &&
            !maximumHullPoints.IsValid)
        {
            return new ClientTargetHullObservation
            {
                IsAvailable = true,
                Status =
                    "Target hull properties are present but not valid",
                AuxDataLookupAddress =
                    lookup.LookupAddress,
                HullPointsPropertyAddress =
                    hullPoints.PropertyAddress,
                MaximumHullPointsPropertyAddress =
                    maximumHullPoints.PropertyAddress,
            };
        }

        var hasCompleteHull =
            hullPoints.IsValid &&
            maximumHullPoints is { IsValid: true, Value: > 0.0f };

        var hullFraction = hasCompleteHull
            ? Math.Clamp(
                hullPoints.Value /
                maximumHullPoints.Value,
                0.0f,
                1.0f)
            : 0.0f;

        var status = hasCompleteHull
            ? "Available"
            : hullPoints.IsValid &&
              maximumHullPoints.IsValid
                ? "Hull values available; MaxHullPoints is not positive"
                : hullPoints.IsValid
                    ? "HullPoints available; MaxHullPoints unavailable"
                    : "MaxHullPoints available; HullPoints unavailable";

        return new ClientTargetHullObservation
        {
            IsAvailable = true,
            Status = status,
            AuxDataLookupAddress =
                lookup.LookupAddress,
            HullPointsPropertyAddress =
                hullPoints.PropertyAddress,
            MaximumHullPointsPropertyAddress =
                maximumHullPoints.PropertyAddress,
            HasHullPoints =
                hullPoints.IsValid,
            HasMaximumHullPoints =
                maximumHullPoints.IsValid,
            HullPointsRaw =
                hullPoints.Value,
            MaximumHullPointsRaw =
                maximumHullPoints.Value,
            HullPoints =
                TruncateToInt32(
                    hullPoints.Value),
            MaximumHullPoints =
                TruncateToInt32(
                    maximumHullPoints.Value),
            HullFraction =
                hullFraction,
            HullPercent =
                hullFraction *
                100.0f,
        };
    }

    private ClientTargetEnergyObservation ObserveEnergy(
        ProcessMemoryReader memory,
        ClientAuxDataLookupSnapshot lookup,
        uint clientTime)
    {
        if (!this.auxDataReader.TryReadDeltaInterpolatedFloatProperty(
                memory,
                lookup,
                EnergyPercentName,
                clientTime,
                out var energyPercent,
                out var error) ||
            !this.auxDataReader.TryReadFloatProperty(
                memory,
                lookup,
                EnergyChangePerTickName,
                out var energyChangePerTick,
                out error) ||
            !this.auxDataReader.TryReadFloatProperty(
                memory,
                lookup,
                MaximumEnergyPowerName,
                out var maximumEnergyPower,
                out error))
        {
            return ClientTargetEnergyObservation.Unavailable(
                error,
                lookup.LookupAddress);
        }

        if (energyPercent.PropertyAddress == 0 &&
            energyChangePerTick.PropertyAddress == 0 &&
            maximumEnergyPower.PropertyAddress == 0)
        {
            return ClientTargetEnergyObservation.NoEnergyData(
                lookup.LookupAddress);
        }

        if (!energyPercent.IsValid &&
            !energyChangePerTick.IsValid &&
            !maximumEnergyPower.IsValid)
        {
            return new ClientTargetEnergyObservation
            {
                IsAvailable = true,
                Status =
                    "Target energy properties are present but not valid",
                AuxDataLookupAddress =
                    lookup.LookupAddress,
                EnergyPercentPropertyAddress =
                    energyPercent.PropertyAddress,
                EnergyChangePerTickPropertyAddress =
                    energyChangePerTick.PropertyAddress,
                MaximumEnergyPowerPropertyAddress =
                    maximumEnergyPower.PropertyAddress,
            };
        }

        var energyFraction = energyPercent.IsValid
            ? Math.Clamp(
                energyPercent.Value,
                0.0f,
                1.0f)
            : 0.0f;

        var hasCompleteEnergy =
            energyPercent.IsValid &&
            maximumEnergyPower.IsValid;

        var status = hasCompleteEnergy
            ? "Available; current energy is derived from MaxEnergyPower × EnergyPercent"
            : maximumEnergyPower.IsValid
                ? "MaxEnergyPower available; EnergyPercent unavailable"
                : "EnergyPercent available; MaxEnergyPower unavailable";

        return new ClientTargetEnergyObservation
        {
            IsAvailable = true,
            Status = status,
            AuxDataLookupAddress =
                lookup.LookupAddress,
            EnergyPercentPropertyAddress =
                energyPercent.PropertyAddress,
            EnergyChangePerTickPropertyAddress =
                energyChangePerTick.PropertyAddress,
            MaximumEnergyPowerPropertyAddress =
                maximumEnergyPower.PropertyAddress,
            HasEnergyPercent =
                energyPercent.IsValid,
            HasEnergyChangePerTick =
                energyChangePerTick.IsValid,
            HasMaximumEnergyPower =
                maximumEnergyPower.IsValid,
            EnergyFraction =
                energyFraction,
            EnergyFractionChangePerTick =
                energyChangePerTick.Value,
            EnergyPercent =
                TruncateToInt32(
                    energyFraction *
                    100.0f),
            MaximumEnergyPowerRaw =
                maximumEnergyPower.Value,
            MaximumEnergyPower =
                TruncateToInt32(
                    maximumEnergyPower.Value),
            DerivedCurrentEnergyPower =
                hasCompleteEnergy
                    ? maximumEnergyPower.Value *
                      energyFraction
                    : 0.0f,
        };
    }

    private static int TruncateToInt32(
        float value)
    {
        if (!float.IsFinite(value))
        {
            return 0;
        }

        if (value <= int.MinValue)
        {
            return int.MinValue;
        }

        if (value >= int.MaxValue)
        {
            return int.MaxValue;
        }

        return (int)value;
    }
}
