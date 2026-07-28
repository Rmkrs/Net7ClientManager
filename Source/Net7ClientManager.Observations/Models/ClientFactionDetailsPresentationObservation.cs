namespace Net7ClientManager.Observations.Models;

public sealed record ClientFactionDetailsPresentationObservation
{
    public bool IsAvailable { get; init; }

    public string Status { get; init; } = "";

    public uint FactionPanelAddress { get; init; }

    public uint FactionPanelVTableAddress { get; init; }

    public uint DetailsViewAddress { get; init; }

    public uint DetailsViewVTableAddress { get; init; }

    public bool IsFactionPanelDisplayed { get; init; }

    public bool IsDetailsViewDisplayed { get; init; }

    public int FirstVisibleFactionOffset { get; init; }

    public int SelectedVisibleRow { get; init; } = -1;

    public IReadOnlyList<uint> VisibleFactionRowAddresses { get; init; } = [];

    public IReadOnlyList<string> VisibleFactionKeys { get; init; } = [];

    public string SelectedFactionKey { get; init; } = "";

    public bool HasSelection =>
        !string.IsNullOrWhiteSpace(this.SelectedFactionKey);

    public bool IsDisplayed { get; init; }

    public static ClientFactionDetailsPresentationObservation Unavailable(
        string status,
        uint factionPanelAddress = 0,
        uint factionPanelVTableAddress = 0,
        uint detailsViewAddress = 0,
        uint detailsViewVTableAddress = 0)
    {
        return new ClientFactionDetailsPresentationObservation
        {
            Status = status,
            FactionPanelAddress = factionPanelAddress,
            FactionPanelVTableAddress = factionPanelVTableAddress,
            DetailsViewAddress = detailsViewAddress,
            DetailsViewVTableAddress = detailsViewVTableAddress,
        };
    }
}
