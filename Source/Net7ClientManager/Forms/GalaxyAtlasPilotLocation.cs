namespace Net7ClientManager.Forms;

internal sealed record GalaxyAtlasPilotLocation
{
    public required int ProcessId { get; init; }

    public required string PilotName { get; init; }

    public required string SectorKey { get; init; }

    public float X { get; init; }

    public float Y { get; init; }

    public float Z { get; init; }

    public bool IsSelectedPilot { get; init; }

    public bool IsGroupMember { get; init; }

    public bool IsSocialPilot { get; init; }

    public bool IsNearNavApproximation { get; init; }

    public bool IsDocked { get; init; }

    public string? AnchorName { get; init; }

    public DateTimeOffset ObservedAt { get; init; }
}
