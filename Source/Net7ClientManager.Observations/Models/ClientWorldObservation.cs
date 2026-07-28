namespace Net7ClientManager.Observations.Models;

public sealed record ClientWorldObservation
{
    public bool IsAvailable { get; init; }

    public string Status { get; init; } = "";

    public ClientWorldEnvironment Environment { get; init; }

    public bool IsLocalObjectAvailable { get; init; }

    public string LocalObjectStatus { get; init; } = "";

    public uint PlayerId { get; init; }

    public uint ActiveSectorNumber { get; init; }

    public uint CurrentStarbaseId { get; init; }

    public uint GalaxyMapAddress { get; init; }

    public uint GalaxyMapLocationKey { get; init; }

    public string CurrentSystemName { get; init; } = "";

    public string CurrentSectorName { get; init; } = "";

    public string CurrentStarbaseName { get; init; } = "";

    public uint PresentationMode { get; init; }

    public uint SavedModeBeforeMovie { get; init; }

    public uint ModeChangeUpdateCountdown { get; init; }

    public uint PresentationUpdatePending { get; init; }

    public static ClientWorldObservation Unavailable(
        string status)
    {
        return new ClientWorldObservation
        {
            Status = status,
        };
    }
}
