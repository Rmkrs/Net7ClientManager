namespace Net7ClientManager.Navigation;

internal sealed record ForgeNavigationDepartureDocument
{
    public required string Id { get; init; }

    public int Ordinal { get; init; }

    public string? ToSectorKey { get; init; }

    public required string DestinationName { get; init; }

    public required string DepartureTargetName { get; init; }

    public byte RawObjectType { get; init; }

    public required string Status { get; init; }

    public bool HasExpectedPosition { get; init; }

    public float ExpectedX { get; init; }

    public float ExpectedY { get; init; }

    public float ExpectedZ { get; init; }

    public uint DestinationSectorNumber { get; init; }

    public int VerificationCount { get; init; }

    public string AccessRequirement { get; init; } = "";

    public string Note { get; init; } = "";
}
