namespace Net7ClientManager.Models;

public sealed class AddonCenterSettings
{
    public bool CheckForUpdatesAutomatically { get; set; } = true;


    public string EditorFontFamily { get; set; } = "Consolas";

    public int EditorFontSize { get; set; } = 10;

    public List<string> UnassignedEnabledAddonIds { get; set; } = [];

    public List<AddonWindowPlacement> UnassignedAddonWindowPlacements { get; set; } = [];
}
