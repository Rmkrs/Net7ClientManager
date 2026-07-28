namespace Net7ClientManager.Navigation;

using System.Globalization;
using Net7ClientManager.Observations.Models;

public sealed class WorldSearchIndex
{
    private readonly IReadOnlyList<IndexedEntry> entries;

    public WorldSearchIndex(GalaxyDataSet dataSet)
    {
        ArgumentNullException.ThrowIfNull(dataSet);
        this.entries = BuildEntries(dataSet);
    }

    public IReadOnlyList<WorldSearchMatch> Search(
        string? query,
        WorldSearchKind? kind = null,
        int maximumResults = 250)
    {
        if (maximumResults <= 0)
        {
            return [];
        }

        var normalizedQuery = Normalize(query);
        var browseWithoutText = normalizedQuery.Length == 0;

        return
        [
            .. this.entries
                .Where(entry =>
                    !kind.HasValue ||
                    entry.Value.Kind == kind.Value)
                .Select(entry => new
                {
                    Entry = entry,
                    Rank = browseWithoutText
                        ? 500
                        : Score(entry, normalizedQuery),
                })
                .Where(candidate => candidate.Rank != int.MaxValue)
                .OrderBy(candidate => candidate.Rank)
                .ThenBy(
                    candidate => candidate.Entry.Value.Name,
                    StringComparer.OrdinalIgnoreCase)
                .ThenBy(
                    candidate => candidate.Entry.Value.SectorName,
                    StringComparer.OrdinalIgnoreCase)
                .Take(maximumResults)
                .Select(candidate => new WorldSearchMatch
                {
                    Entry = candidate.Entry.Value,
                    Rank = candidate.Rank,
                }),
        ];
    }

    private static IReadOnlyList<IndexedEntry> BuildEntries(
        GalaxyDataSet dataSet)
    {
        List<IndexedEntry> entries = [];

        foreach (var sector in dataSet.Topology.Sectors)
        {
            Add(
                entries,
                new WorldSearchEntry
                {
                    Identity = $"sector:{sector.Key}",
                    Name = sector.Name,
                    Kind = WorldSearchKind.Sector,
                    SystemName = sector.SystemName,
                    SectorKey = sector.Key,
                    SectorName = sector.Name,
                    Location = "Sector",
                    SearchTerms =
                    [
                        sector.Name,
                        sector.SystemName,
                        .. sector.Aliases,
                    ],
                    Destination = NavigationDestination.ForSector(sector),
                });

            if (!dataSet.Catalog.TryGetSector(
                    sector.Key,
                    out var catalogSector))
            {
                continue;
            }

            foreach (var target in catalogSector.Targets)
            {
                var kind = ToSearchKind(target.Kind);

                if (!kind.HasValue)
                {
                    continue;
                }

                var destination = NavigationDestination.ForTarget(
                    sector,
                    target);
                var targetName = destination.TargetName ??
                                 NavigationDestination.GetTargetDisplayName(
                                     target);

                Add(
                    entries,
                    new WorldSearchEntry
                    {
                        Identity = $"target:{destination.TargetKey}",
                        Name = targetName,
                        Kind = kind.Value,
                        SystemName = sector.SystemName,
                        SectorKey = sector.Key,
                        SectorName = sector.Name,
                        Location = DescribeTargetLocation(
                            dataSet.Catalog,
                            sector.Key,
                            target),
                        SearchTerms =
                        [
                            targetName,
                            target.Name,
                            target.MapDisplayName,
                            sector.Name,
                            sector.SystemName,
                            ToSearchTerm(kind.Value),
                        ],
                        Destination = destination,
                    });
            }
        }

        var vendorNpcIds = dataSet.Document.VendorItems
            .Select(item => item.VendorNpcId)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var npc in dataSet.Document.Npcs)
        {
            if (string.IsNullOrWhiteSpace(npc.Name) ||
                !dataSet.Topology.TryResolve(
                    npc.SectorName,
                    out var sector))
            {
                continue;
            }

            NavigationDestination? destination = null;

            if (dataSet.Catalog.TryGetSector(
                    sector.Key,
                    out var catalogSector))
            {
                var stationTarget = FindStationTarget(
                    catalogSector,
                    npc.StationName);

                if (stationTarget != null)
                {
                    destination = NavigationDestination.ForTarget(
                        sector,
                        stationTarget);
                }
            }

            Add(
                entries,
                new WorldSearchEntry
                {
                    Identity = $"npc:{npc.Id}",
                    Name = npc.Name.Trim(),
                    Kind = WorldSearchKind.Npc,
                    SystemName = sector.SystemName,
                    SectorKey = sector.Key,
                    SectorName = sector.Name,
                    Location = npc.StationName,
                    ScopeType = vendorNpcIds.Contains(npc.Id)
                        ? "Vendor"
                        : "NPC",
                    SearchTerms =
                    [
                        npc.Name!,
                        npc.StationName,
                        npc.SectorName,
                        sector.Name,
                        sector.SystemName,
                        vendorNpcIds.Contains(npc.Id)
                            ? "vendor merchant shop NPC"
                            : "NPC",
                    ],
                    Destination = destination,
                });
        }

