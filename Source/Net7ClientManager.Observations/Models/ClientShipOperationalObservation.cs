namespace Net7ClientManager.Observations.Models;

public sealed record ClientShipOperationalObservation
{
    public bool IsAvailable { get; init; }

    public string Status { get; init; } = "";

    public uint AuxDataLookupAddress { get; init; }

    public int PresentPropertyCount { get; init; }

    public int ValidPropertyCount { get; init; }

    public ClientShipIdentityObservation Identity { get; init; } =
        new();

    public ClientShipControlFlagsObservation Flags { get; init; } =
        new();

    public ClientShipRuntimeStateObservation Runtime { get; init; } =
        new();

    public ClientShipMovementObservation Movement { get; init; } =
        new();

    public ClientShipStatsObservation BaseStats { get; init; } =
        new();

    public ClientShipStatsObservation CurrentStats { get; init; } =
        new();

    public IReadOnlyList<ClientShipQuadrantObservation> Quadrants
    { get; init; } = [];

    public ClientNavigationRadarObservation Radar { get; init; } =
        new();

    public IReadOnlyDictionary<string, uint> PropertyAddresses
    { get; init; } =
        new Dictionary<string, uint>(
            StringComparer.Ordinal);

    public IReadOnlyDictionary<string, ClientRawAuxDataValueObservation>
        RawValues
    { get; init; } =
        new Dictionary<string, ClientRawAuxDataValueObservation>(
            StringComparer.Ordinal);

    public bool HasAnyValidData =>
        this.ValidPropertyCount > 0;

    public static ClientShipOperationalObservation Unavailable(
        string status,
        uint auxDataLookupAddress = 0)
    {
        return new ClientShipOperationalObservation
        {
            Status = status,
            AuxDataLookupAddress =
                auxDataLookupAddress,
        };
    }
}
