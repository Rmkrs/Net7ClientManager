namespace Net7ClientManager.Forms;

using Net7ClientManager.Models;

public sealed class SlotBoundsChangedEventArgs(ClientSlot slot) : EventArgs
{
    public ClientSlot Slot { get; } = slot;
}
