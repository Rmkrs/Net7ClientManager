namespace Net7ClientManager.Observations.Observers;

using System.Diagnostics;
using Net7ClientManager.Observations.Models;

/// <summary>
/// Process-wide, read-only combat packet coordinator.
///
/// One scheduler owns every attached client so high-frequency combat polling
/// consumes at most one worker thread rather than one busy thread per client.
/// MusicManager.CurrentMusicType and live equipped-weapon ItemState transitions
/// are early arming signals. Damage packets sustain capture and a cooling window
/// protects delayed projectile arrivals. Idle polling remains a deliberately
/// lossy 1 ms sentinel, now paired with a 1 ms weapon-state tripwire for
/// player-initiated combat that begins before native combat music changes.
/// </summary>
internal sealed class ClientCombatObserver : IDisposable
{
    private const uint ImageBase = 0x00400000;

    private const uint ClientContextCurrentTime = 0x10bc;
    private const uint ClientContextConnectionWrapper = 0x1124;
    private const uint ClientContextLocalPlayerObjectId = 0x112c;
    private const uint ClientContextMusicManager = 0x132c;

    private const uint ConnectionWrapperVTableStatic = 0x00b0854c;
    private const uint ConnectionWrapperVTableRva =
        ConnectionWrapperVTableStatic - ImageBase;
    private const uint ConnectionWrapperCachedPacket = 0x0c;

    private const uint MusicManagerCurrentMusicType = 0x1a4;
    private const uint MusicManagerPreviousMusicType = 0x1a8;

    private const uint ItemStatePropertyValueOffset = 0x84;
    private const uint WeaponActivationPhaseStateFlag = 0x00000001;
    private const uint WeaponBusyStateFlag = 0x00000080;

    private const byte MissileObjectType = 0x09;

    private const uint DamagePacketVTableStatic = 0x00b0a1a0;
    private const uint DamagePacketVTableRva =
        DamagePacketVTableStatic - ImageBase;
    private const int DamagePacketSize = 0x24;

    private const int IdlePacketPollingIntervalMicroseconds = 1000;
    private const int ArmedPacketPollingIntervalMicroseconds = 100;
    private const int BurstDurationMicroseconds = 2000;
    private const int CoolingDurationMilliseconds = 5000;
    private const int GroupArmDurationMilliseconds = 5000;
    private const int WeaponArmDurationMilliseconds = 12000;
    private const int IdleWeaponStateSamplingIntervalMicroseconds = 1000;
    private const int ActiveWeaponStateSamplingIntervalMilliseconds = 10;
    private const int ActiveContextSamplingIntervalMilliseconds = 10;
    private const int DormantContextSamplingIntervalMilliseconds = 100;
    private const int TopologyValidationIntervalMilliseconds = 1000;
    private const int ReaderRetryIntervalMilliseconds = 1000;
    private const int SnapshotPublishIntervalMilliseconds = 100;
    private const int MaximumRecentEventCount = 64;
    private const int MaximumRememberedMessageInstanceIds = 8192;

    private static readonly long idlePacketPollingIntervalTicks =
        MicrosecondsToStopwatchTicks(
            IdlePacketPollingIntervalMicroseconds);

    private static readonly long armedPacketPollingIntervalTicks =
        MicrosecondsToStopwatchTicks(
            ArmedPacketPollingIntervalMicroseconds);

    private static readonly long burstDurationTicks =
        MicrosecondsToStopwatchTicks(
            BurstDurationMicroseconds);

    private static readonly long coolingDurationTicks =
        MillisecondsToStopwatchTicks(
            CoolingDurationMilliseconds);

    private static readonly long groupArmDurationTicks =
        MillisecondsToStopwatchTicks(
            GroupArmDurationMilliseconds);

    private static readonly long weaponArmDurationTicks =
        MillisecondsToStopwatchTicks(
            WeaponArmDurationMilliseconds);

    private static readonly long idleWeaponStateSamplingIntervalTicks =
        MicrosecondsToStopwatchTicks(
            IdleWeaponStateSamplingIntervalMicroseconds);

    private static readonly long activeWeaponStateSamplingIntervalTicks =
        MillisecondsToStopwatchTicks(
            ActiveWeaponStateSamplingIntervalMilliseconds);

    private static readonly long activeContextSamplingIntervalTicks =
        MillisecondsToStopwatchTicks(
            ActiveContextSamplingIntervalMilliseconds);

    private static readonly long dormantContextSamplingIntervalTicks =
        MillisecondsToStopwatchTicks(
            DormantContextSamplingIntervalMilliseconds);

    private static readonly long topologyValidationIntervalTicks =
        MillisecondsToStopwatchTicks(
            TopologyValidationIntervalMilliseconds);

    private static readonly long readerRetryIntervalTicks =
        MillisecondsToStopwatchTicks(
            ReaderRetryIntervalMilliseconds);

    private static readonly long snapshotPublishIntervalTicks =
        MillisecondsToStopwatchTicks(
            SnapshotPublishIntervalMilliseconds);

    private static readonly long busyWaitSleepZeroThresholdTicks =
        MicrosecondsToStopwatchTicks(750);

    private static readonly long busyWaitYieldThresholdTicks =
        MicrosecondsToStopwatchTicks(200);

    private readonly Lock coordinatorLock = new();
    private readonly Dictionary<int, CombatProcessState> processStates = [];
    private readonly AutoResetEvent wakeSignal = new(initialState: false);

    private readonly ClientCombatIdentityResolver identityResolver =
        new();

    private CancellationTokenSource? cancellationTokenSource;
    private Task? pollingTask;
    private int roundRobinStartIndex;

