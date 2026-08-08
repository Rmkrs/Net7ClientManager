namespace Net7ClientManager.Forms;

using System.Globalization;
using System.Text;
using Net7ClientManager.Addons.Contracts;
using Net7ClientManager.Addons.Projection;
using Net7ClientManager.Observations;
using Net7ClientManager.Observations.Models;
using Net7ClientManager.RecipeMapping;

internal static class GameItemToolTipPresentationBuilder
{
    private const uint ItemSlotGadgetVTableRva = 0x006fb0cc;

    public static GameItemToolTipPreparation? Prepare(
        ClientObservationSnapshot snapshot,
        ClientTooltipHoverObservation hover,
        Func<int?, Size, Image?> resolveIcon,
        Func<int, RecipeMappingItemPresentation?>? resolveRecipeMapping = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(hover);
        ArgumentNullException.ThrowIfNull(resolveIcon);

        if (!hover.IsAvailable ||
            !hover.HasActiveGadget ||
            string.IsNullOrWhiteSpace(hover.ControlName))
        {
            return null;
        }

        var item = ResolveItem(snapshot, hover);

        if (item == null ||
            item.Slot.TemplateId is not > 0 ||
            string.IsNullOrWhiteSpace(item.Slot.Name))
        {
            return null;
        }

        var template = item.Template ??
            ClientItemTemplateNameResolver.GetKnownTemplate(
                item.Slot.TemplateId);
        var icon = resolveIcon(
            item.Slot.TemplateId,
            new Size(32, 32));
        var recipeMapping = resolveRecipeMapping?.Invoke(
            item.Slot.TemplateId.Value);
        var content = PilotArchiveItemToolTipBuilder.Build(
            item.Slot,
            template,
            icon,
            recipeMapping);
        var key = string.Create(
            CultureInfo.InvariantCulture,
            $"{hover.ViewKind}:{hover.ActiveGadgetAddress:X8}:{hover.ControlName}:{item.Slot.TemplateId.Value}");

        return new GameItemToolTipPreparation(
            key,
            item.Slot.Name,
            hover.ViewKind,
            hover.ActiveGadgetAddress,
            hover.ControlName,
            content);
    }

    public static bool CanShow(
        GameItemToolTipPreparation preparation,
        ClientTooltipHoverObservation hover)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        ArgumentNullException.ThrowIfNull(hover);

