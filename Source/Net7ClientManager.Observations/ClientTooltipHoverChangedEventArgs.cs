namespace Net7ClientManager.Observations;

using Net7ClientManager.Observations.Models;

public sealed class ClientTooltipHoverChangedEventArgs(
    int processId,
    ClientTooltipHoverObservation previous,
    ClientTooltipHoverObservation current,
    DateTimeOffset observedAt) : EventArgs
{
    public int ProcessId { get; } = processId;

    public ClientTooltipHoverObservation Previous { get; } =
        previous ?? throw new ArgumentNullException(nameof(previous));

    public ClientTooltipHoverObservation Current { get; } =
        current ?? throw new ArgumentNullException(nameof(current));

    public DateTimeOffset ObservedAt { get; } = observedAt;
}
