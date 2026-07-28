namespace Net7ClientManager.Observations.Observers;

using System.Globalization;
using Net7ClientManager.Observations.Models;

internal sealed class ClientFrameRateObserver
{
    /*
     * SClient +0x1354 points to MainView.
     * MainView embeds its interface collection at +0x08, whose list head is
     * therefore MainView +0x0C. Each eight-byte node stores Next at +0x00 and
     * InterfaceObject at +0x04.
     *
     * The frame-rate interface is identified by vtable 0x00AEED2C rather than
     * by list position. Its client-owned statistics are:
     *   +0x84 smoothed FPS
     *   +0x88 minimum FPS
     *   +0x8C maximum FPS
     */
    private const uint ImageBase = 0x00400000;

    private const uint FrameRatePerformanceInterfaceVTableStatic =
        0x00aeed2c;

    private const uint FrameRatePerformanceInterfaceVTableRva =
        FrameRatePerformanceInterfaceVTableStatic - ImageBase;

    private const uint ClientContextMainViewOffset =
        0x1354;

    private const uint MainViewInterfaceListHeadOffset =
        0x0c;

    private const uint InterfaceNodeNextOffset =
        0x00;

    private const uint InterfaceNodeObjectOffset =
        0x04;

    private const uint InterfaceObjectVTableOffset =
        0x00;

    private const uint SmoothedFramesPerSecondOffset =
        0x84;

    private const uint MinimumFramesPerSecondOffset =
        0x88;

    private const uint MaximumFramesPerSecondOffset =
        0x8c;

    private const int MaximumInterfaceNodeCount = 1024;

    public void Refresh(
        ProcessMemoryReader memory,
        ObservedClientState state)
    {
        if (!state.HasDirectClientState ||
            state.ModuleBaseAddress == 0 ||
            state.ClientContextAddress == 0)
        {
            state.FrameRate =
                ClientFrameRateObservation.Unavailable(
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
            state.FrameRate =
                ClientFrameRateObservation.Unavailable(
                    error);

            return;
        }

        if (mainViewAddress == 0)
        {
            state.FrameRate =
                ClientFrameRateObservation.Unavailable(
                    "SClient MainView is null");

            return;
        }

        if (!TryReadPointer(
                memory,
                mainViewAddress,
                MainViewInterfaceListHeadOffset,
                "MainView.InterfaceObjects.Head",
                out var interfaceListHeadAddress,
                out error))
        {
            state.FrameRate =
                ClientFrameRateObservation.Unavailable(
                    error,
                    mainViewAddress);

            return;
        }

        if (interfaceListHeadAddress == 0)
        {
            state.FrameRate =
                ClientFrameRateObservation.Unavailable(
                    "MainView interface list is empty",
                    mainViewAddress);

            return;
        }

        uint expectedVTableAddress;

        try
        {
            expectedVTableAddress = checked(
                state.ModuleBaseAddress +
                FrameRatePerformanceInterfaceVTableRva);
        }
        catch (OverflowException)
        {
            state.FrameRate =
                ClientFrameRateObservation.Unavailable(
                    "Frame-rate interface vtable address overflow",
                    mainViewAddress,
                    interfaceListHeadAddress);

            return;
        }

        HashSet<uint> visitedNodes = [];

        var nodeAddress = interfaceListHeadAddress;
        var scannedInterfaceCount = 0;

        while (nodeAddress != 0)
        {
            if (scannedInterfaceCount >=
                MaximumInterfaceNodeCount)
            {
                state.FrameRate =
                    ClientFrameRateObservation.Unavailable(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"MainView interface list exceeded diagnostic limit {MaximumInterfaceNodeCount}"),
                        mainViewAddress,
                        interfaceListHeadAddress,
                        scannedInterfaceCount:
                            scannedInterfaceCount);

                return;
            }

            if (!visitedNodes.Add(nodeAddress))
            {
                state.FrameRate =
                    ClientFrameRateObservation.Unavailable(
                            $"MainView interface list contains a cycle at {FormatPointer(nodeAddress)}",
                        mainViewAddress,
                        interfaceListHeadAddress,
                        nodeAddress,
                        scannedInterfaceCount:
                            scannedInterfaceCount);

                return;
            }

            scannedInterfaceCount++;

            if (!TryReadPointer(
                    memory,
                    nodeAddress,
                    InterfaceNodeObjectOffset,
                    "ViewInterfaceNode.InterfaceObject",
                    out var interfaceAddress,
                    out error))
            {
                state.FrameRate =
                    ClientFrameRateObservation.Unavailable(
                        error,
                        mainViewAddress,
                        interfaceListHeadAddress,
                        nodeAddress,
                        scannedInterfaceCount:
                            scannedInterfaceCount);

                return;
            }

            if (interfaceAddress == 0)
            {
                state.FrameRate =
                    ClientFrameRateObservation.Unavailable(
                            $"View-interface node {FormatPointer(nodeAddress)} has a null interface object",
                        mainViewAddress,
                        interfaceListHeadAddress,
                        nodeAddress,
                        scannedInterfaceCount:
                            scannedInterfaceCount);

                return;
            }

            if (!TryReadPointer(
                    memory,
                    interfaceAddress,
                    InterfaceObjectVTableOffset,
                    "ViewInterface.VTable",
                    out var interfaceVTableAddress,
                    out error))
            {
                state.FrameRate =
                    ClientFrameRateObservation.Unavailable(
                        error,
                        mainViewAddress,
                        interfaceListHeadAddress,
                        nodeAddress,
                        interfaceAddress,
                        scannedInterfaceCount:
                            scannedInterfaceCount);

                return;
            }

            if (interfaceVTableAddress ==
                expectedVTableAddress)
            {
                this.ReadFrameRateInterface(
                    memory,
                    state,
                    mainViewAddress,
                    interfaceListHeadAddress,
                    nodeAddress,
                    interfaceAddress,
                    interfaceVTableAddress,
                    scannedInterfaceCount);

                return;
            }

            if (!TryReadPointer(
                    memory,
                    nodeAddress,
                    InterfaceNodeNextOffset,
                    "ViewInterfaceNode.Next",
                    out nodeAddress,
                    out error))
            {
                state.FrameRate =
                    ClientFrameRateObservation.Unavailable(
                        error,
                        mainViewAddress,
                        interfaceListHeadAddress,
                        scannedInterfaceCount:
                            scannedInterfaceCount);

                return;
            }
        }

        state.FrameRate =
            ClientFrameRateObservation.Unavailable(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Frame-rate interface vtable {FormatPointer(expectedVTableAddress)} was not found in {scannedInterfaceCount} attached interface(s)"),
                mainViewAddress,
                interfaceListHeadAddress,
                scannedInterfaceCount:
                    scannedInterfaceCount);
    }

