namespace Net7ClientManager.Navigation;

public sealed record GalaxyAtlasDepartureAccess
{
    public required GalaxyAtlasAccessState State { get; init; }

    public string Description { get; init; } = "";
}
