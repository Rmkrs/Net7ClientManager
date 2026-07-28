namespace Net7ClientManager.Navigation;

public sealed record NavigationDataUpdateStatus
{
    public long ActiveRevision { get; init; }

    public long? PendingRevision { get; init; }

    public bool IsChecking { get; init; }

    public DateTimeOffset? LastSuccessfulCheck { get; init; }

    public DateTimeOffset? LastFailedCheck { get; init; }

    public string? LastError { get; init; }

    public bool AutomaticUpdatesEnabled { get; init; }

    public TimeSpan AutomaticCheckInterval { get; init; }

    public DateTimeOffset? NextAutomaticCheck { get; init; }

    public bool HasPendingUpdate =>
        this.PendingRevision is { } pendingRevision &&
        pendingRevision > this.ActiveRevision;
}
