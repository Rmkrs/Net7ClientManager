namespace Net7ClientManager.Observations.Observers;

using System.Globalization;
using Net7ClientManager.Observations.Models;

internal sealed class ClientInventoryObserver
{
    private const int MaximumCargoSlotCount = 40;
    private const int MaximumEquipmentSlotCount = 20;

    private const string CargoSpaceName =
        "Inventory.CargoSpace";

    private const string FutureWeaponsName =
        "Inventory.FutureWeapons";

    private const string FutureDevicesName =
        "Inventory.FutureDevices";

    private const string EquipMountModelName =
        "Inventory.EquipMountModel";

    private static readonly string[] detailedItemPropertySuffixes =
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
    ];

    private static readonly string[] equippedOperationalPropertySuffixes =
    [
        "ItemState",
        "ReadyTime",
        "TargetRange",
    ];

    public static readonly string[] PropertyNames =
        BuildPropertyNames();

    private readonly ClientAuxDataLookupReader auxDataReader =
        new();

    private readonly ClientEquippedItemOperationalObserver equippedItemOperationalObserver =
        new();

    private readonly ClientRuntimeItemTemplateObserver runtimeItemTemplateObserver =
        new();

    public ClientInventoryObservation Observe(
        ProcessMemoryReader memory,
        int processId,
        uint moduleBaseAddress,
        uint clientContextAddress,
        uint clientTime,
        ClientAuxDataLookupSnapshot lookup)
    {
        List<string> errors = [];

        var cargoSpace = this.ReadInt32(
            memory,
            lookup,
            CargoSpaceName,
            errors);

        var futureWeapons = this.ReadInt32(
            memory,
            lookup,
            FutureWeaponsName,
            errors);

        var futureDevices = this.ReadInt32(
            memory,
            lookup,
            FutureDevicesName,
            errors);

        var equipMountModel = this.ReadString(
            memory,
            lookup,
            EquipMountModelName,
            errors);

        var cargoSlots = this.ReadCollection(
            memory,
            moduleBaseAddress,
            clientTime,
            lookup,
            ClientInventoryCollectionKind.Cargo,
            "Inventory.Cargo",
            MaximumCargoSlotCount,
            readDetailedFields: true,
            readMountBoneNames: false,
            errors: errors);

        var equippedSlots = this.ReadCollection(
            memory,
            moduleBaseAddress,
            clientTime,
            lookup,
            ClientInventoryCollectionKind.Equipped,
            "Inventory.Equipped",
            MaximumEquipmentSlotCount,
            readDetailedFields: true,
            readMountBoneNames: true,
            errors: errors);

        var ammoSlots = this.ReadCollection(
            memory,
            moduleBaseAddress,
            clientTime,
            lookup,
            ClientInventoryCollectionKind.Ammo,
            "Inventory.Ammo",
            MaximumEquipmentSlotCount,
            readDetailedFields: false,
            readMountBoneNames: false,
            errors: errors);

        var definitions = this.runtimeItemTemplateObserver.ResolveDefinitions(
            memory,
            processId,
            moduleBaseAddress,
            clientContextAddress,
            cargoSlots
                .Concat(equippedSlots)
                .Where(slot => slot.IsOccupied)
                .Select(slot => slot.ItemTemplateId!.Value));

        cargoSlots = AttachRuntimeTemplates(
            cargoSlots,
            definitions);
        equippedSlots = AttachRuntimeTemplates(
            equippedSlots,
            definitions);
        ammoSlots = AttachRuntimeTemplates(
            ammoSlots,
            definitions);

        var occupiedCargoCount = cargoSlots.Count(
            slot => slot.IsOccupied);

        var occupiedEquipmentCount = equippedSlots.Count(
            slot => slot.IsOccupied);

        var occupiedAmmoCount = ammoSlots.Count(
            slot => slot.IsOccupied);

        string status;

        if (cargoSlots.All(slot => !slot.IsPresent) &&
            equippedSlots.All(slot => !slot.IsPresent) &&
            ammoSlots.All(slot => !slot.IsPresent) &&
            !cargoSpace.HasValue &&
            !futureWeapons.HasValue &&
            !futureDevices.HasValue)
        {
            status =
                "Local player exposes no promoted inventory fields";
        }
        else if (errors.Count == 0)
        {
            var cargoCapacityText = cargoSpace?.ToString(
                CultureInfo.InvariantCulture) ?? "Unavailable";

            status = string.Create(
                CultureInfo.InvariantCulture,
                $"Available; cargo {occupiedCargoCount}/{cargoCapacityText}, {occupiedEquipmentCount} equipped item(s), {occupiedAmmoCount} ammo stack(s)");
        }
        else
        {
            status = string.Create(
                CultureInfo.InvariantCulture,
                $"Available with {errors.Count} field read error(s): {errors[0]}");
        }

        return new ClientInventoryObservation
        {
            IsAvailable = true,
            Status = status,
            AuxDataLookupAddress = lookup.LookupAddress,
            CargoCapacity = cargoSpace,
            FutureWeaponSlotCount = futureWeapons,
            FutureDeviceSlotCount = futureDevices,
            EquipMountModel = equipMountModel,
            CargoSlots = cargoSlots,
            EquippedSlots = equippedSlots,
            AmmoSlots = ammoSlots,
        };
    }

    public ClientInventoryObservation ObserveCargo(
        ProcessMemoryReader memory,
        int processId,
        uint moduleBaseAddress,
        uint clientContextAddress,
        uint clientTime,
        ClientAuxDataLookupSnapshot lookup,
        ClientInventoryObservation current)
    {
        ArgumentNullException.ThrowIfNull(current);
        List<string> errors = [];

        var cargoSpace = this.ReadInt32(
            memory,
            lookup,
            CargoSpaceName,
            errors);

        var cargoSlots = this.ReadCollection(
            memory,
            moduleBaseAddress,
            clientTime,
            lookup,
            ClientInventoryCollectionKind.Cargo,
            "Inventory.Cargo",
            MaximumCargoSlotCount,
            readDetailedFields: true,
            readMountBoneNames: false,
            errors: errors);

        var definitions = this.runtimeItemTemplateObserver.ResolveDefinitions(
            memory,
            processId,
            moduleBaseAddress,
            clientContextAddress,
            cargoSlots
                .Where(slot => slot.IsOccupied)
                .Select(slot => slot.ItemTemplateId!.Value));

        cargoSlots = AttachRuntimeTemplates(
            cargoSlots,
            definitions);

        var occupiedCargoCount = cargoSlots.Count(
            slot => slot.IsOccupied);
        string status;

        if (cargoSlots.All(slot => !slot.IsPresent) &&
            !cargoSpace.HasValue)
        {
            status =
                "Local player exposes no promoted cargo fields";
        }
        else if (errors.Count == 0)
        {
            var cargoCapacityText = cargoSpace?.ToString(
                CultureInfo.InvariantCulture) ?? "Unavailable";

            status = string.Create(
                CultureInfo.InvariantCulture,
                $"Available; cargo {occupiedCargoCount}/{cargoCapacityText}");
        }
        else
        {
            status = string.Create(
                CultureInfo.InvariantCulture,
                $"Available with {errors.Count} cargo field read error(s): {errors[0]}");
        }

        return current with
        {
            IsAvailable = true,
            Status = status,
            AuxDataLookupAddress = lookup.LookupAddress,
            CargoCapacity = cargoSpace,
            CargoSlots = cargoSlots,
        };
    }

    private IReadOnlyList<ClientInventoryItemObservation> ReadCollection(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        uint clientTime,
        ClientAuxDataLookupSnapshot lookup,
        ClientInventoryCollectionKind collection,
        string prefix,
        int slotCount,
        bool readDetailedFields,
        bool readMountBoneNames,
        List<string> errors)
    {
        List<ClientInventoryItemObservation> slots = [];

        for (var slotIndex = 0;
             slotIndex < slotCount;
             slotIndex++)
        {
            var propertyPrefix = string.Create(
                CultureInfo.InvariantCulture,
                $"{prefix}.{slotIndex}");

            var itemTemplateIdName =
                $"{propertyPrefix}.ItemTemplateID";

            var mountBoneName = readMountBoneNames
                ? this.ReadString(
                    memory,
                    lookup,
                    string.Create(CultureInfo.InvariantCulture, $"Inventory.MountBoneNames.{slotIndex}"),
                    errors)
                : null;

            if (!this.auxDataReader.TryReadInt32Property(
                    memory,
                    lookup,
                    itemTemplateIdName,
                    out var itemTemplateId,
                    out var itemTemplateIdError))
            {
                errors.Add(itemTemplateIdError);

                slots.Add(
                    new ClientInventoryItemObservation
                    {
                        Collection = collection,
                        Slot = slotIndex,
                        PropertyPrefix = propertyPrefix,
                        MountBoneName = mountBoneName,
                        Status = itemTemplateIdError,
                    });

                continue;
            }

            if (itemTemplateId.PropertyAddress == 0)
            {
                slots.Add(
                    new ClientInventoryItemObservation
                    {
                        Collection = collection,
                        Slot = slotIndex,
                        PropertyPrefix = propertyPrefix,
                        MountBoneName = mountBoneName,
                        Status =
                            "Inventory slot is not present in the AuxData lookup",
                    });

                continue;
            }

            Dictionary<string, uint> propertyAddresses =
                new(StringComparer.Ordinal)
                {
                    [itemTemplateIdName] =
                        itemTemplateId.PropertyAddress,
                };

            if (!itemTemplateId.IsValid)
            {
                slots.Add(
                    new ClientInventoryItemObservation
                    {
                        Collection = collection,
                        Slot = slotIndex,
                        PropertyPrefix = propertyPrefix,
                        IsPresent = true,
                        MountBoneName = mountBoneName,
                        Status =
                            "Inventory slot is present but not valid",
                        PropertyAddresses = propertyAddresses,
                    });

                continue;
            }

            List<string> slotErrors = [];

            ClientEquippedItemOperationalObservation operational;

            if (collection ==
                ClientInventoryCollectionKind.Equipped)
            {
                operational =
                    this.equippedItemOperationalObserver.Observe(
                        memory,
                        moduleBaseAddress,
                        clientTime,
                        lookup,
                        propertyPrefix,
                        itemTemplateId.Value,
                        propertyAddresses,
                        out var operationalErrors);

                slotErrors.AddRange(
                    operationalErrors);
            }
            else
            {
                operational =
                    ClientEquippedItemOperationalObservation.Unavailable(
                        "Operational state applies only to equipped slots");
            }

            if (itemTemplateId.Value <= 0)
            {
                errors.AddRange(slotErrors);

                slots.Add(
                    new ClientInventoryItemObservation
                    {
                        Collection = collection,
                        Slot = slotIndex,
                        PropertyPrefix = propertyPrefix,
                        IsPresent = true,
                        IsValid = true,
                        ItemTemplateId = itemTemplateId.Value,
                        MountBoneName = mountBoneName,
                        Operational = operational,
                        Status = slotErrors.Count == 0
                            ? string.Create(
                                CultureInfo.InvariantCulture,
                                $"Empty or unavailable inventory slot; ItemTemplateID={itemTemplateId.Value}")
                            : string.Create(
                                CultureInfo.InvariantCulture,
                                $"Empty or unavailable inventory slot with {slotErrors.Count} field read error(s): {slotErrors[0]}"),
                        PropertyAddresses = propertyAddresses,
                    });

                continue;
            }

            var stackCount = this.ReadInt32(
                memory,
                lookup,
                $"{propertyPrefix}.StackCount",
                slotErrors,
                propertyAddresses);

            float? quality = null;
            float? structure = null;
            float? averageCost = null;
            string? builderName = null;
            string? instanceInfo = null;
            string? activatedEffectInfo = null;
            string? equipEffectInfo = null;

            if (readDetailedFields)
            {
                quality = this.ReadFloat(
                    memory,
                    lookup,
                    $"{propertyPrefix}.Quality",
                    slotErrors,
                    propertyAddresses);

                structure = this.ReadFloat(
                    memory,
                    lookup,
                    $"{propertyPrefix}.Structure",
                    slotErrors,
                    propertyAddresses);

                averageCost = this.ReadFloat(
                    memory,
                    lookup,
                    $"{propertyPrefix}.AveCost",
                    slotErrors,
                    propertyAddresses);

                builderName = this.ReadString(
                    memory,
                    lookup,
                    $"{propertyPrefix}.BuilderName",
                    slotErrors,
                    propertyAddresses);

                instanceInfo = this.ReadString(
                    memory,
                    lookup,
                    $"{propertyPrefix}.InstanceInfo",
                    slotErrors,
                    propertyAddresses);

                activatedEffectInfo = this.ReadString(
                    memory,
                    lookup,
                    $"{propertyPrefix}.InstanceActivatedEffectInfo",
                    slotErrors,
                    propertyAddresses);

                equipEffectInfo = this.ReadString(
                    memory,
                    lookup,
                    $"{propertyPrefix}.InstanceEquipEffectInfo",
                    slotErrors,
                    propertyAddresses);
            }

            errors.AddRange(slotErrors);

            slots.Add(
                new ClientInventoryItemObservation
                {
                    Collection = collection,
                    Slot = slotIndex,
                    PropertyPrefix = propertyPrefix,
                    IsPresent = true,
                    IsValid = true,
                    Status = slotErrors.Count == 0
                        ? "Occupied inventory slot"
                        : string.Create(
                            CultureInfo.InvariantCulture,
                            $"Occupied inventory slot with {slotErrors.Count} field read error(s): {slotErrors[0]}"),
                    ItemTemplateId = itemTemplateId.Value,
                    StackCount = stackCount,
                    Quality = quality,
                    Structure = structure,
                    AverageCost = averageCost,
                    BuilderName = builderName,
                    InstanceInfo = instanceInfo,
                    InstanceActivatedEffectInfo = activatedEffectInfo,
                    InstanceEquipEffectInfo = equipEffectInfo,
                    MountBoneName = mountBoneName,
                    Operational = operational,
                    PropertyAddresses = propertyAddresses,
                });
        }

        return slots;
    }

    private static IReadOnlyList<ClientInventoryItemObservation> AttachRuntimeTemplates(
        IReadOnlyList<ClientInventoryItemObservation> slots,
        IReadOnlyDictionary<int, ClientRuntimeItemTemplateObservation> definitions)
    {
        return
        [
            .. slots.Select(slot =>
                slot.ItemTemplateId is > 0 &&
                definitions.TryGetValue(
                    slot.ItemTemplateId.Value,
                    out var definition)
                    ? slot with
                    {
                        Template = definition,
                    }
                    : slot),
        ];
    }

    private int? ReadInt32(
        ProcessMemoryReader memory,
        ClientAuxDataLookupSnapshot lookup,
        string propertyName,
        List<string> errors,
        IDictionary<string, uint>? propertyAddresses = null)
    {
        if (!this.auxDataReader.TryReadInt32Property(
                memory,
                lookup,
                propertyName,
                out var sample,
                out var error))
        {
            errors.Add(error);
            return null;
        }

        TrackPropertyAddress(
            propertyAddresses,
            propertyName,
            sample.PropertyAddress);

        return sample.IsValid
            ? sample.Value
            : null;
    }

    private float? ReadFloat(
        ProcessMemoryReader memory,
        ClientAuxDataLookupSnapshot lookup,
        string propertyName,
        List<string> errors,
        IDictionary<string, uint>? propertyAddresses = null)
    {
        if (!this.auxDataReader.TryReadFloatProperty(
                memory,
                lookup,
                propertyName,
                out var sample,
                out var error))
        {
            errors.Add(error);
            return null;
        }

        TrackPropertyAddress(
            propertyAddresses,
            propertyName,
            sample.PropertyAddress);

        return sample.IsValid
            ? sample.Value
            : null;
    }

    private string? ReadString(
        ProcessMemoryReader memory,
        ClientAuxDataLookupSnapshot lookup,
        string propertyName,
        List<string> errors,
        IDictionary<string, uint>? propertyAddresses = null)
    {
        if (!this.auxDataReader.TryReadStringProperty(
                memory,
                lookup,
                propertyName,
                out var sample,
                out var error))
        {
            errors.Add(error);
            return null;
        }

        TrackPropertyAddress(
            propertyAddresses,
            propertyName,
            sample.PropertyAddress);

        return sample.IsValid
            ? sample.Value
            : null;
    }

    private static void TrackPropertyAddress(
        IDictionary<string, uint>? propertyAddresses,
        string propertyName,
        uint propertyAddress)
    {
        if (propertyAddresses != null &&
            propertyAddress != 0)
        {
            propertyAddresses[propertyName] =
                propertyAddress;
        }
    }

    private static string[] BuildPropertyNames()
    {
        List<string> names =
        [
            CargoSpaceName,
            FutureWeaponsName,
            FutureDevicesName,
            EquipMountModelName,
        ];

        for (var slotIndex = 0;
             slotIndex < MaximumCargoSlotCount;
             slotIndex++)
        {
            var prefix = string.Create(
                CultureInfo.InvariantCulture,
                $"Inventory.Cargo.{slotIndex}");

            foreach (var suffix in detailedItemPropertySuffixes)
            {
                names.Add(
                    $"{prefix}.{suffix}");
            }
        }

        for (var slotIndex = 0;
             slotIndex < MaximumEquipmentSlotCount;
             slotIndex++)
        {
            var equippedPrefix = string.Create(
                CultureInfo.InvariantCulture,
                $"Inventory.Equipped.{slotIndex}");

            foreach (var suffix in detailedItemPropertySuffixes)
            {
                names.Add(
                    $"{equippedPrefix}.{suffix}");
            }

            foreach (var suffix in equippedOperationalPropertySuffixes)
            {
                names.Add(
                    $"{equippedPrefix}.{suffix}");
            }

            names.Add(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Inventory.Ammo.{slotIndex}.ItemTemplateID"));

            names.Add(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Inventory.Ammo.{slotIndex}.StackCount"));

            names.Add(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Inventory.MountBoneNames.{slotIndex}"));
        }

        return [.. names];
    }
}
