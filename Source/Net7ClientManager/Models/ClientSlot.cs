namespace Net7ClientManager.Models;

public sealed class ClientSlot
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "New Slot";

    public string? AccountName { get; set; }

    public WindowBounds Bounds { get; set; } = new();

    public bool AutoLogin { get; set; }

    public string? ResolutionPresetName { get; set; }
}
