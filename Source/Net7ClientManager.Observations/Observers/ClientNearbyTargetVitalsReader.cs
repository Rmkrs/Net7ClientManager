namespace Net7ClientManager.Observations.Observers;

using Net7ClientManager.Observations.Models;

internal sealed class ClientNearbyTargetVitalsReader
{
    private const string HullPointsName =
        "HullPoints";

    private const string MaximumHullPointsName =
        "MaxHullPoints";

    private const string MaximumShieldPowerName =
        "MaxShieldPower";

    private const string ShieldPercentName =
        "ShieldPercent";

    private static readonly string[] propertyNames =
    [
        HullPointsName,
        MaximumHullPointsName,
        MaximumShieldPowerName,
        ShieldPercentName,
    ];

    private readonly ClientAuxDataLookupReader auxDataReader =
        new();

    public ClientNearbyTargetVitalsObservation Observe(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        uint clientTime,
        uint auxDataAddress,
        ClientNearbyTargetVitalsBinding? cachedBinding)
    {
        if (auxDataAddress == 0)
        {
            return ClientNearbyTargetVitalsObservation.Unavailable(
                "Nearby target ObjectAuxData address is zero");
        }

        var binding = cachedBinding;

        if (binding == null ||
            binding.AuxDataAddress != auxDataAddress)
        {
            if (!this.TryDiscoverBinding(
                    memory,
                    moduleBaseAddress,
                    auxDataAddress,
                    out binding,
                    out var discoveryError))
            {
                return ClientNearbyTargetVitalsObservation.Unavailable(
                    discoveryError);
            }
        }

        if (this.TryReadVitals(
                memory,
                clientTime,
                binding,
                out var observation,
                out _))
        {
            return observation;
        }

        // A cached property address can become stale when a native object is
        // recycled. Rediscover once before surfacing an unavailable sample.
        if (!this.TryDiscoverBinding(
                memory,
                moduleBaseAddress,
                auxDataAddress,
                out binding,
                out var rediscoveryError))
        {
            return ClientNearbyTargetVitalsObservation.Unavailable(
                rediscoveryError);
        }

        if (!this.TryReadVitals(
                memory,
                clientTime,
                binding,
                out observation,
                out var readError))
        {
            return ClientNearbyTargetVitalsObservation.Unavailable(
                readError,
                binding);
        }

        return observation;
    }

    private bool TryDiscoverBinding(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        uint auxDataAddress,
        out ClientNearbyTargetVitalsBinding binding,
        out string error)
    {
        binding = null!;

        if (!this.auxDataReader.TryOpenTargeted(
                memory,
                moduleBaseAddress,
                auxDataAddress,
                propertyNames,
                out var lookup,
                out _,
                out error))
        {
            return false;
        }

        binding = new ClientNearbyTargetVitalsBinding(
            auxDataAddress,
            lookup);

        return true;
    }

    private bool TryReadVitals(
        ProcessMemoryReader memory,
        uint clientTime,
        ClientNearbyTargetVitalsBinding binding,
        out ClientNearbyTargetVitalsObservation observation,
        out string error)
    {
        observation = default;
        error = "";

        if (!this.auxDataReader.TryReadFloatProperty(
                memory,
                binding.Lookup,
                HullPointsName,
                out var hullPoints,
                out error) ||
            !this.auxDataReader.TryReadFloatProperty(
                memory,
                binding.Lookup,
                MaximumHullPointsName,
                out var maximumHullPoints,
                out error) ||
            !this.auxDataReader.TryReadFloatProperty(
                memory,
                binding.Lookup,
                MaximumShieldPowerName,
                out var maximumShieldPower,
                out error) ||
            !this.auxDataReader.TryReadDeltaInterpolatedFloatProperty(
                memory,
                binding.Lookup,
                ShieldPercentName,
                clientTime,
                out var shieldPercent,
                out error))
        {
            return false;
        }

        observation = new ClientNearbyTargetVitalsObservation(
            BuildHullObservation(
                binding.Lookup.LookupAddress,
                hullPoints,
                maximumHullPoints),
            BuildShieldObservation(
                binding.Lookup.LookupAddress,
                maximumShieldPower,
                shieldPercent),
            binding,
            "Available");

        return true;
    }

