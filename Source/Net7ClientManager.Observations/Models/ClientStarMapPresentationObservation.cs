namespace Net7ClientManager.Observations.Models;

public sealed record ClientStarMapPresentationObservation
{
    public bool IsAvailable { get; init; }

    public string Status { get; init; } = "";

    public uint GameplayUtilityControllerAddress { get; init; }

    public uint StarMapViewAddress { get; init; }

    public uint StarMapViewVTableAddress { get; init; }

    public uint RadarViewAddress { get; init; }

    public uint RadarViewVTableAddress { get; init; }

    public uint AlternateViewAddress { get; init; }

    public uint SelectedViewAddress { get; init; }

    public uint SelectedViewVTableAddress { get; init; }

    public bool IsDisplayed { get; init; }

    public bool IsMaximized { get; init; }

    public bool IsRadarPresentationActive { get; init; }

    public bool IsSelectedPresentationActive { get; init; }

    public ClientStarMapPresentationKind SelectedPresentation { get; init; }

    public static ClientStarMapPresentationObservation Unavailable(
        string status,
        uint gameplayUtilityControllerAddress = 0,
        uint starMapViewAddress = 0,
        uint starMapViewVTableAddress = 0,
        uint radarViewAddress = 0,
        uint radarViewVTableAddress = 0,
        uint alternateViewAddress = 0,
        uint selectedViewAddress = 0,
        uint selectedViewVTableAddress = 0)
    {
        return new ClientStarMapPresentationObservation
        {
            Status = status,
            GameplayUtilityControllerAddress =
                gameplayUtilityControllerAddress,
            StarMapViewAddress = starMapViewAddress,
            StarMapViewVTableAddress =
                starMapViewVTableAddress,
            RadarViewAddress = radarViewAddress,
            RadarViewVTableAddress =
                radarViewVTableAddress,
            AlternateViewAddress = alternateViewAddress,
            SelectedViewAddress = selectedViewAddress,
            SelectedViewVTableAddress =
                selectedViewVTableAddress,
        };
    }
}
