namespace Net7ClientManager.Navigation;

public sealed record GalaxyRouteDistanceResult
{
    public bool Succeeded { get; init; }

    public string Error { get; init; } = "";

    public string? SourceSectorKey { get; init; }

    public IReadOnlyDictionary<string, int> DistancesBySectorKey
    {
        get;
        init;
    } = new Dictionary<string, int>(StringComparer.Ordinal);

    public bool TryGetDistance(
        string sectorKey,
        out int distance)
    {
        return this.DistancesBySectorKey.TryGetValue(
            sectorKey,
            out distance);
    }

    public static GalaxyRouteDistanceResult Success(
        string sourceSectorKey,
        IReadOnlyDictionary<string, int> distancesBySectorKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceSectorKey);
        ArgumentNullException.ThrowIfNull(distancesBySectorKey);

        return new GalaxyRouteDistanceResult
        {
            Succeeded = true,
            SourceSectorKey = sourceSectorKey,
            DistancesBySectorKey = distancesBySectorKey,
        };
    }

    public static GalaxyRouteDistanceResult Failure(string error)
    {
        return new GalaxyRouteDistanceResult
        {
            Error = error,
        };
    }
}
