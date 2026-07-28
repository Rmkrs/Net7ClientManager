namespace Net7ClientManager.Observations.Observers;

using System.Globalization;
using Net7ClientManager.Observations.Models;

internal sealed class ClientAsteroidObserver
{
    /*
     * The mining census exposed HarvestableResources.0 through .39.
     * ItemTemplateID > 0 is an occupied resource slot.
     * Empty valid slots use ItemTemplateID == -2.
     *
     * PercentFull is authoritative asteroid availability state, but it is
     * not a simple sum-of-stack-count ratio. An exploding asteroid was
     * observed with PercentFull == 0 while a stale resource slot remained.
     */
    private const int MaximumObservedResourceSlotCount = 40;

    private const string TechLevelName =
        "TechLevel";

    private const string PercentFullName =
        "PercentFull";

    private static readonly string[] slotPropertySuffixes =
    [
        "ItemTemplateID",
        "StackCount",
        "Quality",
        "Structure",
        "AveCost",
        "BuilderName",
        "InstanceInfo",
        "InstanceActivatedEffectInfo",
        "InstanceEquipEffectInfo",
        "Price",
    ];

    private static readonly string[] propertyNames =
        BuildPropertyNames();

    private readonly ClientAuxDataLookupReader auxDataReader =
        new();

