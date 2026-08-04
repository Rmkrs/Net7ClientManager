namespace Net7ClientManager.Models;

public sealed record GameBuffOverlayPresentation
{
    public DateTimeOffset ObservedAt { get; init; }

    public IReadOnlyList<GameBuffOverlayEntry> Buffs { get; init; } = [];

    public static GameBuffOverlayPresentation Empty { get; } = new();
}

public sealed record GameBuffOverlayEntry
{
    public int Slot { get; init; }

    public string Identity { get; init; } = "";

    public string Name { get; init; } = "";

    public string Description { get; init; } = "";

    public bool? IsGoodBuff { get; init; }

    public bool? IsPermanent { get; init; }

    public long? RemainingMilliseconds { get; init; }

    public ulong? RemovalAtClientTime { get; init; }
}
