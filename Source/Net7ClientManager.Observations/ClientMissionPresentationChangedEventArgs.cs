namespace Net7ClientManager.Observations;

using Net7ClientManager.Observations.Models;

public sealed class ClientMissionPresentationChangedEventArgs(
    int processId,
    ClientPanelPresentationObservation panelPresentation,
    ClientMissionLogObservation missions,
    DateTimeOffset observedAt) : EventArgs
{
    public int ProcessId { get; } = processId;

    public ClientPanelPresentationObservation PanelPresentation { get; } =
        panelPresentation;

    public ClientMissionLogObservation Missions { get; } =
        missions;

    public DateTimeOffset ObservedAt { get; } = observedAt;
}
