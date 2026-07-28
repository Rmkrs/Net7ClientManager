using System.Diagnostics;
using System.Globalization;

namespace Net7ClientManager.Observations;

internal sealed class ClientTopologyObserver
{
    private const uint ImageBase = 0x00400000;

    private const uint KernelInstanceStatic = 0x00bdadbc;
    private const uint KernelInstanceRva =
        KernelInstanceStatic - ImageBase;

    private const uint KernelTaskListSentinel = 0x14;
    private const uint KernelTaskNodeNext = 0x00;
    private const uint KernelTaskNodePrevious = 0x04;
    private const uint KernelTaskNodeTask = 0x08;

    private const uint InitialLoadTaskVTableStatic =
        0x00b04280;

    private const uint InitialLoadTaskVTableRva =
        InitialLoadTaskVTableStatic - ImageBase;

    private const uint InitialLoadTaskKernel = 0x04;

    private const uint LoginTaskVTableStatic =
        0x00b040d4;

    private const uint LoginTaskVTableRva =
        LoginTaskVTableStatic - ImageBase;

    private const uint LoginTaskKernel = 0x04;
    private const uint LoginTaskCharacterView = 0x10;
    private const uint LoginTaskState = 0x24;

    private const uint CharacterViewMode = 0x1ac;

    private const uint ClientSessionTaskVTableStatic =
        0x00b03ff4;

    private const uint ClientSessionTaskVTableRva =
        ClientSessionTaskVTableStatic - ImageBase;

    private const uint ClientSessionTaskKernel = 0x04;
    private const uint ClientSessionTaskClientContext = 0x78;

    private const uint ClientContextVTableStatic =
        0x00b03240;

    private const uint ClientContextVTableRva =
        ClientContextVTableStatic - ImageBase;

    private const uint ClientContextOwningTask = 0x1384;

    private const int MaxKernelTaskCount = 256;

    public string? Refresh(
        Process process,
        ProcessMemoryReader memory,
        ObservedClientState state)
    {
        var moduleBase =
            ProcessMemoryReader.GetMainModuleBase(
                process);

        state.ModuleBaseAddress = moduleBase;

        var initialLoadTaskVTable = checked(
            moduleBase + InitialLoadTaskVTableRva);

        var loginTaskVTable = checked(
            moduleBase + LoginTaskVTableRva);

        var sessionTaskVTable = checked(
            moduleBase + ClientSessionTaskVTableRva);

        var clientContextVTable = checked(
            moduleBase + ClientContextVTableRva);

        var kernelPointerAddress = checked(
            moduleBase + KernelInstanceRva);

        if (!memory.TryReadUInt32(
                kernelPointerAddress,
                out var kernelAddress) ||
            kernelAddress == 0)
        {
            ClearKernelTopology(
                state,
                "Attached, waiting for Kernel");

            return null;
        }

        if (!memory.TryReadUInt32(
                kernelAddress,
                out var kernelVTable) ||
            kernelVTable == 0)
        {
            ClearKernelTopology(
                state,
                $"Kernel=0x{kernelAddress:X8}, could not read Kernel vtable");

            return null;
        }

        state.KernelAddress = kernelAddress;

        /*
         * Strongest state first.
         *
         * Preserve the proven session lookup path exactly:
         * walk the Kernel task list and return immediately when
         * ClientSessionTask is found.
         */
        if (this.TryLocateClientContextFromKernel(
                memory,
                kernelAddress,
                sessionTaskVTable,
                clientContextVTable,
                out var taskAddress,
                out var clientContextAddress))
        {
            ClearInitialLoadTopology(state);
            ClearLoginTopology(state);

            SetGameplaySession(
                state,
                taskAddress,
                clientContextAddress);

            return "Kernel task list";
        }

        /*
         * LoginTask owns both the login screen and character-selection
         * pipeline. CharacterView mode distinguishes those screens.
         */
        if (this.TryLocateLoginTaskFromKernel(
                memory,
                kernelAddress,
                loginTaskVTable,
                out var loginTask))
        {
            ClearInitialLoadTopology(state);
            ClearGameplaySession(state, "");

            state.LoginTaskAddress =
                loginTask.TaskAddress;

            state.LoginTaskState =
                loginTask.State;

            state.CharacterViewAddress =
                loginTask.CharacterViewAddress;

            state.CharacterViewMode =
                loginTask.CharacterViewMode;

            var loginState =
                loginTask.State?.ToString(
                    CultureInfo.InvariantCulture) ?? "unavailable";

            var characterViewMode =
                loginTask.CharacterViewMode?.ToString(
                    CultureInfo.InvariantCulture) ?? "unavailable";

            state.StatusText =
                $"LoginTask=0x{loginTask.TaskAddress:X8}, State={loginState}, CharacterView=0x{loginTask.CharacterViewAddress:X8}, Mode={characterViewMode}; no active gameplay session";

            return null;
        }

        /*
         * Runtime observation showed InitialLoadTask only while the
         * Bink sizzle was playing. Its constructor also initializes
         * the Bink sound system.
         */
        if (this.TryLocateInitialLoadTaskFromKernel(
                memory,
                kernelAddress,
                initialLoadTaskVTable,
                out var initialLoadTaskAddress))
        {
            ClearLoginTopology(state);
            ClearGameplaySession(state, "");

            state.InitialLoadTaskAddress =
                initialLoadTaskAddress;

            state.StatusText =
                $"InitialLoadTask=0x{initialLoadTaskAddress:X8}; intro scene / sizzle active";

            return null;
        }

        ClearInitialLoadTopology(state);
        ClearLoginTopology(state);

        /*
         * Preserve the existing full-memory scan as session fallback
         * only. It does not participate in intro or login detection.
         */
        var now = DateTimeOffset.UtcNow;

        if (now >= state.NextFallbackScanAt)
        {
            state.NextFallbackScanAt =
                now.AddSeconds(5);

            if (this.TryLocateClientContextByScan(
                    memory,
                    kernelAddress,
                    sessionTaskVTable,
                    clientContextVTable,
                    out taskAddress,
                    out clientContextAddress))
            {
                SetGameplaySession(
                    state,
                    taskAddress,
                    clientContextAddress);

                return "validated scan fallback";
            }
        }

        ClearGameplaySession(
            state,
            $"Kernel available at 0x{kernelAddress:X8}; no active InitialLoadTask, LoginTask or gameplay session");

        return null;
    }