    private static ClientTargetHullObservation BuildHullObservation(
        uint lookupAddress,
        ClientFloatAuxDataPropertySample hullPoints,
        ClientFloatAuxDataPropertySample maximumHullPoints)
    {
        if (hullPoints.PropertyAddress == 0 &&
            maximumHullPoints.PropertyAddress == 0)
        {
            return ClientTargetHullObservation.NoHullData(
                lookupAddress);
        }

        if (!hullPoints.IsValid &&
            !maximumHullPoints.IsValid)
        {
            return new ClientTargetHullObservation
            {
                IsAvailable = true,
                Status =
                    "Target hull properties are present but not valid",
                AuxDataLookupAddress = lookupAddress,
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

        return new ClientTargetHullObservation
        {
            IsAvailable = true,
            Status = hasCompleteHull
                ? "Available"
                : hullPoints.IsValid &&
                  maximumHullPoints.IsValid
                    ? "Hull values available; MaxHullPoints is not positive"
                    : hullPoints.IsValid
                        ? "HullPoints available; MaxHullPoints unavailable"
                        : "MaxHullPoints available; HullPoints unavailable",
            AuxDataLookupAddress = lookupAddress,
            HullPointsPropertyAddress =
                hullPoints.PropertyAddress,
            MaximumHullPointsPropertyAddress =
                maximumHullPoints.PropertyAddress,
            HasHullPoints = hullPoints.IsValid,
            HasMaximumHullPoints = maximumHullPoints.IsValid,
            HullPointsRaw = hullPoints.Value,
            MaximumHullPointsRaw = maximumHullPoints.Value,
            HullPoints = TruncateToInt32(hullPoints.Value),
            MaximumHullPoints =
                TruncateToInt32(maximumHullPoints.Value),
            HullFraction = hullFraction,
            HullPercent = hullFraction * 100.0f,
        };
    }

    private static ClientTargetShieldObservation BuildShieldObservation(
        uint lookupAddress,
        ClientFloatAuxDataPropertySample maximumShieldPower,
        ClientDeltaFloatAuxDataPropertySample shieldPercent)
    {
        if (maximumShieldPower.PropertyAddress == 0 &&
            shieldPercent.PropertyAddress == 0)
        {
            return ClientTargetShieldObservation.NoShieldData(
                lookupAddress);
        }

        if (!maximumShieldPower.IsValid &&
            !shieldPercent.IsValid)
        {
            return new ClientTargetShieldObservation
            {
                IsAvailable = true,
                Status =
                    "Target shield properties are present but not valid",
                AuxDataLookupAddress = lookupAddress,
                MaximumShieldPowerPropertyAddress =
                    maximumShieldPower.PropertyAddress,
                ShieldPercentPropertyAddress =
                    shieldPercent.PropertyAddress,
            };
        }

        var shieldFraction = shieldPercent.IsValid
            ? Math.Clamp(
                shieldPercent.Value,
                0.0f,
                1.0f)
            : 0.0f;

        var hasCompleteShield =
            maximumShieldPower.IsValid &&
            shieldPercent.IsValid;

        return new ClientTargetShieldObservation
        {
            IsAvailable = true,
            Status = hasCompleteShield
                ? "Available"
                : maximumShieldPower.IsValid
                    ? "Maximum shield power available; ShieldPercent unavailable"
                    : "ShieldPercent available; MaxShieldPower unavailable",
            AuxDataLookupAddress = lookupAddress,
            MaximumShieldPowerPropertyAddress =
                maximumShieldPower.PropertyAddress,
            ShieldPercentPropertyAddress =
                shieldPercent.PropertyAddress,
            HasMaximumShieldPower =
                maximumShieldPower.IsValid,
            HasShieldPercent = shieldPercent.IsValid,
            MaximumShieldPowerRaw =
                maximumShieldPower.Value,
            MaximumShieldPower =
                TruncateToInt32(maximumShieldPower.Value),
            ShieldFraction = shieldFraction,
            ShieldPercent =
                TruncateToInt32(shieldFraction * 100.0f),
            CurrentShieldPower = hasCompleteShield
                ? maximumShieldPower.Value * shieldFraction
                : 0.0f,
        };
    }

    private static int TruncateToInt32(float value)
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
