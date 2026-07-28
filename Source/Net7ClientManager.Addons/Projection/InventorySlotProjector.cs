namespace Net7ClientManager.Addons.Projection;

using Net7ClientManager.Addons.Contracts;
using Net7ClientManager.Observations.Models;

public static class InventorySlotProjector
{
    public static IReadOnlyList<AddonInventorySlotSnapshot>
        ProjectEquipmentSlots(ClientInventoryObservation inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);

        return inventory.EquippedSlots
            .Where(slot =>
                inventory.GetEquipmentSlotKind(slot.Slot) !=
                ClientEquipmentSlotKind.Reserved)
            .OrderBy(slot => slot.Slot)
            .Select(slot => Project(inventory, slot))
            .ToArray();
    }

    public static IReadOnlyList<AddonInventorySlotSnapshot>
        ProjectAmmoSlots(ClientInventoryObservation inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);

        var equipmentBySlot = inventory.EquippedSlots
            .ToDictionary(slot => slot.Slot);

        return inventory.AmmoSlots
            .Where(slot => IsRelevantAmmoSlot(
                inventory,
                equipmentBySlot,
                slot))
            .OrderBy(slot => slot.Slot)
            .Select(slot => Project(inventory, slot))
            .ToArray();
    }

    public static AddonInventorySlotSnapshot Project(
        ClientInventoryObservation inventory,
        ClientInventoryItemObservation slot)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(slot);

        var hasEquipmentPosition =
            slot.Collection is ClientInventoryCollectionKind.Equipped or
                ClientInventoryCollectionKind.Ammo;
        var equipmentKind = hasEquipmentPosition
            ? NormalizeEnum(inventory.GetEquipmentSlotKind(slot.Slot))
            : null;
        var equipmentOrdinal = hasEquipmentPosition
            ? inventory.GetEquipmentSlotOrdinal(slot.Slot)
            : null;

        return ProjectCore(slot) with
        {
            EquipmentKind = equipmentKind,
            EquipmentOrdinal = equipmentOrdinal,
            MountBoneName = Normalize(slot.MountBoneName),
            PriceLow = slot.PriceLow,
            PriceHigh = slot.PriceHigh,
            Operational = MapOperational(slot.Operational),
        };
    }

    public static AddonInventorySlotSnapshot Project(
        ClientInventoryItemObservation slot)
    {
        ArgumentNullException.ThrowIfNull(slot);
        return ProjectCore(slot);
    }

    public static IReadOnlyDictionary<string, object?> ToPublicData(
        AddonInventorySlotSnapshot slot)
    {
        ArgumentNullException.ThrowIfNull(slot);

        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["collection"] = slot.Collection,
            ["slot"] = slot.Slot,
            ["state"] = slot.State,
            ["is_usable"] = slot.IsUsable,
            ["name"] = slot.Name,
            ["stack_count"] = slot.StackCount,
            ["quality_percent"] = slot.QualityPercent,
            ["structure_percent"] = slot.StructurePercent,
            ["average_cost"] = slot.AverageCost,
            ["builder_name"] = slot.BuilderName,
            ["instance_info"] = slot.InstanceInfo,
            ["activated_effect_info"] = slot.ActivatedEffectInfo,
            ["equip_effect_info"] = slot.EquipEffectInfo,
            ["price_low"] = slot.PriceLow,
            ["price_high"] = slot.PriceHigh,
            ["equipment_kind"] = slot.EquipmentKind,
            ["equipment_ordinal"] = slot.EquipmentOrdinal,
            ["operational"] = slot.Operational,
        };
    }

    private static AddonInventorySlotSnapshot ProjectCore(
        ClientInventoryItemObservation slot)
    {
        return new AddonInventorySlotSnapshot
        {
            Collection = NormalizeEnum(slot.Collection),
            Slot = slot.Slot,
            State = ResolveState(slot),
            IsUsable = slot.IsUsable,
            TemplateId = slot.IsOccupied ? slot.ItemTemplateId : null,
            Name = slot.IsOccupied
                ? ClientItemTemplateNameResolver.GetKnownName(slot.ItemTemplateId) ??
                  "Unknown item"
                : null,
            StackCount = slot.IsOccupied ? slot.StackCount : null,
            QualityPercent = slot.IsOccupied ? slot.QualityPercent : null,
            StructurePercent = slot.IsOccupied ? slot.StructurePercent : null,
            AverageCost = slot.IsOccupied ? slot.AverageCost : null,
            BuilderName = slot.IsOccupied ? Normalize(slot.BuilderName) : null,
            InstanceInfo = slot.IsOccupied ? Normalize(slot.InstanceInfo) : null,
            ActivatedEffectInfo = slot.IsOccupied
                ? Normalize(slot.InstanceActivatedEffectInfo)
                : null,
            EquipEffectInfo = slot.IsOccupied
                ? Normalize(slot.InstanceEquipEffectInfo)
                : null,
            Operational = MapOperational(slot.Operational),
        };
    }

    private static bool IsRelevantAmmoSlot(
        ClientInventoryObservation inventory,
        IReadOnlyDictionary<int, ClientInventoryItemObservation>
            equipmentBySlot,
        ClientInventoryItemObservation ammoSlot)
    {
        if (inventory.GetEquipmentSlotKind(ammoSlot.Slot) !=
            ClientEquipmentSlotKind.Weapon)
        {
            return false;
        }

        // An occupied ammo record is authoritative even if the matching
        // equipment record is momentarily transitional.
        if (ammoSlot.IsOccupied)
        {
            return true;
        }

        // Beam weapons and non-ammunition equipment expose no usable ammo
        // subslot. Empty projectile/missile ammo slots remain visible.
        return !ammoSlot.IsUnavailable &&
               equipmentBySlot.TryGetValue(
                   ammoSlot.Slot,
                   out var equipmentSlot) &&
               equipmentSlot.IsOccupied;
    }

    private static string ResolveState(ClientInventoryItemObservation slot)
    {
        if (slot.IsOccupied)
        {
            return "occupied";
        }

        if (slot.IsUsableEmpty)
        {
            return "empty";
        }

        return "unavailable";
    }

    private static IReadOnlyDictionary<string, object?> MapOperational(
        ClientEquippedItemOperationalObservation operational)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["available"] = operational.IsAvailable,
            ["busy"] = operational.IsBusy,
            ["ready"] = operational.IsOperationallyReady,
            ["activation_phase"] = operational.HasActivationPhaseFlag,
            ["remaining_milliseconds"] =
                operational.NominalRemainingMilliseconds,
            ["post_deadline_busy_tail"] =
                operational.IsInPostDeadlineBusyTail,
            ["target_range"] = operational.TargetRange,
        };
    }

    private static string NormalizeEnum<T>(T value)
        where T : struct, Enum
    {
        var text = value.ToString();
        var result = new System.Text.StringBuilder(text.Length + 8);

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];

            if (index > 0 && char.IsUpper(character))
            {
                result.Append('_');
            }

            result.Append(char.ToLowerInvariant(character));
        }

        return result.ToString();
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }
}
