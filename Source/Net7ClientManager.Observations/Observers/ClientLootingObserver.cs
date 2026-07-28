// ReSharper disable GrammarMistakeInComment
namespace Net7ClientManager.Observations.Observers;

using Net7ClientManager.Observations.Models;

internal sealed class ClientLootingObserver
{
    /*
     * Stable ownership path:
     *   SClient +0x1354 -> MainView
     *   MainView +0x1D0 -> CockpitController
     *
     * Runtime-validated cockpit fields:
     *   +0xC8 -> LootPanelView
     *   +0xF4 embedded LootTargetBinding
     *   +0xF8 current attached loot-target ObjectId
     *
     * LootPanelView inherits its displayed/active byte at +0x7C.
     * Merely selecting a corpse does not populate +0xF8. The ObjectId is
     * attached only while the loot panel is open for that corpse/wreck.
     */
    private const uint ImageBase = 0x00400000;

    private const uint CockpitControllerVTableStatic =
        0x00af0cfc;

    private const uint CockpitControllerVTableRva =
        CockpitControllerVTableStatic - ImageBase;

    private const uint LootPanelViewVTableStatic =
        0x00af0e80;

    private const uint LootPanelViewVTableRva =
        LootPanelViewVTableStatic - ImageBase;

    private const uint LootTargetBindingVTableStatic =
        0x00af0dbc;

    private const uint LootTargetBindingVTableRva =
        LootTargetBindingVTableStatic - ImageBase;

    private const uint ClientContextMainViewOffset =
        0x1354;

    private const uint MainViewCockpitControllerOffset =
        0x1d0;

    private const uint ObjectVTableOffset =
        0x00;

    private const uint CockpitControllerLootPanelViewOffset =
        0xc8;

    private const uint CockpitControllerLootTargetBindingOffset =
        0xf4;

    private const uint CockpitControllerLootTargetObjectIdOffset =
        0xf8;

    private const uint InterfaceViewDisplayedOffset =
        0x7c;

    public void Refresh(
        ProcessMemoryReader memory,
        ObservedClientState state)
    {
        if (!state.HasDirectClientState ||
            state.ModuleBaseAddress == 0 ||
            state.ClientContextAddress == 0)
        {
            state.Looting =
                ClientLootingObservation.Unavailable(
                    "Direct SClient state is unavailable");

            return;
        }

        if (!TryReadPointer(
                memory,
                state.ClientContextAddress,
                ClientContextMainViewOffset,
                "SClient.MainView",
                out var mainViewAddress,
                out var error))
        {
            state.Looting =
                ClientLootingObservation.Unavailable(
                    error);

            return;
        }

        if (mainViewAddress == 0)
        {
            state.Looting =
                ClientLootingObservation.Unavailable(
                    "SClient MainView is null");

            return;
        }

        if (!TryReadPointer(
                memory,
                mainViewAddress,
                MainViewCockpitControllerOffset,
                "MainView.CockpitController",
                out var cockpitControllerAddress,
                out error))
        {
            state.Looting =
                ClientLootingObservation.Unavailable(
                    error,
                    mainViewAddress);

            return;
        }

        if (cockpitControllerAddress == 0)
        {
            state.Looting =
                ClientLootingObservation.Unavailable(
                    "MainView cockpit controller is null",
                    mainViewAddress);

            return;
        }

        if (!TryReadPointer(
                memory,
                cockpitControllerAddress,
                ObjectVTableOffset,
                "CockpitController.VTable",
                out var cockpitControllerVTableAddress,
                out error) ||
            !TryValidateRelocatedAddress(
                state.ModuleBaseAddress,
                CockpitControllerVTableRva,
                cockpitControllerVTableAddress,
                "CockpitController vtable",
                out error))
        {
            state.Looting =
                ClientLootingObservation.Unavailable(
                    error,
                    mainViewAddress,
                    cockpitControllerAddress,
                    cockpitControllerVTableAddress);

            return;
        }

        if (!TryReadPointer(
                memory,
                cockpitControllerAddress,
                CockpitControllerLootPanelViewOffset,
                "CockpitController.LootPanelView",
                out var lootPanelViewAddress,
                out error))
        {
            state.Looting =
                ClientLootingObservation.Unavailable(
                    error,
                    mainViewAddress,
                    cockpitControllerAddress,
                    cockpitControllerVTableAddress);

            return;
        }

        if (lootPanelViewAddress == 0)
        {
            state.Looting =
                ClientLootingObservation.Unavailable(
                    "CockpitController loot-panel view is null",
                    mainViewAddress,
                    cockpitControllerAddress,
                    cockpitControllerVTableAddress);

            return;
        }

        if (!TryReadPointer(
                memory,
                lootPanelViewAddress,
                ObjectVTableOffset,
                "LootPanelView.VTable",
                out var lootPanelViewVTableAddress,
                out error) ||
            !TryValidateRelocatedAddress(
                state.ModuleBaseAddress,
                LootPanelViewVTableRva,
                lootPanelViewVTableAddress,
                "LootPanelView vtable",
                out error))
        {
            state.Looting =
                ClientLootingObservation.Unavailable(
                    error,
                    mainViewAddress,
                    cockpitControllerAddress,
                    cockpitControllerVTableAddress,
                    lootPanelViewAddress,
                    lootPanelViewVTableAddress);

            return;
        }

        uint lootTargetBindingVTableAddress = 0;

        if (!TryResolveOffsetAddress(
                cockpitControllerAddress,
                CockpitControllerLootTargetBindingOffset,
                "CockpitController.LootTargetBinding",
                out var lootTargetBindingAddress,
                out error) ||
            !TryReadPointer(
                memory,
                cockpitControllerAddress,
                CockpitControllerLootTargetBindingOffset,
                "LootTargetBinding.VTable",
                out lootTargetBindingVTableAddress,
                out error) ||
            !TryValidateRelocatedAddress(
                state.ModuleBaseAddress,
                LootTargetBindingVTableRva,
                lootTargetBindingVTableAddress,
                "LootTargetBinding vtable",
                out error))
        {
            state.Looting =
                ClientLootingObservation.Unavailable(
                    error,
                    mainViewAddress,
                    cockpitControllerAddress,
                    cockpitControllerVTableAddress,
                    lootPanelViewAddress,
                    lootPanelViewVTableAddress,
                    lootTargetBindingAddress,
                    lootTargetBindingVTableAddress);

            return;
        }

        if (!TryReadBooleanByte(
                memory,
                lootPanelViewAddress,
                InterfaceViewDisplayedOffset,
                "LootPanelView.IsDisplayed",
                out var isLootPanelDisplayed,
                out error) ||
            !TryReadUInt32(
                memory,
                cockpitControllerAddress,
                CockpitControllerLootTargetObjectIdOffset,
                "CockpitController.LootTargetObjectId",
                out var lootTargetObjectId,
                out error))
        {
            state.Looting =
                ClientLootingObservation.Unavailable(
                    error,
                    mainViewAddress,
                    cockpitControllerAddress,
                    cockpitControllerVTableAddress,
                    lootPanelViewAddress,
                    lootPanelViewVTableAddress,
                    lootTargetBindingAddress,
                    lootTargetBindingVTableAddress);

            return;
        }

        var hasAttachedLootTarget =
            lootTargetObjectId != 0;

        var status =
            isLootPanelDisplayed == hasAttachedLootTarget
                ? "Available; client-owned loot-panel visibility and attached loot target"
                : "Available; loot-panel visibility and target attachment are temporarily inconsistent";

        state.Looting =
            new ClientLootingObservation
            {
                IsAvailable = true,
                Status = status,
                MainViewAddress = mainViewAddress,
                CockpitControllerAddress =
                    cockpitControllerAddress,
                CockpitControllerVTableAddress =
                    cockpitControllerVTableAddress,
                LootPanelViewAddress =
                    lootPanelViewAddress,
                LootPanelViewVTableAddress =
                    lootPanelViewVTableAddress,
                LootTargetBindingAddress =
                    lootTargetBindingAddress,
                LootTargetBindingVTableAddress =
                    lootTargetBindingVTableAddress,
                IsLootPanelDisplayed =
                    isLootPanelDisplayed,
                LootTargetObjectId =
                    lootTargetObjectId,
            };
    }

