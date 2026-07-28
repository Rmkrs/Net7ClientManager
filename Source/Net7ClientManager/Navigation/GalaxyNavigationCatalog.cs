namespace Net7ClientManager.Navigation;

using System.Globalization;

public sealed class GalaxyNavigationCatalog
{
    private const int SupportedSchemaVersion = 2;

    private readonly IReadOnlyDictionary<string, GalaxyNavigationCatalogSector>
        sectorsByKey;

    private readonly IReadOnlyDictionary<uint, GalaxyNavigationCatalogSector>
        sectorsByActiveSectorNumber;

    private readonly IReadOnlySet<string> landablePlanetSectorKeys;

    private readonly IReadOnlyDictionary<string, HashSet<string>>
        landablePlanetTargetNamesBySector;

    internal GalaxyNavigationCatalog(
        GalaxyTopology topology,
        GalaxyNavigationCatalogDocument document)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(document);

        if (document.SchemaVersion != SupportedSchemaVersion)
        {
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Unsupported navigation catalog schema {document.SchemaVersion}."));
        }

        Dictionary<string, GalaxyNavigationCatalogSector> byKey =
            new(StringComparer.Ordinal);

        Dictionary<uint, GalaxyNavigationCatalogSector>
            byActiveSectorNumber = [];

        HashSet<string> landableSectorKeys =
            new(StringComparer.Ordinal);

        Dictionary<string, HashSet<string>>
            landableTargetNamesBySector =
                new(StringComparer.Ordinal);

        foreach (var sector in document.Sectors)
        {
            if (!topology.TryGetByKey(sector.SectorKey, out var topologySector))
            {
                throw new InvalidOperationException(
                        $"Navigation catalog references unknown sector '{sector.SectorKey}'.");
            }

            if (!byKey.TryAdd(sector.SectorKey, sector))
            {
                throw new InvalidOperationException(
                        $"Navigation catalog contains duplicate sector '{sector.SectorKey}'.");
            }

            if (sector.ActiveSectorNumber != 0 &&
                !byActiveSectorNumber.TryAdd(
                    sector.ActiveSectorNumber,
                    sector))
            {
                throw new InvalidOperationException(
                        $"Navigation catalog contains duplicate active sector number {sector.ActiveSectorNumber}.");
            }

            foreach (var departure in sector.Departures)
            {
                if (string.IsNullOrWhiteSpace(departure.DepartureTargetName) ||
                    string.IsNullOrWhiteSpace(departure.DestinationName))
                {
                    throw new InvalidOperationException(
                            $"Navigation catalog sector '{sector.SectorKey}' contains an incomplete departure.");
                }

                var normalizedDepartureTargetName =
                    GalaxyTopology.NormalizeName(
                        departure.DepartureTargetName);

                var departureTargetExists = sector.Targets.Any(target =>
                    target.RawObjectType == departure.RawObjectType &&
                    (string.Equals(
                         GalaxyTopology.NormalizeName(target.Name),
                         normalizedDepartureTargetName,
                         StringComparison.Ordinal) ||
                     string.Equals(
                         GalaxyTopology.NormalizeName(
                             target.MapDisplayName),
                         normalizedDepartureTargetName,
                         StringComparison.Ordinal)));

                if (!departureTargetExists)
                {
                    throw new InvalidOperationException(
                            $"Navigation catalog departure '{sector.SectorKey}:{departure.DepartureTargetName}' has no matching target in the sector inventory.");
                }

                if (departure.Status != GalaxyNavigationDepartureStatus.Verified)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(departure.ToSectorKey) ||
                    !topology.TryGetByKey(
                        departure.ToSectorKey,
                        out var destination) ||
                    !topologySector.Connections.Contains(
                        destination.Key,
                        StringComparer.Ordinal))
                {
                    throw new InvalidOperationException(
                            $"Verified catalog departure '{sector.SectorKey}' → '{departure.ToSectorKey}' is not present in the topology.");
                }

                if (departure.Kind !=
                    GalaxyNavigationTargetKind.Planet)
                {
                    continue;
                }

                landableSectorKeys.Add(destination.Key);

                if (!landableTargetNamesBySector.TryGetValue(
                        sector.SectorKey,
                        out var targetNames))
                {
                    targetNames = new HashSet<string>(
                        StringComparer.Ordinal);

                    landableTargetNamesBySector[sector.SectorKey] =
                        targetNames;
                }

                targetNames.Add(normalizedDepartureTargetName);
            }
        }

        this.GeneratedAt = document.GeneratedAt;
        this.sectorsByKey = byKey;
        this.sectorsByActiveSectorNumber = byActiveSectorNumber;
        this.landablePlanetSectorKeys = landableSectorKeys;
        this.landablePlanetTargetNamesBySector =
            landableTargetNamesBySector;
        this.Sectors =
        [
            .. byKey.Values
                .OrderBy(sector =>
                    topology.GetByKey(sector.SectorKey).SystemName,
                    StringComparer.OrdinalIgnoreCase)
                .ThenBy(sector =>
                    topology.GetByKey(sector.SectorKey).Name,
                    StringComparer.OrdinalIgnoreCase),
        ];
    }

    public DateTimeOffset GeneratedAt { get; }

    public IReadOnlyList<GalaxyNavigationCatalogSector> Sectors { get; }

    public bool TryGetSector(
        string sectorKey,
        out GalaxyNavigationCatalogSector sector)
    {
        return this.sectorsByKey.TryGetValue(sectorKey, out sector!);
    }

    public bool IsLandablePlanetSector(string sectorKey)
    {
        return this.landablePlanetSectorKeys.Contains(sectorKey);
    }

    public bool IsLandablePlanetTarget(
        string sectorKey,
        GalaxyNavigationCatalogTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (target.Kind != GalaxyNavigationTargetKind.Planet ||
            !this.landablePlanetTargetNamesBySector.TryGetValue(
                sectorKey,
                out var targetNames))
        {
            return false;
        }

        return targetNames.Contains(
                   GalaxyTopology.NormalizeName(target.Name)) ||
               targetNames.Contains(
                   GalaxyTopology.NormalizeName(
                       target.MapDisplayName));
    }

    public GalaxyNavigationCatalogSector? FindSectorByActiveSectorNumber(
        uint activeSectorNumber)
    {
        return activeSectorNumber == 0
            ? null
            : this.sectorsByActiveSectorNumber.GetValueOrDefault(
                activeSectorNumber);
    }

    private bool TryGetDeparture(
        string fromSectorKey,
        string toSectorKey,
        out GalaxyNavigationCatalogDeparture departure)
    {
        departure = null!;

        if (!this.sectorsByKey.TryGetValue(
                fromSectorKey,
                out var sector))
        {
            return false;
        }

        departure = sector.Departures.FirstOrDefault(candidate =>
            string.Equals(
                candidate.ToSectorKey,
                toSectorKey,
                StringComparison.Ordinal))!;

        return true;
    }

    public bool TryGetVerifiedDeparture(
        string fromSectorKey,
        string toSectorKey,
        out GalaxyNavigationCatalogDeparture departure)
    {
        return this.TryGetDeparture(
                   fromSectorKey,
                   toSectorKey,
                   out departure) &&
               departure.Status ==
               GalaxyNavigationDepartureStatus.Verified;
    }

}