    private void ReadFrameRateInterface(
        ProcessMemoryReader memory,
        ObservedClientState state,
        uint mainViewAddress,
        uint interfaceListHeadAddress,
        uint interfaceNodeAddress,
        uint interfaceAddress,
        uint interfaceVTableAddress,
        int scannedInterfaceCount)
    {
        if (!TryReadSingle(
                memory,
                interfaceAddress,
                SmoothedFramesPerSecondOffset,
                "FrameRatePerformanceInterface.SmoothedFramesPerSecond",
                out var smoothedFramesPerSecond,
                out var error) ||
            !TryReadSingle(
                memory,
                interfaceAddress,
                MinimumFramesPerSecondOffset,
                "FrameRatePerformanceInterface.MinimumFramesPerSecond",
                out var minimumFramesPerSecond,
                out error) ||
            !TryReadSingle(
                memory,
                interfaceAddress,
                MaximumFramesPerSecondOffset,
                "FrameRatePerformanceInterface.MaximumFramesPerSecond",
                out var maximumFramesPerSecond,
                out error))
        {
            state.FrameRate =
                ClientFrameRateObservation.Unavailable(
                    error,
                    mainViewAddress,
                    interfaceListHeadAddress,
                    interfaceNodeAddress,
                    interfaceAddress,
                    interfaceVTableAddress,
                    scannedInterfaceCount);

            return;
        }

        if (!float.IsFinite(smoothedFramesPerSecond) ||
            !float.IsFinite(minimumFramesPerSecond) ||
            !float.IsFinite(maximumFramesPerSecond))
        {
            state.FrameRate =
                ClientFrameRateObservation.Unavailable(
                    "Frame-rate interface contains a non-finite value",
                    mainViewAddress,
                    interfaceListHeadAddress,
                    interfaceNodeAddress,
                    interfaceAddress,
                    interfaceVTableAddress,
                    scannedInterfaceCount);

            return;
        }

        if (smoothedFramesPerSecond < 0 ||
            minimumFramesPerSecond < 0 ||
            maximumFramesPerSecond < 0)
        {
            state.FrameRate =
                ClientFrameRateObservation.Unavailable(
                    "Frame-rate interface contains a negative value",
                    mainViewAddress,
                    interfaceListHeadAddress,
                    interfaceNodeAddress,
                    interfaceAddress,
                    interfaceVTableAddress,
                    scannedInterfaceCount);

            return;
        }

        state.FrameRate =
            new ClientFrameRateObservation
            {
                IsAvailable = true,
                Status =
                    "Available; client-owned smoothed, minimum and maximum frame rate",
                MainViewAddress =
                    mainViewAddress,
                InterfaceListHeadAddress =
                    interfaceListHeadAddress,
                InterfaceNodeAddress =
                    interfaceNodeAddress,
                InterfaceAddress =
                    interfaceAddress,
                InterfaceVTableAddress =
                    interfaceVTableAddress,
                ScannedInterfaceCount =
                    scannedInterfaceCount,
                SmoothedFramesPerSecond =
                    smoothedFramesPerSecond,
                MinimumFramesPerSecond =
                    minimumFramesPerSecond,
                MaximumFramesPerSecond =
                    maximumFramesPerSecond,
            };
    }

    private static bool TryReadSingle(
        ProcessMemoryReader memory,
        uint baseAddress,
        uint offset,
        string fieldName,
        out float value,
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
                sizeof(float),
                out var bytes))
        {
            value = 0;
            error = $"Could not read {fieldName} at {FormatPointer(address)}";

            return false;
        }

        value = BitConverter.ToSingle(
            bytes,
            0);

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
