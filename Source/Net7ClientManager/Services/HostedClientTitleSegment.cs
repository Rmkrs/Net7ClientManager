namespace Net7ClientManager.Services;

public sealed record HostedClientTitleSegment
{
    public required HostedClientTitleSegmentKind Kind { get; init; }

    public required string Label { get; init; }

    public required string Value { get; init; }

    public string? CompactValue { get; init; }

    public string? PlainText { get; init; }

    public int Priority { get; init; }

    public bool CanHide { get; init; } = true;

    public bool IsEmphasized { get; init; }
}
