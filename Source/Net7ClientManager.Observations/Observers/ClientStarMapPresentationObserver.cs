// ReSharper disable GrammarMistakeInComment
// ReSharper disable StringLiteralTypo
namespace Net7ClientManager.Observations.Observers;

using Net7ClientManager.Observations.Models;

internal sealed class ClientStarMapPresentationObserver
{
    /*
     * Stable ownership path:
     *   SClient +0x1284 -> GameplayUtilityController
     *   GameplayUtilityController +0xA8 -> StarMapView
     *
     * StarMapView is validated by relocated vtable 0x00B071E4.
     * Its client-owned presentation fields are:
     *   +0x7C displayed/active byte
     *   +0x9C maximized-layout byte
     *   +0xA0 StarMapRadarView pointer
     *   +0xA4 alternate-view pointer
     *   +0xA8 selected child-view pointer
     *
     * StarMapRadarView is validated by relocated vtable 0x00B06C8C.
     * Render-view children expose their active byte at +0x40.
     */
    private const uint ImageBase = 0x00400000;

    private const uint StarMapViewVTableStatic =
        0x00b071e4;

    private const uint StarMapViewVTableRva =
        StarMapViewVTableStatic - ImageBase;

    private const uint StarMapRadarViewVTableStatic =
        0x00b06c8c;

    private const uint StarMapRadarViewVTableRva =
        StarMapRadarViewVTableStatic - ImageBase;

    private const uint ClientContextGameplayUtilityControllerOffset =
        0x1284;

    private const uint GameplayUtilityControllerStarMapViewOffset =
        0xa8;

    private const uint ObjectVTableOffset =
        0x00;

    private const uint StarMapViewDisplayedOffset =
        0x7c;

    private const uint StarMapViewMaximizedOffset =
        0x9c;

    private const uint StarMapViewRadarViewOffset =
        0xa0;

    private const uint StarMapViewAlternateViewOffset =
        0xa4;

    private const uint StarMapViewSelectedViewOffset =
        0xa8;

    private const uint StarMapRenderViewActiveOffset =
        0x40;

