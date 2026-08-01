namespace Net7ClientManager.Models;

/// <summary>
/// User presentation state relative to the position and size declared by an
/// addon. Keeping a delta lets addon updates move their default without
/// discarding the user's adjustment.
/// </summary>
public sealed class AddonWindowPlacement
{
    public string AddonId { get; set; } = "";

    public string WidgetId { get; set; } = "";

    public int OffsetX { get; set; }

    public int OffsetY { get; set; }

    /// <summary>
    /// Optional user-selected expanded size. Zero keeps the addon-authored
    /// dimension.
    /// </summary>
    public int Width { get; set; }

    public int Height { get; set; }

    /// <summary>
    /// User visibility preference. This remains separate from the addon's
    /// temporary visibility so contextual hiding never overwrites the user's
    /// choice.
    /// </summary>
    public bool IsVisible { get; set; } = true;

    public bool IsClosed { get; set; }

    public bool IsMinimized { get; set; }

    /// <summary>
    /// Desktop companion windows can restore their maximized presentation
    /// separately from the normal bounds kept in Width and Height.
    /// </summary>
    public bool IsMaximized { get; set; }

    public AddonWindowHorizontalEdge HorizontalEdge { get; set; }

    /// <summary>
    /// User adjustment applied only to the compact minimized window. Expanded
    /// bounds remain untouched so restoring returns to the previous position.
    /// </summary>
    public int MinimizedOffsetX { get; set; }

    public int MinimizedOffsetY { get; set; }
}
