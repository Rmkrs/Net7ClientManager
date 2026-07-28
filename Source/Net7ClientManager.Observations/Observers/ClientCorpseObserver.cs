namespace Net7ClientManager.Observations.Observers;

using System.Globalization;
using Net7ClientManager.Observations.Models;

internal sealed class ClientCorpseObserver
{
    /*
     * The census exposed Cargo.0 through Cargo.39 on the corpse object.
     * Slot validity determines whether a slot participates in the current
     * object. ItemTemplateID > 0 is the authoritative occupied-slot test.
     * Empty valid slots were observed with ItemTemplateID == -2.
     */
    private const int MaximumObservedCargoSlotCount = 40;

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

    private static readonly string[] itemTemplatePropertyNames =
        BuildItemTemplatePropertyNames();

    private readonly ClientAuxDataLookupReader auxDataReader =
        new();

    public ClientCorpseObservation Observe(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        uint corpseAuxDataAddress)
    {
        return this.Observe(
            memory,
            moduleBaseAddress,
            corpseAuxDataAddress,
            readOccupiedDetails: true);
    }

    public ClientCorpseObservation Observe(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        uint corpseAuxDataAddress,
        bool readOccupiedDetails)
    {
        var requestedPropertyNames = readOccupiedDetails
            ? propertyNames
            : itemTemplatePropertyNames;

        ClientAuxDataLookupSnapshot lookup;
        string openError;
        var opened = readOccupiedDetails
            ? this.auxDataReader.TryOpen(
                memory,
                moduleBaseAddress,
                corpseAuxDataAddress,
                requestedPropertyNames,
                out lookup,
                out openError)
            : this.auxDataReader.TryOpenAvailableFromPropertyVector(
                memory,
                moduleBaseAddress,
                corpseAuxDataAddress,
                requestedPropertyNames,
                out lookup,
                out openError);

        if (!opened)
        {
            return ClientCorpseObservation.Unavailable(
                openError);
        }

        List<ClientCorpseCargoSlotObservation> slots = [];
        List<string> errors = [];

        var presentSlotCount = 0;
        var validSlotCount = 0;

        for (var slotIndex = 0;
             slotIndex < MaximumObservedCargoSlotCount;
             slotIndex++)
        {
            var prefix = string.Create(
                CultureInfo.InvariantCulture,
                $"Cargo.{slotIndex}");

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
                    new ClientCorpseCargoSlotObservation
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
                    new ClientCorpseCargoSlotObservation
                    {
                        Slot = slotIndex,
                        PropertyPrefix = prefix,
                        Status =
                            "Cargo slot is not present in the AuxData lookup",
                    });

                continue;
            }

            presentSlotCount++;

            if (!itemTemplateId.IsValid)
            {
                slots.Add(
                    new ClientCorpseCargoSlotObservation
                    {
                        Slot = slotIndex,
                        PropertyPrefix = prefix,
                        IsPresent = true,
                        Status =
                            "Cargo slot is present but not valid",
                        PropertyAddresses =
                            new Dictionary<string, uint>(
                                StringComparer.Ordinal)
                            {
                                [itemTemplateIdName] =
                                    itemTemplateId.PropertyAddress,
                            },
                    });

                continue;
            }

            validSlotCount++;

            Dictionary<string, uint> propertyAddresses =
                new(StringComparer.Ordinal)
                {
                    [itemTemplateIdName] =
                        itemTemplateId.PropertyAddress,
                };

            if (itemTemplateId.Value <= 0)
            {
                slots.Add(
                    new ClientCorpseCargoSlotObservation
                    {
                        Slot = slotIndex,
                        PropertyPrefix = prefix,
                        IsPresent = true,
                        IsValid = true,
                        Status = string.Create(
                            CultureInfo.InvariantCulture,
                            $"Empty cargo slot; ItemTemplateID={itemTemplateId.Value}"),
                        ItemTemplateId =
                            itemTemplateId.Value,
                        PropertyAddresses =
                            propertyAddresses,
                    });

                continue;
            }

            if (!readOccupiedDetails)
            {
                slots.Add(
                    new ClientCorpseCargoSlotObservation
                    {
                        Slot = slotIndex,
                        PropertyPrefix = prefix,
                        IsPresent = true,
                        IsValid = true,
                        Status = "Occupied cargo slot",
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
                new ClientCorpseCargoSlotObservation
                {
                    Slot = slotIndex,
                    PropertyPrefix = prefix,
                    IsPresent = true,
                    IsValid = true,
                    Status = slotErrors.Count == 0
                        ? "Occupied cargo slot"
                        : string.Create(
                            CultureInfo.InvariantCulture,
                            $"Occupied cargo slot with {slotErrors.Count} field read error(s): {slotErrors[0]}"),
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

        var occupiedCount = slots.Count(
            slot =>
                slot.IsOccupied);

        string status;

        if (presentSlotCount == 0)
        {
            status =
                "Corpse exposes no Cargo.N.ItemTemplateID properties";
        }
        else if (errors.Count == 0)
        {
            status = string.Create(
                CultureInfo.InvariantCulture,
                $"Available; {occupiedCount} loot item(s), {validSlotCount} valid cargo slot(s), {presentSlotCount} slot object(s) present");
        }
        else
        {
            status = string.Create(
                CultureInfo.InvariantCulture,
                $"Available with {errors.Count} field read error(s); {occupiedCount} loot item(s): {errors[0]}");
        }

        return new ClientCorpseObservation
        {
            IsAvailable = true,
            Status = status,
            AuxDataLookupAddress =
                lookup.LookupAddress,
            MaximumObservedSlotCount =
                MaximumObservedCargoSlotCount,
            PresentSlotCount =
                presentSlotCount,
            ValidSlotCount =
                validSlotCount,
            Slots = slots,
        };
    }

    private static string[] BuildItemTemplatePropertyNames()
    {
        List<string> names = [];

        for (var slotIndex = 0;
             slotIndex < MaximumObservedCargoSlotCount;
             slotIndex++)
        {
            names.Add(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Cargo.{slotIndex}.ItemTemplateID"));
        }

        return [.. names];
    }

    private static string[] BuildPropertyNames()
    {
        List<string> names = [];

        for (var slotIndex = 0;
             slotIndex < MaximumObservedCargoSlotCount;
             slotIndex++)
        {
            var prefix = string.Create(
                CultureInfo.InvariantCulture,
                $"Cargo.{slotIndex}");

            foreach (var suffix in slotPropertySuffixes)
            {
                names.Add(
                    $"{prefix}.{suffix}");
            }
        }

        return [.. names];
    }
}
