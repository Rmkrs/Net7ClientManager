namespace Net7ClientManager.Observations.Observers;

using Net7ClientManager.Observations.Models;

internal sealed class ClientWorldObserver
{
    private const uint ClientContextPlayerId = 0x1138;
    private const uint ClientContextGalaxyMap = 0x12a0;

    // The exact independently loaded location. Stations and planet surfaces
    // have their own values rather than reusing the surrounding space-sector
    // number. During a handoff this commits to the destination early.
    private const uint ClientContextActiveSectorNumber = 0x12f0;

    private const uint ClientContextCurrentStarbaseId = 0x1324;

    // Only meaningful while PresentationMode is Movie3D or ScriptedMovie.
    // Outside those modes the stored value is intentionally treated as stale.
    private const uint ClientContextSavedModeBeforeMovie = 0x1370;
    private const uint ClientContextModeChangeUpdateCountdown = 0x1374;
    private const uint ClientContextPresentationUpdatePending = 0x14f0;

    // This changes with the current GalaxyMap location record, but its exact
    // numeric domain is not yet known. It is not an ActiveSectorNumber.
    private const uint GalaxyMapLocationKey = 0x7c;

    // Human-readable hierarchy. While docked, CurrentSectorName remains the
    // surrounding sector and CurrentStarbaseName names the loaded station.
    private const uint GalaxyMapCurrentSystemName = 0xa0;
    private const uint GalaxyMapCurrentSectorName = 0xb0;
    private const uint GalaxyMapCurrentStarbaseName = 0xc0;

    private const int MaximumLocationNameLength = 256;

    private readonly ClientObjectResolver objectResolver =
        new();

    public void Refresh(
        ProcessMemoryReader memory,
        ObservedClientState state)
    {
        if (!state.HasDirectClientState ||
            state.ClientContextAddress == 0)
        {
            state.World =
                ClientWorldObservation.Unavailable(
                    "Direct SClient state is unavailable");

            return;
        }

        var clientContextAddress =
            state.ClientContextAddress;

        if (!TryReadUInt32(
                memory,
                clientContextAddress,
                ClientContextPlayerId,
                "PlayerId",
                out var playerId,
                out var error) ||
            !TryReadUInt32(
                memory,
                clientContextAddress,
                ClientContextActiveSectorNumber,
                "ActiveSectorNumber",
                out var activeSectorNumber,
                out error) ||
            !TryReadUInt32(
                memory,
                clientContextAddress,
                ClientContextCurrentStarbaseId,
                "CurrentStarbaseId",
                out var currentStarbaseId,
                out error) ||
            !TryReadUInt32(
                memory,
                clientContextAddress,
                ClientContextSavedModeBeforeMovie,
                "SavedModeBeforeMovie",
                out var savedModeBeforeMovie,
                out error) ||
            !TryReadUInt32(
                memory,
                clientContextAddress,
                ClientContextModeChangeUpdateCountdown,
                "ModeChangeUpdateCountdown",
                out var modeChangeUpdateCountdown,
                out error) ||
            !TryReadUInt32(
                memory,
                clientContextAddress,
                ClientContextPresentationUpdatePending,
                "PresentationUpdatePending",
                out var presentationUpdatePending,
                out error) ||
            !TryReadUInt32(
                memory,
                clientContextAddress,
                ClientContextGalaxyMap,
                "GalaxyMap",
                out var galaxyMapAddress,
                out error))
        {
            state.World =
                ClientWorldObservation.Unavailable(
                    error);

            return;
        }

        var isLocalObjectAvailable =
            this.objectResolver.TryResolveLocalPlayerClientObject(
                memory,
                clientContextAddress,
                out _,
                out var localObjectStatus,
                out _);

        if (isLocalObjectAvailable)
        {
            localObjectStatus = "Available";
        }

        var galaxyMapLocationKey = 0u;
        var currentSystemName = "";
        var currentSectorName = "";
        var currentStarbaseName = "";

        List<string> locationErrors = [];

        if (galaxyMapAddress == 0)
        {
            locationErrors.Add(
                "GalaxyMap pointer is null");
        }
        else
        {
            if (!TryReadUInt32(
                    memory,
                    galaxyMapAddress,
                    GalaxyMapLocationKey,
                    "GalaxyMap.LocationKey",
                    out galaxyMapLocationKey,
                    out var locationError))
            {
                locationErrors.Add(
                    locationError);
            }

            currentSystemName = ReadOptionalPointerString(
                memory,
                galaxyMapAddress,
                GalaxyMapCurrentSystemName,
                "GalaxyMap.CurrentSystemName",
                locationErrors);

            currentSectorName = ReadOptionalPointerString(
                memory,
                galaxyMapAddress,
                GalaxyMapCurrentSectorName,
                "GalaxyMap.CurrentSectorName",
                locationErrors);

            currentStarbaseName = ReadOptionalPointerString(
                memory,
                galaxyMapAddress,
                GalaxyMapCurrentStarbaseName,
                "GalaxyMap.CurrentStarbaseName",
                locationErrors);
        }

        var environment = ClassifyEnvironment(
            state.LoadingOrTransitionFlag,
            isLocalObjectAvailable,
            state.PresentationMode);

        state.World =
            new ClientWorldObservation
            {
                IsAvailable = true,
                Status = locationErrors.Count == 0
                    ? "Available"
                    : string.Join(
                        "; ",
                        locationErrors),
                Environment = environment,
                IsLocalObjectAvailable =
                    isLocalObjectAvailable,
                LocalObjectStatus =
                    localObjectStatus,
                PlayerId = playerId,
                ActiveSectorNumber =
                    activeSectorNumber,
                CurrentStarbaseId =
                    currentStarbaseId,
                GalaxyMapAddress =
                    galaxyMapAddress,
                GalaxyMapLocationKey =
                    galaxyMapLocationKey,
                CurrentSystemName =
                    currentSystemName,
                CurrentSectorName =
                    currentSectorName,
                CurrentStarbaseName =
                    currentStarbaseName,
                PresentationMode =
                    state.PresentationMode,
                SavedModeBeforeMovie =
                    savedModeBeforeMovie,
                ModeChangeUpdateCountdown =
                    modeChangeUpdateCountdown,
                PresentationUpdatePending =
                    presentationUpdatePending,
            };
    }

