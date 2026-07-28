namespace Net7ClientManager.Navigation;

internal sealed record ForgeNavigationDepartureDeltaRecord
{
    public required string SectorId { get; init; }

    public required ForgeNavigationDepartureDocument Departure { get; init; }
}
