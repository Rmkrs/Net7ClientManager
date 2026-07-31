
namespace Net7ClientManager.Models;

using System.Diagnostics;
using Net7ClientManager.Forms;
using Net7ClientManager.Observations;
using Net7ClientManager.Observations.Models;

public sealed class ClientInstance(int processId, Process process)
{
    public int ProcessId { get; } = processId;

    public Process Process { get; } = process;

    public IntPtr GameWindowHandle { get; set; }

    public ClientState State { get; set; } = ClientState.WaitingForGameWindow;

    public ClientLifecycleState LifecycleState { get; set; } =
        ClientLifecycleState.Unknown;

    public uint LoadingOrTransitionFlag { get; set; }

    public string ObservationStatus { get; set; } =
        "Not attached";

    public long ObservationSequence { get; set; }

    public DateTimeOffset? LastObservedAt { get; set; }

    public ClientHostForm? HostForm { get; set; }

    public Guid? AssignedSlotId { get; set; }

    public bool StartedByManager { get; set; }

    public DateTimeOffset? StartedByManagerAt { get; set; }

    public ManagedClientLaunchRequest? ManagedLaunchRequest { get; set; }

    public bool AllowProfileAutoAssignment { get; set; } = true;

    public DateTimeOffset? DockedAt { get; set; }

    public string? AutomationStatus { get; set; }

    public DateTimeOffset? LastIntroSkipClickAt { get; set; }

    public DateTimeOffset? LoginSubmittedAt { get; set; }

    public AutoLoginProvenance? AutoLoginProvenance { get; set; }

    public DateTimeOffset? InGameSince { get; set; }

    public ClientLiveCharacterIdentity LiveCharacterIdentity { get; set; } =
        ClientLiveCharacterIdentity.Unavailable(
            processId,
            "Live character identity has not been observed");

    public DateTimeOffset? CharacterSelectionObservedAt { get; set; }

    public DateTimeOffset? EnterGameClickAt { get; set; }

    public DateTimeOffset? EnterGameSubmittedAt { get; set; }

    public InputActionDefinition? PendingEnterGameAction { get; set; }
}
