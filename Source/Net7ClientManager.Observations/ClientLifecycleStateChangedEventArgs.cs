namespace Net7ClientManager.Observations;

public sealed class ClientLifecycleStateChangedEventArgs : EventArgs
{
    public required int ProcessId { get; init; }

    public required ClientLifecycleState Previous { get; init; }

    public required ClientLifecycleState Current { get; init; }

    public required ClientObservationSnapshot Snapshot { get; init; }
}
