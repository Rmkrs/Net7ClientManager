namespace Net7ClientManager.Observations;

using Net7ClientManager.Observations.Models;

public sealed class ClientCraftingActivityRealtimeChangedEventArgs(
    int processId,
    ClientManufacturingActivityObservation activity) : EventArgs
{
    public int ProcessId { get; } = processId;

    public ClientManufacturingActivityObservation Activity { get; } = activity;
}
