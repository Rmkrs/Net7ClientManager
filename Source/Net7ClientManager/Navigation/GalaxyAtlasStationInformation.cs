namespace Net7ClientManager.Navigation;

internal sealed record GalaxyAtlasStationInformation
{
    public bool HasFacilityContribution { get; init; }

    public bool HasNpcContribution { get; init; }

    public IReadOnlyList<string> Facilities { get; init; } = [];

    public IReadOnlyList<GalaxyAtlasStationNpcInformation> Npcs { get; init; } = [];

    public bool HasAnyContribution =>
        this.HasFacilityContribution || this.HasNpcContribution;
}

internal sealed record GalaxyAtlasStationNpcInformation
{
    public required string Name { get; init; }

    public string? VendorDescription { get; init; }
}