    public void Start()
    {
        lock (this.coordinatorLock)
        {
            if (this.pollingTask != null)
            {
                return;
            }

            var tokenSource =
                new CancellationTokenSource();

            this.cancellationTokenSource =
                tokenSource;

            this.pollingTask = Task.Factory.StartNew(
                () => this.PollLoop(
                    tokenSource),
                tokenSource.Token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }
    }

    public void RefreshTarget(
        ObservedClientState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var target = CreateTarget(state);

        this.identityResolver.RefreshTarget(
            state.ProcessId,
            state.ModuleBaseAddress,
            state.ClientContextAddress,
            state.World.ActiveSectorNumber,
            target.HasClientContext);

        this.identityResolver.Request(
            state.ProcessId,
            target.LocalPlayerObjectId);

        CombatProcessState processState;

        lock (this.coordinatorLock)
        {
            if (this.processStates.TryGetValue(
                    state.ProcessId,
                    out var existingProcessState) &&
                !existingProcessState.IsRemoved)
            {
                processState = existingProcessState;
            }
            else
            {
                processState = new CombatProcessState(
                    state.ProcessId);

                this.processStates[state.ProcessId] =
                    processState;
            }
        }

        processState.SetPendingTarget(target);
        this.wakeSignal.Set();
    }

    public void RemoveProcess(
        int processId)
    {
        this.identityResolver.RemoveProcess(
            processId);

        CombatProcessState? processState;

        lock (this.coordinatorLock)
        {
            this.processStates.TryGetValue(
                processId,
                out processState);
        }

        processState?.MarkRemoved();
        this.wakeSignal.Set();
    }

    public ClientCombatObservation GetObservation(
        int processId)
    {
        lock (this.coordinatorLock)
        {
            return this.processStates.TryGetValue(
                    processId,
                    out var processState) &&
                !processState.IsRemoved
                ? processState.Snapshot
                : ClientCombatObservation.Unavailable(
                    "Combat observer is not attached to this process");
        }
    }

    public void Dispose()
    {
        CancellationTokenSource? tokenSource;
        Task? task;

        lock (this.coordinatorLock)
        {
            tokenSource =
                this.cancellationTokenSource;

            task = this.pollingTask;
        }

        tokenSource?.Cancel();
        this.wakeSignal.Set();

        try
        {
            task?.Wait(
                TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Best-effort shutdown. The worker owns only read handles.
        }

        lock (this.coordinatorLock)
        {
            this.pollingTask = null;
            this.cancellationTokenSource = null;
        }

        tokenSource?.Dispose();
        this.identityResolver.Dispose();
        this.wakeSignal.Dispose();
    }

    private void PollLoop(
        CancellationTokenSource cts)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            while (!cts.IsCancellationRequested)
            {
                CombatProcessState[] states;

                lock (this.coordinatorLock)
                {
                    states =
                    [
                        .. this.processStates.Values
                            .OrderBy(
                                state => state.ProcessId),
                    ];
                }

                if (states.Length == 0)
                {
                    this.wakeSignal.WaitOne(100);
                    continue;
                }

                var startIndex =
                    this.roundRobinStartIndex % states.Length;

                this.roundRobinStartIndex =
                    (startIndex + 1) % states.Length;

                var nextDueAtTicks = long.MaxValue;
                var hasHotClient = false;
                List<CombatProcessState> fanOutSources = [];

                for (var offset = 0;
                     offset < states.Length;
                     offset++)
                {
                    var index =
                        (startIndex + offset) % states.Length;

                    var state = states[index];
                    var nowTicks = stopwatch.ElapsedTicks;

                    ProcessClientResult result;

                    try
                    {
                        result = this.ProcessClient(
                            state,
                            nowTicks);
                    }
                    catch (Exception ex)
                    {
                        state.LastError =
                            $"Combat coordinator iteration failed: {ex.Message}";

                        PublishUnavailableSnapshot(
                            state,
                            state.LastError,
                            state.CurrentTarget.ClientContextAddress);

                        result = new ProcessClientResult(
                            checked(
                                nowTicks +
                                readerRetryIntervalTicks),
                            IsHot: false,
                            ShouldArmGroupPeers: false);
                    }

                    nextDueAtTicks = Math.Min(
                        nextDueAtTicks,
                        result.NextDueAtTicks);

                    hasHotClient |= result.IsHot;

                    if (result.ShouldArmGroupPeers)
                    {
                        fanOutSources.Add(state);
                    }
                }

                if (fanOutSources.Count != 0)
                {
                    var nowTicks = stopwatch.ElapsedTicks;

                    foreach (var source in fanOutSources)
                    {
                        this.ArmGroupPeers(
                            source,
                            states,
                            nowTicks);
                    }

                    nextDueAtTicks = Math.Min(
                        nextDueAtTicks,
                        nowTicks);
                }

                WaitUntilNextDue(
                    this.wakeSignal,
                    stopwatch,
                    nextDueAtTicks,
                    hasHotClient,
                    cts.Token);
            }
        }
        catch (Exception)
        {
            // The normal one-second observer remains authoritative for process
            // lifetime. A coordinator failure must never disturb the client or
            // crash the diagnostic application.
        }
        finally
        {
            CombatProcessState[] states;

            lock (this.coordinatorLock)
            {
                states = [.. this.processStates.Values];
            }

            foreach (var state in states)
            {
                state.DisposeReader();
            }
        }
    }

    private ProcessClientResult ProcessClient(
        CombatProcessState state,
        long nowTicks)
    {
        if (state.IsRemoved)
        {
            state.DisposeReader();

            lock (this.coordinatorLock)
            {
                if (this.processStates.TryGetValue(
                        state.ProcessId,
                        out var current) &&
                    ReferenceEquals(current, state))
                {
                    this.processStates.Remove(
                        state.ProcessId);
                }
            }

            return ProcessClientResult.None;
        }

        if (state.TryTakePendingTarget(
                out var pendingTarget))
        {
            ApplyTarget(
                state,
                pendingTarget,
                nowTicks);
        }

        var target = state.CurrentTarget;

        if (!target.HasClientContext)
        {
            state.DisposeReader();
            state.Mode = ClientCombatPollingMode.Dormant;
            state.ArmingSource = ClientCombatArmingSource.None;

            PublishUnavailableSnapshot(
                state,
                target.Status,
                target.ClientContextAddress);

            return ProcessClientResult.None;
        }

        if (state.Memory == null)
        {
            if (nowTicks < state.NextReaderOpenAtTicks)
            {
                return new ProcessClientResult(
                    state.NextReaderOpenAtTicks,
                    IsHot: false,
                    ShouldArmGroupPeers: false);
            }

            try
            {
                state.Memory = ProcessMemoryReader.Open(
                    state.ProcessId);

                state.NextContextSampleAtTicks = 0;
                state.NextTopologyValidationAtTicks = 0;
                state.NextPacketPollAtTicks = 0;
                state.LastError = "";
            }
            catch (Exception ex)
            {
                state.NextReaderOpenAtTicks = checked(
                    nowTicks + readerRetryIntervalTicks);

                state.LastError =
                    $"Could not open process for combat observation: {ex.Message}";

                PublishUnavailableSnapshot(
                    state,
                    state.LastError,
                    target.ClientContextAddress);

                return new ProcessClientResult(
                    state.NextReaderOpenAtTicks,
                    IsHot: false,
                    ShouldArmGroupPeers: false);
            }
        }

        var didWork = false;
        var shouldArmGroupPeers = false;

        if (nowTicks >= state.NextTopologyValidationAtTicks)
        {
            ValidateTopology(
                state,
                nowTicks);

            didWork = true;
        }

        var contextIntervalTicks =
            target.AllowPacketPolling
                ? activeContextSamplingIntervalTicks
                : dormantContextSamplingIntervalTicks;

        if (nowTicks >= state.NextContextSampleAtTicks)
        {
            shouldArmGroupPeers |= ReadMusicContext(
                state,
                nowTicks);

            state.NextContextSampleAtTicks = checked(
                nowTicks + contextIntervalTicks);

            didWork = true;
        }

        UpdateModeForTime(
            state,
            nowTicks);

        if (target.AllowPacketPolling &&
            target.WeaponItemStatePropertyAddresses.Count != 0 &&
            nowTicks >= state.NextWeaponStateSampleAtTicks)
        {
            shouldArmGroupPeers |= PollWeaponStates(
                state,
                nowTicks);

            UpdateModeForTime(
                state,
                nowTicks);

            var weaponSampleIntervalTicks =
                GetWeaponStateSamplingIntervalTicks(
                    state.Mode);

            state.NextWeaponStateSampleAtTicks =
                weaponSampleIntervalTicks == long.MaxValue
                    ? long.MaxValue
                    : checked(
                        nowTicks + weaponSampleIntervalTicks);

            didWork = true;
        }

        if (target.AllowPacketPolling &&
            state.ConnectionWrapperAddress != 0 &&
            nowTicks >= state.NextPacketPollAtTicks)
        {
            shouldArmGroupPeers |= this.PollCachedPacket(
                state,
                nowTicks);

            UpdateModeForTime(
                state,
                nowTicks);

            state.NextPacketPollAtTicks = checked(
                nowTicks + GetPacketPollingIntervalTicks(
                    state.Mode));

            didWork = true;
        }

        if (nowTicks >= state.NextSnapshotPublishAtTicks ||
            didWork && state.Snapshot.PollingMode != state.Mode)
        {
            this.PublishSnapshot(
                state,
                nowTicks);

            state.NextSnapshotPublishAtTicks = checked(
                nowTicks + snapshotPublishIntervalTicks);
        }

        var nextDueAtTicks = Math.Min(
            state.NextContextSampleAtTicks,
            state.NextTopologyValidationAtTicks);

        if (target.AllowPacketPolling &&
            state.ConnectionWrapperAddress != 0)
        {
            nextDueAtTicks = Math.Min(
                nextDueAtTicks,
                state.NextPacketPollAtTicks);
        }

        if (target.AllowPacketPolling &&
            target.WeaponItemStatePropertyAddresses.Count != 0)
        {
            nextDueAtTicks = Math.Min(
                nextDueAtTicks,
                state.NextWeaponStateSampleAtTicks);
        }

        nextDueAtTicks = Math.Min(
            nextDueAtTicks,
            state.NextSnapshotPublishAtTicks);

        return new ProcessClientResult(
            nextDueAtTicks,
            state.Mode is
                ClientCombatPollingMode.Armed or
                ClientCombatPollingMode.Burst or
                ClientCombatPollingMode.Cooling,
            shouldArmGroupPeers);
    }

