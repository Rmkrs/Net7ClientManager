namespace Net7ClientManager.Addons.Contracts;

public sealed record AddonActionRequest
{
    public required AddonActionKind Kind { get; init; }

    public required int OwnerProcessId { get; init; }

    public required string AddonId { get; init; }

    public uint ObjectId { get; init; }

    public uint? ExpectedSectorId { get; init; }

    public long SnapshotSequence { get; init; }
}
