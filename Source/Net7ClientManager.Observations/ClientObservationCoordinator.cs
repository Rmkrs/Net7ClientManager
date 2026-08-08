namespace Net7ClientManager.Observations;

using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Channels;
using Net7ClientManager.Observations.Models;
using Net7ClientManager.Observations.Observers;

/// <summary>
/// Process-wide authority for all Net7 client observation.
///
/// The application creates exactly one coordinator. It owns every attached
/// client session, all ordinary observation scheduling, immutable snapshot
/// publication and the one process-wide high-frequency combat gatekeeper.
/// Individual clients never own observers or polling threads.
/// </summary>
public sealed class ClientObservationCoordinator : IDisposable
{
    // The coordinator scheduler is only a due-time gate. Tightening it does
    // not pull ordinary feature lanes forward; their own intervals remain
    // unchanged. The 10 ms quantum primarily services the narrow 20 ms vendor
    // transaction lane; crafting itself uses its proven 50 ms cadence.
    private static readonly TimeSpan schedulerInterval =
        TimeSpan.FromMilliseconds(10);

    // Panel presentation does not need the crafting scheduler quantum. Keep its
    // independent lightweight scheduler at the existing 50 ms cadence.
    private static readonly TimeSpan panelPresentationSchedulerInterval =
        TimeSpan.FromMilliseconds(50);

    private static readonly TimeSpan navigationStatePollInterval =
        TimeSpan.FromMilliseconds(100);

    private static readonly TimeSpan startupPollInterval =
        TimeSpan.FromMilliseconds(100);

    private static readonly TimeSpan inGamePollInterval =
        TimeSpan.FromMilliseconds(500);

    // Character and inventory panel presentation is a lightweight,
    // operator-driven lane. It reads only the persistent view pointers and
    // displayed/tab fields, never inventory contents. Keep it responsive
    // without accelerating topology, world, inventory or addon snapshot
    // publication. The lane emits only when presentation actually changes,
    // avoiding a UI message backlog.
    private static readonly TimeSpan hiddenCharacterInfoPresentationPollInterval =
        TimeSpan.FromMilliseconds(100);

    private static readonly TimeSpan visibleCharacterInfoPresentationPollInterval =
        TimeSpan.FromMilliseconds(50);

    // Most promoted observers originally ran once per second in the probe.
    // Preserve that proven cadence while the coordinator samples core
    // session state twice per second normally and accelerates only while an
    // operator-facing panel needs interaction-speed feedback.
    private static readonly TimeSpan featurePollInterval =
        TimeSpan.FromSeconds(1);

    // Group Skills needs interaction-speed target/name/status/shortcut refresh
    // while its compact overlay is open, but it must not pull the full feature
    // lane forward. Keep this as a narrow, explicitly requested one-shot lane.
    // The overlay requests it on its own timer; closing the overlay naturally
    // stops the requests.

    // Loot tractor objects usually live for roughly one second. Keep the
    // detector in its own lightweight adaptive lane so addon events do not
    // depend on the ordinary feature cadence.
    private static readonly TimeSpan lootTractorIdlePollInterval =
        TimeSpan.FromMilliseconds(100);

    private static readonly TimeSpan lootTractorActivePollInterval =
        TimeSpan.FromMilliseconds(50);

    // Analyze/Dismantle terminal results remain resident for seconds in the
    // native ManufacturingLab state. 50 ms is comfortably inside that proven
    // window while retaining interaction-speed responsiveness. The hot path
    // still reads only ManufacturingLab plus cached direct cargo properties.
    private static readonly TimeSpan craftingActivityPollInterval =
        TimeSpan.FromMilliseconds(50);

    // The visible Analyze/Dismantle countdown must not share the general
    // coordinator loop. PollClient and other due lanes can legitimately take
    // hundreds of milliseconds, which turns an otherwise correct 50 ms due
    // time into a visibly frozen countdown. This dedicated sampler reads only
    // the Analyze panel's active/phase/timer fields plus ManufacturingLab Mode.
    private static readonly TimeSpan dismantlePacingPollInterval =
        TimeSpan.FromMilliseconds(50);

    // Keep a tiny gatekeeper alive while the known native crafting panels are
    // inactive. It reads only their active flags, so a player can open a
    // terminal and immediately use it without waiting for the 500 ms ordinary
    // snapshot lane to notice first.
    private static readonly TimeSpan craftingActivityIdlePollInterval =
        TimeSpan.FromMilliseconds(100);

    // Native tooltip hover is a tiny interaction lane: two stable ClientView
    // roots, two controller blocks and at most one gadget. Poll slowly while
    // idle and briefly tighten only while a gadget is active. Item contents
    // remain on the existing authoritative inventory lanes.
    private static readonly TimeSpan tooltipHoverIdlePollInterval =
        TimeSpan.FromMilliseconds(250);

    private static readonly TimeSpan tooltipHoverActivePollInterval =
        TimeSpan.FromMilliseconds(50);

    // Character-owned catalogues and collection-heavy observations do not
    // need sub-second updates. They remain centrally scheduled here rather
    // than creating per-client timers.
    private static readonly TimeSpan slowFeaturePollInterval =
        TimeSpan.FromSeconds(2);

    // Shopping at an open vendor needs quicker cargo and vendor-catalogue
    // feedback than the ordinary collection-heavy lane. Refresh only those
    // two bounded collections while Vendor Trade is active; all other local
    // player observations retain their proven two-second cadence.
    private static readonly TimeSpan vendorShoppingPollInterval =
        TimeSpan.FromMilliseconds(500);

    // Rapid vendor clicks need transaction-speed credit/cargo observation, but
    // the complete inventory and 128-slot vendor catalogue must stay out of
    // the hot path. This lane reads only Hull.Money plus cached Cargo
    // ItemTemplateID/StackCount properties.
    private static readonly TimeSpan vendorTransactionPollInterval =
        TimeSpan.FromMilliseconds(20);

    private static readonly TimeSpan vendorTransactionIdlePollInterval =
        TimeSpan.FromMilliseconds(50);

    private readonly System.Threading.Lock lockObject = new();

    private readonly Dictionary<int, ObservedClientState> observedClients = [];

    private readonly ClientTopologyObserver topologyObserver = new();

    private readonly ClientSessionObserver sessionObserver = new();

    private readonly ClientNetworkTrafficObserver networkTrafficObserver = new();

    private readonly ClientFrameRateObserver frameRateObserver = new();

    private readonly ClientStarMapPresentationObserver starMapPresentationObserver = new();

    private readonly ClientPanelPresentationObserver panelPresentationObserver = new();

    private readonly ClientLootingObserver lootingObserver = new();

    private readonly ClientLootTractorObserver lootTractorObserver =
        new();

    private readonly ClientWorldObserver worldObserver = new();

    private readonly ClientStarbaseContextObserver starbaseContextObserver = new();

    private readonly ClientAudioCueObserver audioCueObserver = new();

    // Jobs Terminal state carries per-panel caches and catalogue generations.
    // Keep one bounded observer per attached process so JobIDs and UI resources
    // from different clients can never contaminate each other.
    private readonly Dictionary<int, ClientJobTerminalObserver>
        jobTerminalObservers = [];

    private readonly ClientNavigationObserver navigationObserver = new();

    private readonly ClientNavigationStateObserver navigationStateObserver =
        new();

    // Auto Pilot may need the exact next target before the ordinary one-second
    // feature lane republishes after a sector transition. Keep an independent
    // reader so a fresh, bounded read cannot race the scheduled observer cache.
    private readonly ClientNavigationObserver directNavigationObserver = new();

    private readonly System.Threading.Lock directNavigationReadLock = new();

    private readonly ClientGutterRadarObserver gutterRadarObserver = new();

    private readonly ClientNearbyTargetActionReader nearbyTargetActionReader = new();

    private readonly ClientLocalPlayerObserver localPlayerObserver = new();

    private readonly ClientSecureInventoryObserver secureInventoryObserver = new();

    private readonly ClientMissionObserver missionObserver = new();

    private readonly ClientFactionCatalog factionCatalog = new();

    private readonly ClientReputationObserver reputationObserver;

    private readonly ClientGroupObserver groupObserver = new();

    private readonly ClientTargetObserver targetObserver = new();

    private readonly ClientObjectResolver objectResolver = new();

    private readonly ClientTargetInteractionObserver targetInteractionObserver = new();

    private readonly ClientShortcutBarObserver shortcutBarObserver = new();

    private readonly ClientTooltipDelayObserver tooltipDelayObserver = new();

    private readonly ClientChatChannelOptionsReader
        chatChannelOptionsReader = new();

    private readonly ClientChatColorOptionsReader
        chatColorOptionsReader = new();

    private readonly ClientTooltipHoverObserver tooltipHoverObserver = new();

    private readonly ClientManufacturingLabObserver manufacturingLabObserver =
        new();

    private readonly ClientRecipeMappingObserver recipeMappingObserver =
        new();

    private readonly ClientCombatObserver combatObserver = new();

    private CancellationTokenSource? cancellationTokenSource;

    private Task? pollingTask;

    private Task? navigationStatePollingTask;

    private Task? panelPresentationPollingTask;

    private Task? dismantlePacingPollingTask;

