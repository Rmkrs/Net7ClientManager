namespace Net7ClientManager.Observations.Observers;

using System.Globalization;
using Net7ClientManager.Observations.Models;

internal sealed class ClientTooltipHoverObserver
{
    private const uint MaximumExpectedImageSpan = 0x01000000;

    private const uint ClientContextMainViewOffset = 0x1354;
    private const uint ClientContextStarbaseViewOffset = 0x135c;
    private const uint ClientViewTooltipControllerOffset = 0x30;

    private const uint ControllerOwnerOffset = 0x00;
    private const uint ControllerActiveGadgetOffset = 0x04;
    private const uint ControllerDisplayedOffset = 0x0c;

    private const uint GadgetNativeTooltipTextOffset = 0x14;
    private const uint GadgetControlNameOffset = 0x24;

    private const int MaximumTooltipTextLength = 2048;
    private const int MaximumControlNameLength = 256;

    public ClientTooltipHoverObservation Observe(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        uint clientContextAddress)
    {
        if (moduleBaseAddress == 0 ||
            clientContextAddress == 0)
        {
            return ClientTooltipHoverObservation.Unavailable(
                "SClient state is unavailable");
        }

        var mainView = this.ObserveView(
            memory,
            moduleBaseAddress,
            clientContextAddress,
            ClientTooltipHoverViewKind.MainView,
            ClientContextMainViewOffset);
        var starbaseView = this.ObserveView(
            memory,
            moduleBaseAddress,
            clientContextAddress,
            ClientTooltipHoverViewKind.StarbaseView,
            ClientContextStarbaseViewOffset);

        var candidates = new[]
        {
            mainView,
            starbaseView,
        };

        return candidates.FirstOrDefault(value =>
                   value.HasActiveGadget &&
                   value.IsDisplayed) ??
               candidates.FirstOrDefault(value =>
                   value.HasActiveGadget) ??
               candidates.FirstOrDefault(value =>
                   value.IsAvailable) ??
               ClientTooltipHoverObservation.Unavailable(
                   "No ClientView tooltip controller is available");
    }

    private ClientTooltipHoverObservation ObserveView(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        uint clientContextAddress,
        ClientTooltipHoverViewKind viewKind,
        uint viewOffset)
    {
        if (!TryReadPointer(
                memory,
                clientContextAddress,
                viewOffset,
                out var viewAddress))
        {
            return ClientTooltipHoverObservation.Unavailable(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Could not read {viewKind} pointer")) with
            {
                ViewKind = viewKind,
            };
        }

        if (viewAddress == 0)
        {
            return new ClientTooltipHoverObservation
            {
                IsAvailable = true,
                ViewKind = viewKind,
                Status = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{viewKind} is not present"),
            };
        }

        if (!TryReadPointer(
                memory,
                viewAddress,
                ClientViewTooltipControllerOffset,
                out var controllerAddress))
        {
            return ClientTooltipHoverObservation.Unavailable(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Could not read {viewKind} tooltip controller")) with
            {
                ViewKind = viewKind,
                ViewAddress = viewAddress,
            };
        }

        if (controllerAddress == 0)
        {
            return new ClientTooltipHoverObservation
            {
                IsAvailable = true,
                ViewKind = viewKind,
                ViewAddress = viewAddress,
                Status = "Tooltip controller is not present",
            };
        }

        if (!memory.TryReadBytes(
                controllerAddress,
                0x10,
                out var controllerBytes))
        {
            return ClientTooltipHoverObservation.Unavailable(
                "Could not read tooltip controller state") with
            {
                ViewKind = viewKind,
                ViewAddress = viewAddress,
                ControllerAddress = controllerAddress,
            };
        }

        var ownerAddress = BitConverter.ToUInt32(
            controllerBytes,
            (int)ControllerOwnerOffset);
        var activeGadgetAddress = BitConverter.ToUInt32(
            controllerBytes,
            (int)ControllerActiveGadgetOffset);
        var displayed = controllerBytes[
            (int)ControllerDisplayedOffset] != 0;

        if (ownerAddress != viewAddress)
        {
            return new ClientTooltipHoverObservation
            {
                IsAvailable = true,
                ViewKind = viewKind,
                ViewAddress = viewAddress,
                ControllerAddress = controllerAddress,
                Status = "Tooltip controller owner does not match the ClientView",
            };
        }

        var gadgetVTableRva = 0u;
        var controlName = "";
        var nativeTooltipText = "";

        if (activeGadgetAddress != 0)
        {
            if (memory.TryReadUInt32(
                    activeGadgetAddress,
                    out var gadgetVTableAddress) &&
                gadgetVTableAddress >= moduleBaseAddress &&
                gadgetVTableAddress - moduleBaseAddress <
                    MaximumExpectedImageSpan)
            {
                gadgetVTableRva =
                    gadgetVTableAddress - moduleBaseAddress;
            }

            nativeTooltipText = ReadIndirectText(
                memory,
                activeGadgetAddress,
                GadgetNativeTooltipTextOffset,
                MaximumTooltipTextLength);
            controlName = ReadIndirectText(
                memory,
                activeGadgetAddress,
                GadgetControlNameOffset,
                MaximumControlNameLength).Trim();
        }

        return new ClientTooltipHoverObservation
        {
            IsAvailable = true,
            Status = "Tooltip controller available",
            ViewKind = viewKind,
            ViewAddress = viewAddress,
            ControllerAddress = controllerAddress,
            ActiveGadgetAddress = activeGadgetAddress,
            ActiveGadgetVTableRva = gadgetVTableRva,
            IsDisplayed = displayed,
            ControlName = controlName,
            NativeTooltipText = nativeTooltipText.Trim(),
        };
    }

    private static string ReadIndirectText(
        ProcessMemoryReader memory,
        uint objectAddress,
        uint pointerOffset,
        int maximumLength)
    {
        return TryReadPointer(
                   memory,
                   objectAddress,
                   pointerOffset,
                   out var textAddress) &&
               textAddress != 0 &&
               memory.TryReadNullTerminatedLatin1String(
                   textAddress,
                   maximumLength,
                   out var text)
            ? text
            : "";
    }

    private static bool TryReadPointer(
        ProcessMemoryReader memory,
        uint baseAddress,
        uint offset,
        out uint pointer)
    {
        pointer = 0;

        try
        {
            return memory.TryReadUInt32(
                checked(baseAddress + offset),
                out pointer);
        }
        catch (OverflowException)
        {
            return false;
        }
    }
}
