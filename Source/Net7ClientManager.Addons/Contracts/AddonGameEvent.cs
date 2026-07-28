namespace Net7ClientManager.Addons.Contracts;

public sealed record AddonGameEvent
{
    public required string Name { get; init; }

    public required AddonGameSnapshot Snapshot { get; init; }

    /// <summary>
    /// Optional time of the underlying host event. Snapshot-derived events
    /// normally use the snapshot observation time, while journal and Forge
    /// events can preserve their original occurrence time.
    /// </summary>
    public DateTimeOffset? OccurredAt { get; init; }

    public IReadOnlyDictionary<string, object?> Data { get; init; } =
        new Dictionary<string, object?>(StringComparer.Ordinal);
}

