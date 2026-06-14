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

    public Guid? AssignedSlotId { get; set; }

    public bool StartedByManager { get; set; }

    public DateTimeOffset? StartedByManagerAt { get; set; }

    public DateTimeOffset? DockedAt { get; set; }

    public DateTimeOffset? SizzleInterruptedAt { get; set; }

    public bool LoginNameFilled { get; set; }

    public bool PasswordFilled { get; set; }

    public bool LoginSubmitted { get; set; }

    public string? AutomationStatus { get; set; }

    public string? SizzleFilePath { get; set; }

    public bool SizzleSeen { get; set; }

    public DateTimeOffset? LastSizzleEscapeSentAt { get; set; }

    public DateTimeOffset? CharacterSelectReadyAt { get; set; }

    public DateTimeOffset? EnterGameClickAt { get; set; }

    public InputClickActionDefinition? PendingEnterGameAction { get; set; }

}
