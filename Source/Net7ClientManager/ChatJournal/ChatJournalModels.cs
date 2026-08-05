namespace Net7ClientManager.ChatJournal;

public sealed record ChatJournalEntry
{
    public required string EntryId { get; init; }

    public required string SessionId { get; init; }

    public required uint CharacterId { get; init; }

    public required string PilotName { get; init; }

    public required int ProcessId { get; init; }

    public required DateTimeOffset ProcessStartedAt { get; init; }

    public required uint Sequence { get; init; }

    public required int Channel { get; init; }

    public required string Text { get; init; }

    public required DateTimeOffset ObservedAt { get; init; }

    public required bool IsSnapshot { get; init; }

    public bool IsPersisted => !this.IsSnapshot;
}

public sealed record ChatJournalSnapshot
{
    public required uint CharacterId { get; init; }

    public required string PilotName { get; init; }

    public required long Revision { get; init; }

    public required IReadOnlyList<ChatJournalEntry> Entries { get; init; }
}

public sealed class ChatJournalChangedEventArgs(
    uint characterId,
    long revision)
    : EventArgs
{
    public uint CharacterId { get; } = characterId;

    public long Revision { get; } = revision;
}