    private static void ApplyTarget(
        CombatProcessState state,
        CombatObservationTarget target,
        long nowTicks)
    {
        var previousTarget = state.CurrentTarget;
        var identityChanged =
            previousTarget.HasClientContext != target.HasClientContext ||
            previousTarget.ModuleBaseAddress != target.ModuleBaseAddress ||
            previousTarget.ClientContextAddress != target.ClientContextAddress ||
            previousTarget.ActiveSectorNumber != target.ActiveSectorNumber;

        state.CurrentTarget = target;

        if (!identityChanged)
        {
            SynchronizeWeaponStateTargets(
                state,
                target,
                nowTicks);

            if (!target.AllowPacketPolling)
            {
                state.BurstUntilTicks = 0;
                state.CoolingUntilTicks = 0;
                state.GroupArmUntilTicks = 0;
                state.WeaponArmUntilTicks = 0;
                state.Mode = ClientCombatPollingMode.Dormant;
                state.ArmingSource = ClientCombatArmingSource.None;
            }

            return;
        }

        state.DisposeReader();
        state.ResetSession();
        state.NextReaderOpenAtTicks = nowTicks;
        state.Snapshot = ClientCombatObservation.Unavailable(
            target.Status,
            target.ClientContextAddress);
    }

    private static void SynchronizeWeaponStateTargets(
        CombatProcessState state,
        CombatObservationTarget target,
        long nowTicks)
    {
        if (!target.AllowPacketPolling)
        {
            state.WeaponItemStates.Clear();
            state.WeaponActivationPhaseSeen.Clear();
            state.NextWeaponStateSampleAtTicks = 0;
            return;
        }

        var activeAddresses =
            target.WeaponItemStatePropertyAddresses;

        foreach (var address in state.WeaponItemStates.Keys
                     .Where(address => !activeAddresses.Contains(address))
                     .ToArray())
        {
            state.WeaponItemStates.Remove(address);
            state.WeaponActivationPhaseSeen.Remove(address);
        }

        if (activeAddresses.Any(
                address => !state.WeaponItemStates.ContainsKey(address)))
        {
            state.NextWeaponStateSampleAtTicks = nowTicks;
        }
    }

    private static void ValidateTopology(
        CombatProcessState state,
        long nowTicks)
    {
        state.NextTopologyValidationAtTicks = checked(
            nowTicks + topologyValidationIntervalTicks);

        var memory = state.Memory;
        var target = state.CurrentTarget;

        if (memory == null)
        {
            return;
        }

        if (!TryReadClientUInt32(
                memory,
                target.ClientContextAddress,
                ClientContextConnectionWrapper,
                out var connectionWrapperAddress))
        {
            state.ConnectionWrapperAddress = 0;
            state.PacketReadFailureCount++;
            state.LastError =
                "Could not read SClient.ConnectionWrapper";

            return;
        }

        if (connectionWrapperAddress == 0)
        {
            state.ConnectionWrapperAddress = 0;
            state.LastError =
                "SClient connection wrapper is null";

            return;
        }

        if (!memory.TryReadUInt32(
                connectionWrapperAddress,
                out var connectionWrapperVTableAddress))
        {
            state.ConnectionWrapperAddress = 0;
            state.PacketReadFailureCount++;
            state.LastError =
                "Could not read the connection-wrapper vtable";

            return;
        }

        if (connectionWrapperVTableAddress !=
            target.ExpectedConnectionWrapperVTableAddress)
        {
            state.ConnectionWrapperAddress = 0;
            state.LastError = $"Unexpected connection-wrapper vtable 0x{connectionWrapperVTableAddress:X8}; expected 0x{target.ExpectedConnectionWrapperVTableAddress:X8}";

            return;
        }

        if (state.ConnectionWrapperAddress !=
            connectionWrapperAddress)
        {
            state.ConnectionWrapperAddress =
                connectionWrapperAddress;

            state.SeenMessageInstanceIds.Clear();
            state.SeenMessageInstanceIdOrder.Clear();
            state.NextPacketPollAtTicks = nowTicks;
        }

        state.LastError = "";
    }

    private static bool ReadMusicContext(
        CombatProcessState state,
        long nowTicks)
    {
        state.ContextSampleCount++;

        var memory = state.Memory;
        var target = state.CurrentTarget;

        if (memory == null ||
            !TryReadClientUInt32(
                memory,
                target.ClientContextAddress,
                ClientContextMusicManager,
                out var musicManagerAddress) ||
            musicManagerAddress == 0 ||
            !TryReadInt32(
                memory,
                musicManagerAddress,
                MusicManagerCurrentMusicType,
                out var currentMusicType) ||
            !TryReadInt32(
                memory,
                musicManagerAddress,
                MusicManagerPreviousMusicType,
                out var previousMusicType))
        {
            state.ContextReadFailureCount++;
            state.LastError =
                "Could not read the live MusicManager context";

            return false;
        }

        var wasCombat =
            state.CurrentMusicTypeRaw ==
            (int)ClientMusicType.Combat;

        var isCombat =
            currentMusicType ==
            (int)ClientMusicType.Combat;

        state.HasContextSample = true;
        state.MusicManagerAddress = musicManagerAddress;
        state.CurrentMusicTypeRaw = currentMusicType;
        state.PreviousMusicTypeRaw = previousMusicType;

        if (isCombat)
        {
            state.CoolingUntilTicks = 0;
        }
        else if (wasCombat)
        {
            state.CoolingUntilTicks = Math.Max(
                state.CoolingUntilTicks,
                checked(
                    nowTicks + coolingDurationTicks));
        }

        if (!target.AllowPacketPolling ||
            state.ConnectionWrapperAddress != 0)
        {
            state.LastError = "";
        }

        return isCombat && !wasCombat;
    }

