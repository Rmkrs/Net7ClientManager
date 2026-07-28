namespace Net7ClientManager.Navigation;

public sealed record GalaxyRouteResult
{
    public bool Succeeded { get; init; }

    public string Error { get; init; } = "";

    public IReadOnlyList<GalaxySectorDefinition> Sectors { get; init; } = [];

    public int HopCount => Math.Max(0, this.Sectors.Count - 1);

    public GalaxySectorDefinition? NextSector =>
        this.Sectors.Count > 1
            ? this.Sectors[1]
            : null;

    public static GalaxyRouteResult Success(
        IReadOnlyList<GalaxySectorDefinition> sectors)
    {
        return new GalaxyRouteResult
        {
            Succeeded = true,
            Sectors = sectors,
        };
    }

    public static GalaxyRouteResult Failure(string error)
    {
        return new GalaxyRouteResult
        {
            Error = error,
        };
    }
}
