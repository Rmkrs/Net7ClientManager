namespace Net7ClientManager.Navigation;

using System.Security.Cryptography;
using System.Text;

public sealed record NavigationDestination
{
    public required NavigationDestinationKind Kind { get; init; }

    public required string SectorKey { get; init; }

    public required string SectorName { get; init; }

    public required string SystemName { get; init; }

    public string? TargetKey { get; init; }

    public string? TargetName { get; init; }

    public string? TargetType { get; init; }

    public byte? TargetRawObjectType { get; init; }

    public GalaxyNavigationTargetKind TargetKind { get; init; }

    public GalaxyNavigationTargetSelectionContext TargetSelectionContext
    { get; init; } = GalaxyNavigationTargetSelectionContext.Navigation;

    public bool HasTargetPosition { get; init; }

    public float TargetX { get; init; }

    public float TargetY { get; init; }

    public float TargetZ { get; init; }

    public string DisplayName =>
        this.Kind == NavigationDestinationKind.Target &&
        !string.IsNullOrWhiteSpace(this.TargetName)
            ? $"{this.TargetName} · {this.SectorName}" : this.SectorName;

    public static NavigationDestination ForSector(
        GalaxySectorDefinition sector)
    {
        ArgumentNullException.ThrowIfNull(sector);

        return new NavigationDestination
        {
            Kind = NavigationDestinationKind.Sector,
            SectorKey = sector.Key,
            SectorName = sector.Name,
            SystemName = sector.SystemName,
        };
    }

    public static NavigationDestination ForTarget(
        GalaxySectorDefinition sector,
        GalaxyNavigationCatalogTarget target)
    {
        ArgumentNullException.ThrowIfNull(sector);
        ArgumentNullException.ThrowIfNull(target);

        var name = GetTargetDisplayName(target);

        return new NavigationDestination
        {
            Kind = NavigationDestinationKind.Target,
            SectorKey = sector.Key,
            SectorName = sector.Name,
            SystemName = sector.SystemName,
            TargetKey = CreateTargetKey(sector.Key, target),
            TargetName = name,
            TargetType = ClassifyTarget(target),
            TargetRawObjectType = target.RawObjectType,
            TargetKind = target.Kind,
            TargetSelectionContext = target.SelectionContext,
            HasTargetPosition = target.HasPosition,
            TargetX = target.X,
            TargetY = target.Y,
            TargetZ = target.Z,
        };
    }

    public static string CreateTargetKey(
        string sectorKey,
        GalaxyNavigationCatalogTarget target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sectorKey);
        ArgumentNullException.ThrowIfNull(target);

        return CreateStableTargetKey(
            sectorKey,
            GetTargetDisplayName(target),
            target.HasPosition,
            target.X,
            target.Y,
            target.Z);
    }

    public static string CreateDepartureTargetKey(
        string fromSectorKey,
        string toSectorKey,
        GalaxyNavigationCatalogDeparture departure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fromSectorKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(toSectorKey);
        ArgumentNullException.ThrowIfNull(departure);

        return CreateStableTargetKey(
            string.Concat(fromSectorKey, ">", toSectorKey),
            departure.DepartureTargetName,
            departure.HasExpectedPosition,
            departure.ExpectedX,
            departure.ExpectedY,
            departure.ExpectedZ);
    }

    private static string CreateStableTargetKey(
        string scope,
        string name,
        bool hasPosition,
        float x,
        float y,
        float z)
    {
        var identity = $"{scope}|{GalaxyTopology.NormalizeName(name)}|{hasPosition}|{BitConverter.SingleToInt32Bits(x):X8}|{BitConverter.SingleToInt32Bits(y):X8}|{BitConverter.SingleToInt32Bits(z):X8}";

        var digest = SHA256.HashData(
            Encoding.UTF8.GetBytes(identity));

        return string.Concat(
            scope,
            ":",
            Convert.ToHexString(digest.AsSpan(0, 10)));
    }

    public static string GetTargetDisplayName(
        GalaxyNavigationCatalogTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        return !string.IsNullOrWhiteSpace(target.Name)
            ? target.Name.Trim()
            : !string.IsNullOrWhiteSpace(target.MapDisplayName)
                ? target.MapDisplayName.Trim()
                : "Unnamed navigation target";
    }

    public static string ClassifyTarget(
        GalaxyNavigationCatalogTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        return target.Kind.ToPublicName();
    }
}
