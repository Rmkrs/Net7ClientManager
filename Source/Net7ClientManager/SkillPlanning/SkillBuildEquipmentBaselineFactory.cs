namespace Net7ClientManager.SkillPlanning;

using Net7ClientManager.Addons.Contracts;
using Net7ClientManager.Observations.Models;

internal static class SkillBuildEquipmentBaselineFactory
{
    public static SkillBuildEquipmentBaseline Create(
        ClientInventoryObservation inventory,
        ClientSecureInventoryObservation secureInventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(secureInventory);

        if (!inventory.IsAvailable)
        {
            return SkillBuildEquipmentBaseline.Unavailable(
                inventory.Status.Length > 0
                    ? inventory.Status
                    : "Live equipment is unavailable.");
        }

        Dictionary<SkillBuildEquipmentSlot, SkillBuildEquippedItem> items = [];

        foreach (var observedItem in inventory.EquippedSlots)
        {
            if (!observedItem.IsOccupied ||
                observedItem.ItemTemplateId is not > 0)
            {
                continue;
            }

            var slot = ResolveSlot(
                inventory,
                observedItem.Slot);

            if (!slot.HasValue)
            {
                continue;
            }

            items[slot.Value] =
                new SkillBuildEquippedItem(
                    observedItem.ItemTemplateId.Value,
                    observedItem.Template?.Name.Trim() ?? "");
        }

        var hasVariableSlotCounts =
            inventory.FutureWeaponSlotCount.HasValue &&
            inventory.FutureDeviceSlotCount.HasValue;

        return new SkillBuildEquipmentBaseline
        {
            IsAvailable = true,
            Status = hasVariableSlotCounts
                ? "Available; equipped, inventory, and vault template ids"
                : "Available; fixed equipment observed, weapon and device slot counts are unavailable",
            WeaponSlotCount = Math.Max(
                0,
                inventory.FutureWeaponSlotCount.GetValueOrDefault()),
            DeviceSlotCount = Math.Max(
                0,
                inventory.FutureDeviceSlotCount.GetValueOrDefault()),
            HasVariableSlotCounts = hasVariableSlotCounts,
            Items = items,
            InventoryItemCounts = CountItems(inventory.CargoItems),
            VaultItemCounts = secureInventory.IsAvailable
                ? CountItems(secureInventory.Items)
                : new Dictionary<int, int>(),
        };
    }

    public static SkillBuildEquipmentBaseline Create(
        IReadOnlyList<AddonInventorySlotSnapshot> equipmentSlots,
        IReadOnlyList<AddonInventorySlotSnapshot> cargoSlots,
        IReadOnlyList<AddonInventorySlotSnapshot> vaultSlots)
    {
        ArgumentNullException.ThrowIfNull(equipmentSlots);
        ArgumentNullException.ThrowIfNull(cargoSlots);
        ArgumentNullException.ThrowIfNull(vaultSlots);

        Dictionary<SkillBuildEquipmentSlot, SkillBuildEquippedItem> items = [];
        var weaponSlotCount = 0;
        var deviceSlotCount = 0;

        foreach (var archivedItem in equipmentSlots)
        {
            var slot = ResolveArchivedSlot(archivedItem);

            if (!slot.HasValue)
            {
                continue;
            }

            if (slot.Value.Kind == SkillBuildEquipmentKind.Weapon)
            {
                weaponSlotCount = Math.Max(
                    weaponSlotCount,
                    slot.Value.Ordinal);
            }
            else if (slot.Value.Kind == SkillBuildEquipmentKind.Device)
            {
                deviceSlotCount = Math.Max(
                    deviceSlotCount,
                    slot.Value.Ordinal);
            }

            if (!archivedItem.IsOccupied ||
                archivedItem.TemplateId is not > 0)
            {
                continue;
            }

            items[slot.Value] =
                new SkillBuildEquippedItem(
                    archivedItem.TemplateId.Value,
                    archivedItem.Name?.Trim() ?? "");
        }

        var hasVariableSlotCounts =
            weaponSlotCount > 0 &&
            deviceSlotCount > 0;

        return new SkillBuildEquipmentBaseline
        {
            IsAvailable = true,
            Status = hasVariableSlotCounts
                ? "Stored equipment, cargo, and vault state is available."
                : "Stored equipment is available; weapon or device slot counts are incomplete.",
            WeaponSlotCount = weaponSlotCount,
            DeviceSlotCount = deviceSlotCount,
            HasVariableSlotCounts = hasVariableSlotCounts,
            Items = items,
            InventoryItemCounts = CountArchivedItems(cargoSlots),
            VaultItemCounts = CountArchivedItems(vaultSlots),
        };
    }