    private bool TryLocateInitialLoadTaskFromKernel(
        ProcessMemoryReader memory,
        uint kernelAddress,
        uint expectedInitialLoadTaskVTable,
        out uint taskAddress)
    {
        taskAddress = 0;

        var sentinelAddress = checked(
            kernelAddress + KernelTaskListSentinel);

        if (!memory.TryReadUInt32(
                sentinelAddress + KernelTaskNodeNext,
                out var nodeAddress))
        {
            return false;
        }

        var visited = new HashSet<uint>();

        for (var index = 0;
             index < MaxKernelTaskCount &&
             nodeAddress != 0 &&
             nodeAddress != sentinelAddress;
             index++)
        {
            if (!visited.Add(nodeAddress))
            {
                return false;
            }

            if (!this.TryValidateKernelTaskNode(
                    memory,
                    nodeAddress,
                    sentinelAddress,
                    out var nextNodeAddress,
                    out var candidateTaskAddress))
            {
                return false;
            }

            if (candidateTaskAddress != 0 &&
                memory.TryReadUInt32(
                    candidateTaskAddress,
                    out var candidateVTable) &&
                candidateVTable ==
                expectedInitialLoadTaskVTable &&
                memory.TryReadUInt32(
                    candidateTaskAddress +
                    InitialLoadTaskKernel,
                    out var owningKernelAddress) &&
                owningKernelAddress == kernelAddress)
            {
                taskAddress = candidateTaskAddress;
                return true;
            }

            nodeAddress = nextNodeAddress;
        }

        return false;
    }

    private bool TryLocateClientContextFromKernel(
        ProcessMemoryReader memory,
        uint kernelAddress,
        uint expectedSessionTaskVTable,
        uint expectedClientContextVTable,
        out uint taskAddress,
        out uint clientContextAddress)
    {
        taskAddress = 0;
        clientContextAddress = 0;

        var sentinelAddress = checked(
            kernelAddress + KernelTaskListSentinel);

        if (!memory.TryReadUInt32(
                sentinelAddress + KernelTaskNodeNext,
                out var nodeAddress))
        {
            return false;
        }

        var visited = new HashSet<uint>();

        for (var index = 0;
             index < MaxKernelTaskCount &&
             nodeAddress != 0 &&
             nodeAddress != sentinelAddress;
             index++)
        {
            if (!visited.Add(nodeAddress))
            {
                return false;
            }

            if (!this.TryValidateKernelTaskNode(
                    memory,
                    nodeAddress,
                    sentinelAddress,
                    out var nextNodeAddress,
                    out var candidateTaskAddress))
            {
                return false;
            }

            if (candidateTaskAddress != 0 &&
                this.TryValidateClientSessionTask(
                    memory,
                    kernelAddress,
                    candidateTaskAddress,
                    expectedSessionTaskVTable,
                    expectedClientContextVTable,
                    out var candidateClientContextAddress))
            {
                taskAddress = candidateTaskAddress;

                clientContextAddress =
                    candidateClientContextAddress;

                return true;
            }

            nodeAddress = nextNodeAddress;
        }

        return false;
    }

