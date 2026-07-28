namespace Net7ClientManager.Navigation;

using Net7ClientManager.Observations.Models;

public sealed record NavigationRouteSnapshot
{
    public required int ProcessId { get; init; }

    public required DateTimeOffset ObservedAt { get; init; }

    public bool IsAvailable { get; init; }

    public NavigationRouteStatus Status { get; init; }

    public string StatusText { get; init; } = "";

    public string? CharacterKey { get; init; }

    public string? CharacterName { get; init; }

    public string? Profession { get; init; }

    public GalaxySectorDefinition? CurrentSector { get; init; }

    public NavigationRoutePlan? Route { get; init; }

    public bool HasRoute { get; init; }

    public ClientReputationObservation CurrentPilotReputation { get; init; } =
        ClientReputationObservation.Unavailable(
            "Current pilot reputation was not observed");

    public static NavigationRouteSnapshot Unavailable(
        int processId,
        string statusText,
        DateTimeOffset? observedAt = null)
    {
        return new NavigationRouteSnapshot
        {
            ProcessId = processId,
            ObservedAt = observedAt ?? DateTimeOffset.MinValue,
            Status = NavigationRouteStatus.Unavailable,
            StatusText = statusText,
        };
    }
}