    // Observation-event consumers perform persistence, companion refreshes and
    // addon publication. Never execute that work on the observation scheduler:
    // doing so can make a 10 ms crafting result disappear while the sampler is
    // busy writing the previous one. A single-reader channel preserves the
    // coordinator's publication order without blocking memory sampling.
    private readonly Channel<Action> eventDispatchChannel =
        Channel.CreateUnbounded<Action>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false,
            });

    // Dismantle cooldown text is latency-sensitive presentation, not journal
    // or persistence work. Keep it off the general ordered event queue so a
    // burst of snapshot consumers cannot make a native 2-second countdown
    // arrive late or play back stale intermediate values.
    private readonly Channel<Action> realtimeEventDispatchChannel =
        Channel.CreateUnbounded<Action>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false,
            });

    private Task? eventDispatchTask;

    private Task? realtimeEventDispatchTask;

    public ClientObservationCoordinator()
    {
        this.reputationObserver =
            new ClientReputationObserver(
                this.factionCatalog);
    }

    public event EventHandler<ClientObservationSnapshotChangedEventArgs>?
        SnapshotChanged;

    public event EventHandler<ClientCraftingActivityRealtimeChangedEventArgs>?
        CraftingActivityRealtimeChanged;

    public event EventHandler<ClientMissionPresentationChangedEventArgs>?
        MissionPresentationChanged;

    public event EventHandler<ClientFactionPresentationChangedEventArgs>?
        FactionPresentationChanged;

    public event EventHandler<ClientSkillPresentationChangedEventArgs>?
        SkillPresentationChanged;

    public event EventHandler<ClientInventoryPresentationChangedEventArgs>?
        InventoryPresentationChanged;

    public event EventHandler<ClientTooltipHoverChangedEventArgs>?
        TooltipHoverChanged;

    public event EventHandler<ClientLifecycleStateChangedEventArgs>?
        LifecycleStateChanged;

    public event EventHandler<ClientNavigationStateChangedEventArgs>?
        NavigationStateChanged;

    public event EventHandler<ClientChatMessageObservedEventArgs>?
        ChatMessageObserved;

    public void Start()
    {
        lock (this.lockObject)
        {
            if (this.pollingTask != null)
            {
                return;
            }

            this.cancellationTokenSource =
                new CancellationTokenSource();

            this.combatObserver.Start();

            var cancellationToken =
                this.cancellationTokenSource.Token;

            this.pollingTask = Task.Run(
                () => this.PollLoopAsync(cancellationToken),
                cancellationToken);

            this.navigationStatePollingTask = Task.Run(
                () => this.NavigationStatePollLoopAsync(
                    cancellationToken),
                cancellationToken);

            this.panelPresentationPollingTask = Task.Run(
                () => this.PanelPresentationPollLoopAsync(
                    cancellationToken),
                cancellationToken);

            this.dismantlePacingPollingTask = Task.Run(
                () => this.DismantlePacingPollLoopAsync(
                    cancellationToken),
                cancellationToken);

            this.eventDispatchTask = Task.Run(
                this.EventDispatchLoopAsync);

            this.realtimeEventDispatchTask = Task.Run(
                this.RealtimeEventDispatchLoopAsync);
        }
    }

    public void Attach(int processId)
    {
        DateTimeOffset processStartedAt;

        try
        {
            using var process = Process.GetProcessById(processId);

            if (process.HasExited)
            {
                return;
            }

            processStartedAt =
                new DateTimeOffset(process.StartTime.ToUniversalTime());
        }
        catch (ArgumentException)
        {
            return;
        }
        catch (InvalidOperationException)
        {
            return;
        }
        catch (Win32Exception)
        {
            return;
        }
        catch (NotSupportedException)
        {
            return;
        }

        ClientObservationSnapshot initialSnapshot;

        lock (this.lockObject)
        {
            if (this.observedClients.TryGetValue(
                    processId,
                    out var existing) &&
                existing.ProcessStartedAt == processStartedAt)
            {
                return;
            }

            var state = new ObservedClientState(
                processId,
                processStartedAt)
            {
                NextPollAt = DateTimeOffset.UtcNow,
                NextNavigationStatePollAt = DateTimeOffset.UtcNow,
                NextPanelPresentationPollAt = DateTimeOffset.UtcNow,
                NextFeaturePollAt = DateTimeOffset.UtcNow,
                NextLootTractorPollAt = DateTimeOffset.UtcNow,
                NextCraftingActivityPollAt = DateTimeOffset.MaxValue,
                NextTooltipHoverPollAt = DateTimeOffset.UtcNow,
                NextTooltipItemPollAt = DateTimeOffset.MaxValue,
                NextGroupSkillsTargetPollAt = DateTimeOffset.MaxValue,
                NextSlowFeaturePollAt = DateTimeOffset.UtcNow,
                NextVendorShoppingPollAt = DateTimeOffset.UtcNow,
                NextVendorTransactionPollAt = DateTimeOffset.UtcNow,
            };

            initialSnapshot = this.CreateSnapshot(state);
            state.Snapshot = initialSnapshot;

            this.observedClients[processId] = state;
        }

        this.QueueSnapshotChanged(initialSnapshot);
    }

    public void Detach(int processId)
    {
        bool removed;

        lock (this.lockObject)
        {
            removed = this.observedClients.Remove(processId);
        }

        if (!removed)
        {
            return;
        }

        this.ForgetProcessCaches(processId);
    }


    public void RequestGroupSkillsTargetPoll(
        IEnumerable<int> processIds)
    {
        ArgumentNullException.ThrowIfNull(processIds);

        var requested = processIds.ToHashSet();

        if (requested.Count == 0)
        {
            return;
        }

        lock (this.lockObject)
        {
            var now = DateTimeOffset.UtcNow;

            foreach (var processId in requested)
            {
                if (this.observedClients.TryGetValue(
                        processId,
                        out var state) &&
                    state.NextGroupSkillsTargetPollAt > now)
                {
                    state.NextGroupSkillsTargetPollAt = now;
                }
            }
        }
    }

    public void RequestTooltipItemPoll(
        int processId,
        ClientTooltipHoverObservation hover)
    {
        ArgumentNullException.ThrowIfNull(hover);

        var scope = ResolveTooltipItemRefreshScope(hover);

        if (scope == ClientTooltipItemRefreshScope.None)
        {
            return;
        }

        lock (this.lockObject)
        {
            if (!this.observedClients.TryGetValue(
                    processId,
                    out var state))
            {
                return;
            }

            state.PendingTooltipItemRefreshScope |= scope;

            var now = DateTimeOffset.UtcNow;

            if (state.NextTooltipItemPollAt > now)
            {
                state.NextTooltipItemPollAt = now;
            }
        }
    }

    public void SetRecipeMappingCatalogObservationEnabled(
        int processId,
        bool enabled)
    {
        lock (this.lockObject)
        {
            if (!this.observedClients.TryGetValue(processId, out var state) ||
                state.RecipeMappingCatalogObservationEnabled == enabled)
            {
                return;
            }

            state.RecipeMappingCatalogObservationEnabled = enabled;

            if (!enabled &&
                (state.ManufacturingCatalog.IsAvailable ||
                 state.ManufacturingCatalog.ObservedAt != DateTimeOffset.MinValue))
            {
                state.ManufacturingCatalog =
                    ClientManufacturingCatalogObservation.Unavailable(
                        "Crafting catalogue observation is idle");
                this.recipeMappingObserver.ForgetCatalog(processId);
            }
        }
    }

    public bool TryGetSnapshot(
        int processId,
        out ClientObservationSnapshot snapshot)
    {
        lock (this.lockObject)
        {
            if (this.observedClients.TryGetValue(
                    processId,
                    out var state) &&
                state.Snapshot != null)
            {
                snapshot = state.Snapshot;
                return true;
            }
        }

        snapshot = null!;
        return false;
    }

    public ClientChatChannelOptionsState ReadChatChannelOptions(
        int processId)
    {
        ObservedClientState state;

        lock (this.lockObject)
        {
            if (!this.observedClients.TryGetValue(processId, out state) ||
                state.LifecycleState != ClientLifecycleState.InGame ||
                !state.HasDirectClientState)
            {
                return ClientChatChannelOptionsState.Unavailable(
                    "The client is not in an observable gameplay session");
            }
        }

        try
        {
            using var memory = ProcessMemoryReader.Open(processId);
            return this.chatChannelOptionsReader.Read(memory, state);
        }
        catch (Exception exception) when (
            exception is System.ComponentModel.Win32Exception or
            InvalidOperationException or
            ArgumentException)
        {
            return ClientChatChannelOptionsState.Unavailable(
                string.Concat(
                    "Live chat-channel options could not be read: ",
                    exception.Message));
        }
    }

    public ClientChatColorOptionsState ReadChatColorOptions(
        int processId)
    {
        ObservedClientState state;

        lock (this.lockObject)
        {
            if (!this.observedClients.TryGetValue(processId, out state) ||
                state.LifecycleState != ClientLifecycleState.InGame ||
                !state.HasDirectClientState)
            {
                return ClientChatColorOptionsState.Unavailable(
                    "The client is not in an observable gameplay session");
            }
        }

        try
        {
            using var memory = ProcessMemoryReader.Open(processId);
            return this.chatColorOptionsReader.Read(memory, state);
        }
        catch (Exception exception) when (
            exception is System.ComponentModel.Win32Exception or
            InvalidOperationException or
            ArgumentException)
        {
            return ClientChatColorOptionsState.Unavailable(
                string.Concat(
                    "Live chat colors could not be read: ",
                    exception.Message));
        }
    }

    public ClientChatState ReadChatState(int processId)
    {
        const uint mainHudOffset = 0x127C;
        const uint selectedChannelOffset = 0x148;
        const uint selectedChannelNamePointerOffset = 0x23C;
        const uint replyTargetPointerOffset = 0x1DC;

        var inputState = this.ReadChatInputState(
            processId,
            out var inputStatus);

        uint clientContextAddress;

        lock (this.lockObject)
        {
            if (!this.observedClients.TryGetValue(
                    processId,
                    out var state) ||
                state.LifecycleState != ClientLifecycleState.InGame ||
                !state.HasDirectClientState ||
                state.ClientContextAddress == 0)
            {
                return new ClientChatState(
                    inputState,
                    -1,
                    ClientSelectedChatChannel.Unknown,
                    null,
                    null,
                    inputStatus);
            }

            clientContextAddress = state.ClientContextAddress;
        }

        try
        {
            using var memory = ProcessMemoryReader.Open(processId);

            if (!TryAddOffset(
                    clientContextAddress,
                    mainHudOffset,
                    out var mainHudPointerAddress) ||
                !memory.TryReadUInt32(
                    mainHudPointerAddress,
                    out var mainHudAddress) ||
                mainHudAddress == 0)
            {
                return new ClientChatState(
                    inputState,
                    -1,
                    ClientSelectedChatChannel.Unknown,
                    null,
                    null,
                    string.Concat(
                        inputStatus,
                        "; SClient.MainHud is unavailable"));
            }

            var rawSelectedChannel = -1;

            if (TryAddOffset(
                    mainHudAddress,
                    selectedChannelOffset,
                    out var selectedChannelAddress) &&
                memory.TryReadUInt32(
                    selectedChannelAddress,
                    out var selectedChannelValue))
            {
                rawSelectedChannel = unchecked((int)selectedChannelValue);
            }

            string? selectedChannelName = null;

            if (TryAddOffset(
                    mainHudAddress,
                    selectedChannelNamePointerOffset,
                    out var selectedChannelNamePointerAddress) &&
                memory.TryReadUInt32(
                    selectedChannelNamePointerAddress,
                    out var selectedChannelNameAddress) &&
                selectedChannelNameAddress != 0 &&
                memory.TryReadNullTerminatedLatin1String(
                    selectedChannelNameAddress,
                    maximumLength: 96,
                    out var observedSelectedChannelName) &&
                !string.IsNullOrWhiteSpace(observedSelectedChannelName))
            {
                selectedChannelName = observedSelectedChannelName
                    .Trim()
                    .TrimEnd(':')
                    .Trim();
            }

            string? replyTarget = null;

            if (TryAddOffset(
                    mainHudAddress,
                    replyTargetPointerOffset,
                    out var replyTargetPointerAddress) &&
                memory.TryReadUInt32(
                    replyTargetPointerAddress,
                    out var replyTargetAddress) &&
                replyTargetAddress != 0 &&
                memory.TryReadNullTerminatedLatin1String(
                    replyTargetAddress,
                    maximumLength: 64,
                    out var observedReplyTarget) &&
                !string.IsNullOrWhiteSpace(observedReplyTarget))
            {
                replyTarget = observedReplyTarget.Trim();
            }

            var selectedChannel = rawSelectedChannel switch
            {
                0 => ClientSelectedChatChannel.Broadcast,
                1 => ClientSelectedChatChannel.Local,
                2 => ClientSelectedChatChannel.Guild,
                3 => ClientSelectedChatChannel.Group,
                4 => ClientSelectedChatChannel.PrivateChannel,
                5 => ClientSelectedChatChannel.PublicChannel,
                >= 6 and <= 9 => ClientSelectedChatChannel.DirectMessage,
                _ => ClientSelectedChatChannel.Unknown,
            };

            return new ClientChatState(
                inputState,
                rawSelectedChannel,
                selectedChannel,
                selectedChannelName,
                replyTarget,
                string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"{inputStatus}; MainHud=0x{mainHudAddress:X8}, selected={rawSelectedChannel}, name={selectedChannelName ?? "<none>"}, reply={replyTarget ?? "<none>"}"));
        }
        catch (Exception exception) when (
            exception is System.ComponentModel.Win32Exception or
            InvalidOperationException or
            ArgumentException)
        {
            return new ClientChatState(
                inputState,
                -1,
                ClientSelectedChatChannel.Unknown,
                null,
                null,
                string.Concat(
                    inputStatus,
                    "; chat routing state could not be read: ",
                    exception.Message));
        }
    }

    public ClientChatInputState ReadChatInputState(
        int processId,
        out string status)
    {
        const uint mainHudOffset = 0x127C;
        const uint chatInputOffset = 0x140;
        const uint activeByteOffset = 0x3C;

        uint clientContextAddress;

        lock (this.lockObject)
        {
            if (!this.observedClients.TryGetValue(
                    processId,
                    out var state))
            {
                status = "The observed client is not attached";
                return ClientChatInputState.Unknown;
            }

            if (state.LifecycleState != ClientLifecycleState.InGame ||
                !state.HasDirectClientState ||
                state.ClientContextAddress == 0)
            {
                status = "Direct in-game SClient state is unavailable";
                return ClientChatInputState.Unknown;
            }

            clientContextAddress = state.ClientContextAddress;
        }

        try
        {
            using var memory = ProcessMemoryReader.Open(processId);

            if (!TryAddOffset(
                    clientContextAddress,
                    mainHudOffset,
                    out var mainHudPointerAddress) ||
                !memory.TryReadUInt32(
                    mainHudPointerAddress,
                    out var mainHudAddress) ||
                mainHudAddress == 0)
            {
                status = "SClient.MainHud is unavailable";
                return ClientChatInputState.Unknown;
            }

            if (!TryAddOffset(
                    mainHudAddress,
                    chatInputOffset,
                    out var chatInputPointerAddress) ||
                !memory.TryReadUInt32(
                    chatInputPointerAddress,
                    out var chatInputAddress) ||
                chatInputAddress == 0)
            {
                status = "MainHud.ChatInput is unavailable";
                return ClientChatInputState.Unknown;
            }

            if (!TryAddOffset(
                    chatInputAddress,
                    activeByteOffset,
                    out var activeByteAddress) ||
                !memory.TryReadBytes(
                    activeByteAddress,
                    length: 1,
                    out var activeBytes))
            {
                status = "ChatInput active byte could not be read";
                return ClientChatInputState.Unknown;
            }

            var activeByte = activeBytes[0];
            status = string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"SClient=0x{clientContextAddress:X8}, MainHud=0x{mainHudAddress:X8}, ChatInput=0x{chatInputAddress:X8}, active=0x{activeByte:X2}");

            return activeByte == 0
                ? ClientChatInputState.Inactive
                : ClientChatInputState.Active;
        }
        catch (Exception exception) when (
            exception is System.ComponentModel.Win32Exception or
            InvalidOperationException or
            ArgumentException)
        {
            status = string.Concat(
                "Chat input state could not be read: ",
                exception.Message);

            return ClientChatInputState.Unknown;
        }
    }

    private static bool TryAddOffset(
        uint address,
        uint offset,
        out uint result)
    {
        if (address > uint.MaxValue - offset)
        {
            result = 0;
            return false;
        }

        result = address + offset;
        return true;
    }

    public bool TryBeginNavigationStateObservation(
        int processId,
        out IDisposable lease,
        out string error)
    {
        lock (this.lockObject)
        {
            if (!this.observedClients.TryGetValue(
                    processId,
                    out var state))
            {
                lease = NullNavigationStateLease.Instance;
                error = "The observed client is not attached";
                return false;
            }

            if (state.NavigationStateObserverReferenceCount == 0)
            {
                this.navigationStateObserver.Forget(processId);

                lock (state.NavigationStateLock)
                {
                    state.NavigationState =
                        ClientNavigationStateObservation.Unavailable(
                            state.ProcessId,
                            state.ProcessStartedAt,
                            sequence: 0,
                            generationSequence: state.NavigationState.GenerationSequence,
                            status: "Navigation control observation is starting",
                            lifecycleState: state.LifecycleState,
                            environment: state.World.Environment,
                            activeSectorNumber: state.World.ActiveSectorNumber,
                            sectorName: state.World.CurrentSectorName);
                }
            }

            state.NavigationStateObserverReferenceCount++;
            state.NextNavigationStatePollAt = DateTimeOffset.UtcNow;

            lease = new NavigationStateLease(
                this,
                processId,
                state.ProcessStartedAt);

            error = "";
            return true;
        }
    }

    public bool TryGetNavigationStateObservation(
        int processId,
        out ClientNavigationStateObservation observation)
    {
        lock (this.lockObject)
        {
            if (!this.observedClients.TryGetValue(
                    processId,
                    out var state))
            {
                observation = null!;
                return false;
            }

            lock (state.NavigationStateLock)
            {
                observation = state.NavigationState;
                return observation.Sequence > 0;
            }
        }
    }

    private void ReleaseNavigationStateObservation(
        int processId,
        DateTimeOffset processStartedAt)
    {
        lock (this.lockObject)
        {
            if (!this.observedClients.TryGetValue(
                    processId,
                    out var state) ||
                state.ProcessStartedAt != processStartedAt ||
                state.NavigationStateObserverReferenceCount <= 0)
            {
                return;
            }

            state.NavigationStateObserverReferenceCount--;

            if (state.NavigationStateObserverReferenceCount != 0)
            {
                return;
            }

            lock (state.NavigationStateLock)
            {
                state.NavigationState =
                    ClientNavigationStateObservation.Unavailable(
                        state.ProcessId,
                        state.ProcessStartedAt,
                        sequence: 0,
                        generationSequence: state.NavigationState.GenerationSequence,
                        status: "Navigation control observation is inactive",
                        lifecycleState: state.LifecycleState,
                        environment: state.World.Environment,
                        activeSectorNumber: state.World.ActiveSectorNumber,
                        sectorName: state.World.CurrentSectorName);
            }
        }
    }

    public bool TryReadNavigation(
        int processId,
        uint? expectedSectorId,
        out ClientNavigationObservation navigation,
        out string error)
    {
        navigation = ClientNavigationObservation.Unavailable(
            "Fresh navigation state is unavailable");
        error = "";

        DateTimeOffset processStartedAt;
        uint moduleBaseAddress;
        uint clientContextAddress;
        uint currentClientTime;
        ClientWorldObservation world;

        lock (this.lockObject)
        {
            if (!this.observedClients.TryGetValue(
                    processId,
                    out var state))
            {
                error = "The observed client is not attached";
                return false;
            }

            if (state.LoadingOrTransitionFlag != 0)
            {
                error = "The client is transitioning";
                return false;
            }

            if (state.LifecycleState !=
                    ClientLifecycleState.InGame ||
                !state.HasDirectClientState ||
                state.ModuleBaseAddress == 0 ||
                state.ClientContextAddress == 0 ||
                !state.World.IsAvailable ||
                state.World.Environment !=
                    ClientWorldEnvironment.Space ||
                state.World.ActiveSectorNumber == 0)
            {
                error =
                    "Fresh navigation state is unavailable for this client";

                return false;
            }

            if (expectedSectorId.HasValue &&
                expectedSectorId.Value != 0 &&
                state.World.ActiveSectorNumber !=
                    expectedSectorId.Value)
            {
                error =
                    "The client changed sector before the navigation read";

                return false;
            }

            processStartedAt = state.ProcessStartedAt;
            moduleBaseAddress = state.ModuleBaseAddress;
            clientContextAddress = state.ClientContextAddress;
            currentClientTime = state.CurrentClientTime;
            world = state.World;
        }

        try
        {
            lock (this.directNavigationReadLock)
            {
                using var memory =
                    ProcessMemoryReader.Open(processId);

                var probeState = new ObservedClientState(
                    processId,
                    processStartedAt)
                {
                    ModuleBaseAddress = moduleBaseAddress,
                    ClientContextAddress = clientContextAddress,
                    HasDirectClientState = true,
                    CurrentClientTime = currentClientTime,
                    LoadingOrTransitionFlag = 0,
                    LifecycleState = ClientLifecycleState.InGame,
                    World = world,
                };

                this.directNavigationObserver.Refresh(
                    memory,
                    probeState);

                navigation = probeState.Navigation;
            }

            if (!navigation.IsAvailable)
            {
                error = string.IsNullOrWhiteSpace(
                        navigation.Status)
                    ? "Fresh navigation state is unavailable"
                    : navigation.Status;

                return false;
            }

            if (expectedSectorId.HasValue &&
                expectedSectorId.Value != 0 &&
                navigation.ActiveSectorNumber != 0 &&
                navigation.ActiveSectorNumber !=
                    expectedSectorId.Value)
            {
                error =
                    "Fresh navigation state belongs to another sector";

                return false;
            }

            return true;
        }
        catch (Win32Exception ex)
        {
            error = string.Concat(
                "Could not read fresh navigation state: Win32 ",
                ex.NativeErrorCode.ToString(
                    CultureInfo.InvariantCulture));

            return false;
        }
        catch (Exception ex)
        {
            error = string.Concat(
                "Could not read fresh navigation state: ",
                ex.Message);

            return false;
        }
    }

    public bool TryReadCurrentTargetObjectId(
        int processId,
        uint? expectedSectorId,
        out bool hasTarget,
        out uint objectId,
        out string error)
    {
        hasTarget = false;
        objectId = 0;
        error = "";

        uint clientContextAddress;

        lock (this.lockObject)
        {
            if (!this.observedClients.TryGetValue(
                    processId,
                    out var state))
            {
                error = "The observed client is not attached";
                return false;
            }

            if (state.LoadingOrTransitionFlag != 0)
            {
                error = "The client is transitioning";
                return false;
            }

            if (state.LifecycleState !=
                ClientLifecycleState.InGame ||
                state.ClientContextAddress == 0)
            {
                error =
                    "Current target state is unavailable for this client";

                return false;
            }

            if (expectedSectorId.HasValue &&
                expectedSectorId.Value != 0 &&
                state.World.IsAvailable &&
                state.World.ActiveSectorNumber !=
                    expectedSectorId.Value)
            {
                error =
                    "The client changed sector before the target read";

                return false;
            }

            clientContextAddress =
                state.ClientContextAddress;
        }

        try
        {
            using var memory =
                ProcessMemoryReader.Open(processId);

            if (!this.objectResolver.TryReadCurrentTargetSource(
                    memory,
                    clientContextAddress,
                    out var source,
                    out error,
                    out _))
            {
                return false;
            }

            if (ClientObjectResolver.IsAbsentObjectId(
                    source.TargetObjectId))
            {
                return true;
            }

            hasTarget = true;
            objectId = source.TargetObjectId;
            return true;
        }
        catch (Win32Exception ex)
        {
            error = string.Concat(
                "Could not read current target state: Win32 ",
                ex.NativeErrorCode.ToString(
                    CultureInfo.InvariantCulture));

            return false;
        }
        catch (Exception ex)
        {
            error = string.Concat(
                "Could not read current target state: ",
                ex.Message);

            return false;
        }
    }

    public bool TryReadCurrentTargetInteraction(
        int processId,
        uint? expectedSectorId,
        out ClientTargetInteractionObservation interaction,
        out string error)
    {
        interaction =
            ClientTargetInteractionObservation.Unavailable(
                "Current target interaction is unavailable");

        error = "";

        DateTimeOffset processStartedAt;
        uint moduleBaseAddress;
        uint clientContextAddress;

        lock (this.lockObject)
        {
            if (!this.observedClients.TryGetValue(
                    processId,
                    out var state))
            {
                error = "The observed client is not attached";
                return false;
            }

            if (state.LoadingOrTransitionFlag != 0)
            {
                error = "The client is transitioning";
                return false;
            }

            if (state.LifecycleState !=
                    ClientLifecycleState.InGame ||
                !state.HasDirectClientState ||
                state.ModuleBaseAddress == 0 ||
                state.ClientContextAddress == 0)
            {
                error =
                    "Current target interaction is unavailable for this client";

                return false;
            }

            if (expectedSectorId.HasValue &&
                expectedSectorId.Value != 0 &&
                state.World.IsAvailable &&
                state.World.ActiveSectorNumber !=
                    expectedSectorId.Value)
            {
                error =
                    "The client changed sector before the interaction read";

                return false;
            }

            processStartedAt = state.ProcessStartedAt;
            moduleBaseAddress = state.ModuleBaseAddress;
            clientContextAddress = state.ClientContextAddress;
        }

        try
        {
            using var memory =
                ProcessMemoryReader.Open(processId);

            var probeState = new ObservedClientState(
                processId,
                processStartedAt)
            {
                ModuleBaseAddress = moduleBaseAddress,
                ClientContextAddress = clientContextAddress,
                HasDirectClientState = true,
                LoadingOrTransitionFlag = 0,
                Target = ClientTargetObservation.Unavailable(
                    "Fresh target-interaction read"),
            };

            this.targetInteractionObserver.Refresh(
                memory,
                probeState);

            interaction = probeState.TargetInteraction;

            if (!interaction.IsAvailable)
            {
                error = string.IsNullOrWhiteSpace(
                        interaction.Status)
                    ? "Current target interaction is unavailable"
                    : interaction.Status;

                return false;
            }

            return true;
        }
        catch (Win32Exception ex)
        {
            error = string.Concat(
                "Could not read current target interaction: Win32 ",
                ex.NativeErrorCode.ToString(
                    CultureInfo.InvariantCulture));

            return false;
        }
        catch (Exception ex)
        {
            error = string.Concat(
                "Could not read current target interaction: ",
                ex.Message);

            return false;
        }
    }

    public bool TryReadNearbyTargetActionState(
        int processId,
        uint objectId,
        uint? expectedSectorId,
        out ClientNearbyTargetActionState actionState,
        out string error)
    {
        actionState = ClientNearbyTargetActionState.Unavailable(
            "Nearby target is unavailable",
            objectId,
            expectedSectorId ?? 0);

        error = "";

        uint moduleBaseAddress;
        uint clientContextAddress;
        uint radarSystemAddress;
        ClientGutterRadarTargetObservation? target;

        lock (this.lockObject)
        {
            if (!this.observedClients.TryGetValue(
                    processId,
                    out var state))
            {
                error = "The observed client is not attached";
                return false;
            }

            if (state.LoadingOrTransitionFlag != 0)
            {
                error = "The client is transitioning";
                return false;
            }

            if (state.LifecycleState != ClientLifecycleState.InGame ||
                !state.NearbyTargets.IsAvailable)
            {
                error = "Nearby targets are unavailable for this client";
                return false;
            }

            if (expectedSectorId.HasValue &&
                expectedSectorId.Value != 0 &&
                state.NearbyTargets.ActiveSectorNumber !=
                    expectedSectorId.Value)
            {
                error = "The client changed sector before the action ran";
                return false;
            }

            target = state.NearbyTargets.Targets
                .FirstOrDefault(candidate =>
                    candidate.IsAvailable &&
                    candidate.ObjectId == objectId);

            if (target == null)
            {
                error = "The requested object is no longer a nearby target";
                return false;
            }

            moduleBaseAddress = state.ModuleBaseAddress;
            clientContextAddress = state.ClientContextAddress;
            radarSystemAddress =
                state.NearbyTargets.RadarSystemAddress;
        }

        try
        {
            using var memory = ProcessMemoryReader.Open(processId);

            return this.nearbyTargetActionReader.TryRead(
                memory,
                moduleBaseAddress,
                clientContextAddress,
                radarSystemAddress,
                target,
                out actionState,
                out error);
        }
        catch (Win32Exception ex)
        {
            error = string.Create(CultureInfo.InvariantCulture, $"Could not read nearby-target action state: Win32 {ex.NativeErrorCode}");
            return false;
        }
        catch (Exception ex)
        {
            error = $"Could not read nearby-target action state: {ex.Message}";
            return false;
        }
    }

    public bool TryReadShortcutState(
        int processId,
        out ClientShortcutStateObservation shortcuts,
        out string error)
    {
        uint moduleBaseAddress;
        uint clientContextAddress;
        bool hasDirectClientState;
        ClientLocalPlayerObservation localPlayer;

        lock (this.lockObject)
        {
            if (!this.observedClients.TryGetValue(
                    processId,
                    out var state))
            {
                shortcuts = ClientShortcutStateObservation.Unavailable(
                    "The observed client is not attached");
                error = shortcuts.Status;
                return false;
            }

            moduleBaseAddress = state.ModuleBaseAddress;
            clientContextAddress = state.ClientContextAddress;
            hasDirectClientState = state.HasDirectClientState;
            localPlayer = state.LocalPlayer;
        }

        try
        {
            using var memory = ProcessMemoryReader.Open(processId);

            shortcuts = this.shortcutBarObserver.Observe(
                memory,
                moduleBaseAddress,
                clientContextAddress,
                hasDirectClientState,
                localPlayer);

            error = shortcuts.Status;
            return shortcuts.IsAvailable;
        }
        catch (Win32Exception ex)
        {
            error = string.Create(
                CultureInfo.InvariantCulture,
                $"Could not read shortcut state: Win32 {ex.NativeErrorCode}");
            shortcuts = ClientShortcutStateObservation.Unavailable(error);
            return false;
        }
        catch (Exception ex)
        {
            error = $"Could not read shortcut state: {ex.Message}";
            shortcuts = ClientShortcutStateObservation.Unavailable(error);
            return false;
        }
    }

    public IReadOnlyList<ClientObservationSnapshot> GetSnapshots()
    {
        lock (this.lockObject)
        {
            return
            [
                .. this.observedClients.Values
                    .Where(state => state.Snapshot != null)
                    .OrderBy(state => state.ProcessId)
                    .Select(state => state.Snapshot!),
            ];
        }
    }

    public void Dispose()
    {
        CancellationTokenSource? coordinatorCancellationTokenSource;
        Task? coordinatorPollingTask;
        Task? navigationPollingTask;
        Task? presentationPollingTask;
        Task? dismantlePacingPollingTask;
        Task? eventDispatchTask;
        Task? realtimeEventDispatchTask;
        int[] processIds;

        lock (this.lockObject)
        {
            coordinatorCancellationTokenSource =
                this.cancellationTokenSource;

            coordinatorPollingTask = this.pollingTask;
            navigationPollingTask =
                this.navigationStatePollingTask;
            presentationPollingTask =
                this.panelPresentationPollingTask;
            dismantlePacingPollingTask =
                this.dismantlePacingPollingTask;
            eventDispatchTask = this.eventDispatchTask;
            realtimeEventDispatchTask = this.realtimeEventDispatchTask;

            this.cancellationTokenSource = null;
            this.pollingTask = null;
            this.navigationStatePollingTask = null;
            this.panelPresentationPollingTask = null;
            this.dismantlePacingPollingTask = null;
            this.eventDispatchTask = null;
            this.realtimeEventDispatchTask = null;

            processIds = [.. this.observedClients.Keys];
            this.observedClients.Clear();
        }

        coordinatorCancellationTokenSource?.Cancel();

        try
        {
            coordinatorPollingTask?.Wait(TimeSpan.FromSeconds(2));
            navigationPollingTask?.Wait(TimeSpan.FromSeconds(2));
            presentationPollingTask?.Wait(
                TimeSpan.FromSeconds(2));
            dismantlePacingPollingTask?.Wait(
                TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Best-effort shutdown. Observation must never block app exit.
        }

        // No sampler can enqueue after the polling tasks have stopped. Let the
        // ordered event worker drain anything already captured before exit so
        // the last crafting/vendor event is not discarded during shutdown.
        this.eventDispatchChannel.Writer.TryComplete();
        this.realtimeEventDispatchChannel.Writer.TryComplete();

        try
        {
            eventDispatchTask?.Wait(TimeSpan.FromSeconds(2));
            realtimeEventDispatchTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Best-effort drain. Application shutdown still owns the deadline.
        }

        foreach (var processId in processIds)
        {
            this.ForgetProcessCaches(processId);
        }

        this.combatObserver.Dispose();
        coordinatorCancellationTokenSource?.Dispose();
    }

    private void QueueSnapshotChanged(
        ClientObservationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        this.QueueEventDispatch(() =>
            this.SnapshotChanged?.Invoke(
                this,
                new ClientObservationSnapshotChangedEventArgs(snapshot)));
    }

    private void QueueCraftingActivityRealtimeChanged(
        int processId,
        ClientManufacturingActivityObservation activity)
    {
        ArgumentNullException.ThrowIfNull(activity);

        this.QueueRealtimeEventDispatch(() =>
            this.CraftingActivityRealtimeChanged?.Invoke(
                this,
                new ClientCraftingActivityRealtimeChangedEventArgs(
                    processId,
                    activity)));
    }

    private void QueueEventDispatch(Action dispatch)
    {
        ArgumentNullException.ThrowIfNull(dispatch);

        // Unbounded single-reader channel: TryWrite only fails after the
        // writer is completed. We intentionally never complete it during the
        // coordinator lifetime; cancellation stops the reader on disposal.
        _ = this.eventDispatchChannel.Writer.TryWrite(dispatch);
    }

    private async Task EventDispatchLoopAsync()
    {
        await foreach (var dispatch in this.eventDispatchChannel.Reader
                           .ReadAllAsync()
                           .ConfigureAwait(false))
        {
            try
            {
                dispatch();
            }
            catch (Exception ex)
            {
                // A consumer bug must not kill observation delivery for every
                // client. Preserve best-effort delivery and leave a debugger
                // breadcrumb.
                Debug.WriteLine(
                    $"Observation event consumer failed: {ex}");
            }
        }
    }

    private void QueueRealtimeEventDispatch(Action dispatch)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        _ = this.realtimeEventDispatchChannel.Writer.TryWrite(dispatch);
    }

    private async Task RealtimeEventDispatchLoopAsync()
    {
        await foreach (var dispatch in this.realtimeEventDispatchChannel.Reader
                           .ReadAllAsync()
                           .ConfigureAwait(false))
        {
            try
            {
                dispatch();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"Realtime observation event consumer failed: {ex}");
            }
        }
    }

    private async Task PollLoopAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(
                schedulerInterval);

            while (await timer
                       .WaitForNextTickAsync(cancellationToken)
                       .ConfigureAwait(false))
            {
                this.PollDueClients();
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    private async Task NavigationStatePollLoopAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(
                navigationStatePollInterval);

            while (await timer
                       .WaitForNextTickAsync(cancellationToken)
                       .ConfigureAwait(false))
            {
                this.PollDueNavigationStates();
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    private async Task PanelPresentationPollLoopAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(
                panelPresentationSchedulerInterval);

            while (await timer
                       .WaitForNextTickAsync(cancellationToken)
                       .ConfigureAwait(false))
            {
                this.PollDuePanelPresentations();
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    private async Task DismantlePacingPollLoopAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(
                dismantlePacingPollInterval);

            while (await timer
                       .WaitForNextTickAsync(cancellationToken)
                       .ConfigureAwait(false))
            {
                this.PollDismantlePacingStates();
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    private void PollDismantlePacingStates()
    {
        ObservedClientState[] states;

        lock (this.lockObject)
        {
            states =
            [
                .. this.observedClients.Values,
            ];
        }

        foreach (var state in states)
        {
            if (!this.IsCurrentObservedState(state))
            {
                continue;
            }

            this.PollDismantlePacing(state);
        }
    }

    private void PollDismantlePacing(ObservedClientState state)
    {
        var previous = state.RealtimeManufacturingActivity;
        ClientManufacturingActivityObservation current;
        var observedAt = DateTimeOffset.UtcNow;

        if (state.LifecycleState != ClientLifecycleState.InGame ||
            !state.HasDirectClientState ||
            state.ClientContextAddress == 0 ||
            state.LoadingOrTransitionFlag != 0)
        {
            current = ClientManufacturingActivityObservation.Unavailable(
                "Analyze pacing is inactive",
                observedAt);
        }
        else
        {
            try
            {
                using var process = Process.GetProcessById(state.ProcessId);
                if (process.HasExited ||
                    new DateTimeOffset(
                        process.StartTime.ToUniversalTime()) !=
                    state.ProcessStartedAt)
                {
                    this.Detach(state.ProcessId);
                    return;
                }

                using var memory = ProcessMemoryReader.Open(state.ProcessId);
                current = this.recipeMappingObserver.ObserveAnalyzeUiPacing(
                    memory,
                    state,
                    observedAt);
            }
            catch (ArgumentException)
            {
                this.Detach(state.ProcessId);
                return;
            }
            catch (InvalidOperationException)
            {
                this.Detach(state.ProcessId);
                return;
            }
            catch (Win32Exception)
            {
                current = ClientManufacturingActivityObservation.Unavailable(
                    "Analyze pacing read failed",
                    observedAt);
            }
            catch
            {
                current = ClientManufacturingActivityObservation.Unavailable(
                    "Analyze pacing read failed",
                    observedAt);
            }
        }

        if (!this.IsCurrentObservedState(state))
        {
            return;
        }

        state.RealtimeManufacturingActivity = current;
        this.QueueCraftingRealtimeIfChanged(
            state.ProcessId,
            previous,
            current);
    }

    private void PollDueNavigationStates()
    {
        ObservedClientState[] states;
        var now = DateTimeOffset.UtcNow;

        lock (this.lockObject)
        {
            states =
            [
                .. this.observedClients.Values
                    .Where(state =>
                        state.NavigationStateObserverReferenceCount > 0 &&
                        state.NextNavigationStatePollAt <= now),
            ];

            foreach (var state in states)
            {
                state.NextNavigationStatePollAt =
                    now + navigationStatePollInterval;
            }
        }

        foreach (var state in states)
        {
            if (this.IsCurrentObservedState(state))
            {
                this.PollNavigationState(state);
            }
        }
    }

    private void PollDuePanelPresentations()
    {
        ObservedClientState[] states;

        lock (this.lockObject)
        {
            states =
            [
                .. this.observedClients.Values,
            ];
        }

        foreach (var state in states)
        {
            if (!this.IsCurrentObservedState(state))
            {
                continue;
            }

            if (state.NextPanelPresentationPollAt <=
                DateTimeOffset.UtcNow)
            {
                this.PollPanelPresentation(state);
            }
        }
    }

    private void PollDueClients()
    {
        ObservedClientState[] states;

        lock (this.lockObject)
        {
            states =
            [
                .. this.observedClients.Values,
            ];
        }

        foreach (var state in states)
        {
            if (!this.IsCurrentObservedState(state))
            {
                continue;
            }

            var now = DateTimeOffset.UtcNow;

            if (state.NextPollAt <= now)
            {
                this.PollClient(state);
            }

            if (this.IsCurrentObservedState(state) &&
                state.NextGroupSkillsTargetPollAt <= now)
            {
                this.PollGroupSkillsTargets(state);
            }

            if (this.IsCurrentObservedState(state) &&
                state.NextLootTractorPollAt <= now)
            {
                this.PollLootTractor(state);
            }

            if (this.IsCurrentObservedState(state) &&
                state.NextCraftingActivityPollAt <= now)
            {
                this.PollCraftingActivity(state);
            }

            if (this.IsCurrentObservedState(state) &&
                state.NextVendorTransactionPollAt <= now)
            {
                this.PollVendorTransactionState(state);
            }

            if (this.IsCurrentObservedState(state) &&
                state.NextTooltipHoverPollAt <= now)
            {
                this.PollTooltipHover(state);
            }

            if (this.IsCurrentObservedState(state) &&
                state.NextTooltipItemPollAt <= now)
            {
                this.PollTooltipItemState(state);
            }
        }
    }

    private static ClientTooltipItemRefreshScope
        ResolveTooltipItemRefreshScope(
            ClientTooltipHoverObservation hover)
    {
        if (!hover.IsAvailable ||
            !hover.HasActiveGadget ||
            string.IsNullOrWhiteSpace(hover.ControlName))
        {
            return ClientTooltipItemRefreshScope.None;
        }

        var controlName = hover.ControlName;

        if (controlName.StartsWith(
                "UI_SCUT_SHORTCUTS_",
                StringComparison.Ordinal))
        {
            return ClientTooltipItemRefreshScope.LocalPlayer |
                ClientTooltipItemRefreshScope.Shortcuts;
        }

        if (controlName.StartsWith(
                "PLAYER_VAULT_",
                StringComparison.Ordinal))
        {
            return ClientTooltipItemRefreshScope.SecureInventory;
        }

        if (controlName.StartsWith(
                "HULK_CARGO_",
                StringComparison.Ordinal))
        {
            return ClientTooltipItemRefreshScope.Target;
        }

        if (controlName.StartsWith(
                "PDA_CARGO_",
                StringComparison.Ordinal) ||
            controlName.StartsWith(
                "WEAPON_SLOT_",
                StringComparison.Ordinal) ||
            controlName.StartsWith(
                "SYSTEM_SLOT_",
                StringComparison.Ordinal) ||
            string.Equals(
                controlName,
                "SHIELD_SLOT_0",
                StringComparison.Ordinal) ||
            string.Equals(
                controlName,
                "POWER_SLOT_0",
                StringComparison.Ordinal) ||
            string.Equals(
                controlName,
                "ENGINE_SLOT_0",
                StringComparison.Ordinal))
        {
            return ClientTooltipItemRefreshScope.LocalPlayer;
        }

        return ClientTooltipItemRefreshScope.None;
    }

    private void PollTooltipItemState(
        ObservedClientState state)
    {
        ClientTooltipItemRefreshScope scope;

        lock (this.lockObject)
        {
            if (!this.observedClients.TryGetValue(
                    state.ProcessId,
                    out var currentState) ||
                !ReferenceEquals(currentState, state))
            {
                return;
            }

            scope = state.PendingTooltipItemRefreshScope;
            state.PendingTooltipItemRefreshScope =
                ClientTooltipItemRefreshScope.None;
            state.NextTooltipItemPollAt = DateTimeOffset.MaxValue;
        }

        if (scope == ClientTooltipItemRefreshScope.None ||
            state.LifecycleState != ClientLifecycleState.InGame ||
            !state.HasDirectClientState ||
            state.ModuleBaseAddress == 0 ||
            state.ClientContextAddress == 0 ||
            state.LoadingOrTransitionFlag != 0)
        {
            return;
        }

        ClientObservationSnapshot? snapshot = null;

        try
        {
            using var process = Process.GetProcessById(
                state.ProcessId);

            if (process.HasExited ||
                new DateTimeOffset(
                    process.StartTime.ToUniversalTime()) !=
                state.ProcessStartedAt)
            {
                this.Detach(state.ProcessId);
                return;
            }

            using var memory = ProcessMemoryReader.Open(
                state.ProcessId);

            if ((scope & ClientTooltipItemRefreshScope.LocalPlayer) != 0)
            {
                this.localPlayerObserver.Refresh(
                    memory,
                    state);
            }

            if ((scope & ClientTooltipItemRefreshScope.SecureInventory) != 0)
            {
                this.secureInventoryObserver.Refresh(
                    memory,
                    state);
            }

            if ((scope & ClientTooltipItemRefreshScope.Target) != 0)
            {
                this.targetObserver.Refresh(
                    memory,
                    state);
            }

            if ((scope & ClientTooltipItemRefreshScope.Shortcuts) != 0)
            {
                this.shortcutBarObserver.Refresh(
                    memory,
                    state);
            }

            state.SnapshotSequence++;
            snapshot = this.CreateSnapshot(state);

            lock (this.lockObject)
            {
                if (!this.observedClients.TryGetValue(
                        state.ProcessId,
                        out var currentState) ||
                    !ReferenceEquals(currentState, state))
                {
                    return;
                }

                state.Snapshot = snapshot;
            }
        }
        catch (ArgumentException)
        {
            this.Detach(state.ProcessId);
            return;
        }
        catch (InvalidOperationException)
        {
            this.Detach(state.ProcessId);
            return;
        }
        catch (Win32Exception)
        {
            // Best effort. The ordinary observation lanes remain authoritative
            // and will retry on their established cadence.
        }
        catch
        {
            // A hover-triggered refresh must never destabilize the main loop.
        }

        if (snapshot != null)
        {
            this.QueueSnapshotChanged(snapshot);
        }
    }

    private void PollTooltipHover(
        ObservedClientState state)
    {
        var previous = state.TooltipHover;
        ClientTooltipHoverObservation observed;
        var observedAt = DateTimeOffset.UtcNow;

        try
        {
            if (state.LifecycleState != ClientLifecycleState.InGame ||
                !state.HasDirectClientState ||
                state.ModuleBaseAddress == 0 ||
                state.ClientContextAddress == 0 ||
                state.LoadingOrTransitionFlag != 0)
            {
                observed = ClientTooltipHoverObservation.Unavailable(
                    "No active gameplay session");
            }
            else
            {
                using var process = Process.GetProcessById(
                    state.ProcessId);

                if (process.HasExited ||
                    new DateTimeOffset(
                        process.StartTime.ToUniversalTime()) !=
                    state.ProcessStartedAt)
                {
                    this.Detach(state.ProcessId);
                    return;
                }

                using var memory = ProcessMemoryReader.Open(
                    state.ProcessId);

                observed = this.tooltipHoverObserver.Observe(
                    memory,
                    state.ModuleBaseAddress,
                    state.ClientContextAddress);
            }
        }
        catch (ArgumentException)
        {
            this.Detach(state.ProcessId);
            return;
        }
        catch (InvalidOperationException)
        {
            this.Detach(state.ProcessId);
            return;
        }
        catch (Win32Exception ex)
        {
            observed = ClientTooltipHoverObservation.Unavailable(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Tooltip hover read failed: Win32 {ex.NativeErrorCode}"));
        }
        catch (Exception ex)
        {
            observed = ClientTooltipHoverObservation.Unavailable(
                string.Concat(
                    "Tooltip hover read failed: ",
                    ex.Message));
        }

        if (!this.IsCurrentObservedState(state))
        {
            return;
        }

        state.TooltipHover = observed;
        state.NextTooltipHoverPollAt =
            observed.HasActiveGadget
                ? observedAt + tooltipHoverActivePollInterval
                : observedAt + tooltipHoverIdlePollInterval;

        if (!HasMeaningfulTooltipHoverChange(
                previous,
                observed))
        {
            return;
        }

        this.TooltipHoverChanged?.Invoke(
            this,
            new ClientTooltipHoverChangedEventArgs(
                state.ProcessId,
                previous,
                observed,
                observedAt));
    }

    private static bool HasMeaningfulTooltipHoverChange(
        ClientTooltipHoverObservation previous,
        ClientTooltipHoverObservation current)
    {
        return previous.IsAvailable != current.IsAvailable ||
            previous.ViewKind != current.ViewKind ||
            previous.ActiveGadgetAddress != current.ActiveGadgetAddress ||
            previous.ActiveGadgetVTableRva != current.ActiveGadgetVTableRva ||
            previous.IsDisplayed != current.IsDisplayed ||
            !string.Equals(
                previous.ControlName,
                current.ControlName,
                StringComparison.Ordinal) ||
            !string.Equals(
                previous.NativeTooltipText,
                current.NativeTooltipText,
                StringComparison.Ordinal);
    }

    private void PollNavigationState(
        ObservedClientState state)
    {
        ClientNavigationStateObservation previous;
        long sequence;

        lock (state.NavigationStateLock)
        {
            previous = state.NavigationState;
            sequence = ++state.NavigationStateSequence;
        }

        ClientNavigationStateObservation observed;
        var processId = state.ProcessId;
        var processStartedAt = state.ProcessStartedAt;
        var moduleBaseAddress = state.ModuleBaseAddress;
        var clientContextAddress = state.ClientContextAddress;
        var lifecycleState = state.LifecycleState;
        var world = state.World;
        var target = state.Target;
        var targetObservedAt = state.LastFeatureObservedAt;

        try
        {
            using var process = Process.GetProcessById(
                processId);

            if (process.HasExited ||
                new DateTimeOffset(
                    process.StartTime.ToUniversalTime()) !=
                processStartedAt)
            {
                this.Detach(processId);
                return;
            }

            if (moduleBaseAddress == 0 ||
                clientContextAddress == 0)
            {
                observed = ClientNavigationStateObservation.Unavailable(
                    processId,
                    processStartedAt,
                    sequence,
                    previous.GenerationSequence,
                    "Navigation control roots are unavailable",
                    lifecycleState,
                    world.Environment,
                    world.ActiveSectorNumber,
                    world.CurrentSectorName);
            }
            else
            {
                using var memory = ProcessMemoryReader.Open(
                    processId);

                observed = this.navigationStateObserver.Observe(
                    memory,
                    processId,
                    processStartedAt,
                    sequence,
                    moduleBaseAddress,
                    clientContextAddress,
                    lifecycleState,
                    world,
                    target,
                    targetObservedAt,
                    previous);
            }
        }
        catch (ArgumentException)
        {
            this.Detach(processId);
            return;
        }
        catch (InvalidOperationException)
        {
            this.Detach(processId);
            return;
        }
        catch (Win32Exception ex)
        {
            observed = ClientNavigationStateObservation.Unavailable(
                processId,
                processStartedAt,
                sequence,
                previous.GenerationSequence,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Navigation control read failed: Win32 {ex.NativeErrorCode}"),
                lifecycleState,
                world.Environment,
                world.ActiveSectorNumber,
                world.CurrentSectorName);
        }
        catch (Exception ex)
        {
            observed = ClientNavigationStateObservation.Unavailable(
                processId,
                processStartedAt,
                sequence,
                previous.GenerationSequence,
                $"Navigation control read failed: {ex.Message}",
                lifecycleState,
                world.Environment,
                world.ActiveSectorNumber,
                world.CurrentSectorName);
        }

        if (!this.IsCurrentObservedState(state))
        {
            return;
        }

        lock (state.NavigationStateLock)
        {
            state.NavigationState = observed;
        }

        if (HasMeaningfulNavigationStateChange(
                previous,
                observed))
        {
            this.NavigationStateChanged?.Invoke(
                this,
                new ClientNavigationStateChangedEventArgs(
                    previous,
                    observed));
        }
    }

    private static bool HasMeaningfulNavigationStateChange(
        ClientNavigationStateObservation previous,
        ClientNavigationStateObservation current)
    {
        return previous.IsAvailable != current.IsAvailable ||
            previous.GenerationSequence !=
                current.GenerationSequence ||
            previous.ActiveSectorNumber !=
                current.ActiveSectorNumber ||
            previous.SectorNameDirectlyObserved !=
                current.SectorNameDirectlyObserved ||
            !string.Equals(
                previous.SectorName,
                current.SectorName,
                StringComparison.Ordinal) ||
            previous.PresentationMode !=
                current.PresentationMode ||
            previous.Environment != current.Environment ||
            previous.Phase != current.Phase ||
            previous.PrivateWarpState.StateMarker !=
                current.PrivateWarpState.StateMarker ||
            previous.PrivateWarpState.Value !=
                current.PrivateWarpState.Value ||
            previous.GlobalWarpState.StateMarker !=
                current.GlobalWarpState.StateMarker ||
            previous.GlobalWarpState.Value !=
                current.GlobalWarpState.Value ||
            previous.WarpAvailable.StateMarker !=
                current.WarpAvailable.StateMarker ||
            previous.WarpAvailable.Value !=
                current.WarpAvailable.Value ||
            previous.PathBuildStateKnown !=
                current.PathBuildStateKnown ||
            previous.PathBuildBusy != current.PathBuildBusy ||
            previous.SelectedTargetKnown !=
                current.SelectedTargetKnown ||
            previous.SelectedTargetObjectId !=
                current.SelectedTargetObjectId ||
            previous.LockSpeed.StateMarker !=
                current.LockSpeed.StateMarker ||
            previous.LockSpeed.Value != current.LockSpeed.Value ||
            previous.LockOrient.StateMarker !=
                current.LockOrient.StateMarker ||
            previous.LockOrient.Value != current.LockOrient.Value ||
            previous.LastTerminalWarpReason !=
                current.LastTerminalWarpReason;
    }

    private void PollPanelPresentation(
        ObservedClientState state)
    {
        var nextInterval =
            hiddenCharacterInfoPresentationPollInterval;
        ClientMissionPresentationChangedEventArgs?
            missionChangedEvent = null;
        ClientFactionPresentationChangedEventArgs?
            factionChangedEvent = null;
        ClientSkillPresentationChangedEventArgs?
            skillChangedEvent = null;
        ClientInventoryPresentationChangedEventArgs?
            inventoryChangedEvent = null;

        try
        {
            if (state.LifecycleState !=
                    ClientLifecycleState.InGame ||
                !state.HasDirectClientState ||
                state.ModuleBaseAddress == 0 ||
                state.ClientContextAddress == 0)
            {
                return;
            }

            using var process = Process.GetProcessById(
                state.ProcessId);

            if (process.HasExited ||
                new DateTimeOffset(
                    process.StartTime.ToUniversalTime()) !=
                state.ProcessStartedAt)
            {
                this.Detach(state.ProcessId);
                return;
            }

            using var memory = ProcessMemoryReader.Open(
                state.ProcessId);

            ClientPanelPresentationChanges changes;
            ClientPanelPresentationObservation panelPresentation;

            lock (state.PanelPresentationLock)
            {
                changes =
                    this.panelPresentationObserver
                        .RefreshPresentationFast(
                            memory,
                            state);

                panelPresentation =
                    state.PanelPresentation;
            }

            nextInterval =
                panelPresentation.IsCharacterInfoDisplayed ||
                panelPresentation.IsInventoryDisplayed ||
                panelPresentation.IsEquipmentDisplayed
                    ? visibleCharacterInfoPresentationPollInterval
                    : hiddenCharacterInfoPresentationPollInterval;

            var observedAt = DateTimeOffset.UtcNow;

            if ((changes & ClientPanelPresentationChanges.Mission) != 0)
            {
                missionChangedEvent =
                    new ClientMissionPresentationChangedEventArgs(
                        state.ProcessId,
                        panelPresentation,
                        state.LocalPlayer.Missions,
                        observedAt);
            }

            if ((changes & ClientPanelPresentationChanges.Faction) != 0)
            {
                factionChangedEvent =
                    new ClientFactionPresentationChangedEventArgs(
                        state.ProcessId,
                        panelPresentation,
                        state.LocalPlayer,
                        observedAt);
            }

            if ((changes & ClientPanelPresentationChanges.Skill) != 0)
            {
                skillChangedEvent =
                    new ClientSkillPresentationChangedEventArgs(
                        state.ProcessId,
                        panelPresentation,
                        observedAt);
            }

            if ((changes & ClientPanelPresentationChanges.Inventory) != 0)
            {
                inventoryChangedEvent =
                    new ClientInventoryPresentationChangedEventArgs(
                        state.ProcessId,
                        panelPresentation,
                        observedAt);
            }
        }
        catch (ArgumentException)
        {
            this.Detach(state.ProcessId);
            return;
        }
        catch (InvalidOperationException)
        {
            this.Detach(state.ProcessId);
            return;
        }
        catch (Win32Exception)
        {
            // The ordinary lane remains the authority for availability
            // diagnostics. A transient fast-lane read failure should not
            // create a visible error or a retry storm.
        }
        catch
        {
            // Best effort. The next lightweight pass will try again.
        }
        finally
        {
            if (this.IsCurrentObservedState(state))
            {
                state.NextPanelPresentationPollAt =
                    DateTimeOffset.UtcNow + nextInterval;
            }
        }

        if (missionChangedEvent != null)
        {
            this.MissionPresentationChanged?.Invoke(
                this,
                missionChangedEvent);
        }

        if (factionChangedEvent != null)
        {
            this.FactionPresentationChanged?.Invoke(
                this,
                factionChangedEvent);
        }

        if (skillChangedEvent != null)
        {
            this.SkillPresentationChanged?.Invoke(
                this,
                skillChangedEvent);
        }

        if (inventoryChangedEvent != null)
        {
            this.InventoryPresentationChanged?.Invoke(
                this,
                inventoryChangedEvent);
        }
    }

    private void PollGroupSkillsTargets(
        ObservedClientState state)
    {
        ClientObservationSnapshot? snapshot = null;

        try
        {
            if (state.LifecycleState != ClientLifecycleState.InGame ||
                state.ClientContextAddress == 0)
            {
                state.NextGroupSkillsTargetPollAt = DateTimeOffset.MaxValue;
                return;
            }

            using var memory = ProcessMemoryReader.Open(
                state.ProcessId);

            // Target classification depends on current group relations, so keep
            // the same ordering as the ordinary feature lane. Deliberately do
            // not refresh world, looting, navigation or other heavier feature
            // observers from this interaction-speed lane. Shortcut-bar state is
            // included because this overlay is a shortcut surface and users may
            // rearrange bars while it is open.
            this.groupObserver.Refresh(
                memory,
                state);

            this.targetObserver.Refresh(
                memory,
                state);

            this.combatObserver.RefreshTarget(state);

            this.shortcutBarObserver.Refresh(
                memory,
                state);

            state.NextGroupSkillsTargetPollAt = DateTimeOffset.MaxValue;

            state.SnapshotSequence++;
            snapshot = this.CreateSnapshot(state);

            lock (this.lockObject)
            {
                if (!this.observedClients.TryGetValue(
                        state.ProcessId,
                        out var currentState) ||
                    !ReferenceEquals(currentState, state))
                {
                    return;
                }

                state.Snapshot = snapshot;
            }
        }
        catch (ArgumentException)
        {
            this.Detach(state.ProcessId);
            return;
        }
        catch (InvalidOperationException)
        {
            this.Detach(state.ProcessId);
            return;
        }
        catch (Win32Exception ex)
        {
            state.NextGroupSkillsTargetPollAt = DateTimeOffset.MaxValue;
            state.StatusText = string.Concat(
                "Group Skills refresh unavailable: Win32 ",
                ex.NativeErrorCode.ToString(
                    CultureInfo.InvariantCulture));
        }
        catch (Exception ex)
        {
            state.NextGroupSkillsTargetPollAt = DateTimeOffset.MaxValue;
            state.StatusText = string.Concat(
                "Group Skills refresh unavailable: ",
                ex.Message);
        }

        if (snapshot != null)
        {
            this.QueueSnapshotChanged(snapshot);
        }
    }

    private void QueueCraftingRealtimeIfChanged(
        int processId,
        ClientManufacturingActivityObservation previous,
        ClientManufacturingActivityObservation current)
    {
        if (ReferenceEquals(previous, current) ||
            previous.IsAvailable == current.IsAvailable &&
            previous.IsAnalyzePanelActive == current.IsAnalyzePanelActive &&
            previous.Mode == current.Mode &&
            previous.Validity == current.Validity &&
            previous.TargetItemTemplateId == current.TargetItemTemplateId &&
            previous.IsAnalyzeUiPacingAvailable ==
                current.IsAnalyzeUiPacingAvailable &&
            previous.IsAnalyzeUiAttemptInProgress ==
                current.IsAnalyzeUiAttemptInProgress &&
            previous.AnalyzeUiDelayRemainingDeciseconds ==
                current.AnalyzeUiDelayRemainingDeciseconds)
        {
            return;
        }

        this.QueueCraftingActivityRealtimeChanged(processId, current);
    }

    private void PollCraftingActivity(
        ObservedClientState state)
    {
        var previous = state.ManufacturingActivity;

        try
        {
            using var process = Process.GetProcessById(state.ProcessId);

            if (process.HasExited ||
                new DateTimeOffset(
                    process.StartTime.ToUniversalTime()) !=
                state.ProcessStartedAt)
            {
                this.Detach(state.ProcessId);
                return;
            }

            if (state.LifecycleState != ClientLifecycleState.InGame ||
                !state.HasDirectClientState ||
                state.ModuleBaseAddress == 0 ||
                state.ClientContextAddress == 0 ||
                state.LoadingOrTransitionFlag != 0)
            {
                state.NextCraftingActivityPollAt = DateTimeOffset.MaxValue;
                return;
            }

            using var memory = ProcessMemoryReader.Open(state.ProcessId);
            var observedAt = DateTimeOffset.UtcNow;
            var localPlayerLookup =
                this.localPlayerObserver.TryGetCachedAuxDataLookup(
                    state,
                    out var cachedLocalPlayerLookup)
                    ? cachedLocalPlayerLookup
                    : (ClientAuxDataLookupSnapshot?)null;

            this.recipeMappingObserver.RefreshActivity(
                memory,
                state,
                state.ModuleBaseAddress,
                localPlayerLookup,
                observedAt);

            state.NextCraftingActivityPollAt =
                ResolveNextCraftingActivityPollAt(state, observedAt);
        }
        catch (ArgumentException)
        {
            this.Detach(state.ProcessId);
            return;
        }
        catch (InvalidOperationException)
        {
            this.Detach(state.ProcessId);
            return;
        }
        catch (Win32Exception)
        {
            state.NextCraftingActivityPollAt = DateTimeOffset.MaxValue;
            return;
        }
        catch
        {
            state.NextCraftingActivityPollAt = DateTimeOffset.MaxValue;
            return;
        }

        if (ReferenceEquals(previous, state.ManufacturingActivity))
        {
            return;
        }

        state.SnapshotSequence++;
        var snapshot = this.CreateSnapshot(state);

        lock (this.lockObject)
        {
            if (!this.observedClients.TryGetValue(
                    state.ProcessId,
                    out var currentState) ||
                !ReferenceEquals(currentState, state))
            {
                return;
            }

            state.Snapshot = snapshot;
        }

        this.QueueSnapshotChanged(snapshot);
    }

    private void PollVendorTransactionState(
        ObservedClientState state)
    {
        var observedAt = DateTimeOffset.UtcNow;
        var nextInterval = vendorTransactionIdlePollInterval;

        try
        {
            if (state.LifecycleState != ClientLifecycleState.InGame ||
                !state.HasDirectClientState ||
                state.ModuleBaseAddress == 0 ||
                state.ClientContextAddress == 0 ||
                state.LoadingOrTransitionFlag != 0 ||
                !state.World.IsAvailable ||
                state.World.Environment != ClientWorldEnvironment.Starbase)
            {
                state.NextVendorTransactionPollAt =
                    observedAt + vendorTransactionIdlePollInterval;
                return;
            }

            using var memory = ProcessMemoryReader.Open(state.ProcessId);
            if (!IsVendorTradeActiveFast(memory, state))
            {
                state.NextVendorTransactionPollAt =
                    observedAt + vendorTransactionIdlePollInterval;
                return;
            }

            nextInterval = vendorTransactionPollInterval;
            if (!this.localPlayerObserver.RefreshVendorTransactionState(
                    memory,
                    state))
            {
                state.NextVendorTransactionPollAt = observedAt + nextInterval;
                return;
            }
        }
        catch (Win32Exception)
        {
            state.NextVendorTransactionPollAt =
                observedAt + vendorTransactionIdlePollInterval;
            return;
        }
        catch (InvalidOperationException)
        {
            state.NextVendorTransactionPollAt =
                observedAt + vendorTransactionIdlePollInterval;
            return;
        }
        catch
        {
            state.NextVendorTransactionPollAt =
                observedAt + vendorTransactionIdlePollInterval;
            return;
        }

        state.NextVendorTransactionPollAt = observedAt + nextInterval;
        state.SnapshotSequence++;
        var snapshot = this.CreateSnapshot(state);

        lock (this.lockObject)
        {
            if (!this.observedClients.TryGetValue(
                    state.ProcessId,
                    out var currentState) ||
                !ReferenceEquals(currentState, state))
            {
                return;
            }

            state.Snapshot = snapshot;
        }

        this.QueueSnapshotChanged(snapshot);
    }

    private static bool IsVendorTradeActiveFast(
        ProcessMemoryReader memory,
        ObservedClientState state)
    {
        var talkTreeDefinition = ClientStarbaseInteractionCatalog.Panels
            .FirstOrDefault(panel =>
                panel.Kind == ClientStarbasePanelKind.TalkTree);
        var talkTreeAddress = state.StarbaseContext.Diagnostics.Panels
            .FirstOrDefault(panel =>
                panel.Kind == ClientStarbasePanelKind.TalkTree &&
                panel.Address != 0)
            ?.Address ?? 0;

        try
        {
            // Do not wait for the ordinary 500 ms StarbaseContext refresh to
            // notice a newly opened vendor. The StarbaseView interface pointer
            // is already a stable direct reference, so resolve TalkTree from
            // that one field when diagnostics have not populated it yet.
            if (talkTreeAddress == 0 &&
                talkTreeDefinition != null &&
                state.StarbaseContext.StarbaseViewAddress != 0)
            {
                var talkTreePointerAddress = checked(
                    state.StarbaseContext.StarbaseViewAddress +
                    talkTreeDefinition.ViewFieldOffset);

                if (!memory.TryReadUInt32(
                        talkTreePointerAddress,
                        out talkTreeAddress))
                {
                    return false;
                }
            }

            if (talkTreeAddress == 0)
            {
                return state.StarbaseContext.Interaction.Kind ==
                    ClientStarbaseInteractionKind.VendorTrade;
            }

            var activeAddress = checked(
                talkTreeAddress +
                ClientStarbaseInteractionCatalog.PanelActiveFlagOffset);
            var controllerAddress = checked(
                talkTreeAddress +
                ClientStarbaseInteractionCatalog
                    .TalkTreeVendorTradeControllerOffset);

            if (!TryReadByte(memory, activeAddress, out var activeFlag) ||
                !memory.TryReadUInt32(
                    controllerAddress,
                    out var vendorTradeController))
            {
                return false;
            }

            // A non-zero child is the authoritative VendorTrade discriminator.
            // The outer TalkTree active byte can transition a frame earlier or
            // later, so do not require it when the child is already present.
            return vendorTradeController != 0 ||
                   (activeFlag != 0 &&
                    state.StarbaseContext.Interaction.Kind ==
                        ClientStarbaseInteractionKind.VendorTrade);
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static bool TryReadByte(
        ProcessMemoryReader memory,
        uint address,
        out byte value)
    {
        value = 0;

        if (!memory.TryReadBytes(address, 1, out var bytes) ||
            bytes.Length != 1)
        {
            return false;
        }

        value = bytes[0];
        return true;
    }

    private static DateTimeOffset ResolveNextCraftingActivityPollAt(
        ObservedClientState state,
        DateTimeOffset observedAt)
    {
        if (state.ManufacturingActivity.IsAnalyzePanelActive ||
            state.ManufacturingActivity.IsManufacturingPanelActive)
        {
            return observedAt + craftingActivityPollInterval;
        }

        // The panel addresses are discovered by the ordinary starbase observer
        // and remain stable while the starbase UI is resident. Polling their
        // active bytes is deliberately much smaller than refreshing starbase
        // topology or any inventory tree.
        var hasKnownCraftingPanel = state.StarbaseContext.Diagnostics.Panels.Any(
            panel =>
                panel.Address > 0 &&
                panel.Kind is
                    ClientStarbasePanelKind.Analyze or
                    ClientStarbasePanelKind.Manufacturing);
        return hasKnownCraftingPanel
            ? observedAt + craftingActivityIdlePollInterval
            : DateTimeOffset.MaxValue;
    }

    private void PollLootTractor(
        ObservedClientState state)
    {
        var previous = state.LootTractor;
        var nextInterval = lootTractorIdlePollInterval;

        try
        {
            using var process = Process.GetProcessById(
                state.ProcessId);

            if (process.HasExited ||
                new DateTimeOffset(
                    process.StartTime.ToUniversalTime()) !=
                state.ProcessStartedAt)
            {
                this.Detach(state.ProcessId);
                return;
            }

            if (state.LifecycleState != ClientLifecycleState.InGame ||
                !state.HasDirectClientState ||
                state.ModuleBaseAddress == 0 ||
                state.ClientContextAddress == 0 ||
                state.LoadingOrTransitionFlag != 0)
            {
                state.LootTractor =
                    ClientLootTractorObservation.Unavailable(
                        "No active gameplay session");
            }
            else
            {
                using var memory = ProcessMemoryReader.Open(
                    state.ProcessId);

                this.lootTractorObserver.Refresh(
                    memory,
                    state);
            }
        }
        catch (ArgumentException)
        {
            this.Detach(state.ProcessId);
            return;
        }
        catch (InvalidOperationException)
        {
            this.Detach(state.ProcessId);
            return;
        }
        catch (Win32Exception ex)
        {
            state.LootTractor =
                ClientLootTractorObservation.Unavailable(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Loot tractor read failed: Win32 {ex.NativeErrorCode}"));
        }
        catch (Exception ex)
        {
            state.LootTractor =
                ClientLootTractorObservation.Unavailable(
                    string.Concat(
                        "Loot tractor read failed: ",
                        ex.Message));
        }
        finally
        {
            if (state.LootTractor.IsTractoring ||
                state.LootTractor.WasRecentlyCompleted ||
                state.LootTractor.WasRecentlyInterrupted ||
                state.Looting.IsLootPanelDisplayed)
            {
                nextInterval = lootTractorActivePollInterval;
            }

            if (this.IsCurrentObservedState(state))
            {
                state.NextLootTractorPollAt =
                    DateTimeOffset.UtcNow + nextInterval;
            }
        }

        if (!HasMeaningfulLootTractorChange(
                previous,
                state.LootTractor))
        {
            return;
        }

        state.SnapshotSequence++;
        var snapshot = this.CreateSnapshot(state);

        lock (this.lockObject)
        {
            if (!this.observedClients.TryGetValue(
                    state.ProcessId,
                    out var currentState) ||
                !ReferenceEquals(currentState, state))
            {
                return;
            }

            state.Snapshot = snapshot;
        }

        this.QueueSnapshotChanged(snapshot);
    }

    private static bool HasMeaningfulLootTractorChange(
        ClientLootTractorObservation previous,
        ClientLootTractorObservation current)
    {
        return previous.IsAvailable != current.IsAvailable ||
               previous.IsTractoring != current.IsTractoring ||
               previous.WasRecentlyCompleted != current.WasRecentlyCompleted ||
               previous.WasRecentlyInterrupted != current.WasRecentlyInterrupted ||
               previous.TransitionKind != current.TransitionKind ||
               previous.CameraTargetObjectId != current.CameraTargetObjectId ||
               previous.HandlerTractorEffectId != current.HandlerTractorEffectId ||
               !string.Equals(
                   previous.ItemName,
                   current.ItemName,
                   StringComparison.Ordinal) ||
               previous.CompletedAt != current.CompletedAt ||
               !string.Equals(
                   previous.Status,
                   current.Status,
                   StringComparison.Ordinal);
    }

    private bool IsCurrentObservedState(
        ObservedClientState state)
    {
        lock (this.lockObject)
        {
            return this.observedClients.TryGetValue(
                       state.ProcessId,
                       out var current) &&
                   ReferenceEquals(current, state);
        }
    }

    private void PollClient(
        ObservedClientState state)
    {
        IReadOnlyList<ClientChatMessage> messages = [];
        ClientMissionPresentationChangedEventArgs?
            missionPresentationChangedEvent = null;
        ClientFactionPresentationChangedEventArgs?
            factionPresentationChangedEvent = null;
        ClientSkillPresentationChangedEventArgs?
            skillPresentationChangedEvent = null;
        ClientInventoryPresentationChangedEventArgs?
            inventoryPresentationChangedEvent = null;
        var previousLifecycle = state.LifecycleState;

        try
        {
            using var process = Process.GetProcessById(
                state.ProcessId);

            if (process.HasExited ||
                new DateTimeOffset(
                    process.StartTime.ToUniversalTime()) !=
                state.ProcessStartedAt)
            {
                this.Detach(state.ProcessId);
                return;
            }

            this.factionCatalog.EnsureLoaded(
                TryGetProcessExecutablePath(process));

            using var memory = ProcessMemoryReader.Open(
                state.ProcessId);

            var sessionRootSource =
                this.topologyObserver.Refresh(
                    process,
                    memory,
                    state);

            if (state.ClientContextAddress != 0)
            {
                this.sessionObserver.RefreshState(
                    memory,
                    state,
                    sessionRootSource ?? "unknown source");
            }

            var currentLifecycle =
                ClientLifecycleClassifier.Evaluate(state);

            state.LifecycleState = currentLifecycle;

            if (currentLifecycle == ClientLifecycleState.InGame)
            {
                // The ordinary lane keeps the complete panel model fresh for
                // inventory/equipment/vault consumers. Character-panel
                // interaction responsiveness is handled separately by the
                // lightweight presentation lane.
                ClientPanelPresentationObservation
                    previousPanelPresentation;
                ClientPanelPresentationObservation
                    currentPanelPresentation;

                lock (state.PanelPresentationLock)
                {
                    previousPanelPresentation =
                        state.PanelPresentation;

                    this.panelPresentationObserver.Refresh(
                        memory,
                        state);

                    currentPanelPresentation =
                        state.PanelPresentation;
                }

                var panelObservedAt = DateTimeOffset.UtcNow;

                if (ClientPanelPresentationObserver
                    .HasMissionPresentationChanged(
                        previousPanelPresentation,
                        currentPanelPresentation))
                {
                    missionPresentationChangedEvent =
                        new ClientMissionPresentationChangedEventArgs(
                            state.ProcessId,
                            currentPanelPresentation,
                            state.LocalPlayer.Missions,
                            panelObservedAt);
                }

                if (ClientPanelPresentationObserver
                    .HasFactionPresentationChanged(
                        previousPanelPresentation,
                        currentPanelPresentation))
                {
                    factionPresentationChangedEvent =
                        new ClientFactionPresentationChangedEventArgs(
                            state.ProcessId,
                            currentPanelPresentation,
                            state.LocalPlayer,
                            panelObservedAt);
                }

                if (ClientPanelPresentationObserver
                    .HasSkillPresentationChanged(
                        previousPanelPresentation,
                        currentPanelPresentation))
                {
                    skillPresentationChangedEvent =
                        new ClientSkillPresentationChangedEventArgs(
                            state.ProcessId,
                            currentPanelPresentation,
                            panelObservedAt);
                }

                if (ClientPanelPresentationObserver
                    .HasInventoryPresentationChanged(
                        previousPanelPresentation,
                        currentPanelPresentation))
                {
                    inventoryPresentationChangedEvent =
                        new ClientInventoryPresentationChangedEventArgs(
                            state.ProcessId,
                            currentPanelPresentation,
                            panelObservedAt);
                }

                var gameplayObservedAt = DateTimeOffset.UtcNow;

                this.RefreshFeatureLanes(
                    memory,
                    state,
                    gameplayObservedAt);

                // Crafting consumes the starbase panel state refreshed
                // by the feature lane above. Activity remains a fast read on
                // every normal in-game poll; catalogue capture is armed only while
                // Pilot Archive → Crafting Recipes needs it.
                var localPlayerLookup =
                    this.localPlayerObserver.TryGetCachedAuxDataLookup(
                        state,
                        out var cachedLocalPlayerLookup)
                        ? cachedLocalPlayerLookup
                        : (ClientAuxDataLookupSnapshot?)null;

                this.recipeMappingObserver.RefreshActivity(
                    memory,
                    state,
                    state.ModuleBaseAddress,
                    localPlayerLookup,
                    gameplayObservedAt);

                state.NextCraftingActivityPollAt =
                    ResolveNextCraftingActivityPollAt(
                        state,
                        gameplayObservedAt);

                if (state.RecipeMappingCatalogObservationEnabled)
                {
                    this.recipeMappingObserver.RefreshCatalog(
                        memory,
                        state,
                        gameplayObservedAt);
                }
            }
            else
            {
                if (previousLifecycle == ClientLifecycleState.InGame)
                {
                    this.ForgetProcessCaches(state.ProcessId);
                }

                ResetFeatureObservations(
                    state,
                    "No active gameplay session");
            }

            // The combat observer is the one special high-frequency lane, but
            // its topology/context target is still refreshed by this singleton
            // authority. It receives no independent per-client ownership.
            this.combatObserver.RefreshTarget(state);

            if (state.RingAddress != 0)
            {
                messages =
                    this.sessionObserver.ReadNewMessages(
                        memory,
                        state);
            }
        }
        catch (ArgumentException)
        {
            this.Detach(state.ProcessId);
            return;
        }
        catch (InvalidOperationException)
        {
            this.Detach(state.ProcessId);
            return;
        }
        catch (Win32Exception ex)
        {
            state.StatusText = string.Concat(
                "Unavailable: Win32 ",
                ex.NativeErrorCode.ToString(
                    CultureInfo.InvariantCulture));
        }
        catch (Exception ex)
        {
            state.StatusText = string.Concat(
                "Unavailable: ",
                ex.Message);
        }

        var currentObservedLifecycle =
            ClientLifecycleClassifier.Evaluate(state);

        state.LifecycleState = currentObservedLifecycle;
        var nextPollInterval =
            currentObservedLifecycle == ClientLifecycleState.InGame
                ? inGamePollInterval
                : startupPollInterval;

        state.NextPollAt = DateTimeOffset.UtcNow +
            nextPollInterval;

        state.SnapshotSequence++;
        var snapshot = this.CreateSnapshot(state);

        lock (this.lockObject)
        {
            if (!this.observedClients.TryGetValue(
                    state.ProcessId,
                    out var currentState) ||
                !ReferenceEquals(currentState, state))
            {
                return;
            }

            state.Snapshot = snapshot;
        }

        // Preserve the original publication order while keeping subscriber
        // work off the observation scheduler. SnapshotChanged historically ran
        // before the presentation/lifecycle/chat events from this poll.
        this.QueueEventDispatch(() =>
        {
            this.SnapshotChanged?.Invoke(
                this,
                new ClientObservationSnapshotChangedEventArgs(snapshot));

            if (missionPresentationChangedEvent != null)
            {
                this.MissionPresentationChanged?.Invoke(
                    this,
                    missionPresentationChangedEvent);
            }

            if (factionPresentationChangedEvent != null)
            {
                this.FactionPresentationChanged?.Invoke(
                    this,
                    factionPresentationChangedEvent);
            }

            if (skillPresentationChangedEvent != null)
            {
                this.SkillPresentationChanged?.Invoke(
                    this,
                    skillPresentationChangedEvent);
            }

            if (inventoryPresentationChangedEvent != null)
            {
                this.InventoryPresentationChanged?.Invoke(
                    this,
                    inventoryPresentationChangedEvent);
            }

            if (currentObservedLifecycle != previousLifecycle)
            {
                this.LifecycleStateChanged?.Invoke(
                    this,
                    new ClientLifecycleStateChangedEventArgs
                    {
                        ProcessId = state.ProcessId,
                        Previous = previousLifecycle,
                        Current = currentObservedLifecycle,
                        Snapshot = snapshot,
                    });
            }

            foreach (var message in messages)
            {
                this.ChatMessageObserved?.Invoke(
                    this,
                    new ClientChatMessageObservedEventArgs(message));
            }
        });
    }

    private void RefreshFeatureLanes(
        ProcessMemoryReader memory,
        ObservedClientState state,
        DateTimeOffset now)
    {
        if (now >= state.NextFeaturePollAt)
        {
            state.NextFeaturePollAt =
                now + featurePollInterval;

            this.networkTrafficObserver.Refresh(
                memory,
                state);

            this.frameRateObserver.Refresh(
                memory,
                state);

            this.starMapPresentationObserver.Refresh(
                memory,
                state);

            this.lootingObserver.Refresh(
                memory,
                state);

            this.worldObserver.Refresh(
                memory,
                state);

            this.starbaseContextObserver.Refresh(
                memory,
                state);

            this.audioCueObserver.Refresh(
                memory,
                state);

            if (!this.jobTerminalObservers.TryGetValue(
                    state.ProcessId,
                    out var jobTerminalObserver))
            {
                jobTerminalObserver = new ClientJobTerminalObserver();
                this.jobTerminalObservers[state.ProcessId] =
                    jobTerminalObserver;
            }

            try
            {
                state.JobTerminal = jobTerminalObserver.Observe(
                    memory,
                    state.ClientContextAddress,
                    state.ModuleBaseAddress,
                    now);
            }
            catch (Exception exception)
            {
                // This observer is contribution intelligence only. A malformed
                // or transient client-side Jobs panel must never destabilize
                // the general observation loop or any hosted game process.
                this.jobTerminalObservers.Remove(state.ProcessId);
                state.JobTerminal =
                    ClientJobTerminalObservation.Unavailable(
                        string.Concat(
                            "Jobs Terminal observation failed safely: ",
                            exception.Message),
                        state.ClientContextAddress);
                System.Diagnostics.Debug.WriteLine(
                    string.Concat(
                        "[Jobs Terminal observer] ",
                        exception));
            }

            this.navigationObserver.Refresh(
                memory,
                state);

            this.gutterRadarObserver.Refresh(
                memory,
                state);

            this.groupObserver.Refresh(
                memory,
                state);

            // Target relation depends on current GroupInfo, so target refresh
            // deliberately follows group refresh.
            this.targetObserver.Refresh(
                memory,
                state);

            // Target-interaction consistency is cross-checked against the
            // current target observation, so it deliberately follows it.
            this.targetInteractionObserver.Refresh(
                memory,
                state);

            this.shortcutBarObserver.Refresh(
                memory,
                state);

            this.tooltipDelayObserver.Refresh(
                memory,
                state);

            this.manufacturingLabObserver.Refresh(
                memory,
                state);

            state.LastFeatureObservedAt = now;
        }

        var refreshedSlowFeatures = false;

        if (now >= state.NextSlowFeaturePollAt)
        {
            state.NextSlowFeaturePollAt =
                now + slowFeaturePollInterval;

            this.localPlayerObserver.Refresh(
                memory,
                state);

            this.secureInventoryObserver.Refresh(
                memory,
                state);

            this.missionObserver.Refresh(
                memory,
                state);

            this.reputationObserver.Refresh(
                memory,
                state);

            state.LastSlowFeatureObservedAt = now;
            refreshedSlowFeatures = true;
        }

        this.RefreshVendorShoppingLane(
            memory,
            state,
            now,
            refreshedSlowFeatures);
    }

    private void RefreshVendorShoppingLane(
        ProcessMemoryReader memory,
        ObservedClientState state,
        DateTimeOffset now,
        bool refreshedSlowFeatures)
    {
        var vendorTradeActive =
            state.LifecycleState == ClientLifecycleState.InGame &&
            state.LoadingOrTransitionFlag == 0 &&
            state.World.IsAvailable &&
            state.World.Environment == ClientWorldEnvironment.Starbase &&
            state.StarbaseContext.IsAvailable &&
            state.StarbaseContext.Interaction.Kind ==
                ClientStarbaseInteractionKind.VendorTrade;

        if (!vendorTradeActive)
        {
            // Keep the lane primed so the first feature observation that sees
            // Vendor Trade can refresh immediately in the same main poll.
            state.NextVendorShoppingPollAt = now;
            return;
        }

        if (refreshedSlowFeatures)
        {
            state.NextVendorShoppingPollAt =
                now + vendorShoppingPollInterval;
            return;
        }

        if (now < state.NextVendorShoppingPollAt)
        {
            return;
        }

        state.NextVendorShoppingPollAt =
            now + vendorShoppingPollInterval;

        this.localPlayerObserver.RefreshVendorShoppingState(
            memory,
            state);
    }

    private ClientObservationSnapshot CreateSnapshot(
        ObservedClientState state)
    {
        ClientPanelPresentationObservation panelPresentation;
        ClientNavigationStateObservation navigationState;

        lock (state.PanelPresentationLock)
        {
            panelPresentation = state.PanelPresentation;
        }

        lock (state.NavigationStateLock)
        {
            navigationState = state.NavigationState;
        }

        return new ClientObservationSnapshot
        {
            ProcessId = state.ProcessId,
            ProcessStartedAt = state.ProcessStartedAt,
            Sequence = state.SnapshotSequence,
            ObservedAt = DateTimeOffset.UtcNow,
            IsAttached = true,
            IsAvailable = state.ClientContextAddress != 0,
            LifecycleState = state.LifecycleState,
            StatusText = state.StatusText,
            LastMessageAt = state.LastMessageAt,
            HasGameState = state.HasDirectClientState,
            GameStateStatus = state.DirectClientStateStatus,
            CurrentClientTime = state.CurrentClientTime,
            LocalPlayerObjectId = state.LocalPlayerObjectId,
            TargetObjectId = state.TargetObjectId,
            LoadingOrTransitionFlag =
                state.LoadingOrTransitionFlag,
            LocalPlayerAuxDataAddress =
                state.LocalPlayerAuxDataAddress,
            PresentationMode = state.PresentationMode,
            DockingTargetObjectId =
                state.DockingTargetObjectId,
            PendingLandOrDockTargetObjectId =
                state.PendingLandOrDockTargetObjectId,
            LastStateObservedAt =
                state.LastStateObservedAt,
            LastFeatureObservedAt =
                state.LastFeatureObservedAt,
            LastSlowFeatureObservedAt =
                state.LastSlowFeatureObservedAt,
            NetworkTraffic = state.NetworkTraffic,
            FrameRate = state.FrameRate,
            StarMapPresentation = state.StarMapPresentation,
            PanelPresentation = panelPresentation,
            Looting = state.Looting,
            LootTractor = state.LootTractor,
            World = state.World,
            Navigation = state.Navigation,
            NavigationState = navigationState,
            NearbyTargets = state.NearbyTargets,
            StarbaseContext = state.StarbaseContext,
            AudioCue = state.AudioCue,
            JobTerminal = state.JobTerminal,
            LocalPlayer = state.LocalPlayer,
            Group = state.Group,
            Target = state.Target,
            TargetInteraction = state.TargetInteraction,
            Shortcuts = state.Shortcuts,
            TooltipDelay = state.TooltipDelay,
            TooltipHover = state.TooltipHover,
            ProductionRecipe = state.ProductionRecipe,
            ManufacturingActivity = state.ManufacturingActivity,
            ManufacturingCatalog = state.ManufacturingCatalog,
            Combat = this.combatObserver.GetObservation(
                state.ProcessId),
        };
    }

    private void ForgetProcessCaches(
        int processId)
    {
        this.combatObserver.RemoveProcess(processId);
        this.lootTractorObserver.Forget(processId);
        this.starbaseContextObserver.Forget(processId);
        this.localPlayerObserver.Forget(processId);
        this.jobTerminalObservers.Remove(processId);
        this.navigationObserver.Forget(processId);
        this.navigationStateObserver.Forget(processId);

        lock (this.directNavigationReadLock)
        {
            this.directNavigationObserver.Forget(processId);
        }
        this.gutterRadarObserver.Forget(processId);
        this.secureInventoryObserver.Forget(processId);
        this.missionObserver.Forget(processId);
        this.reputationObserver.Forget(processId);
        this.panelPresentationObserver.Forget(processId);
        this.tooltipDelayObserver.Forget(processId);
        this.recipeMappingObserver.Forget(processId);
        this.chatChannelOptionsReader.Forget(processId);
        this.chatColorOptionsReader.Forget(processId);
    }

    private static void ResetFeatureObservations(
        ObservedClientState state,
        string status)
    {
        state.NetworkTraffic =
            ClientNetworkTrafficObservation.Unavailable(status);

        state.FrameRate =
            ClientFrameRateObservation.Unavailable(status);

        state.StarMapPresentation =
            ClientStarMapPresentationObservation.Unavailable(status);

        lock (state.PanelPresentationLock)
        {
            state.PanelPresentation =
                ClientPanelPresentationObservation.Unavailable(status);
        }

        state.Looting =
            ClientLootingObservation.Unavailable(status);

        state.LootTractor =
            ClientLootTractorObservation.Unavailable(status);

        state.World =
            ClientWorldObservation.Unavailable(status);

        state.Navigation =
            ClientNavigationObservation.Unavailable(status);

        state.NearbyTargets =
            ClientGutterRadarObservation.Unavailable(status);

        state.StarbaseContext =
            ClientStarbaseContextObservation.Unavailable(status);

        state.AudioCue =
            ClientAudioCueObservation.Unavailable(status);

        state.JobTerminal =
            ClientJobTerminalObservation.Unavailable(status);

        state.LocalPlayer =
            ClientLocalPlayerObservation.Unavailable(status);

        state.Group =
            ClientGroupObservation.Unavailable(status);

        state.Target =
            ClientTargetObservation.Unavailable(status);

        state.TargetInteraction =
            ClientTargetInteractionObservation.Unavailable(status);

        state.Shortcuts =
            ClientShortcutStateObservation.Unavailable(status);

        state.TooltipDelay =
            ClientTooltipDelayObservation.Unavailable(status);

        state.TooltipHover =
            ClientTooltipHoverObservation.Unavailable(status);

        state.ManufacturingActivity =
            ClientManufacturingActivityObservation.Unavailable(status);
        state.RealtimeManufacturingActivity =
            ClientManufacturingActivityObservation.Unavailable(status);

        state.ManufacturingCatalog =
            ClientManufacturingCatalogObservation.Unavailable(status);

        state.LastFeatureObservedAt = null;
        state.LastSlowFeatureObservedAt = null;
        state.NextPanelPresentationPollAt = DateTimeOffset.UtcNow;
        state.NextFeaturePollAt = DateTimeOffset.UtcNow;
        state.NextLootTractorPollAt = DateTimeOffset.UtcNow;
        state.NextCraftingActivityPollAt = DateTimeOffset.MaxValue;
        state.NextTooltipHoverPollAt = DateTimeOffset.UtcNow;
        state.NextTooltipItemPollAt = DateTimeOffset.MaxValue;
        state.PendingTooltipItemRefreshScope =
            ClientTooltipItemRefreshScope.None;
        state.NextGroupSkillsTargetPollAt = DateTimeOffset.MaxValue;
        state.NextSlowFeaturePollAt = DateTimeOffset.UtcNow;
        state.NextVendorShoppingPollAt = DateTimeOffset.UtcNow;
    }

    private sealed class NavigationStateLease(
        ClientObservationCoordinator owner,
        int processId,
        DateTimeOffset processStartedAt) : IDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(
                    ref this.disposed,
                    1) != 0)
            {
                return;
            }

            owner.ReleaseNavigationStateObservation(
                processId,
                processStartedAt);
        }
    }

    private sealed class NullNavigationStateLease : IDisposable
    {
        public static NullNavigationStateLease Instance { get; } = new();

        private NullNavigationStateLease()
        {
        }

        public void Dispose()
        {
        }
    }

    private static string? TryGetProcessExecutablePath(
        Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }
}