    private static bool PollWeaponStates(
        CombatProcessState state,
        long nowTicks)
    {
        var memory = state.Memory;

        if (memory == null)
        {
            return false;
        }

        var activationCount = 0L;
        var activationPhaseCount = 0L;
        var busyTransitionCount = 0L;

        foreach (var propertyAddress in
                 state.CurrentTarget.WeaponItemStatePropertyAddresses)
        {
            state.WeaponStateSampleCount++;

            if (!TryReadWeaponItemState(
                    memory,
                    propertyAddress,
                    out var currentState))
            {
                state.WeaponStateReadFailureCount++;
                continue;
            }

            var hasPreviousState =
                state.WeaponItemStates.TryGetValue(
                    propertyAddress,
                    out var previousState);

            state.WeaponItemStates[propertyAddress] =
                currentState;

            var isInActivationPhase =
                (currentState &
                 WeaponActivationPhaseStateFlag) != 0;

            var isBusy =
                (currentState & WeaponBusyStateFlag) != 0;

            var enteredActivationPhase =
                isInActivationPhase &&
                (!hasPreviousState ||
                 (previousState &
                  WeaponActivationPhaseStateFlag) == 0);

            if (isInActivationPhase)
            {
                state.WeaponActivationPhaseSeen.Add(
                    propertyAddress);
            }

            var enteredBusyState =
                hasPreviousState &&
                (previousState & WeaponBusyStateFlag) == 0 &&
                isBusy;

            var busyFallbackTriggered =
                enteredBusyState &&
                !state.WeaponActivationPhaseSeen.Contains(
                    propertyAddress);

            if (enteredActivationPhase ||
                busyFallbackTriggered)
            {
                activationCount++;

                if (enteredActivationPhase)
                {
                    activationPhaseCount++;
                }

                if (busyFallbackTriggered)
                {
                    busyTransitionCount++;
                }
            }

            if (!isInActivationPhase &&
                !isBusy)
            {
                state.WeaponActivationPhaseSeen.Remove(
                    propertyAddress);
            }
        }

        if (activationCount == 0)
        {
            return false;
        }

        state.WeaponActivationCount += activationCount;
        state.WeaponActivationPhaseCount +=
            activationPhaseCount;
        state.WeaponBusyTransitionCount +=
            busyTransitionCount;
        state.LastWeaponActivationObservedAt =
            DateTimeOffset.UtcNow;
        state.WeaponArmUntilTicks = Math.Max(
            state.WeaponArmUntilTicks,
            checked(
                nowTicks + weaponArmDurationTicks));

        // Do not wait for the previously scheduled 1 ms idle sentinel. The
        // weapon transition logically precedes outgoing damage, so switch to
        // the armed cadence and inspect the cached packet immediately.
        state.NextPacketPollAtTicks = nowTicks;

        return true;
    }

    private bool PollCachedPacket(
        CombatProcessState state,
        long nowTicks)
    {
        state.PacketPollCount++;

        switch (state.Mode)
        {
            case ClientCombatPollingMode.Idle:
                state.IdlePacketPollCount++;
                break;

            case ClientCombatPollingMode.Armed:
            case ClientCombatPollingMode.Cooling:
                state.ArmedPacketPollCount++;
                break;

            case ClientCombatPollingMode.Burst:
                state.BurstPacketPollCount++;
                break;
            case ClientCombatPollingMode.Dormant:
            default:
                break;
        }

        var memory = state.Memory;

        if (memory == null)
        {
            return false;
        }

        uint cachedPacketAddress;

        try
        {
            if (!memory.TryReadUInt32(
                    checked(
                        state.ConnectionWrapperAddress +
                        ConnectionWrapperCachedPacket),
                    out cachedPacketAddress))
            {
                state.PacketReadFailureCount++;
                return false;
            }
        }
        catch (OverflowException)
        {
            state.PacketReadFailureCount++;
            return false;
        }

        if (cachedPacketAddress == 0)
        {
            return false;
        }

        state.CachedPacketObservationCount++;

        if (!memory.TryReadBytes(
                cachedPacketAddress,
                state.PacketBuffer))
        {
            state.PacketReadFailureCount++;
            return false;
        }

        var packetVTableAddress =
            BitConverter.ToUInt32(
                state.PacketBuffer,
                0x00);

        var connectionAddress =
            BitConverter.ToUInt32(
                state.PacketBuffer,
                0x04);

        var messageInstanceId =
            BitConverter.ToUInt32(
                state.PacketBuffer,
                0x08);

        if (connectionAddress !=
            state.ConnectionWrapperAddress)
        {
            state.PacketReadFailureCount++;
            return false;
        }

        var isNewMessage = RememberMessageInstanceId(
            state,
            messageInstanceId);

        if (isNewMessage)
        {
            state.UniquePacketCount++;
        }

        var isDamagePacket =
            packetVTableAddress ==
            state.CurrentTarget.ExpectedDamagePacketVTableAddress;

        // Once any combat signal has armed the client, packet activity opens
        // or extends a tiny unrestricted burst. While idle, ordinary
        // world traffic does not wake the observer; only a caught DamagePacket
        // does. This avoids turning ambient packets into permanent hot polling.
        if (state.Mode != ClientCombatPollingMode.Idle ||
            isDamagePacket)
        {
            state.BurstUntilTicks = Math.Max(
                state.BurstUntilTicks,
                checked(
                    nowTicks + burstDurationTicks));
        }

        if (!isDamagePacket)
        {
            return false;
        }

        if (!isNewMessage)
        {
            state.DuplicatePacketObservationCount++;
            return false;
        }

        var damageRaw =
            BitConverter.ToUInt32(
                state.PacketBuffer,
                0x0c);

        var modifierRaw =
            BitConverter.ToUInt32(
                state.PacketBuffer,
                0x10);

        var sourceObjectId =
            BitConverter.ToUInt32(
                state.PacketBuffer,
                0x1c);

        var victimObjectId =
            BitConverter.ToUInt32(
                state.PacketBuffer,
                0x20);

        TryReadClientUInt32(
            memory,
            state.CurrentTarget.ClientContextAddress,
            ClientContextCurrentTime,
            out var clientTime);

        TryReadClientUInt32(
            memory,
            state.CurrentTarget.ClientContextAddress,
            ClientContextLocalPlayerObjectId,
            out var localPlayerObjectId);

        var observedAt = DateTimeOffset.UtcNow;

        var capturedDirection = GetCombatPacketDirection(
            sourceObjectId,
            victimObjectId,
            localPlayerObjectId);

        var combatEvent =
            new ClientCombatEventObservation
            {
                Sequence = state.NextEventSequence++,
                ObservedAt = observedAt,
                ClientTime = clientTime,
                MessageInstanceId = messageInstanceId,
                Damage = BitConverter.Int32BitsToSingle(
                    unchecked((int)damageRaw)),
                Modifier = BitConverter.Int32BitsToSingle(
                    unchecked((int)modifierRaw)),
                DamageType = BitConverter.ToInt32(
                    state.PacketBuffer,
                    0x14),
                Inflicted = BitConverter.ToInt32(
                    state.PacketBuffer,
                    0x18),
                SourceObjectId = sourceObjectId,
                VictimObjectId = victimObjectId,
                LocalPlayerObjectId = localPlayerObjectId,
                CapturedDirection = capturedDirection,
                Direction = capturedDirection,
                DirectionAttribution =
                    ClientCombatDirectionAttribution.PacketObjectIds,
            };

        state.DamagePacketCount++;
        state.LastDamageObservedAt = observedAt;
        state.CoolingUntilTicks = Math.Max(
            state.CoolingUntilTicks,
            checked(
                nowTicks + coolingDurationTicks));

        state.RecentEvents.Add(combatEvent);

        if (state.RecentEvents.Count >
            MaximumRecentEventCount)
        {
            state.RecentEvents.RemoveAt(0);
        }

        this.identityResolver.Request(
            state.ProcessId,
            sourceObjectId,
            victimObjectId);

        return true;
    }

