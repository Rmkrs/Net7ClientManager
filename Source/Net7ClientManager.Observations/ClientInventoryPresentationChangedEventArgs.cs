namespace Net7ClientManager.Observations;

using Net7ClientManager.Observations.Models;

public sealed class ClientInventoryPresentationChangedEventArgs(
    int processId,
    ClientPanelPresentationObservation panelPresentation,
    DateTimeOffset observedAt) : EventArgs
{
    public int ProcessId { get; } = processId;

    public ClientPanelPresentationObservation PanelPresentation { get; } =
        panelPresentation ?? throw new ArgumentNullException(
            nameof(panelPresentation));

    public DateTimeOffset ObservedAt { get; } = observedAt;
}
