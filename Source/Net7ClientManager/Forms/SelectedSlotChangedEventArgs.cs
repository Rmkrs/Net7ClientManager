namespace Net7ClientManager.Forms;

using Net7ClientManager.Models;

public sealed class SelectedSlotChangedEventArgs(ClientSlot? selectedSlot) : EventArgs
{
    public ClientSlot? SelectedSlot { get; } = selectedSlot;
}