    private static bool RememberMessageInstanceId(
        CombatProcessState state,
        uint messageInstanceId)
    {
        if (!state.SeenMessageInstanceIds.Add(
                messageInstanceId))
        {
            return false;
        }

        state.SeenMessageInstanceIdOrder.Enqueue(
            messageInstanceId);

        while (state.SeenMessageInstanceIdOrder.Count >
               MaximumRememberedMessageInstanceIds)
        {
            var expired =
                state.SeenMessageInstanceIdOrder.Dequeue();

            state.SeenMessageInstanceIds.Remove(
                expired);
        }

        return true;
    }

    private static void UpdateModeForTime(
        CombatProcessState state,
        long nowTicks)
    {
        if (!state.CurrentTarget.AllowPacketPolling)
        {
            state.Mode = ClientCombatPollingMode.Dormant;
            state.ArmingSource = ClientCombatArmingSource.None;
            return;
        }

        if (nowTicks < state.BurstUntilTicks)
        {
            state.Mode = ClientCombatPollingMode.Burst;
            state.ArmingSource =
                ClientCombatArmingSource.PacketActivity;
            return;
        }

        if (state.CurrentMusicTypeRaw ==
            (int)ClientMusicType.Combat)
        {
            state.Mode = ClientCombatPollingMode.Armed;
            state.ArmingSource =
                ClientCombatArmingSource.MusicContext;
            return;
        }

        if (nowTicks < state.WeaponArmUntilTicks)
        {
            state.Mode = ClientCombatPollingMode.Armed;
            state.ArmingSource =
                ClientCombatArmingSource.WeaponActivation;
            return;
        }

        if (nowTicks < state.GroupArmUntilTicks)
        {
            state.Mode = ClientCombatPollingMode.Armed;
            state.ArmingSource =
                ClientCombatArmingSource.GroupPeer;
            return;
        }

        if (nowTicks < state.CoolingUntilTicks)
        {
            state.Mode = ClientCombatPollingMode.Cooling;
            state.ArmingSource =
                ClientCombatArmingSource.Cooling;
            return;
        }

        state.Mode = ClientCombatPollingMode.Idle;
        state.ArmingSource = ClientCombatArmingSource.None;
    }

    private void ArmGroupPeers(
        CombatProcessState source,
        IReadOnlyList<CombatProcessState> states,
        long nowTicks)
    {
        var sourceTarget = source.CurrentTarget;

        if (sourceTarget.LocalPlayerObjectId is 0 or uint.MaxValue)
        {
            return;
        }

        foreach (var candidate in states)
        {
            if (ReferenceEquals(candidate, source) ||
                candidate.IsRemoved)
            {
                continue;
            }

            var candidateTarget = candidate.CurrentTarget;

            if (!candidateTarget.AllowPacketPolling ||
                candidateTarget.LocalPlayerObjectId is 0 or uint.MaxValue)
            {
                continue;
            }

            var isPeer =
                sourceTarget.GroupMemberObjectIds.Contains(
                    candidateTarget.LocalPlayerObjectId) ||
                candidateTarget.GroupMemberObjectIds.Contains(
                    sourceTarget.LocalPlayerObjectId);

            if (!isPeer)
            {
                continue;
            }

            candidate.GroupArmUntilTicks = Math.Max(
                candidate.GroupArmUntilTicks,
                checked(
                    nowTicks + groupArmDurationTicks));

            candidate.NextPacketPollAtTicks = Math.Min(
                candidate.NextPacketPollAtTicks,
                nowTicks);

            UpdateModeForTime(
                candidate,
                nowTicks);

            this.PublishSnapshot(
                candidate,
                nowTicks);
        }
    }

    private void PublishSnapshot(
        CombatProcessState state,
        long nowTicks)
    {
        var target = state.CurrentTarget;
        var identitySnapshot =
            this.identityResolver.GetSnapshot(
                state.ProcessId);

        var coolingRemainingMilliseconds =
            state.CoolingUntilTicks > nowTicks
                ? StopwatchTicksToMilliseconds(
                    state.CoolingUntilTicks - nowTicks)
                : 0;

        var weaponArmRemainingMilliseconds =
            state.WeaponArmUntilTicks > nowTicks
                ? StopwatchTicksToMilliseconds(
                    state.WeaponArmUntilTicks - nowTicks)
                : 0;

        var isAvailable =
            state is { Memory: not null, HasContextSample: true } &&
            (!target.AllowPacketPolling ||
             state.ConnectionWrapperAddress != 0);

        var status = BuildStatus(
            state,
            target,
            isAvailable);

        state.Snapshot = new ClientCombatObservation
        {
            IsAvailable = isAvailable,
            Status = status,
            ObservedAt = DateTimeOffset.UtcNow,
            ClientContextAddress =
                target.ClientContextAddress,
            ConnectionWrapperAddress =
                state.ConnectionWrapperAddress,
            MusicManagerAddress =
                state.MusicManagerAddress,
            CurrentMusicTypeRaw =
                state.CurrentMusicTypeRaw,
            PreviousMusicTypeRaw =
                state.PreviousMusicTypeRaw,
            PollingMode = state.Mode,
            ArmingSource = state.ArmingSource,
            PacketPollingIntervalMicroseconds =
                GetPacketPollingIntervalMicroseconds(
                    state.Mode),
            ContextSamplingIntervalMilliseconds =
                target.AllowPacketPolling
                    ? ActiveContextSamplingIntervalMilliseconds
                    : DormantContextSamplingIntervalMilliseconds,
            CoolingRemainingMilliseconds =
                coolingRemainingMilliseconds,
            WeaponArmRemainingMilliseconds =
                weaponArmRemainingMilliseconds,
            TrackedWeaponCount =
                target.WeaponItemStatePropertyAddresses.Count,
            WeaponStateSampleCount =
                state.WeaponStateSampleCount,
            WeaponActivationCount =
                state.WeaponActivationCount,
            WeaponActivationPhaseCount =
                state.WeaponActivationPhaseCount,
            WeaponBusyTransitionCount =
                state.WeaponBusyTransitionCount,
            WeaponStateReadFailureCount =
                state.WeaponStateReadFailureCount,
            LastWeaponActivationObservedAt =
                state.LastWeaponActivationObservedAt,
            PacketPollCount = state.PacketPollCount,
            IdlePacketPollCount =
                state.IdlePacketPollCount,
            ArmedPacketPollCount =
                state.ArmedPacketPollCount,
            BurstPacketPollCount =
                state.BurstPacketPollCount,
            ContextSampleCount =
                state.ContextSampleCount,
            CachedPacketObservationCount =
                state.CachedPacketObservationCount,
            UniquePacketCount =
                state.UniquePacketCount,
            DamagePacketCount =
                state.DamagePacketCount,
            DuplicatePacketObservationCount =
                state.DuplicatePacketObservationCount,
            PacketReadFailureCount =
                state.PacketReadFailureCount,
            ContextReadFailureCount =
                state.ContextReadFailureCount,
            ResolvedIdentityCount =
                identitySnapshot.Identities.Count(
                    item => item.Value.IsResolved),
            PendingIdentityCount =
                identitySnapshot.PendingCount,
            IdentityResolutionFailureCount =
                identitySnapshot.ResolutionFailureCount,
            LastDamageObservedAt =
                state.LastDamageObservedAt,
            RecentEvents =
                [.. state.RecentEvents
                    .Select(
                        combatEvent =>
                            EnrichCombatEvent(
                                combatEvent,
                                identitySnapshot,
                                target))],
        };
    }

