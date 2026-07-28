namespace Net7ClientManager.Observations.Models;

public sealed record ClientRawAuxDataValueObservation
{
    public required string Name { get; init; }

    public uint PropertyAddress { get; init; }

    public bool IsValid { get; init; }

    public uint PrimaryValue { get; init; }

    public uint SecondaryValue { get; init; }

    public bool HasSecondaryValue { get; init; }

    public int PrimaryInt32 =>
        unchecked((int)this.PrimaryValue);

    public ulong CombinedUInt64 =>
        ((ulong)this.SecondaryValue << 32) |
        this.PrimaryValue;
}