    private static bool TryValidateRelocatedAddress(
        uint moduleBaseAddress,
        uint relativeVirtualAddress,
        uint actualAddress,
        string fieldName,
        out string error)
    {
        if (!TryResolveOffsetAddress(
                moduleBaseAddress,
                relativeVirtualAddress,
                fieldName,
                out var expectedAddress,
                out error))
        {
            return false;
        }

        if (actualAddress != expectedAddress)
        {
            error = $"Unexpected {fieldName} {FormatPointer(actualAddress)}; expected {FormatPointer(expectedAddress)}";

            return false;
        }

        error = "";
        return true;
    }

    private static bool TryReadBooleanByte(
        ProcessMemoryReader memory,
        uint baseAddress,
        uint offset,
        string fieldName,
        out bool value,
        out string error)
    {
        if (!TryResolveOffsetAddress(
                baseAddress,
                offset,
                fieldName,
                out var address,
                out error))
        {
            value = false;
            return false;
        }

        if (!memory.TryReadBytes(
                address,
                sizeof(byte),
                out var bytes))
        {
            value = false;
            error = $"Could not read {fieldName} at {FormatPointer(address)}";

            return false;
        }

        var rawValue = bytes[0];

        if (rawValue > 1)
        {
            value = false;
            error = $"Unexpected boolean value {rawValue} in {fieldName}";

            return false;
        }

        value = rawValue != 0;
        error = "";
        return true;
    }

    private static bool TryReadPointer(
        ProcessMemoryReader memory,
        uint baseAddress,
        uint offset,
        string fieldName,
        out uint value,
        out string error)
    {
        return TryReadUInt32(
            memory,
            baseAddress,
            offset,
            fieldName,
            out value,
            out error);
    }

    private static bool TryReadUInt32(
        ProcessMemoryReader memory,
        uint baseAddress,
        uint offset,
        string fieldName,
        out uint value,
        out string error)
    {
        if (!TryResolveOffsetAddress(
                baseAddress,
                offset,
                fieldName,
                out var address,
                out error))
        {
            value = 0;
            return false;
        }

        if (!memory.TryReadUInt32(
                address,
                out value))
        {
            error = $"Could not read {fieldName} at {FormatPointer(address)}";

            return false;
        }

        error = "";
        return true;
    }

    private static bool TryResolveOffsetAddress(
        uint baseAddress,
        uint offset,
        string fieldName,
        out uint address,
        out string error)
    {
        try
        {
            address = checked(
                baseAddress + offset);

            error = "";
            return true;
        }
        catch (OverflowException)
        {
            address = 0;
            error =
                $"Address overflow while resolving {fieldName}";

            return false;
        }
    }

    private static string FormatPointer(
        uint address)
    {
        return address == 0
            ? "None"
            : $"0x{address:X8}";
    }
}
