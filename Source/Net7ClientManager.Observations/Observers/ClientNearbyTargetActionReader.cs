namespace Net7ClientManager.Observations.Observers;

using System.Globalization;
using Net7ClientManager.Observations.Models;

internal sealed class ClientNearbyTargetActionReader
{
    private const uint ImageBase = 0x00400000;

    private const uint GutterRadarObjectPresentationVTableStatic =
        0x00b06068;

    private const uint GutterRadarObjectPresentationVTableRva =
        GutterRadarObjectPresentationVTableStatic - ImageBase;

    private const uint ClientGameObjectVTableStatic =
        0x00b0070c;

    private const uint ClientGameObjectVTableRva =
        ClientGameObjectVTableStatic - ImageBase;

    private const uint RadarSystemClientContext = 0x00;
    private const uint RadarSystemHoveredClientObject = 0x08;

    private const uint PresentationVTable = 0x00;
    private const uint PresentationClientObject = 0x04;
    private const uint PresentationNormalizedX = 0x08;
    private const uint PresentationNormalizedY = 0x0c;
    private const uint PresentationIsInsideViewport = 0x24;
    private const int PresentationSnapshotLength = 0x25;

    private const uint ClientObjectVTable = 0x00;
    private const uint ClientObjectObjectId = 0x90;
    private const int ClientObjectSnapshotLength = 0x94;

    private const uint ClientContextTargetObjectId = 0x1130;

    private const float MinimumNormalizedCoordinate = -0.01f;
    private const float MaximumNormalizedCoordinate = 1.01f;

    public bool TryRead(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        uint clientContextAddress,
        uint radarSystemAddress,
        ClientGutterRadarTargetObservation target,
        out ClientNearbyTargetActionState state,
        out string error)
    {
        state = ClientNearbyTargetActionState.Unavailable(
            "Nearby-target action state has not been read",
            target.ObjectId,
            target.ActiveSectorNumber);

        error = "";

        if (moduleBaseAddress == 0 ||
            clientContextAddress == 0 ||
            radarSystemAddress == 0 ||
            target.ClientObjectAddress == 0 ||
            target.PresentationAddress == 0)
        {
            error =
                "Nearby-target native references are incomplete";

            return false;
        }

        if (!memory.TryReadBytes(
                target.PresentationAddress,
                PresentationSnapshotLength,
                out var presentationBytes))
        {
            error = $"Could not read nearby-target presentation at 0x{target.PresentationAddress:X8}";

            return false;
        }

        var expectedPresentationVTable = checked(
            moduleBaseAddress +
            GutterRadarObjectPresentationVTableRva);

        var presentationVTable = BitConverter.ToUInt32(
            presentationBytes,
            checked((int)PresentationVTable));

        if (presentationVTable != expectedPresentationVTable)
        {
            error = $"Nearby-target presentation vtable changed from 0x{expectedPresentationVTable:X8} to 0x{presentationVTable:X8}";

            return false;
        }

        var presentationClientObject = BitConverter.ToUInt32(
            presentationBytes,
            checked((int)PresentationClientObject));

        if (presentationClientObject !=
            target.ClientObjectAddress)
        {
            error = $"Nearby-target presentation now belongs to 0x{presentationClientObject:X8} instead of 0x{target.ClientObjectAddress:X8}";

            return false;
        }

        var normalizedX = BitConverter.ToSingle(
            presentationBytes,
            checked((int)PresentationNormalizedX));

        var normalizedY = BitConverter.ToSingle(
            presentationBytes,
            checked((int)PresentationNormalizedY));

        if (!IsValidNormalizedCoordinate(normalizedX) ||
            !IsValidNormalizedCoordinate(normalizedY))
        {
            error = string.Create(
                CultureInfo.InvariantCulture,
                $"Nearby-target presentation contains invalid normalized position ({normalizedX}, {normalizedY})");

            return false;
        }

        if (!memory.TryReadBytes(
                target.ClientObjectAddress,
                ClientObjectSnapshotLength,
                out var objectBytes))
        {
            error = $"Could not read nearby target object at 0x{target.ClientObjectAddress:X8}";

            return false;
        }

        var expectedClientObjectVTable = checked(
            moduleBaseAddress +
            ClientGameObjectVTableRva);

        var objectVTable = BitConverter.ToUInt32(
            objectBytes,
            checked((int)ClientObjectVTable));

        if (objectVTable != expectedClientObjectVTable)
        {
            error = $"Nearby target object vtable changed from 0x{expectedClientObjectVTable:X8} to 0x{objectVTable:X8}";

            return false;
        }

        var objectId = BitConverter.ToUInt32(
            objectBytes,
            checked((int)ClientObjectObjectId));

        if (objectId != target.ObjectId)
        {
            error = $"Nearby target object identity changed from {target.ObjectId} to {objectId}";

            return false;
        }

        if (!memory.TryReadUInt32(
                checked(
                    radarSystemAddress +
                    RadarSystemClientContext),
                out var radarClientContext) ||
            radarClientContext != clientContextAddress)
        {
            error =
                "Nearby-target radar system no longer belongs to the active client context";

            return false;
        }

        if (!memory.TryReadUInt32(
                checked(
                    radarSystemAddress +
                    RadarSystemHoveredClientObject),
                out var hoveredClientObjectAddress) ||
            !memory.TryReadUInt32(
                checked(
                    clientContextAddress +
                    ClientContextTargetObjectId),
                out var currentTargetObjectId))
        {
            error =
                "Could not read current nearby-target hover or selected-target state";

            return false;
        }

        state = new ClientNearbyTargetActionState
        {
            IsAvailable = true,
            Status = "Available",
            ActiveSectorNumber = target.ActiveSectorNumber,
            ObjectId = target.ObjectId,
            ClientObjectAddress = target.ClientObjectAddress,
            RadarSystemAddress = radarSystemAddress,
            HoveredClientObjectAddress =
                hoveredClientObjectAddress,
            CurrentTargetObjectId = currentTargetObjectId,
            NormalizedX = normalizedX,
            NormalizedY = normalizedY,
            IsInsideViewport =
                presentationBytes[
                    checked((int)PresentationIsInsideViewport)] != 0,
        };

        return true;
    }

    private static bool IsValidNormalizedCoordinate(float value)
    {
        return float.IsFinite(value) && value is >= MinimumNormalizedCoordinate and <= MaximumNormalizedCoordinate;
    }
}
