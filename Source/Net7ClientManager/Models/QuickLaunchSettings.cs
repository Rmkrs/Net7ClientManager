namespace Net7ClientManager.Models;

public sealed class QuickLaunchSettings
{
    public string HostResolutionPresetName { get; set; } = "1280×720";

    public bool MatchGameResolutionToHost { get; set; } = true;

    public int GameResolutionWidth { get; set; } = 1280;

    public int GameResolutionHeight { get; set; } = 720;
}