    public ClientAsteroidObservation Observe(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        uint asteroidAuxDataAddress)
    {
        if (!this.auxDataReader.TryOpen(
                memory,
                moduleBaseAddress,
                asteroidAuxDataAddress,
                propertyNames,
                out var lookup,
                out var openError))
        {
            return ClientAsteroidObservation.Unavailable(
                openError);
        }

        List<string> errors = [];

        int? techLevel = null;
        float? percentFull = null;
        uint techLevelPropertyAddress = 0;
        uint percentFullPropertyAddress = 0;

        if (!this.auxDataReader.TryReadInt32Property(
                memory,
                lookup,
                TechLevelName,
                out var techLevelSample,
                out var techLevelError))
        {
            errors.Add(techLevelError);
        }
        else
        {
            techLevelPropertyAddress =
                techLevelSample.PropertyAddress;

            if (techLevelSample.IsValid)
            {
                techLevel = techLevelSample.Value;
            }
        }

        if (!this.auxDataReader.TryReadFloatProperty(
                memory,
                lookup,
                PercentFullName,
                out var percentFullSample,
                out var percentFullError))
        {
            errors.Add(percentFullError);
        }
        else
        {
            percentFullPropertyAddress =
                percentFullSample.PropertyAddress;

            if (percentFullSample.IsValid)
            {
                percentFull = percentFullSample.Value;
            }
        }

        List<ClientAsteroidResourceSlotObservation> slots = [];

        var presentSlotCount = 0;
        var validSlotCount = 0;

        for (var slotIndex = 0;
             slotIndex < MaximumObservedResourceSlotCount;
             slotIndex++)
        {
            var prefix = string.Create(
                CultureInfo.InvariantCulture,
                $"HarvestableResources.{slotIndex}");

            var itemTemplateIdName =
                $"{prefix}.ItemTemplateID";

            if (!this.auxDataReader.TryReadInt32Property(
                    memory,
                    lookup,
                    itemTemplateIdName,
                    out var itemTemplateId,
                    out var itemTemplateIdError))
            {
                errors.Add(itemTemplateIdError);

                slots.Add(
                    new ClientAsteroidResourceSlotObservation
                    {
                        Slot = slotIndex,
                        PropertyPrefix = prefix,
                        Status = itemTemplateIdError,
                    });

                continue;
            }

            if (itemTemplateId.PropertyAddress == 0)
            {
                slots.Add(
                    new ClientAsteroidResourceSlotObservation
                    {
                        Slot = slotIndex,
                        PropertyPrefix = prefix,
                        Status =
                            "Resource slot is not present in the AuxData lookup",
                    });

                continue;
            }

            presentSlotCount++;

            Dictionary<string, uint> propertyAddresses =
                new(StringComparer.Ordinal)
                {
                    [itemTemplateIdName] =
                        itemTemplateId.PropertyAddress,
                };

            if (!itemTemplateId.IsValid)
            {
                slots.Add(
                    new ClientAsteroidResourceSlotObservation
                    {
                        Slot = slotIndex,
                        PropertyPrefix = prefix,
                        IsPresent = true,
                        Status =
                            "Resource slot is present but not valid",
                        PropertyAddresses =
                            propertyAddresses,
                    });

                continue;
            }

            validSlotCount++;

            if (itemTemplateId.Value <= 0)
            {
                slots.Add(
                    new ClientAsteroidResourceSlotObservation
                    {
                        Slot = slotIndex,
                        PropertyPrefix = prefix,
                        IsPresent = true,
                        IsValid = true,
                        Status = string.Create(
                            CultureInfo.InvariantCulture,
                            $"Empty resource slot; ItemTemplateID={itemTemplateId.Value}"),
                        ItemTemplateId =
                            itemTemplateId.Value,
                        PropertyAddresses =
                            propertyAddresses,
                    });

                continue;
            }

            List<string> slotErrors = [];

            ReadInt32(
                "StackCount",
                out var stackCount);

            ReadFloat(
                "Quality",
                out var quality);

            ReadFloat(
                "Structure",
                out var structure);

            ReadFloat(
                "AveCost",
                out var averageCost);

            ReadString(
                "BuilderName",
                out var builderName);

            ReadString(
                "InstanceInfo",
                out var instanceInfo);

            ReadString(
                "InstanceActivatedEffectInfo",
                out var activatedEffectInfo);

            ReadString(
                "InstanceEquipEffectInfo",
                out var equipEffectInfo);

            ReadPriceRaw(
                out var priceRaw);

            errors.AddRange(slotErrors);

            slots.Add(
                new ClientAsteroidResourceSlotObservation
                {
                    Slot = slotIndex,
                    PropertyPrefix = prefix,
                    IsPresent = true,
                    IsValid = true,
                    Status = slotErrors.Count == 0
                        ? "Occupied resource slot"
                        : string.Create(
                            CultureInfo.InvariantCulture,
                            $"Occupied resource slot with {slotErrors.Count} field read error(s): {slotErrors[0]}"),
                    ItemTemplateId =
                        itemTemplateId.Value,
                    StackCount = stackCount,
                    Quality = quality,
                    Structure = structure,
                    AverageCost = averageCost,
                    BuilderName = builderName,
                    InstanceInfo = instanceInfo,
                    InstanceActivatedEffectInfo =
                        activatedEffectInfo,
                    InstanceEquipEffectInfo =
                        equipEffectInfo,
                    PriceRaw = priceRaw,
                    PropertyAddresses =
                        propertyAddresses,
                });

            continue;

            void ReadInt32(
                string suffix,
                out int? value)
            {
                value = null;

                var propertyName =
                    $"{prefix}.{suffix}";

                if (!this.auxDataReader.TryReadInt32Property(
                        memory,
                        lookup,
                        propertyName,
                        out var sample,
                        out var error))
                {
                    slotErrors.Add(error);
                    return;
                }

                TrackPropertyAddress(
                    propertyName,
                    sample.PropertyAddress);

                if (sample.IsValid)
                {
                    value = sample.Value;
                }
            }

            void ReadFloat(
                string suffix,
                out float? value)
            {
                value = null;

                var propertyName =
                    $"{prefix}.{suffix}";

                if (!this.auxDataReader.TryReadFloatProperty(
                        memory,
                        lookup,
                        propertyName,
                        out var sample,
                        out var error))
                {
                    slotErrors.Add(error);
                    return;
                }

                TrackPropertyAddress(
                    propertyName,
                    sample.PropertyAddress);

                if (sample.IsValid)
                {
                    value = sample.Value;
                }
            }

            void ReadString(
                string suffix,
                out string? value)
            {
                value = null;

                var propertyName =
                    $"{prefix}.{suffix}";

                if (!this.auxDataReader.TryReadStringProperty(
                        memory,
                        lookup,
                        propertyName,
                        out var sample,
                        out var error))
                {
                    slotErrors.Add(error);
                    return;
                }

                TrackPropertyAddress(
                    propertyName,
                    sample.PropertyAddress);

                if (sample.IsValid)
                {
                    value = sample.Value;
                }
            }

            void ReadPriceRaw(
                out ClientRawAuxDataValueObservation? value)
            {
                value = null;

                var propertyName =
                    $"{prefix}.Price";

                if (!this.auxDataReader.TryReadRawProperty(
                        memory,
                        lookup,
                        propertyName,
                        readSecondaryValue: true,
                        out var sample,
                        out var error))
                {
                    slotErrors.Add(error);
                    return;
                }

                TrackPropertyAddress(
                    propertyName,
                    sample.PropertyAddress);

                if (sample.PropertyAddress == 0)
                {
                    return;
                }

                value =
                    new ClientRawAuxDataValueObservation
                    {
                        Name = propertyName,
                        PropertyAddress =
                            sample.PropertyAddress,
                        IsValid = sample.IsValid,
                        PrimaryValue =
                            sample.PrimaryValue,
                        SecondaryValue =
                            sample.SecondaryValue,
                        HasSecondaryValue =
                            sample.HasSecondaryValue,
                    };
            }

            void TrackPropertyAddress(
                string propertyName,
                uint propertyAddress)
            {
                if (propertyAddress != 0)
                {
                    propertyAddresses[propertyName] =
                        propertyAddress;
                }
            }
        }

        var resourceCount = slots.Count(
            slot =>
                slot.IsOccupied);

        var hasAnyTopLevelProperty =
            techLevelPropertyAddress != 0 ||
            percentFullPropertyAddress != 0;

        var hasAnyValidTopLevelProperty =
            techLevel.HasValue ||
            percentFull.HasValue;

        string status;

        if (!hasAnyTopLevelProperty &&
            presentSlotCount == 0)
        {
            status =
                "Asteroid exposes no promoted mining fields";
        }
        else if (!hasAnyValidTopLevelProperty &&
                 validSlotCount == 0)
        {
            status =
                "Harvestable mining fields are present but not hydrated in this object snapshot";
        }
        else if (errors.Count == 0)
        {
            var techLevelText = techLevel.HasValue
                ? techLevel.Value.ToString(
                    CultureInfo.InvariantCulture)
                : "Unavailable";

            var percentFullText = percentFull.HasValue
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"{Math.Clamp(percentFull.Value, 0.0f, 1.0f) * 100.0f:0.###}%")
                : "Unavailable";

            status = string.Create(
                CultureInfo.InvariantCulture,
                $"Available; tech level {techLevelText}, fullness {percentFullText}, {resourceCount} resource stack(s), {validSlotCount} valid slot(s)");
        }
        else
        {
            status = string.Create(
                CultureInfo.InvariantCulture,
                $"Available with {errors.Count} field read error(s); {resourceCount} resource stack(s): {errors[0]}");
        }

        return new ClientAsteroidObservation
        {
            IsAvailable = true,
            Status = status,
            AuxDataLookupAddress =
                lookup.LookupAddress,
            TechLevelPropertyAddress =
                techLevelPropertyAddress,
            PercentFullPropertyAddress =
                percentFullPropertyAddress,
            TechLevel = techLevel,
            PercentFull = percentFull,
            MaximumObservedSlotCount =
                MaximumObservedResourceSlotCount,
            PresentSlotCount =
                presentSlotCount,
            ValidSlotCount =
                validSlotCount,
            Slots = slots,
        };
    }

    private static string[] BuildPropertyNames()
    {
        List<string> names =
        [
            TechLevelName,
            PercentFullName,
        ];

        for (var slotIndex = 0;
             slotIndex < MaximumObservedResourceSlotCount;
             slotIndex++)
        {
            var prefix = string.Create(
                CultureInfo.InvariantCulture,
                $"HarvestableResources.{slotIndex}");

            foreach (var suffix in slotPropertySuffixes)
            {
                names.Add(
                    $"{prefix}.{suffix}");
            }
        }

        return [.. names];
    }
}
