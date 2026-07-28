namespace Net7ClientManager.Models;

public sealed class FleetCommandDefinition
{
    public string Id { get; set; } = "";

    public string Label { get; set; } = "";

    public bool ShowInOverlay { get; set; } = true;

    public bool IsEnabled { get; set; } = true;

    public FleetCommandCategory Category { get; set; } = FleetCommandCategory.Combat;

    public Dictionary<string, string> Arguments { get; set; } = [];

    // Legacy layout fields. Kept temporarily so Command Lab and stored JSON keep compiling.
    // The command palette now lays out by Category + list order.
    public int OverlayOrder { get; set; }

    public int OverlayRow { get; set; }

    public int OverlayColumn { get; set; }

    public int OverlayColumnSpan { get; set; } = 1;

    public List<FleetCommandBlock> Blocks { get; set; } = [];
}