        return hover.IsDisplayed &&
            hover.ViewKind == preparation.ViewKind &&
            hover.ActiveGadgetAddress == preparation.ActiveGadgetAddress &&
            string.Equals(
                hover.ControlName,
                preparation.ControlName,
                StringComparison.Ordinal) &&
            NativeTooltipMatchesItem(
                hover.NativeTooltipText,
                preparation.ItemName);
    }

    public static bool ShouldRequestAuthoritativeRefresh(
        ClientObservationSnapshot snapshot,
        ClientTooltipHoverObservation hover,
        GameItemToolTipPreparation? preparation)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(hover);

        if (!CanResolvePotentialItem(hover))
        {
            return false;
        }

        if (preparation == null)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(hover.NativeTooltipText))
        {
            return false;
        }

        return !NativeTooltipMatchesItem(
            hover.NativeTooltipText,
            preparation.ItemName);
    }

    public static string GetHoverIdentity(
        ClientTooltipHoverObservation hover)
    {
        ArgumentNullException.ThrowIfNull(hover);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{hover.ViewKind}:{hover.ActiveGadgetAddress:X8}:{hover.ControlName}");
    }

    private static ResolvedGameItem? ResolveItem(
        ClientObservationSnapshot snapshot,
        ClientTooltipHoverObservation hover)
    {
        var controlName = hover.ControlName;

        // Native action-bar shortcuts intentionally keep their game tooltip.
        if (hover.ActiveGadgetVTableRva !=
            ItemSlotGadgetVTableRva)
        {
            return null;
        }

        if (TryParseIndexedControl(
                controlName,
                "PDA_CARGO_",
                out var cargoSlot))
        {
            return ResolveInventoryItem(
                snapshot.LocalPlayer.Inventory,
                snapshot.LocalPlayer.Inventory.CargoSlots,
                cargoSlot);
        }

        if (TryParseIndexedControl(
                controlName,
                "PLAYER_VAULT_",
                out var vaultSlot))
        {
            return ResolveSecureItem(
                snapshot.LocalPlayer.SecureInventory,
                vaultSlot);
        }

        if (TryParseIndexedControl(
                controlName,
                "HULK_CARGO_",
                out var lootSlot))
        {
            return ResolveLootItem(
                snapshot.Target.Corpse,
                lootSlot);
        }

        if (string.Equals(
                controlName,
                "SHIELD_SLOT_0",
                StringComparison.Ordinal))
        {
            return ResolveEquippedItem(snapshot, 0);
        }

        if (string.Equals(
                controlName,
                "POWER_SLOT_0",
                StringComparison.Ordinal))
        {
            return ResolveEquippedItem(snapshot, 1);
        }

        if (string.Equals(
                controlName,
                "ENGINE_SLOT_0",
                StringComparison.Ordinal))
        {
            return ResolveEquippedItem(snapshot, 2);
        }

        if (TryParseIndexedControl(
                controlName,
                "WEAPON_SLOT_",
                out var weaponSlot) ||
            TryParseIndexedControl(
                controlName,
                "SYSTEM_SLOT_",
                out weaponSlot))
        {
            return ResolveEquippedItem(
                snapshot,
                weaponSlot);
        }

        return null;
    }

    private static ResolvedGameItem? ResolveEquippedItem(
        ClientObservationSnapshot snapshot,
        int slot)
    {
        return ResolveInventoryItem(
            snapshot.LocalPlayer.Inventory,
            snapshot.LocalPlayer.Inventory.EquippedSlots,
            slot);
    }

    private static ResolvedGameItem? ResolveInventoryItem(
        ClientInventoryObservation inventory,
        IReadOnlyList<ClientInventoryItemObservation> slots,
        int slot)
    {
        if (!inventory.IsAvailable)
        {
            return null;
        }

        var item = slots.FirstOrDefault(value =>
            value.Slot == slot &&
            value.IsOccupied);

        return item == null
            ? null
            : new ResolvedGameItem(
                InventorySlotProjector.Project(
                    inventory,
                    item),
                item.Template);
    }

    private static ResolvedGameItem? ResolveSecureItem(
        ClientSecureInventoryObservation secureInventory,
        int slot)
    {
        if (!secureInventory.IsAvailable)
        {
            return null;
        }

        var item = secureInventory.Slots.FirstOrDefault(value =>
            value.Slot == slot &&
            value.IsOccupied);

        return item == null
            ? null
            : new ResolvedGameItem(
                InventorySlotProjector.Project(item),
                item.Template);
    }

    private static ResolvedGameItem? ResolveLootItem(
        ClientCorpseObservation corpse,
        int slot)
    {
        if (!corpse.IsAvailable)
        {
            return null;
        }

        var item = corpse.Slots.FirstOrDefault(value =>
            value.Slot == slot &&
            value.IsOccupied);

        if (item?.ItemTemplateId is not > 0)
        {
            return null;
        }

        var itemTemplateId = item.ItemTemplateId.Value;
        var template = ClientItemTemplateNameResolver.GetKnownTemplate(
            itemTemplateId);
        var name = template?.Name ??
            ClientItemTemplateNameResolver.GetKnownName(
                itemTemplateId) ??
            string.Create(
                CultureInfo.InvariantCulture,
                $"Item #{itemTemplateId}");

        var projected = new AddonInventorySlotSnapshot
        {
            Collection = "loot",
            Slot = item.Slot,
            State = "occupied",
            IsUsable = true,
            TemplateId = itemTemplateId,
            Name = name,
            StackCount = item.StackCount,
            QualityPercent = item.QualityPercent,
            StructurePercent = item.StructurePercent,
            AverageCost = item.AverageCost,
            BuilderName = Normalize(item.BuilderName),
            InstanceInfo = Normalize(item.InstanceInfo),
            ActivatedEffectInfo = Normalize(
                item.InstanceActivatedEffectInfo),
            EquipEffectInfo = Normalize(
                item.InstanceEquipEffectInfo),
        };

        return new ResolvedGameItem(
            projected,
            template);
    }

    private static bool CanResolvePotentialItem(
        ClientTooltipHoverObservation hover)
    {
        if (!hover.IsAvailable ||
            !hover.HasActiveGadget ||
            string.IsNullOrWhiteSpace(hover.ControlName))
        {
            return false;
        }

        if (hover.ActiveGadgetVTableRva !=
            ItemSlotGadgetVTableRva)
        {
            return false;
        }

        var controlName = hover.ControlName;

        return controlName.StartsWith(
                   "PDA_CARGO_",
                   StringComparison.Ordinal) ||
               controlName.StartsWith(
                   "PLAYER_VAULT_",
                   StringComparison.Ordinal) ||
               controlName.StartsWith(
                   "HULK_CARGO_",
                   StringComparison.Ordinal) ||
               controlName.StartsWith(
                   "WEAPON_SLOT_",
                   StringComparison.Ordinal) ||
               controlName.StartsWith(
                   "SYSTEM_SLOT_",
                   StringComparison.Ordinal) ||
               string.Equals(
                   controlName,
                   "SHIELD_SLOT_0",
                   StringComparison.Ordinal) ||
               string.Equals(
                   controlName,
                   "POWER_SLOT_0",
                   StringComparison.Ordinal) ||
               string.Equals(
                   controlName,
                   "ENGINE_SLOT_0",
                   StringComparison.Ordinal);
    }

    private static bool TryParseIndexedControl(
        string controlName,
        string prefix,
        out int slot)
    {
        slot = -1;

        if (!controlName.StartsWith(
                prefix,
                StringComparison.Ordinal) ||
            !controlName.EndsWith(
                "_0",
                StringComparison.Ordinal))
        {
            return false;
        }

        var valueLength =
            controlName.Length -
            prefix.Length -
            2;

        return valueLength > 0 &&
               int.TryParse(
                   controlName.AsSpan(
                       prefix.Length,
                       valueLength),
                   NumberStyles.None,
                   CultureInfo.InvariantCulture,
                   out slot) &&
               slot >= 0;
    }

    private static bool NativeTooltipMatchesItem(
        string nativeTooltipText,
        string itemName)
    {
        var normalizedTooltip = NormalizeWhitespace(
            nativeTooltipText);
        var normalizedItemName = NormalizeWhitespace(
            itemName);

        if (normalizedTooltip.Length == 0 ||
            normalizedItemName.Length == 0)
        {
            return false;
        }

        if (string.Equals(
                normalizedTooltip,
                normalizedItemName,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return normalizedTooltip.StartsWith(
            string.Concat(
                normalizedItemName,
                " Structure "),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeWhitespace(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;

        foreach (var character in value.Trim())
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private sealed record ResolvedGameItem(
        AddonInventorySlotSnapshot Slot,
        ClientRuntimeItemTemplateObservation? Template);
}

internal sealed record GameItemToolTipPreparation(
    string Key,
    string ItemName,
    ClientTooltipHoverViewKind ViewKind,
    uint ActiveGadgetAddress,
    string ControlName,
    ActionToolTipContent Content);
