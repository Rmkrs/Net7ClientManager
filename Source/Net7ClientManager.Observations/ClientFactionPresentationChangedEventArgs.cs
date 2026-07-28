namespace Net7ClientManager.Observations;

using Net7ClientManager.Observations.Models;

public sealed class ClientFactionPresentationChangedEventArgs(
    int processId,
    ClientPanelPresentationObservation panelPresentation,
    ClientLocalPlayerObservation localPlayer,
    DateTimeOffset observedAt) : EventArgs
{
    public int ProcessId { get; } = processId;

    public ClientPanelPresentationObservation PanelPresentation { get; } =
        panelPresentation ?? throw new ArgumentNullException(
            nameof(panelPresentation));

    public ClientLocalPlayerObservation LocalPlayer { get; } =
        localPlayer ?? throw new ArgumentNullException(nameof(localPlayer));

    public DateTimeOffset ObservedAt { get; } = observedAt;
}