    public void Refresh(
        ProcessMemoryReader memory,
        ObservedClientState state)
    {
        if (!state.HasDirectClientState ||
            state.ModuleBaseAddress == 0 ||
            state.ClientContextAddress == 0)
        {
            state.StarMapPresentation =
                ClientStarMapPresentationObservation.Unavailable(
                    "Direct SClient state is unavailable");

            return;
        }

        if (!TryReadPointer(
                memory,
                state.ClientContextAddress,
                ClientContextGameplayUtilityControllerOffset,
                "SClient.GameplayUtilityController",
                out var gameplayUtilityControllerAddress,
                out var error))
        {
            state.StarMapPresentation =
                ClientStarMapPresentationObservation.Unavailable(
                    error);

            return;
        }

        if (gameplayUtilityControllerAddress == 0)
        {
            state.StarMapPresentation =
                ClientStarMapPresentationObservation.Unavailable(
                    "SClient gameplay utility controller is null");

            return;
        }

        if (!TryReadPointer(
                memory,
                gameplayUtilityControllerAddress,
                GameplayUtilityControllerStarMapViewOffset,
                "GameplayUtilityController.StarMapView",
                out var starMapViewAddress,
                out error))
        {
            state.StarMapPresentation =
                ClientStarMapPresentationObservation.Unavailable(
                    error,
                    gameplayUtilityControllerAddress);

            return;
        }

        if (starMapViewAddress == 0)
        {
            state.StarMapPresentation =
                ClientStarMapPresentationObservation.Unavailable(
                    "Gameplay utility controller StarMapView is null",
                    gameplayUtilityControllerAddress);

            return;
        }

        if (!TryReadPointer(
                memory,
                starMapViewAddress,
                ObjectVTableOffset,
                "StarMapView.VTable",
                out var starMapViewVTableAddress,
                out error))
        {
            state.StarMapPresentation =
                ClientStarMapPresentationObservation.Unavailable(
                    error,
                    gameplayUtilityControllerAddress,
                    starMapViewAddress);

            return;
        }

        if (!TryResolveRelocatedAddress(
                state.ModuleBaseAddress,
                StarMapViewVTableRva,
                "StarMapView vtable",
                out var expectedStarMapViewVTableAddress,
                out error))
        {
            state.StarMapPresentation =
                ClientStarMapPresentationObservation.Unavailable(
                    error,
                    gameplayUtilityControllerAddress,
                    starMapViewAddress,
                    starMapViewVTableAddress);

            return;
        }

        if (starMapViewVTableAddress !=
            expectedStarMapViewVTableAddress)
        {
            state.StarMapPresentation =
                ClientStarMapPresentationObservation.Unavailable(
                        $"Unexpected StarMapView vtable {FormatPointer(starMapViewVTableAddress)}; expected {FormatPointer(expectedStarMapViewVTableAddress)}",
                    gameplayUtilityControllerAddress,
                    starMapViewAddress,
                    starMapViewVTableAddress);

            return;
        }

        if (!TryReadBooleanByte(
                memory,
                starMapViewAddress,
                StarMapViewDisplayedOffset,
                "StarMapView.IsDisplayed",
                out var isDisplayed,
                out error) ||
            !TryReadBooleanByte(
                memory,
                starMapViewAddress,
                StarMapViewMaximizedOffset,
                "StarMapView.IsMaximized",
                out var isMaximized,
                out error) ||
            !TryReadPointer(
                memory,
                starMapViewAddress,
                StarMapViewRadarViewOffset,
                "StarMapView.RadarView",
                out var radarViewAddress,
                out error) ||
            !TryReadPointer(
                memory,
                starMapViewAddress,
                StarMapViewAlternateViewOffset,
                "StarMapView.AlternateView",
                out var alternateViewAddress,
                out error) ||
            !TryReadPointer(
                memory,
                starMapViewAddress,
                StarMapViewSelectedViewOffset,
                "StarMapView.SelectedView",
                out var selectedViewAddress,
                out error))
        {
            state.StarMapPresentation =
                ClientStarMapPresentationObservation.Unavailable(
                    error,
                    gameplayUtilityControllerAddress,
                    starMapViewAddress,
                    starMapViewVTableAddress);

            return;
        }

        if (radarViewAddress == 0)
        {
            state.StarMapPresentation =
                ClientStarMapPresentationObservation.Unavailable(
                    "StarMapView radar view is null",
                    gameplayUtilityControllerAddress,
                    starMapViewAddress,
                    starMapViewVTableAddress,
                    alternateViewAddress:
                        alternateViewAddress,
                    selectedViewAddress:
                        selectedViewAddress);

            return;
        }

        if (selectedViewAddress == 0)
        {
            state.StarMapPresentation =
                ClientStarMapPresentationObservation.Unavailable(
                    "StarMapView selected view is null",
                    gameplayUtilityControllerAddress,
                    starMapViewAddress,
                    starMapViewVTableAddress,
                    radarViewAddress,
                    alternateViewAddress:
                        alternateViewAddress);

            return;
        }

        if (!TryReadPointer(
                memory,
                radarViewAddress,
                ObjectVTableOffset,
                "StarMapRadarView.VTable",
                out var radarViewVTableAddress,
                out error))
        {
            state.StarMapPresentation =
                ClientStarMapPresentationObservation.Unavailable(
                    error,
                    gameplayUtilityControllerAddress,
                    starMapViewAddress,
                    starMapViewVTableAddress,
                    radarViewAddress,
                    alternateViewAddress:
                        alternateViewAddress,
                    selectedViewAddress:
                        selectedViewAddress);

            return;
        }

        if (!TryResolveRelocatedAddress(
                state.ModuleBaseAddress,
                StarMapRadarViewVTableRva,
                "StarMapRadarView vtable",
                out var expectedRadarViewVTableAddress,
                out error))
        {
            state.StarMapPresentation =
                ClientStarMapPresentationObservation.Unavailable(
                    error,
                    gameplayUtilityControllerAddress,
                    starMapViewAddress,
                    starMapViewVTableAddress,
                    radarViewAddress,
                    radarViewVTableAddress,
                    alternateViewAddress,
                    selectedViewAddress);

            return;
        }

        if (radarViewVTableAddress !=
            expectedRadarViewVTableAddress)
        {
            state.StarMapPresentation =
                ClientStarMapPresentationObservation.Unavailable(
                        $"Unexpected StarMapRadarView vtable {FormatPointer(radarViewVTableAddress)}; expected {FormatPointer(expectedRadarViewVTableAddress)}",
                    gameplayUtilityControllerAddress,
                    starMapViewAddress,
                    starMapViewVTableAddress,
                    radarViewAddress,
                    radarViewVTableAddress,
                    alternateViewAddress,
                    selectedViewAddress);

            return;
        }

        if (!TryReadPointer(
                memory,
                selectedViewAddress,
                ObjectVTableOffset,
                "StarMapSelectedView.VTable",
                out var selectedViewVTableAddress,
                out error) ||
            !TryReadBooleanByte(
                memory,
                radarViewAddress,
                StarMapRenderViewActiveOffset,
                "StarMapRadarView.IsActive",
                out var isRadarPresentationActive,
                out error) ||
            !TryReadBooleanByte(
                memory,
                selectedViewAddress,
                StarMapRenderViewActiveOffset,
                "StarMapSelectedView.IsActive",
                out var isSelectedPresentationActive,
                out error))
        {
            state.StarMapPresentation =
                ClientStarMapPresentationObservation.Unavailable(
                    error,
                    gameplayUtilityControllerAddress,
                    starMapViewAddress,
                    starMapViewVTableAddress,
                    radarViewAddress,
                    radarViewVTableAddress,
                    alternateViewAddress,
                    selectedViewAddress);

            return;
        }

        var selectedPresentation =
            selectedViewAddress == radarViewAddress
                ? ClientStarMapPresentationKind.Radar
                : selectedViewAddress == alternateViewAddress
                    ? ClientStarMapPresentationKind.Alternate
                    : ClientStarMapPresentationKind.Unknown;

        state.StarMapPresentation =
            new ClientStarMapPresentationObservation
            {
                IsAvailable = true,
                Status =
                    "Available; client-owned starmap visibility and maximized layout state",
                GameplayUtilityControllerAddress =
                    gameplayUtilityControllerAddress,
                StarMapViewAddress =
                    starMapViewAddress,
                StarMapViewVTableAddress =
                    starMapViewVTableAddress,
                RadarViewAddress =
                    radarViewAddress,
                RadarViewVTableAddress =
                    radarViewVTableAddress,
                AlternateViewAddress =
                    alternateViewAddress,
                SelectedViewAddress =
                    selectedViewAddress,
                SelectedViewVTableAddress =
                    selectedViewVTableAddress,
                IsDisplayed = isDisplayed,
                IsMaximized = isMaximized,
                IsRadarPresentationActive =
                    isRadarPresentationActive,
                IsSelectedPresentationActive =
                    isSelectedPresentationActive,
                SelectedPresentation =
                    selectedPresentation,
            };
    }

