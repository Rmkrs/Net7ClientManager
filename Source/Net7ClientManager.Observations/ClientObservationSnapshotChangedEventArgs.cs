namespace Net7ClientManager.Observations;

public sealed class ClientObservationSnapshotChangedEventArgs(
    ClientObservationSnapshot snapshot)
    : EventArgs
{
    public ClientObservationSnapshot Snapshot { get; } = snapshot;
}