    // Operator-facing world classification. Loading or loss of the local
    // ClientGameObject wins over the renderer mode because both are observed
    // during real sector, starbase, and planet transitions.
    private static ClientWorldEnvironment ClassifyEnvironment(
        uint loadingOrTransitionFlag,
        bool isLocalObjectAvailable,
        uint presentationMode)
    {
        if (loadingOrTransitionFlag != 0 ||
            !isLocalObjectAvailable)
        {
            return ClientWorldEnvironment.Transitioning;
        }

        return presentationMode switch
        {
            0 => ClientWorldEnvironment.Space,
            1 => ClientWorldEnvironment.Planet,
            2 => ClientWorldEnvironment.Initializing,
            3 => ClientWorldEnvironment.Movie3D,
            4 => ClientWorldEnvironment.Starbase,
            5 => ClientWorldEnvironment.GasGiant,
            6 => ClientWorldEnvironment.ScriptedMovie,
            _ => ClientWorldEnvironment.Unknown,
        };
    }

    private static bool TryReadUInt32(
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
                $"Address overflow while resolving {fieldName}";

            return false;
        }

        if (memory.TryReadUInt32(
                address,
                out value))
        {
            error = "";
            return true;
        }

        error = $"Could not read {fieldName} at 0x{address:X8}";

        return false;
    }

    private static string ReadOptionalPointerString(
        ProcessMemoryReader memory,
        uint baseAddress,
        uint offset,
        string fieldName,
        List<string> errors)
    {
        if (!TryReadUInt32(
                memory,
                baseAddress,
                offset,
                fieldName,
                out var stringAddress,
                out var error))
        {
            errors.Add(error);
            return "";
        }

        if (stringAddress == 0)
        {
            return "";
        }

        if (memory.TryReadNullTerminatedLatin1String(
                stringAddress,
                MaximumLocationNameLength,
                out var value))
        {
            return value;
        }

        errors.Add(
                $"Could not read {fieldName} text at 0x{stringAddress:X8}");

        return "";
    }
}
