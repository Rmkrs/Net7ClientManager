namespace Net7ClientManager.Models;

public sealed class SavedWindowPlacement
{
    public WindowBounds Bounds { get; set; } = new();

    public string? MonitorDeviceName { get; set; }

    public int MonitorOffsetLeft { get; set; }

    public int MonitorOffsetTop { get; set; }

    public bool Maximized { get; set; }
}