        foreach (var group in dataSet.Document.StationFacilities
                     .GroupBy(
                         facility => new
                         {
                             facility.StarbaseId,
                             Station = Normalize(facility.StationName),
                             Sector = Normalize(facility.SectorName),
                             facility.FacilityType,
                         }))
        {
            var facilities = group
                .OrderBy(facility => facility.RoomDefinitionKey)
                .ThenBy(facility => facility.RoomFacilitySlot)
                .ToArray();
            var facility = facilities[0];

            if (!dataSet.Topology.TryResolve(
                    facility.SectorName,
                    out var sector))
            {
                continue;
            }

            NavigationDestination? destination = null;

            if (dataSet.Catalog.TryGetSector(
                    sector.Key,
                    out var catalogSector))
            {
                var stationTarget = FindStationTarget(
                    catalogSector,
                    facility.StationName);

                if (stationTarget != null)
                {
                    destination = NavigationDestination.ForTarget(
                        sector,
                        stationTarget);
                }
            }

            var instanceDescription = facilities.Length == 1
                ? facility.StationName
                : string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"{facility.StationName} · {facilities.Length} terminals");
            Add(
                entries,
                new WorldSearchEntry
                {
                    Identity = string.Create(
                        System.Globalization.CultureInfo.InvariantCulture,
                        $"station-service:{facility.StarbaseId}:{Normalize(facility.StationName)}:{facility.FacilityType}"),
                    Name = facility.Name,
                    Kind = WorldSearchKind.StationService,
                    SystemName = sector.SystemName,
                    SectorKey = sector.Key,
                    SectorName = sector.Name,
                    Location = instanceDescription,
                    SearchTerms =
                    [
                        facility.Name,
                        facility.StationName,
                        facility.SectorName,
                        sector.Name,
                        sector.SystemName,
                        "station service facility terminal",
                    ],
                    Destination = destination,
                });
        }

        var npcsById = dataSet.Document.Npcs
            .Where(npc => !string.IsNullOrWhiteSpace(npc.Name))
            .ToDictionary(npc => npc.Id, StringComparer.Ordinal);

        foreach (var item in dataSet.Document.VendorItems)
        {
            if (!npcsById.TryGetValue(item.VendorNpcId, out var vendor) ||
                !dataSet.Topology.TryResolve(vendor.SectorName, out var sector))
            {
                continue;
            }

            var itemName = ClientItemTemplateNameResolver.GetKnownName(
                    item.ItemTemplateId) ??
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Item template {item.ItemTemplateId}");
            NavigationDestination? destination = null;

            if (dataSet.Catalog.TryGetSector(sector.Key, out var catalogSector))
            {
                var stationTarget = FindStationTarget(
                    catalogSector,
                    vendor.StationName);

                if (stationTarget != null)
                {
                    destination = NavigationDestination.ForTarget(
                        sector,
                        stationTarget);
                }
            }

            Add(
                entries,
                new WorldSearchEntry
                {
                    Identity = $"vendor-item:{item.Id}",
                    Name = itemName,
                    Kind = WorldSearchKind.VendorItem,
                    SystemName = sector.SystemName,
                    SectorKey = sector.Key,
                    SectorName = sector.Name,
                    Location = string.Create(
                        CultureInfo.InvariantCulture,
                        $"{vendor.Name} · {vendor.StationName}"),
                    SearchTerms =
                    [
                        itemName,
                        item.ItemTemplateId.ToString(CultureInfo.InvariantCulture),
                        vendor.Name!,
                        vendor.StationName,
                        vendor.SectorName,
                        sector.Name,
                        sector.SystemName,
                        "vendor item sold purchase",
                    ],
                    Destination = destination,
                });
        }

        var mobVariantsById = dataSet.Document.MobVariants
            .ToDictionary(variant => variant.Id, StringComparer.Ordinal);
        var sectorsById = dataSet.Document.Sectors
            .ToDictionary(sector => sector.Id, StringComparer.Ordinal);

        foreach (var cluster in dataSet.Document.MobClusters)
        {
            if (!mobVariantsById.TryGetValue(
                    cluster.MobVariantId,
                    out var variant) ||
                !sectorsById.TryGetValue(cluster.SectorId, out var documentSector) ||
                !dataSet.Topology.TryGetByKey(documentSector.Key, out var sector))
            {
                continue;
            }

            GalaxyNavigationCatalogTarget? nearestTarget = null;

            if (dataSet.Catalog.TryGetSector(
                    sector.Key,
                    out var catalogSector))
            {
                nearestTarget = FindNearestTarget(
                    catalogSector,
                    cluster.CenterX,
                    cluster.CenterY,
                    cluster.CenterZ);
            }

            var destination = nearestTarget != null
                ? NavigationDestination.ForTarget(sector, nearestTarget)
                : NavigationDestination.ForSector(sector);
            var nearestName = nearestTarget == null
                ? sector.Name
                : NavigationDestination.GetTargetDisplayName(nearestTarget);
            var factionIdentifier = variant.FactionIdentifier.Trim();
            var faction = FactionDisplayNameResolver.GetDisplayNameOrDefault(
                factionIdentifier,
                "No faction");
            var organic = variant.IsOrganic ? "Organic" : "Inorganic";
            var location = string.Create(
                CultureInfo.InvariantCulture,
                $"CL {variant.CombatLevel} · Near {nearestName} · radius {cluster.Radius:0} · {cluster.SightingCount} sightings");

            Add(
                entries,
                new WorldSearchEntry
                {
                    Identity = $"mob-cluster:{cluster.Id}",
                    Name = variant.Name,
                    Kind = WorldSearchKind.Mob,
                    SystemName = sector.SystemName,
                    SectorKey = sector.Key,
                    SectorName = sector.Name,
                    Location = location,
                    ScopeType = GetMobDispositionText(
                        variant.IntrinsicDisposition),
                    MobFactionIdentifier = factionIdentifier,
                    MobFactionBindingKind = variant.FactionBindingKind,
                    MobIntrinsicRelationshipRaw =
                        variant.IntrinsicRelationshipRaw,
                    Level = variant.CombatLevel,
                    NearNavigationName = nearestName,
                    SearchTerms =
                    [
                        variant.Name,
                        $"combat level {variant.CombatLevel}",
                        $"CL {variant.CombatLevel}",
                        faction,
                        factionIdentifier,
                        organic,
                        sector.Name,
                        sector.SystemName,
                        nearestName,
                        "mob enemy creature encounter cluster",
                    ],
                    Destination = destination,
                });
        }

        var mobClustersByVariantId = dataSet.Document.MobClusters
            .GroupBy(cluster => cluster.MobVariantId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.ToArray(),
                StringComparer.Ordinal);

        foreach (var relationship in dataSet.Document.MobLoot)
        {
            if (!mobVariantsById.TryGetValue(
                    relationship.MobVariantId,
                    out var variant) ||
                !mobClustersByVariantId.TryGetValue(
                    relationship.MobVariantId,
                    out var clusters))
            {
                continue;
            }

            var itemName = ClientItemTemplateNameResolver.GetKnownName(
                    relationship.ItemTemplateId) ??
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Item template {relationship.ItemTemplateId}");
            var factionIdentifier = variant.FactionIdentifier.Trim();
            var faction = FactionDisplayNameResolver.GetDisplayNameOrDefault(
                factionIdentifier,
                "No faction");
            var organic = variant.IsOrganic ? "Organic" : "Inorganic";

            foreach (var cluster in clusters)
            {
                if (!sectorsById.TryGetValue(
                        cluster.SectorId,
                        out var documentSector) ||
                    !dataSet.Topology.TryGetByKey(
                        documentSector.Key,
                        out var sector))
                {
                    continue;
                }

                GalaxyNavigationCatalogTarget? nearestTarget = null;

                if (dataSet.Catalog.TryGetSector(
                        sector.Key,
                        out var catalogSector))
                {
                    nearestTarget = FindNearestTarget(
                        catalogSector,
                        cluster.CenterX,
                        cluster.CenterY,
                        cluster.CenterZ);
                }

                var destination = nearestTarget != null
                    ? NavigationDestination.ForTarget(sector, nearestTarget)
                    : NavigationDestination.ForSector(sector);
                var nearestName = nearestTarget == null
                    ? sector.Name
                    : NavigationDestination.GetTargetDisplayName(nearestTarget);
                var location = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Dropped by {variant.Name} · CL {variant.CombatLevel} · Near {nearestName}");

                Add(
                    entries,
                    new WorldSearchEntry
                    {
                        Identity = string.Create(
                            CultureInfo.InvariantCulture,
                            $"mob-loot:{relationship.Id}:{cluster.Id}"),
                        Name = itemName,
                        Kind = WorldSearchKind.MobLoot,
                        SystemName = sector.SystemName,
                        SectorKey = sector.Key,
                        SectorName = sector.Name,
                        Location = location,
                        SearchTerms =
                        [
                            itemName,
                            relationship.ItemTemplateId.ToString(
                                CultureInfo.InvariantCulture),
                            variant.Name,
                            $"combat level {variant.CombatLevel}",
                            $"CL {variant.CombatLevel}",
                            faction,
                            factionIdentifier,
                            organic,
                            sector.Name,
                            sector.SystemName,
                            nearestName,
                            "mob loot drop dropped item",
                        ],
                        Destination = destination,
                    });
            }
        }

        var harvestableVariantsById = dataSet.Document.HarvestableVariants
            .ToDictionary(variant => variant.Id, StringComparer.Ordinal);
        var harvestableFieldsById = dataSet.Document.HarvestableFields
            .ToDictionary(field => field.Id, StringComparer.Ordinal);

        foreach (var membership in dataSet.Document.HarvestableResources
                     .GroupBy(
                         resource => new
                         {
                             resource.HarvestableFieldId,
                             resource.HarvestableVariantId,
                         }))
        {
            if (!harvestableFieldsById.TryGetValue(
                    membership.Key.HarvestableFieldId,
                    out var field) ||
                !harvestableVariantsById.TryGetValue(
                    membership.Key.HarvestableVariantId,
                    out var variant) ||
                !sectorsById.TryGetValue(field.SectorId, out var documentSector) ||
                !dataSet.Topology.TryGetByKey(documentSector.Key, out var sector))
            {
                continue;
            }

            GalaxyNavigationCatalogTarget? nearestTarget = null;

            if (dataSet.Catalog.TryGetSector(sector.Key, out var catalogSector))
            {
                nearestTarget = FindNearestTarget(
                    catalogSector,
                    field.CenterX,
                    field.CenterY,
                    field.CenterZ);
            }

            var destination = nearestTarget != null
                ? NavigationDestination.ForTarget(sector, nearestTarget)
                : NavigationDestination.ForSector(sector);
            var nearestName = nearestTarget == null
                ? sector.Name
                : NavigationDestination.GetTargetDisplayName(nearestTarget);
            var location = string.Create(
                CultureInfo.InvariantCulture,
                $"Level {variant.TechLevel} · Near {nearestName} · radius {field.Radius:0}");

            Add(
                entries,
                new WorldSearchEntry
                {
                    Identity = string.Create(
                        CultureInfo.InvariantCulture,
                        $"harvestable:{field.Id}:{variant.Id}"),
                    Name = variant.Name,
                    Kind = WorldSearchKind.Harvestable,
                    SystemName = sector.SystemName,
                    SectorKey = sector.Key,
                    SectorName = sector.Name,
                    Location = location,
                    ScopeType = variant.Category.Trim(),
                    Level = variant.TechLevel,
                    NearNavigationName = nearestName,
                    SearchTerms =
                    [
                        variant.Name,
                        variant.Category,
                        $"level {variant.TechLevel}",
                        $"L{variant.TechLevel}",
                        sector.Name,
                        sector.SystemName,
                        nearestName,
                        "harvestable resource asteroid ore gas hulk field",
                    ],
                    Destination = destination,
                });

            foreach (var resource in membership)
            {
                var itemName = ClientItemTemplateNameResolver.GetKnownName(
                        resource.ItemTemplateId) ??
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Item template {resource.ItemTemplateId}");
                Add(
                    entries,
                    new WorldSearchEntry
                    {
                        Identity = $"harvestable-resource:{resource.Id}",
                        Name = itemName,
                        Kind = WorldSearchKind.HarvestableResource,
                        SystemName = sector.SystemName,
                        SectorKey = sector.Key,
                        SectorName = sector.Name,
                        Location = string.Create(
                            CultureInfo.InvariantCulture,
                            $"Found in {variant.Name} · Level {variant.TechLevel} · Near {nearestName}"),
                        SearchTerms =
                        [
                            itemName,
                            resource.ItemTemplateId.ToString(CultureInfo.InvariantCulture),
                            variant.Name,
                            variant.Category,
                            $"level {variant.TechLevel}",
                            $"L{variant.TechLevel}",
                            sector.Name,
                            sector.SystemName,
                            nearestName,
                            "harvestable resource prospect mining field",
                        ],
                        Destination = destination,
                    });
            }
        }

        return entries;
    }

    private static GalaxyNavigationCatalogTarget? FindNearestTarget(
        GalaxyNavigationCatalogSector sector,
        float x,
        float y,
        float z)
    {
        return sector.Targets
            .Where(target => target.HasPosition)
            .OrderBy(target => SquaredDistance(target, x, y, z))
            .ThenBy(
                target => NavigationDestination.GetTargetDisplayName(target),
                StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static double SquaredDistance(
        GalaxyNavigationCatalogTarget target,
        float x,
        float y,
        float z)
    {
        var deltaX = (double)target.X - x;
        var deltaY = (double)target.Y - y;
        var deltaZ = (double)target.Z - z;
        return deltaX * deltaX + deltaY * deltaY + deltaZ * deltaZ;
    }

    private static GalaxyNavigationCatalogTarget? FindStationTarget(
        GalaxyNavigationCatalogSector sector,
        string stationName)
    {
        var normalizedStationName = Normalize(stationName);

        return sector.Targets.FirstOrDefault(target =>
            target.Kind == GalaxyNavigationTargetKind.Station &&
            (
                string.Equals(
                    Normalize(target.Name),
                    normalizedStationName,
                    StringComparison.Ordinal) ||
                string.Equals(
                    Normalize(target.MapDisplayName),
                    normalizedStationName,
                    StringComparison.Ordinal)
            ));
    }

    private static WorldSearchKind? ToSearchKind(
        GalaxyNavigationTargetKind kind)
    {
        return kind switch
        {
            GalaxyNavigationTargetKind.NavigationPoint =>
                WorldSearchKind.NavigationPoint,
            GalaxyNavigationTargetKind.Station =>
                WorldSearchKind.Station,
            GalaxyNavigationTargetKind.SectorGate =>
                WorldSearchKind.Gate,
            GalaxyNavigationTargetKind.Planet =>
                WorldSearchKind.Planet,
            _ => null,
        };
    }


    private static string GetMobDispositionText(
        ForgeNavigationEncounterResolvedDisposition? disposition)
    {
        return disposition switch
        {
            ForgeNavigationEncounterResolvedDisposition.Hostile =>
                "Hostile",
            ForgeNavigationEncounterResolvedDisposition.Neutral =>
                "Neutral",
            ForgeNavigationEncounterResolvedDisposition.Friendly =>
                "Friendly",
            _ => "Unknown",
        };
    }

    private static string ToSearchTerm(WorldSearchKind kind)
    {
        return kind switch
        {
            WorldSearchKind.NavigationPoint => "navigation point nav",
            WorldSearchKind.Station => "station starbase",
            WorldSearchKind.Gate => "gate sector gate accelerator",
            WorldSearchKind.Planet => "planet landable",
            WorldSearchKind.Sector => "sector",
            WorldSearchKind.Npc => "npc character",
            WorldSearchKind.StationService => "station service facility terminal",
            WorldSearchKind.VendorItem => "vendor item sold purchase",
            WorldSearchKind.Mob => "mob enemy creature encounter cluster",
            WorldSearchKind.MobLoot => "mob loot drop dropped item",
            _ => kind.ToString(),
        };
    }

    private static string DescribeTargetLocation(
        GalaxyNavigationCatalog catalog,
        string sectorKey,
        GalaxyNavigationCatalogTarget target)
    {
        return target.Kind switch
        {
            GalaxyNavigationTargetKind.Station => "Direct station target",
            GalaxyNavigationTargetKind.SectorGate => "Direct gate target",
            GalaxyNavigationTargetKind.NavigationPoint =>
                "Direct navigation target",
            GalaxyNavigationTargetKind.Planet when
                catalog.IsLandablePlanetTarget(sectorKey, target) =>
                "Landable planet",
            GalaxyNavigationTargetKind.Planet => "Planet",
            _ => "Direct target",
        };
    }

    private static void Add(
        ICollection<IndexedEntry> entries,
        WorldSearchEntry entry)
    {
        var normalizedName = Normalize(entry.Name);
        var normalizedTerms = entry.SearchTerms
            .Where(term => !string.IsNullOrWhiteSpace(term))
            .Select(Normalize)
            .Where(term => !string.IsNullOrWhiteSpace(term))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        entries.Add(
            new IndexedEntry(
                entry,
                normalizedName,
                normalizedTerms));
    }

    private static int Score(
        IndexedEntry entry,
        string query)
    {
        var primaryScore = ScoreTerm(
            entry.NormalizedName,
            query);

        if (primaryScore != int.MaxValue)
        {
            return primaryScore;
        }

        var bestSecondaryScore = entry.NormalizedTerms
            .Select(term => ScoreTerm(term, query))
            .DefaultIfEmpty(int.MaxValue)
            .Min();

        return bestSecondaryScore == int.MaxValue
            ? int.MaxValue
            : 40 + bestSecondaryScore;
    }

    private static int ScoreTerm(
        string term,
        string query)
    {
        if (string.Equals(term, query, StringComparison.Ordinal))
        {
            return 0;
        }

        if (term.StartsWith(query, StringComparison.Ordinal))
        {
            return 10;
        }

        if (term.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries)
            .Any(word => word.StartsWith(
                query,
                StringComparison.Ordinal)))
        {
            return 20;
        }

        return term.Contains(query, StringComparison.Ordinal)
            ? 30
            : int.MaxValue;
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? ""
            : GalaxyTopology.NormalizeName(value);
    }

    private sealed record IndexedEntry(
        WorldSearchEntry Value,
        string NormalizedName,
        IReadOnlyList<string> NormalizedTerms);
}
