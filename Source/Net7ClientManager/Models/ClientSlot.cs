

namespace Net7ClientManager.Models;

public sealed class ClientSlot
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "New Slot";

    public WindowBounds Bounds { get; set; } = new();

    public bool AutoLogin { get; set; }

    public string? ResolutionPresetName { get; set; }

    public bool MatchGameResolutionToHost { get; set; } = true;

    public int GameResolutionWidth { get; set; }

    public int GameResolutionHeight { get; set; }

    public Guid? AccountId { get; set; }

    public Guid? CharacterId { get; set; }

    public bool AutoEnterGame { get; set; }

    public bool IncludeInAssistMe { get; set; } = true;

    public List<string> EnabledAddonIds { get; set; } = [];

    public List<AddonWindowPlacement> AddonWindowPlacements { get; set; } = [];
}
