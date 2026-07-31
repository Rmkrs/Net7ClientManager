

namespace Net7ClientManager.Models;

using System.Text.Json.Serialization;

public sealed class ClientSlot
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "New Slot";

    public WindowBounds Bounds { get; set; } = new();

    public bool AutoLogin { get; set; }

    public string? ResolutionPresetName { get; set; }

    public bool MatchGameResolutionToHost { get; set; } = true;

    // Kept for settings-file compatibility with releases that only had an
    // on/off title-bar switch. New code uses TitleBarMode.
    public bool ShowTitleBar { get; set; } = true;

    public ClientTitleBarMode? TitleBarMode { get; set; }

    public decimal TitleBarHoverDelaySeconds { get; set; } = 0.75m;

    public NavigationPresentationMode NavigationPresentationMode { get; set; } =
        NavigationPresentationMode.Companion;

    [JsonIgnore]
    public ClientTitleBarMode EffectiveTitleBarMode =>
        this.TitleBarMode ??
        (this.ShowTitleBar
            ? ClientTitleBarMode.Always
            : ClientTitleBarMode.Hidden);

    public int GameResolutionWidth { get; set; }

    public int GameResolutionHeight { get; set; }

    public Guid? AccountId { get; set; }

    public Guid? CharacterId { get; set; }

    public bool AutoEnterGame { get; set; }

    public bool IncludeInAssistMe { get; set; } = true;

    public List<string> EnabledAddonIds { get; set; } = [];

    public List<AddonWindowPlacement> AddonWindowPlacements { get; set; } = [];
}
