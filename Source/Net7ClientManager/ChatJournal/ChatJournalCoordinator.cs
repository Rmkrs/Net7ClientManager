namespace Net7ClientManager.ChatJournal;

using System.Diagnostics;
using System.Globalization;
using Net7ClientManager.Models;
using Net7ClientManager.Observations;

/// <summary>
/// Associates the raw chat observation stream with the live pilot, persists
/// ordinary new messages, and retains the game's initial ring snapshot only
/// for the current Client Manager session.
/// </summary>
internal sealed class ChatJournalCoordinator
{
    private readonly ChatJournalStore store;
    private readonly Lock stateLock = new();
    private readonly Dictionary<int, ProcessState> processStates = [];
    private readonly Dictionary<uint, List<ChatJournalEntry>> transientEntries = [];
    private readonly Dictionary<uint, long> revisions = [];

    public ChatJournalCoordinator(ChatJournalStore store)
    {
        this.store = store ??
            throw new ArgumentNullException(nameof(store));
    }

    public event EventHandler<ChatJournalChangedEventArgs>?
        JournalChanged;

    public ChatJournalSnapshot GetSnapshot(
        uint characterId,
        int maximumResults = 5000)
    {
        maximumResults = Math.Clamp(maximumResults, 1, 10000);
        var persisted = this.store.GetHistory(characterId, maximumResults);
        ChatJournalEntry[] transient;
        long revision;

        lock (this.stateLock)
        {
            transient = this.transientEntries.TryGetValue(
                    characterId,
                    out var entries)
                ? [.. entries]
                : [];
            this.revisions.TryGetValue(characterId, out revision);
        }

        var merged = persisted
            .Concat(transient)
            .GroupBy(
                entry => BuildSourceKey(
                    entry.ProcessId,
                    entry.ProcessStartedAt,
                    entry.Sequence,
                    entry.Channel,
                    entry.Text),
                StringComparer.Ordinal)
            .Select(group => group
                .OrderBy(entry => entry.IsSnapshot)
                .First())
            .OrderBy(entry => entry.ObservedAt)
            .ThenBy(entry => entry.ProcessStartedAt)
            .ThenBy(entry => entry.Sequence)
            .TakeLast(maximumResults)
            .ToArray();
        var pilotName = merged.LastOrDefault()?.PilotName ?? "";

        return new ChatJournalSnapshot
        {
            CharacterId = characterId,
            PilotName = pilotName,
            Revision = revision,
            Entries = merged,
        };
    }

    public void Observe(
        ClientInstance client,
        ClientChatMessage message)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(message);

        var identity = client.LiveCharacterIdentity;

        if (!identity.IsAvailable ||
            identity.CharacterObjectId is not { } characterId ||
            string.IsNullOrWhiteSpace(identity.Name))
        {
            return;
        }

        var pilotName = identity.Name.Trim();
        var processStartedAt = ResolveProcessStartedAt(client);
        ChatJournalEntry? acceptedEntry = null;
        long revision = 0;

        try
        {
            lock (this.stateLock)
            {
                if (!this.processStates.TryGetValue(
                        client.ProcessId,
                        out var state) ||
                    state.CharacterId != characterId ||
                    state.ProcessStartedAt != processStartedAt)
                {
                    state = new ProcessState(
                        characterId,
                        processStartedAt,
                        Guid.NewGuid().ToString("N"));
                    this.processStates[client.ProcessId] = state;
                }

                var sourceKey = BuildSourceKey(
                    client.ProcessId,
                    processStartedAt,
                    message.Sequence,
                    message.Channel,
                    message.Text);

                if (!state.SeenSources.Add(sourceKey))
                {
                    return;
                }

                if (message.IsSnapshot &&
                    this.store.ContainsObservedSource(
                        characterId,
                        client.ProcessId,
                        processStartedAt,
                        message.Sequence,
                        message.Channel,
                        message.Text))
                {
                    return;
                }

                var entry = new ChatJournalEntry
                {
                    EntryId = string.Create(
                        CultureInfo.InvariantCulture,
                        $"{state.SessionId}:{message.Sequence}"),
                    SessionId = state.SessionId,
                    CharacterId = characterId,
                    PilotName = pilotName,
                    ProcessId = client.ProcessId,
                    ProcessStartedAt = processStartedAt,
                    Sequence = message.Sequence,
                    Channel = message.Channel,
                    Text = message.Text,
                    ObservedAt = message.ObservedAt,
                    IsSnapshot = message.IsSnapshot,
                };

                if (message.IsSnapshot)
                {
                    if (!this.transientEntries.TryGetValue(
                            characterId,
                            out var transient))
                    {
                        transient = [];
                        this.transientEntries[characterId] = transient;
                    }

                    transient.Add(entry);
                }
                else if (!this.store.Save(entry))
                {
                    return;
                }

                revision = this.revisions.TryGetValue(
                        characterId,
                        out var currentRevision)
                    ? currentRevision + 1
                    : 1;
                this.revisions[characterId] = revision;
                acceptedEntry = entry;
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine(
                string.Concat(
                    "Chat Journal observation failed: ",
                    exception),
                "Net7.ChatJournal");
            return;
        }

        if (acceptedEntry != null)
        {
            this.JournalChanged?.Invoke(
                this,
                new ChatJournalChangedEventArgs(
                    characterId,
                    revision));
        }
    }

    public void ForgetProcess(int processId)
    {
        lock (this.stateLock)
        {
            this.processStates.Remove(processId);
        }
    }

    private static DateTimeOffset ResolveProcessStartedAt(
        ClientInstance client)
    {
        try
        {
            return new DateTimeOffset(
                client.Process.StartTime.ToUniversalTime());
        }
        catch (Exception)
        {
            return client.StartedByManagerAt ??
                   client.DockedAt ??
                   DateTimeOffset.MinValue;
        }
    }

    private static string BuildSourceKey(
        int processId,
        DateTimeOffset processStartedAt,
        uint sequence,
        int channel,
        string text)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{processId}|{processStartedAt:O}|{sequence}|{channel}|{text}");
    }

    private sealed class ProcessState(
        uint characterId,
        DateTimeOffset processStartedAt,
        string sessionId)
    {
        public uint CharacterId { get; } = characterId;

        public DateTimeOffset ProcessStartedAt { get; } = processStartedAt;

        public string SessionId { get; } = sessionId;

        public HashSet<string> SeenSources { get; } =
            new(StringComparer.Ordinal);
    }
}
