namespace Net7ClientManager.Navigation;

public sealed record NavigationAutoPilotSnapshot
{
    public required int ProcessId { get; init; }

    public NavigationAutoPilotState State { get; init; }

    public NavigationAutoPilotStopReason StopReason { get; init; }

    public string StatusText { get; init; } = "Auto Pilot is inactive";

    public bool IsActive { get; init; }

    public DateTimeOffset? StartedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; } =
        DateTimeOffset.UtcNow;

    public Guid? RouteId { get; init; }

    public string? ExpectedTargetName { get; init; }

    public uint? ExpectedTargetObjectId { get; init; }

    public string? ExpectedSectorName { get; init; }

    public string? ExpectedSectorKey { get; init; }

    public int? CurrentEnergy { get; init; }

    public int? RequiredEnergy { get; init; }

    public static NavigationAutoPilotSnapshot Inactive(int processId)
    {
        return new NavigationAutoPilotSnapshot
        {
            ProcessId = processId,
            State = NavigationAutoPilotState.Inactive,
        };
    }
}