    private bool TryLocateLoginTaskFromKernel(
        ProcessMemoryReader memory,
        uint kernelAddress,
        uint expectedLoginTaskVTable,
        out LoginTaskObservation observation)
    {
        observation = default;

        var sentinelAddress = checked(
            kernelAddress + KernelTaskListSentinel);

        if (!memory.TryReadUInt32(
                sentinelAddress + KernelTaskNodeNext,
                out var nodeAddress))
        {
            return false;
        }

        var visited = new HashSet<uint>();

        for (var index = 0;
             index < MaxKernelTaskCount &&
             nodeAddress != 0 &&
             nodeAddress != sentinelAddress;
             index++)
        {
            if (!visited.Add(nodeAddress))
            {
                return false;
            }

            if (!this.TryValidateKernelTaskNode(
                    memory,
                    nodeAddress,
                    sentinelAddress,
                    out var nextNodeAddress,
                    out var candidateTaskAddress))
            {
                return false;
            }

            if (candidateTaskAddress != 0 &&
                memory.TryReadUInt32(
                    candidateTaskAddress,
                    out var candidateVTable) &&
                candidateVTable ==
                expectedLoginTaskVTable &&
                memory.TryReadUInt32(
                    candidateTaskAddress +
                    LoginTaskKernel,
                    out var owningKernelAddress) &&
                owningKernelAddress == kernelAddress)
            {
                uint? loginState = null;
                uint characterViewAddress = 0;
                uint? characterViewMode = null;

                if (memory.TryReadUInt32(
                        candidateTaskAddress +
                        LoginTaskState,
                        out var observedState))
                {
                    loginState = observedState;
                }

                if (memory.TryReadUInt32(
                        candidateTaskAddress +
                        LoginTaskCharacterView,
                        out var observedCharacterViewAddress))
                {
                    characterViewAddress =
                        observedCharacterViewAddress;

                    if (characterViewAddress != 0 &&
                        memory.TryReadUInt32(
                            characterViewAddress +
                            CharacterViewMode,
                            out var observedCharacterViewMode))
                    {
                        characterViewMode =
                            observedCharacterViewMode;
                    }
                }

                observation =
                    new LoginTaskObservation(
                        candidateTaskAddress,
                        loginState,
                        characterViewAddress,
                        characterViewMode);

                return true;
            }

            nodeAddress = nextNodeAddress;
        }

        return false;
    }

    private bool TryValidateKernelTaskNode(
        ProcessMemoryReader memory,
        uint nodeAddress,
        uint sentinelAddress,
        out uint nextNodeAddress,
        out uint taskAddress)
    {
        nextNodeAddress = 0;
        taskAddress = 0;

        if (!memory.TryReadUInt32(
                nodeAddress + KernelTaskNodeNext,
                out nextNodeAddress) ||
            !memory.TryReadUInt32(
                nodeAddress + KernelTaskNodePrevious,
                out var previousNodeAddress) ||
            !memory.TryReadUInt32(
                nodeAddress + KernelTaskNodeTask,
                out taskAddress))
        {
            return false;
        }

        if (nextNodeAddress == 0 ||
            previousNodeAddress == 0)
        {
            return false;
        }

        if (nextNodeAddress != sentinelAddress)
        {
            if (!memory.TryReadUInt32(
                    nextNodeAddress +
                    KernelTaskNodePrevious,
                    out var nextNodePrevious) ||
                nextNodePrevious != nodeAddress)
            {
                return false;
            }
        }

        if (previousNodeAddress != sentinelAddress)
        {
            if (!memory.TryReadUInt32(
                    previousNodeAddress +
                    KernelTaskNodeNext,
                    out var previousNodeNext) ||
                previousNodeNext != nodeAddress)
            {
                return false;
            }
        }

        return true;
    }

