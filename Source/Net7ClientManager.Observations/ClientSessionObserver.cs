namespace Net7ClientManager.Observations;

using System.Text;
internal sealed class ClientSessionObserver
{
    private const uint ClientContextCurrentTime = 0x10bc;
    private const uint ClientContextLocalPlayerObjectId = 0x112c;
    private const uint ClientContextTargetObjectId = 0x1130;
    private const uint ClientContextLoadingOrTransitionFlag = 0x1198;
    private const uint ClientContextChatPanel = 0x127c;
    private const uint ClientContextLocalPlayerAuxData = 0x12c0;
    private const uint ClientContextPresentationMode = 0x136c;
    private const uint ClientContextDockingTargetObjectId = 0x13ac;

    private const uint ClientContextPendingLandOrDockTargetObjectId =
        0x13b8;

    private const uint ChatPanelChatHistoryRing = 0x13c;

    private const uint RingCapacity = 0x04;
    private const uint RingWriteCounter = 0x08;
    private const uint RingEntries = 0x0c;

    private const uint RingEntrySize = 0x14;
    private const uint RingEntryChannel = 0x00;
    private const uint RingEntryTextPtr = 0x08;
    private const uint RingEntryTextLength = 0x0c;

    private const int MaxMessagesPerPoll = 50;
    private const int InitialSnapshotMessageCount = 10;

    public void RefreshState(
        ProcessMemoryReader memory,
        ObservedClientState state,
        string rootSource)
    {
        _ = this.TryReadDirectClientState(
            memory,
            state);

        this.RefreshChatRing(
            memory,
            state,
            rootSource);
    }

    public IReadOnlyList<ClientChatMessage> ReadNewMessages(
        ProcessMemoryReader memory,
        ObservedClientState state)
    {
        if (state.RingAddress == 0)
        {
            return [];
        }

        return this.ReadNewMessagesCore(
            memory,
            state);
    }

    private void RefreshChatRing(
        ProcessMemoryReader memory,
        ObservedClientState state,
        string rootSource)
    {
        if (!this.TryLocateChatRingFromClientContext(
                memory,
                state.ClientContextAddress,
                out var located))
        {
            ClearChatRing(
                state,
                "Client context available, waiting for chat ring");

            return;
        }

        var ringChanged =
            state.RingAddress != located.Ring;

        state.ChatPanelAddress =
            located.ChatPanel;

        state.RingAddress =
            located.Ring;

        state.RingCapacity =
            located.Capacity;

        state.RingEntries =
            located.Entries;

        if (ringChanged)
        {
            state.LastWriteCounter = null;
        }

        state.StatusText =
            $"Available via {rootSource}: Kernel=0x{state.KernelAddress:X8}, Task=0x{state.ClientSessionTaskAddress:X8}, SClient=0x{state.ClientContextAddress:X8}, Ring=0x{located.Ring:X8}, Capacity={located.Capacity}";
    }

    private bool TryLocateChatRingFromClientContext(
        ProcessMemoryReader memory,
        uint clientContextAddress,
        out LocatedChatRing located)
    {
        located = default;

        if (!memory.TryReadUInt32(
                clientContextAddress +
                ClientContextChatPanel,
                out var chatPanel) ||
            chatPanel == 0)
        {
            return false;
        }

        if (!memory.TryReadUInt32(
                chatPanel +
                ChatPanelChatHistoryRing,
                out var ring) ||
            ring == 0)
        {
            return false;
        }

        if (!memory.TryReadUInt32(
                ring + RingCapacity,
                out var capacity) ||
            !memory.TryReadUInt32(
                ring + RingWriteCounter,
                out var writeCounter) ||
            !memory.TryReadUInt32(
                ring + RingEntries,
                out var entries))
        {
            return false;
        }

        if (!IsSaneRing(
                capacity,
                writeCounter,
                entries))
        {
            return false;
        }

        located = new LocatedChatRing(
            chatPanel,
            ring,
            capacity,
            writeCounter,
            entries);

        return true;
    }