    private static IReadOnlyDictionary<int, int> CountItems(
        IEnumerable<ClientInventoryItemObservation> items)
    {
        Dictionary<int, int> counts = [];
        foreach (var item in items)
        {
            if (item.ItemTemplateId is not > 0)
            {
                continue;
            }

            counts[item.ItemTemplateId.Value] = checked(
                counts.GetValueOrDefault(item.ItemTemplateId.Value) + 1);
        }

        return counts;
    }

    private static IReadOnlyDictionary<int, int> CountArchivedItems(
        IEnumerable<AddonInventorySlotSnapshot> items)
    {
        Dictionary<int, int> counts = [];

        foreach (var item in items)
        {
            if (!item.IsOccupied ||
                item.TemplateId is not > 0)
            {
                continue;
            }

            counts[item.TemplateId.Value] = checked(
                counts.GetValueOrDefault(item.TemplateId.Value) + 1);
        }

        return counts;
    }

    private static SkillBuildEquipmentSlot? ResolveArchivedSlot(
        AddonInventorySlotSnapshot item)
    {
        var kind = item.EquipmentKind?.Trim();

        if (string.Equals(
                kind,
                "Shield",
                StringComparison.OrdinalIgnoreCase))
        {
            return SkillBuildEquipmentSlot.Shield;
        }

        if (string.Equals(
                kind,
                "Reactor",
                StringComparison.OrdinalIgnoreCase))
        {
            return SkillBuildEquipmentSlot.Reactor;
        }

        if (string.Equals(
                kind,
                "Engine",
                StringComparison.OrdinalIgnoreCase))
        {
            return SkillBuildEquipmentSlot.Engine;
        }

        if (string.Equals(
                kind,
                "Weapon",
                StringComparison.OrdinalIgnoreCase))
        {
            return ResolveVariableSlot(
                SkillBuildEquipmentKind.Weapon,
                item.EquipmentOrdinal);
        }

        if (string.Equals(
                kind,
                "Device",
                StringComparison.OrdinalIgnoreCase))
        {
            return ResolveVariableSlot(
                SkillBuildEquipmentKind.Device,
                item.EquipmentOrdinal);
        }

        return null;
    }

    private static SkillBuildEquipmentSlot? ResolveSlot(
        ClientInventoryObservation inventory,
        int observedSlot)
    {
        var kind = inventory.GetEquipmentSlotKind(observedSlot);

        return kind switch
        {
            ClientEquipmentSlotKind.Shield =>
                SkillBuildEquipmentSlot.Shield,
            ClientEquipmentSlotKind.Reactor =>
                SkillBuildEquipmentSlot.Reactor,
            ClientEquipmentSlotKind.Engine =>
                SkillBuildEquipmentSlot.Engine,
            ClientEquipmentSlotKind.Weapon =>
                ResolveVariableSlot(
                    SkillBuildEquipmentKind.Weapon,
                    inventory.GetEquipmentSlotOrdinal(observedSlot)),
            ClientEquipmentSlotKind.Device =>
                ResolveVariableSlot(
                    SkillBuildEquipmentKind.Device,
                    inventory.GetEquipmentSlotOrdinal(observedSlot)),
            _ => null,
        };
    }

    private static SkillBuildEquipmentSlot? ResolveVariableSlot(
        SkillBuildEquipmentKind kind,
        int? ordinal)
    {
        return ordinal is > 0
            ? new SkillBuildEquipmentSlot(
                kind,
                ordinal.Value)
            : null;
    }
}