    private static bool TryResolveRelocatedAddress(
        uint moduleBaseAddress,
        uint relativeVirtualAddress,
        string fieldName,
        out uint address,
        out string error)
    {
        try
        {
            address = checked(
                moduleBaseAddress +
                relativeVirtualAddress);

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

    private static bool TryReadBooleanByte(
        ProcessMemoryReader memory,
        uint baseAddress,
        uint offset,
        string fieldName,
        out bool value,
        out string error)
    {
        if (!TryReadByte(
                memory,
                baseAddress,
                offset,
                fieldName,
                out var rawValue,
                out error))
        {
            value = false;
            return false;
        }

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

    private static bool TryReadByte(
        ProcessMemoryReader memory,
        uint baseAddress,
        uint offset,
        string fieldName,
        out byte value,
        out string error)
    {
        uint address;

        try
        {
            address = checked(
                baseAddress + offset);
        }
        catch (OverflowException)
        {
            value = 0;
            error =
                $"Address overflow while reading {fieldName}";

            return false;
        }

        if (!memory.TryReadBytes(
                address,
                sizeof(byte),
                out var bytes))
        {
            value = 0;
            error = $"Could not read {fieldName} at {FormatPointer(address)}";

            return false;
        }

        value = bytes[0];
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
        uint address;

        try
        {
            address = checked(
                baseAddress + offset);
        }
        catch (OverflowException)
        {
            value = 0;
            error =
                $"Address overflow while reading {fieldName}";

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

    private static string FormatPointer(
        uint address)
    {
        return address == 0
            ? "None"
            : $"0x{address:X8}";
    }
}