    private static void PublishUnavailableSnapshot(
        CombatProcessState state,
        string status,
        uint clientContextAddress)
    {
        state.Snapshot =
            ClientCombatObservation.Unavailable(
                status,
                clientContextAddress);
    }

    private static string BuildStatus(
        CombatProcessState state,
        CombatObservationTarget target,
        bool isAvailable)
    {
        if (!isAvailable)
        {
            return string.IsNullOrWhiteSpace(
                state.LastError)
                ? target.Status
                : state.LastError;
        }

        if (!string.IsNullOrWhiteSpace(
                state.LastError))
        {
            return $"Available with read warning: {state.LastError}";
        }

        return state.Mode switch
        {
            ClientCombatPollingMode.Dormant =>
                "Available; context observed, packet polling dormant outside a combat-capable presentation",

            ClientCombatPollingMode.Idle
                when target.WeaponItemStatePropertyAddresses.Count != 0 =>
                "Available; 1 ms packet sentinel and equipped-weapon tripwire are best-effort",

            ClientCombatPollingMode.Idle =>
                "Available; 1 ms idle packet sentinel is best-effort; no equipped weapon tripwire is available",

            ClientCombatPollingMode.Armed =>
                BuildArmedStatus(state.ArmingSource),

            ClientCombatPollingMode.Burst =>
                "Available; unrestricted 2 ms packet burst",

            ClientCombatPollingMode.Cooling =>
                "Available; 5s cooling window at 100 us polling cadence protects delayed projectile arrivals",

            _ => "Available",
        };
    }

    private static string BuildArmedStatus(
        ClientCombatArmingSource armingSource)
    {
        if (armingSource ==
            ClientCombatArmingSource.WeaponActivation)
        {
            return "Available; armed at 100 us from local weapon activation";
        }

        if (armingSource ==
            ClientCombatArmingSource.MusicContext)
        {
            return "Available; armed at 100 us from native combat context";
        }

        if (armingSource ==
            ClientCombatArmingSource.GroupPeer)
        {
            return "Available; armed at 100 us from a grouped local client";
        }

        return $"Available; armed at 100 us; unexpected arming source {armingSource}";
    }

    private static CombatObservationTarget CreateTarget(
        ObservedClientState state)
    {
        if (!state.HasDirectClientState ||
            state.ModuleBaseAddress == 0 ||
            state.ClientContextAddress == 0)
        {
            return CombatObservationTarget.Unavailable(
                state.ProcessId,
                state.ClientContextAddress,
                "Direct SClient state is unavailable");
        }

        uint expectedConnectionWrapperVTableAddress;
        uint expectedDamagePacketVTableAddress;

        try
        {
            expectedConnectionWrapperVTableAddress = checked(
                state.ModuleBaseAddress +
                ConnectionWrapperVTableRva);

            expectedDamagePacketVTableAddress = checked(
                state.ModuleBaseAddress +
                DamagePacketVTableRva);
        }
        catch (OverflowException)
        {
            return CombatObservationTarget.Unavailable(
                state.ProcessId,
                state.ClientContextAddress,
                "Relocated combat observer address overflow");
        }

        var allowPacketPolling =
            state is
            {
                LifecycleState: ClientLifecycleState.InGame, World:
                {
                    IsAvailable: true, Environment: ClientWorldEnvironment.Space or ClientWorldEnvironment.Planet or ClientWorldEnvironment.GasGiant,
                },
            };

        // The slow inventory lane performs the expensive AuxData lookup and
        // validates each ItemState property. The combat worker receives only
        // those stable property addresses and performs narrow four-byte reads.
        IReadOnlyList<uint> weaponItemStatePropertyAddresses =
            state.LocalPlayer.Inventory.IsAvailable
                ? state.LocalPlayer.Inventory.EquippedSlots
                    .Where(
                        slot =>
                            slot.IsOccupied &&
                            state.LocalPlayer.Inventory
                                .GetEquipmentSlotKind(slot.Slot) ==
                            ClientEquipmentSlotKind.Weapon &&
                            slot.Operational.ItemStatePropertyAddress != 0 &&
                            slot.Operational.ItemStateValidState != 0 &&
                            slot.Operational.RawItemState.HasValue)
                    .Select(
                        slot =>
                            slot.Operational.ItemStatePropertyAddress)
                    .Distinct()
                    .Order()
                    .ToArray()
                : [];

        var groupMemberObjectIds =
            state.Group is { IsAvailable: true, IsValid: true, IsInGroup: true }
                ? state.Group.Members
                    .Select(member => member.ObjectId)
                    .Where(
                        objectId =>
                            objectId is not 0 and not uint.MaxValue)
                    .Distinct()
                    .ToArray()
                : [];

        return new CombatObservationTarget(
            state.ProcessId,
            HasClientContext: true,
            allowPacketPolling,
            allowPacketPolling
                ? "Combat observer target available"
                : "SClient available; waiting for a combat-capable world presentation",
            state.ModuleBaseAddress,
            state.ClientContextAddress,
            state.World.ActiveSectorNumber,
            state.LocalPlayerObjectId,
            state.LocalPlayer.Operational.Identity.Name ?? "",
            state.LocalPlayer.Operational.Identity.Owner ?? "",
            expectedConnectionWrapperVTableAddress,
            expectedDamagePacketVTableAddress,
            weaponItemStatePropertyAddresses,
            groupMemberObjectIds);
    }