    private bool TryValidateClientSessionTask(
        ProcessMemoryReader memory,
        uint kernelAddress,
        uint taskAddress,
        uint expectedSessionTaskVTable,
        uint expectedClientContextVTable,
        out uint clientContextAddress)
    {
        clientContextAddress = 0;

        if (!memory.TryReadUInt32(
                taskAddress,
                out var actualTaskVTable) ||
            actualTaskVTable !=
            expectedSessionTaskVTable)
        {
            return false;
        }

        if (!memory.TryReadUInt32(
                taskAddress +
                ClientSessionTaskKernel,
                out var owningKernelAddress) ||
            owningKernelAddress != kernelAddress)
        {
            return false;
        }

        if (!memory.TryReadUInt32(
                taskAddress +
                ClientSessionTaskClientContext,
                out clientContextAddress) ||
            clientContextAddress == 0)
        {
            return false;
        }

        if (!memory.TryReadUInt32(
                clientContextAddress,
                out var actualClientContextVTable) ||
            actualClientContextVTable !=
            expectedClientContextVTable)
        {
            return false;
        }

        if (!memory.TryReadUInt32(
                clientContextAddress +
                ClientContextOwningTask,
                out var owningTaskAddress) ||
            owningTaskAddress != taskAddress)
        {
            return false;
        }

        return true;
    }

    private bool TryLocateClientContextByScan(
        ProcessMemoryReader memory,
        uint kernelAddress,
        uint expectedSessionTaskVTable,
        uint expectedClientContextVTable,
        out uint taskAddress,
        out uint clientContextAddress)
    {
        taskAddress = 0;
        clientContextAddress = 0;

        foreach (var candidateTaskAddress
                 in memory.ScanForUInt32(
                     expectedSessionTaskVTable))
        {
            if (!this.TryValidateClientSessionTask(
                    memory,
                    kernelAddress,
                    candidateTaskAddress,
                    expectedSessionTaskVTable,
                    expectedClientContextVTable,
                    out var candidateClientContextAddress))
            {
                continue;
            }

            taskAddress = candidateTaskAddress;

            clientContextAddress =
                candidateClientContextAddress;

            return true;
        }

        return false;
    }

    private static void SetGameplaySession(
        ObservedClientState state,
        uint taskAddress,
        uint clientContextAddress)
    {
        var sessionChanged =
            state.ClientSessionTaskAddress !=
            taskAddress ||
            state.ClientContextAddress !=
            clientContextAddress;

        if (sessionChanged)
        {
            ClearDirectClientState(
                state,
                "Gameplay session changed; waiting for state sample");

            ClearChatRing(state);
        }

        state.ClientSessionTaskAddress =
            taskAddress;

        state.ClientContextAddress =
            clientContextAddress;
    }

    private static void ClearKernelTopology(
        ObservedClientState state,
        string statusText)
    {
        state.KernelAddress = 0;

        ClearInitialLoadTopology(state);
        ClearLoginTopology(state);

        ClearGameplaySession(
            state,
            statusText);
    }

    private static void ClearInitialLoadTopology(
        ObservedClientState state)
    {
        state.InitialLoadTaskAddress = 0;
    }

    private static void ClearLoginTopology(
        ObservedClientState state)
    {
        state.LoginTaskAddress = 0;
        state.LoginTaskState = null;
        state.CharacterViewAddress = 0;
        state.CharacterViewMode = null;
    }

    private static void ClearGameplaySession(
        ObservedClientState state,
        string statusText)
    {
        state.ClientSessionTaskAddress = 0;
        state.ClientContextAddress = 0;

        ClearChatRing(state);

        ClearDirectClientState(
            state,
            "No active gameplay session");

        state.StatusText = statusText;
    }

    private static void ClearDirectClientState(
        ObservedClientState state,
        string statusText)
    {
        state.HasDirectClientState = false;

        state.DirectClientStateStatus =
            statusText;

        state.CurrentClientTime = 0;
        state.LocalPlayerObjectId = 0;
        state.TargetObjectId = 0;
        state.LoadingOrTransitionFlag = 0;
        state.LocalPlayerAuxDataAddress = 0;
        state.PresentationMode = 0;
        state.DockingTargetObjectId = 0;
        state.PendingLandOrDockTargetObjectId = 0;
        state.LastStateObservedAt = null;
    }

    private static void ClearChatRing(
        ObservedClientState state)
    {
        state.ChatPanelAddress = 0;
        state.RingAddress = 0;
        state.RingCapacity = 0;
        state.RingEntries = 0;
        state.LastWriteCounter = null;
    }
}
