namespace Net7ClientManager.Observations;

public sealed record ClientChatMessage(
    int ProcessId,
    uint Sequence,
    int Channel,
    string Text,
    DateTimeOffset ObservedAt,
    bool IsSnapshot);