    private static bool TryReadClientUInt32(
        ProcessMemoryReader memory,
        uint clientContextAddress,
        uint offset,
        out uint value)
    {
        value = 0;

        try
        {
            return memory.TryReadUInt32(
                checked(
                    clientContextAddress + offset),
                out value);
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static bool TryReadInt32(
        ProcessMemoryReader memory,
        uint baseAddress,
        uint offset,
        out int value)
    {
        value = 0;

        try
        {
            if (!memory.TryReadUInt32(
                    checked(baseAddress + offset),
                    out var rawValue))
            {
                return false;
            }

            value = unchecked((int)rawValue);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static bool TryReadWeaponItemState(
        ProcessMemoryReader memory,
        uint propertyAddress,
        out uint value)
    {
        value = 0;

        try
        {
            return memory.TryReadUInt32(
                checked(
                    propertyAddress +
                    ItemStatePropertyValueOffset),
                out value);
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static ClientCombatEventObservation EnrichCombatEvent(
        ClientCombatEventObservation combatEvent,
        ClientCombatIdentityResolverSnapshot identitySnapshot,
        CombatObservationTarget target)
    {
        var sourceIdentity = identitySnapshot.Get(
            combatEvent.SourceObjectId);

        var victimIdentity = identitySnapshot.Get(
            combatEvent.VictimObjectId);

        var localIdentity = identitySnapshot.Get(
            combatEvent.LocalPlayerObjectId);

        var capturedDirection =
            combatEvent.CapturedDirection !=
                ClientCombatPacketDirection.Unknown
                ? combatEvent.CapturedDirection
                : combatEvent.Direction;

        var direction = capturedDirection;
        var attribution =
            ClientCombatDirectionAttribution.PacketObjectIds;

        // DamagePacket source identity can be the missile object rather than
        // the firing ship. Promote only a resolved missile whose explicit
        // Owner exactly matches one of the local pilot identity values. This
        // intentionally avoids timing-only attribution and cannot claim an
        // arbitrary group member's or nearby player's projectile.
        if (capturedDirection ==
            ClientCombatPacketDirection.ObservedThirdParty &&
            sourceIdentity is { IsResolved: true, RawObjectType: MissileObjectType } &&
            IsLocalIdentityOwner(
                sourceIdentity.Owner,
                localIdentity,
                target))
        {
            direction = ClientCombatPacketDirection.Outgoing;
            attribution =
                ClientCombatDirectionAttribution.LocallyOwnedProjectile;
        }

        return combatEvent with
        {
            SourceIdentity = sourceIdentity,
            VictimIdentity = victimIdentity,
            CapturedDirection = capturedDirection,
            Direction = direction,
            DirectionAttribution = attribution,
        };
    }

    private static bool IsLocalIdentityOwner(
        string sourceOwner,
        ClientCombatActorIdentityObservation localIdentity,
        CombatObservationTarget target)
    {
        var normalizedSourceOwner = NormalizeIdentityValue(
            sourceOwner);

        if (normalizedSourceOwner == null)
        {
            return false;
        }

        string?[] localCandidates =
        [
            target.LocalPlayerOwner,
            target.LocalPlayerName,
            localIdentity.Owner,
            localIdentity.Name,
            localIdentity.DisplayName,
        ];

        foreach (var candidate in localCandidates)
        {
            var normalizedCandidate = NormalizeIdentityValue(
                candidate);

            if (normalizedCandidate != null &&
                string.Equals(
                    normalizedSourceOwner,
                    normalizedCandidate,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string? NormalizeIdentityValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();

        return string.Equals(
                normalized,
                "Unknown",
                StringComparison.OrdinalIgnoreCase)
            ? null
            : normalized;
    }

    private static ClientCombatPacketDirection
        GetCombatPacketDirection(
            uint sourceObjectId,
            uint victimObjectId,
            uint localPlayerObjectId)
    {
        if (localPlayerObjectId is 0 or uint.MaxValue)
        {
            return ClientCombatPacketDirection.Unknown;
        }

        if (sourceObjectId == localPlayerObjectId &&
            victimObjectId == localPlayerObjectId)
        {
            return ClientCombatPacketDirection.Self;
        }

        if (sourceObjectId == localPlayerObjectId)
        {
            return ClientCombatPacketDirection.Outgoing;
        }

        if (victimObjectId == localPlayerObjectId)
        {
            return ClientCombatPacketDirection.Incoming;
        }

        return ClientCombatPacketDirection.ObservedThirdParty;
    }

    private static long GetWeaponStateSamplingIntervalTicks(
        ClientCombatPollingMode mode)
    {
        if (mode == ClientCombatPollingMode.Idle)
        {
            return idleWeaponStateSamplingIntervalTicks;
        }

        if (mode is
            ClientCombatPollingMode.Armed or
            ClientCombatPollingMode.Burst or
            ClientCombatPollingMode.Cooling)
        {
            return activeWeaponStateSamplingIntervalTicks;
        }

        return long.MaxValue;
    }

    private static long GetPacketPollingIntervalTicks(
        ClientCombatPollingMode mode)
    {
        if (mode ==
            ClientCombatPollingMode.Burst)
        {
            return 0;
        }

        if (mode is
            ClientCombatPollingMode.Armed or
            ClientCombatPollingMode.Cooling)
        {
            return armedPacketPollingIntervalTicks;
        }

        if (mode ==
            ClientCombatPollingMode.Idle)
        {
            return idlePacketPollingIntervalTicks;
        }

        return long.MaxValue;
    }

    private static int GetPacketPollingIntervalMicroseconds(
        ClientCombatPollingMode mode)
    {
        if (mode ==
            ClientCombatPollingMode.Burst)
        {
            return 0;
        }

        if (mode is
            ClientCombatPollingMode.Armed or
            ClientCombatPollingMode.Cooling)
        {
            return ArmedPacketPollingIntervalMicroseconds;
        }

        if (mode ==
            ClientCombatPollingMode.Idle)
        {
            return IdlePacketPollingIntervalMicroseconds;
        }

        return 0;
    }

    private static void WaitUntilNextDue(
        AutoResetEvent wakeSignal,
        Stopwatch stopwatch,
        long nextDueAtTicks,
        bool hasHotClient,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (nextDueAtTicks == long.MaxValue)
        {
            wakeSignal.WaitOne(100);
            return;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            var remainingTicks =
                nextDueAtTicks - stopwatch.ElapsedTicks;

            if (remainingTicks <= 0)
            {
                return;
            }

            // With only dormant/idle clients, block for a real millisecond.
            // Overshoot is acceptable because the 1 ms sentinel is explicitly
            // best-effort, and this prevents idle observation consuming a core.
            if (!hasHotClient &&
                remainingTicks >=
                busyWaitYieldThresholdTicks)
            {
                wakeSignal.WaitOne(1);
                return;
            }

            if (remainingTicks >
                busyWaitSleepZeroThresholdTicks)
            {
                Thread.Sleep(0);
                continue;
            }

            if (remainingTicks >
                busyWaitYieldThresholdTicks)
            {
                Thread.Yield();
                continue;
            }

            Thread.SpinWait(32);
        }
    }

    private static long MicrosecondsToStopwatchTicks(
        int microseconds)
    {
        return Math.Max(
            1L,
            checked(
                (long)Math.Ceiling(
                    microseconds *
                    Stopwatch.Frequency /
                    1_000_000d)));
    }

    private static long MillisecondsToStopwatchTicks(
        int milliseconds)
    {
        return Math.Max(
            1L,
            checked(
                (long)Math.Ceiling(
                    milliseconds *
                    Stopwatch.Frequency /
                    1000d)));
    }

    private static int StopwatchTicksToMilliseconds(
        long stopwatchTicks)
    {
        return Math.Max(
            0,
            checked(
                (int)Math.Ceiling(
                    stopwatchTicks *
                    1000d /
                    Stopwatch.Frequency)));
    }

    private sealed class CombatProcessState(
        int processId)
    {
        private readonly Lock pendingTargetLock = new();
        private CombatObservationTarget? pendingTarget;
        private bool removed;

        public int ProcessId { get; } = processId;

        public CombatObservationTarget CurrentTarget { get; set; } =
            CombatObservationTarget.Unavailable(
                processId,
                0,
                "Combat observer is waiting for SClient");

        public ProcessMemoryReader? Memory { get; set; }

        public uint ConnectionWrapperAddress { get; set; }

        public uint MusicManagerAddress { get; set; }

        public bool HasContextSample { get; set; }

        public int CurrentMusicTypeRaw { get; set; } = -1;

        public int PreviousMusicTypeRaw { get; set; } = -1;

        public ClientCombatPollingMode Mode { get; set; } =
            ClientCombatPollingMode.Dormant;

        public ClientCombatArmingSource ArmingSource { get; set; }

        public long NextReaderOpenAtTicks { get; set; }

        public long NextTopologyValidationAtTicks { get; set; }

        public long NextContextSampleAtTicks { get; set; }

        public long NextPacketPollAtTicks { get; set; }

        public long NextWeaponStateSampleAtTicks { get; set; }

        public long NextSnapshotPublishAtTicks { get; set; }

        public long BurstUntilTicks { get; set; }

        public long CoolingUntilTicks { get; set; }

        public long GroupArmUntilTicks { get; set; }

        public long WeaponArmUntilTicks { get; set; }

        public long WeaponStateSampleCount { get; set; }

        public long WeaponActivationCount { get; set; }

        public long WeaponActivationPhaseCount { get; set; }

        public long WeaponBusyTransitionCount { get; set; }

        public long WeaponStateReadFailureCount { get; set; }

        public DateTimeOffset? LastWeaponActivationObservedAt { get; set; }

        public Dictionary<uint, uint> WeaponItemStates { get; } = [];

        public HashSet<uint> WeaponActivationPhaseSeen { get; } = [];

        public long PacketPollCount { get; set; }

        public long IdlePacketPollCount { get; set; }

        public long ArmedPacketPollCount { get; set; }

        public long BurstPacketPollCount { get; set; }

        public long ContextSampleCount { get; set; }

        public long CachedPacketObservationCount { get; set; }

        public long UniquePacketCount { get; set; }

        public long DamagePacketCount { get; set; }

        public long DuplicatePacketObservationCount { get; set; }

        public long PacketReadFailureCount { get; set; }

        public long ContextReadFailureCount { get; set; }

        public DateTimeOffset? LastDamageObservedAt { get; set; }

        public long NextEventSequence { get; set; } = 1;

        public string LastError { get; set; } = "";

        public byte[] PacketBuffer { get; } =
            new byte[DamagePacketSize];

        public HashSet<uint> SeenMessageInstanceIds { get; } = [];

        public Queue<uint> SeenMessageInstanceIdOrder { get; } = [];

        public List<ClientCombatEventObservation> RecentEvents { get; } = [];

        public ClientCombatObservation Snapshot
        {
            get => Volatile.Read(
                ref field);

            set => Volatile.Write(
                ref field,
                value);
        } = ClientCombatObservation.Unavailable(
            "Combat observer is waiting for SClient");

        public bool IsRemoved
        {
            get
            {
                lock (this.pendingTargetLock)
                {
                    return this.removed;
                }
            }
        }

        public void SetPendingTarget(
            CombatObservationTarget target)
        {
            lock (this.pendingTargetLock)
            {
                if (this.removed)
                {
                    return;
                }

                this.pendingTarget = target;
            }
        }

        public bool TryTakePendingTarget(
            out CombatObservationTarget target)
        {
            lock (this.pendingTargetLock)
            {
                if (this.pendingTarget == null)
                {
                    target = null!;
                    return false;
                }

                target = this.pendingTarget;
                this.pendingTarget = null;
                return true;
            }
        }

        public void MarkRemoved()
        {
            lock (this.pendingTargetLock)
            {
                this.removed = true;
                this.pendingTarget = null;
            }
        }

        public void DisposeReader()
        {
            this.Memory?.Dispose();
            this.Memory = null;
            this.ConnectionWrapperAddress = 0;
        }

        public void ResetSession()
        {
            this.ConnectionWrapperAddress = 0;
            this.MusicManagerAddress = 0;
            this.HasContextSample = false;
            this.CurrentMusicTypeRaw = -1;
            this.PreviousMusicTypeRaw = -1;
            this.Mode = ClientCombatPollingMode.Dormant;
            this.ArmingSource = ClientCombatArmingSource.None;
            this.NextTopologyValidationAtTicks = 0;
            this.NextContextSampleAtTicks = 0;
            this.NextPacketPollAtTicks = 0;
            this.NextWeaponStateSampleAtTicks = 0;
            this.NextSnapshotPublishAtTicks = 0;
            this.BurstUntilTicks = 0;
            this.CoolingUntilTicks = 0;
            this.GroupArmUntilTicks = 0;
            this.WeaponArmUntilTicks = 0;
            this.WeaponStateSampleCount = 0;
            this.WeaponActivationCount = 0;
            this.WeaponActivationPhaseCount = 0;
            this.WeaponBusyTransitionCount = 0;
            this.WeaponStateReadFailureCount = 0;
            this.LastWeaponActivationObservedAt = null;
            this.WeaponItemStates.Clear();
            this.WeaponActivationPhaseSeen.Clear();
            this.PacketPollCount = 0;
            this.IdlePacketPollCount = 0;
            this.ArmedPacketPollCount = 0;
            this.BurstPacketPollCount = 0;
            this.ContextSampleCount = 0;
            this.CachedPacketObservationCount = 0;
            this.UniquePacketCount = 0;
            this.DamagePacketCount = 0;
            this.DuplicatePacketObservationCount = 0;
            this.PacketReadFailureCount = 0;
            this.ContextReadFailureCount = 0;
            this.LastDamageObservedAt = null;
            this.NextEventSequence = 1;
            this.LastError = "";
            this.SeenMessageInstanceIds.Clear();
            this.SeenMessageInstanceIdOrder.Clear();
            this.RecentEvents.Clear();
        }
    }

    private sealed record CombatObservationTarget(
        int ProcessId,
        bool HasClientContext,
        bool AllowPacketPolling,
        string Status,
        uint ModuleBaseAddress,
        uint ClientContextAddress,
        uint ActiveSectorNumber,
        uint LocalPlayerObjectId,
        string LocalPlayerName,
        string LocalPlayerOwner,
        uint ExpectedConnectionWrapperVTableAddress,
        uint ExpectedDamagePacketVTableAddress,
        IReadOnlyList<uint> WeaponItemStatePropertyAddresses,
        IReadOnlyList<uint> GroupMemberObjectIds)
    {
        public static CombatObservationTarget Unavailable(
            int processId,
            uint clientContextAddress,
            string status)
        {
            return new CombatObservationTarget(
                processId,
                HasClientContext: false,
                AllowPacketPolling: false,
                status,
                0,
                clientContextAddress,
                0,
                0,
                "",
                "",
                0,
                0,
                [],
                []);
        }
    }

    private readonly record struct ProcessClientResult(
        long NextDueAtTicks,
        bool IsHot,
        bool ShouldArmGroupPeers)
    {
        public static ProcessClientResult None { get; } =
            new(
                long.MaxValue,
                IsHot: false,
                ShouldArmGroupPeers: false);
    }
}
