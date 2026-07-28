namespace Net7ClientManager.Navigation;

using System.Text.Json.Serialization;

public sealed record GalaxyNavigationCatalogDeparture
{
    public string? ToSectorKey { get; init; }

    public required string DestinationName { get; init; }

    public required string DepartureTargetName { get; init; }

    public required byte RawObjectType { get; init; }

    [JsonIgnore]
    public GalaxyNavigationTargetKind Kind =>
        GalaxyNavigationTargetKinds.FromRawObjectType(
            this.RawObjectType);

    public GalaxyNavigationDepartureStatus Status { get; init; } =
        GalaxyNavigationDepartureStatus.Verified;

    public bool HasExpectedPosition { get; init; }

    public float ExpectedX { get; init; }

    public float ExpectedY { get; init; }

    public float ExpectedZ { get; init; }

    public uint DestinationSectorNumber { get; init; }

    public int VerificationCount { get; init; }

    public string AccessRequirement { get; init; } = "";

    public string Note { get; init; } = "";
}
