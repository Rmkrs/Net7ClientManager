namespace Net7ClientManager.Navigation;

public sealed record GalaxyRouteResult
{
    public bool Succeeded { get; init; }

    public string Error { get; init; } = "";

    public IReadOnlyList<GalaxySectorDefinition> Sectors { get; init; } = [];

    public IReadOnlyList<GalaxyRouteTransition> Transitions { get; init; } = [];

    public int HopCount => this.Transitions.Count;

    public GalaxySectorDefinition? NextSector =>
        this.Transitions.Count > 0
            ? this.Transitions[0].To
            : null;

    public static GalaxyRouteResult Success(
        IReadOnlyList<GalaxySectorDefinition> sectors,
        IReadOnlyList<GalaxyRouteTransition>? transitions = null)
    {
        return new GalaxyRouteResult
        {
            Succeeded = true,
            Sectors = sectors,
            Transitions = transitions ?? [],
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
