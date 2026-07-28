namespace Net7ClientManager.Models;

public sealed class WorldFindSettings
{
    public WindowBounds? Bounds { get; set; }

    public bool Maximized { get; set; }

    public string Query { get; set; } = "";

    public string KindFilter { get; set; } = "";

    public string SearchScope { get; set; } = "All";

    public string ItemCategory { get; set; } = "All";

    public int SourceFilters { get; set; }

    public string EffectIdentity { get; set; } = "";

    public bool KeepSearchOpenInTab { get; set; } = true;

    public bool ShowVendorCompanion { get; set; } = true;

    public string? SelectedIdentity { get; set; }

    public Dictionary<string, int> ColumnWidths { get; set; } =
        new(StringComparer.Ordinal);
}
