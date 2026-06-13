namespace Net7ClientManager.Models;

using System.Diagnostics;
using Net7ClientManager.Forms;

public sealed class ClientInstance(int processId, Process process)
{
    public int ProcessId { get; } = processId;

    public Process Process { get; } = process;

    public IntPtr GameWindowHandle { get; set; }

    public ClientState State { get; set; } = ClientState.WaitingForGameWindow;

    public ClientHostForm? HostForm { get; set; }

    public bool CloseRequestedByUser { get; set; }

    public Guid? AssignedSlotId { get; set; }
}
