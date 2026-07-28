namespace Net7ClientManager.Addons.Contracts;

public sealed record AddonNavigationJourneySnapshot
{
    public string State { get; init; } = "not_started";

    public string StatusText { get; init; } = "";

    public string? StopReason { get; init; }

    public DateTimeOffset? StartedAt { get; init; }

    public string? PauseReason { get; init; }

    public bool IsActive { get; init; }

    public string? ExpectedTargetName { get; init; }

    public string? ExpectedSectorName { get; init; }

    public int? CurrentEnergy { get; init; }

    public int? RequiredEnergy { get; init; }

    public bool CanStart { get; init; }

    public bool CanStop { get; init; }

    public bool CanSelectNextTarget { get; init; }

    public bool CanPause { get; init; }

    public bool CanResume { get; init; }

    public bool CanClear { get; init; }

    public bool CanPlanReturnTrip { get; init; }
}