    private bool TryReadDirectClientState(
        ProcessMemoryReader memory,
        ObservedClientState state)
    {
        if (state.ClientContextAddress == 0)
        {
            state.DirectClientStateStatus =
                "SClient address is zero";

            state.HasDirectClientState = false;

            return false;
        }

        var clientContextAddress =
            state.ClientContextAddress;

        if (!memory.TryReadUInt32(
                clientContextAddress +
                ClientContextCurrentTime,
                out var currentTime))
        {
            return FailDirectClientState(
                state,
                "CurrentTime",
                clientContextAddress +
                ClientContextCurrentTime);
        }

        if (!memory.TryReadUInt32(
                clientContextAddress +
                ClientContextLocalPlayerObjectId,
                out var localPlayerObjectId))
        {
            return FailDirectClientState(
                state,
                "LocalPlayerObjectId",
                clientContextAddress +
                ClientContextLocalPlayerObjectId);
        }

        if (!memory.TryReadUInt32(
                clientContextAddress +
                ClientContextTargetObjectId,
                out var targetObjectId))
        {
            return FailDirectClientState(
                state,
                "TargetObjectId",
                clientContextAddress +
                ClientContextTargetObjectId);
        }

        if (!memory.TryReadUInt32(
                clientContextAddress +
                ClientContextLoadingOrTransitionFlag,
                out var loadingOrTransitionFlag))
        {
            return FailDirectClientState(
                state,
                "LoadingOrTransitionFlag",
                clientContextAddress +
                ClientContextLoadingOrTransitionFlag);
        }

        if (!memory.TryReadUInt32(
                clientContextAddress +
                ClientContextLocalPlayerAuxData,
                out var localPlayerAuxDataAddress))
        {
            return FailDirectClientState(
                state,
                "LocalPlayerAuxData",
                clientContextAddress +
                ClientContextLocalPlayerAuxData);
        }

        if (!memory.TryReadUInt32(
                clientContextAddress +
                ClientContextPresentationMode,
                out var presentationMode))
        {
            return FailDirectClientState(
                state,
                "PresentationMode",
                clientContextAddress +
                ClientContextPresentationMode);
        }

        if (!memory.TryReadUInt32(
                clientContextAddress +
                ClientContextDockingTargetObjectId,
                out var dockingTargetObjectId))
        {
            return FailDirectClientState(
                state,
                "DockingTargetObjectId",
                clientContextAddress +
                ClientContextDockingTargetObjectId);
        }

        if (!memory.TryReadUInt32(
                clientContextAddress +
                ClientContextPendingLandOrDockTargetObjectId,
                out var pendingLandOrDockTargetObjectId))
        {
            return FailDirectClientState(
                state,
                "PendingLandOrDockTargetObjectId",
                clientContextAddress +
                ClientContextPendingLandOrDockTargetObjectId);
        }

        state.CurrentClientTime =
            currentTime;

        state.LocalPlayerObjectId =
            localPlayerObjectId;

        state.TargetObjectId =
            targetObjectId;

        state.LoadingOrTransitionFlag =
            loadingOrTransitionFlag;

        state.LocalPlayerAuxDataAddress =
            localPlayerAuxDataAddress;

        state.PresentationMode =
            presentationMode;

        state.DockingTargetObjectId =
            dockingTargetObjectId;

        state.PendingLandOrDockTargetObjectId =
            pendingLandOrDockTargetObjectId;

        state.LastStateObservedAt =
            DateTimeOffset.UtcNow;

        state.DirectClientStateStatus =
            "Available";

        state.HasDirectClientState = true;

        return true;
    }

    private IReadOnlyList<ClientChatMessage> ReadNewMessagesCore(
        ProcessMemoryReader memory,
        ObservedClientState state)
    {
        if (!memory.TryReadUInt32(
                state.RingAddress +
                RingWriteCounter,
                out var writeCounter))
        {
            ClearChatRing(
                state,
                "Could not read write counter, will rescan");

            return [];
        }

        if (writeCounter == 0)
        {
            state.LastWriteCounter = 0;
            return [];
        }

        var previous =
            state.LastWriteCounter;

        uint firstSequence;

        var isSnapshot =
            previous == null ||
            previous > writeCounter;

        if (isSnapshot)
        {
            var snapshotCount = Math.Min(
                InitialSnapshotMessageCount,
                Math.Min(
                    state.RingCapacity,
                    writeCounter));

            firstSequence =
                writeCounter - snapshotCount;
        }
        else if (previous == writeCounter)
        {
            return [];
        }
        else if (previous is { } previousWriteCounter)
        {
            firstSequence = previousWriteCounter;
        }
        else
        {
            return [];
        }

        var availableCount =
            writeCounter - firstSequence;

        var count = Math.Min(
            MaxMessagesPerPoll,
            Math.Min(
                state.RingCapacity,
                availableCount));

        firstSequence =
            writeCounter - count;

        List<ClientChatMessage> messages = [];

        for (var sequence = firstSequence;
             sequence < writeCounter;
             sequence++)
        {
            if (!this.TryReadMessage(
                    memory,
                    state,
                    sequence,
                    isSnapshot,
                    out var message))
            {
                continue;
            }

            state.LastMessageAt =
                message.ObservedAt;

            messages.Add(message);
        }

        state.LastWriteCounter =
            writeCounter;

        return messages;
    }

    private bool TryReadMessage(
        ProcessMemoryReader memory,
        ObservedClientState state,
        uint sequence,
        bool isSnapshot,
        out ClientChatMessage message)
    {
        message = null!;

        var index =
            sequence % state.RingCapacity;

        var entry =
            state.RingEntries +
            index * RingEntrySize;

        if (!memory.TryReadUInt32(
                entry + RingEntryChannel,
                out var channel) ||
            !memory.TryReadUInt32(
                entry + RingEntryTextPtr,
                out var textPointer) ||
            !memory.TryReadUInt32(
                entry + RingEntryTextLength,
                out var textLength))
        {
            return false;
        }

        if (textPointer == 0 ||
            textLength > 4096)
        {
            return false;
        }

        if (!memory.TryReadBytes(
                textPointer,
                checked((int)textLength),
                out var bytes))
        {
            return false;
        }

        var text = Encoding.Latin1
            .GetString(bytes)
            .Replace(
                "\0",
                "",
                StringComparison.Ordinal);

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        message = new ClientChatMessage(
            state.ProcessId,
            sequence,
            (int)channel,
            text,
            DateTimeOffset.UtcNow,
            isSnapshot);

        return true;
    }

    private static bool FailDirectClientState(
        ObservedClientState state,
        string fieldName,
        uint address)
    {
        state.HasDirectClientState = false;

        state.DirectClientStateStatus =
            $"Could not read {fieldName} at 0x{address:X8}";

        return false;
    }

    private static void ClearChatRing(
        ObservedClientState state,
        string statusText)
    {
        state.ChatPanelAddress = 0;
        state.RingAddress = 0;
        state.RingCapacity = 0;
        state.RingEntries = 0;
        state.LastWriteCounter = null;

        if (!string.IsNullOrWhiteSpace(
                statusText))
        {
            state.StatusText = statusText;
        }
    }

    private static bool IsSaneRing(
        uint capacity,
        uint writeCounter,
        uint entries)
    {
        return capacity is > 0 and <= 1000 &&
               writeCounter < 100_000_000 &&
               entries != 0;
    }
}
