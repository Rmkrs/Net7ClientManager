namespace Net7ClientManager.Observations.Models;

public sealed record ClientMissionDetailsPresentationObservation
{
    public bool IsAvailable { get; init; }

    public string Status { get; init; } = "";

    public uint MissionPanelAddress { get; init; }

    public uint MissionPanelVTableAddress { get; init; }

    public uint DetailsViewAddress { get; init; }

    public uint ForfeitDialogAddress { get; init; }

    public bool IsForfeitConfirmationDisplayed { get; init; }

    public bool IsMissionPanelDisplayed { get; init; }

    public int FirstVisibleMissionOffset { get; init; }

    public int SelectedVisibleRow { get; init; } = -1;

    public IReadOnlyList<uint> VisibleMissionAddresses { get; init; } = [];

    public uint SelectedMissionAddress { get; init; }

    public string SelectedMissionName { get; init; } = "";

    public bool HasSelection =>
        this.SelectedVisibleRow is >= 0 and < 3 &&
        this.SelectedMissionAddress != 0;

    public bool IsDisplayed { get; init; }

    public static ClientMissionDetailsPresentationObservation Unavailable(
        string status,
        uint missionPanelAddress = 0,
        uint missionPanelVTableAddress = 0,
        uint detailsViewAddress = 0,
        uint forfeitDialogAddress = 0)
    {
        return new ClientMissionDetailsPresentationObservation
        {
            Status = status,
            MissionPanelAddress = missionPanelAddress,
            MissionPanelVTableAddress = missionPanelVTableAddress,
            DetailsViewAddress = detailsViewAddress,
            ForfeitDialogAddress = forfeitDialogAddress,
        };
    }
}
