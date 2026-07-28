namespace Net7ClientManager.Navigation;

using System.Globalization;
using Net7ClientManager.Observations.Models;

internal sealed class GalaxyAtlasForgeOverlayIndex
{
    private readonly IReadOnlyDictionary<string,
        IReadOnlyList<GalaxyAtlasForgeOverlayInformation>> mobsBySectorKey;
    private readonly IReadOnlyDictionary<string,
        IReadOnlyList<GalaxyAtlasForgeOverlayInformation>> resourcesBySectorKey;
    private readonly IReadOnlyDictionary<string,
        IReadOnlyList<GalaxyAtlasForgeOverlayInformation>> gravityWellsBySectorKey;

    public GalaxyAtlasForgeOverlayIndex(GalaxyDataSet dataSet)
    {
        ArgumentNullException.ThrowIfNull(dataSet);

        var sectorsById = dataSet.Document.Sectors
            .ToDictionary(sector => sector.Id, StringComparer.Ordinal);
        var mobVariantsById = dataSet.Document.MobVariants
            .ToDictionary(variant => variant.Id, StringComparer.Ordinal);
        var harvestableVariantsById = dataSet.Document.HarvestableVariants
            .ToDictionary(variant => variant.Id, StringComparer.Ordinal);
        var harvestableResourcesByFieldId = dataSet.Document
            .HarvestableResources
            .GroupBy(resource => resource.HarvestableFieldId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.ToArray(),
                StringComparer.Ordinal);

        var mobs = new Dictionary<string,
            List<GalaxyAtlasForgeOverlayInformation>>(StringComparer.Ordinal);

        foreach (var cluster in dataSet.Document.MobClusters)
        {
            if (!sectorsById.TryGetValue(cluster.SectorId, out var documentSector) ||
                !mobVariantsById.TryGetValue(cluster.MobVariantId, out var variant) ||
                !dataSet.Topology.TryGetByKey(documentSector.Key, out var sector))
            {
                continue;
            }

            var destination = CreateDestination(
                dataSet.Catalog,
                sector,
                cluster.CenterX,
                cluster.CenterY,
                cluster.CenterZ);
            var detailLines = new List<string>
            {
                DescribeCount(
                    cluster.SightingCount,
                    "contributed sighting",
                    "contributed sightings"),
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Approximate radius {cluster.Radius:0}"),
            };

            if (variant.FactionBindingKind ==
                ForgeNavigationEncounterFactionBindingKind.Unaffiliated)
            {
                detailLines.Add(variant.IntrinsicRelationshipRaw switch
                {
                    0 => "Unaffiliated · intrinsic danger",
                    1 => "Unaffiliated · intrinsic neutral",
                    2 or 3 => "Unaffiliated · intrinsic safe",
                    _ => "Unaffiliated · intrinsic disposition unknown",
                });
            }
            else if (!string.IsNullOrWhiteSpace(variant.FactionIdentifier))
            {
                detailLines.Add(string.Concat(
                    "Faction-linked · ",
                    FactionDisplayNameResolver.GetDisplayName(
                        variant.FactionIdentifier)));
            }
            else
            {
                detailLines.Add("Faction unknown");
            }

            detailLines.Add(variant.IsOrganic ? "Organic" : "Inorganic");

            if (cluster.LastSeenAtUtc != default)
            {
                detailLines.Add(string.Concat(
                    "Last observed ",
                    cluster.LastSeenAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)));
            }

            Add(
                mobs,
                sector.Key,
                new GalaxyAtlasForgeOverlayInformation
                {
                    Id = string.Concat("mob:", cluster.Id),
                    Kind = GalaxyAtlasForgeOverlayKind.MobEncounter,
                    SectorKey = sector.Key,
                    Title = variant.Name,
                    MapLabel = variant.Name,
                    Subtitle = "Encounter cluster",
                    CenterX = cluster.CenterX,
                    CenterY = cluster.CenterY,
                    CenterZ = cluster.CenterZ,
                    Radius = cluster.Radius,
                    MobFactionIdentifier = variant.FactionIdentifier,
                    MobFactionBindingKind = ResolveMobFactionBindingKind(
                        variant),
                    IntrinsicRelationshipRaw =
                        variant.IntrinsicRelationshipRaw,
                    DetailLines = detailLines,
                    Sections =
                    [
                        new GalaxyAtlasForgeOverlaySection
                        {
                            Title = "OBSERVED ENCOUNTER",
                            Lines =
                            [
                                string.Create(
                                    CultureInfo.InvariantCulture,
                                    $"{variant.Name} · CL {variant.CombatLevel}"),
                            ],
                        },
                    ],
                    Destination = destination.Destination,
                    RouteDescription = destination.Description,
                });
        }

        var resources = new Dictionary<string,
            List<GalaxyAtlasForgeOverlayInformation>>(StringComparer.Ordinal);

        foreach (var field in dataSet.Document.HarvestableFields)
        {
            if (!sectorsById.TryGetValue(field.SectorId, out var documentSector) ||
                !dataSet.Topology.TryGetByKey(documentSector.Key, out var sector))
            {
                continue;
            }

            harvestableResourcesByFieldId.TryGetValue(
                field.Id,
                out var memberships);
            memberships ??= [];

            var variants = memberships
                .Select(resource => harvestableVariantsById.TryGetValue(
                    resource.HarvestableVariantId,
                    out var variant)
                        ? variant
                        : null)
                .Where(variant => variant != null)
                .Cast<ForgeNavigationHarvestableVariantDocument>()
                .DistinctBy(variant => variant.Id, StringComparer.Ordinal)
                .OrderBy(variant => variant.TechLevel)
                .ThenBy(variant => variant.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var itemNames = memberships
                .Select(resource =>
                    ClientItemTemplateNameResolver.GetKnownName(
                        resource.ItemTemplateId))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var destination = CreateDestination(
                dataSet.Catalog,
                sector,
                field.CenterX,
                field.CenterY,
                field.CenterZ);
            var detailLines = new List<string>
            {
                DescribeCount(
                    field.ObservationCount,
                    "contributed observation",
                    "contributed observations"),
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Approximate radius {field.Radius:0}"),
            };

            if (field.LastSeenAtUtc != default)
            {
                detailLines.Add(string.Concat(
                    "Last observed ",
                    field.LastSeenAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)));
            }

            var title = variants.Length == 1
                ? variants[0].Name
                : "Harvestable field";
            var mapLabel = variants.Length switch
            {
                0 => "Harvestable field",
                1 => variants[0].Name,
                _ => string.Create(
                    CultureInfo.InvariantCulture,
                    $"Harvestable field ({variants.Length})"),
            };
            var sections = new List<GalaxyAtlasForgeOverlaySection>
            {
                new()
                {
                    Title = string.Create(
                        CultureInfo.InvariantCulture,
                        $"HARVESTABLES ({variants.Length})"),
                    Lines = variants.Length == 0
                        ? ["No named harvestables available."]
                        :
                        [
                            .. variants.Select(variant =>
                                string.IsNullOrWhiteSpace(variant.Category)
                                    ? string.Create(
                                        CultureInfo.InvariantCulture,
                                        $"L{variant.TechLevel} {variant.Name}")
                                    : string.Create(
                                        CultureInfo.InvariantCulture,
                                        $"L{variant.TechLevel} {variant.Name} · {variant.Category}")),
                        ],
                },
            };

            if (itemNames.Length > 0)
            {
                sections.Add(new GalaxyAtlasForgeOverlaySection
                {
                    Title = string.Create(
                        CultureInfo.InvariantCulture,
                        $"KNOWN CONTENTS ({itemNames.Length})"),
                    Lines = itemNames,
                });
            }

            Add(
                resources,
                sector.Key,
                new GalaxyAtlasForgeOverlayInformation
                {
                    Id = string.Concat("resource:", field.Id),
                    Kind = GalaxyAtlasForgeOverlayKind.HarvestableField,
                    SectorKey = sector.Key,
                    Title = title,
                    MapLabel = mapLabel,
                    Subtitle = "Harvestable field",
                    CenterX = field.CenterX,
                    CenterY = field.CenterY,
                    CenterZ = field.CenterZ,
                    Radius = field.Radius,
                    DetailLines = detailLines,
                    Sections = sections,
                    Destination = destination.Destination,
                    RouteDescription = destination.Description,
                });
        }

        var gravityWells = new Dictionary<string,
            List<GalaxyAtlasForgeOverlayInformation>>(StringComparer.Ordinal);

        foreach (var gravityWell in dataSet.Document.GravityWells)
        {
            if (!sectorsById.TryGetValue(gravityWell.SectorId, out var documentSector) ||
                !dataSet.Topology.TryGetByKey(documentSector.Key, out var sector))
            {
                continue;
            }

            var destination = CreateDestination(
                dataSet.Catalog,
                sector,
                gravityWell.CenterX,
                gravityWell.CenterY,
                gravityWell.CenterZ);
            var confidenceLabel = gravityWell.Confidence switch
            {
                >= 0.75f => "High confidence",
                >= 0.40f => "Medium confidence",
                _ => "Tentative",
            };
            var detailLines = new List<string>
            {
                confidenceLabel,
                DescribeCount(
                    gravityWell.BoundaryObservationCount,
                    "boundary crossing",
                    "boundary crossings"),
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Approximate radius {gravityWell.Radius:0}"),
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Coverage {gravityWell.AngularCoverage:P0}"),
            };

            if (gravityWell.LastObservedAtUtc != default)
            {
                detailLines.Add(string.Concat(
                    "Last observed ",
                    gravityWell.LastObservedAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)));
            }

            Add(
                gravityWells,
                sector.Key,
                new GalaxyAtlasForgeOverlayInformation
                {
                    Id = string.Concat("gravity-well:", gravityWell.Id),
                    Kind = GalaxyAtlasForgeOverlayKind.GravityWell,
                    SectorKey = sector.Key,
                    Title = "Gravity well",
                    MapLabel = "Gravity well",
                    Subtitle = "Inferred environmental hazard",
                    CenterX = gravityWell.CenterX,
                    CenterY = gravityWell.CenterY,
                    CenterZ = gravityWell.CenterZ,
                    Radius = gravityWell.Radius,
                    DetailLines = detailLines,
                    Sections =
                    [
                        new GalaxyAtlasForgeOverlaySection
                        {
                            Title = "BOUNDARY EVIDENCE",
                            Lines =
                            [
                                string.Create(
                                    CultureInfo.InvariantCulture,
                                    $"Boundary crossings: {gravityWell.BoundaryObservationCount:N0}"),
                                string.Create(
                                    CultureInfo.InvariantCulture,
                                    $"Confidence: {gravityWell.Confidence:P0}"),
                            ],
                        },
                    ],
                    Destination = destination.Destination,
                    RouteDescription = destination.Description,
                });
        }

        this.mobsBySectorKey = Build(mobs);
        this.resourcesBySectorKey = Build(resources);
        this.gravityWellsBySectorKey = Build(gravityWells);
    }

    public IReadOnlyList<GalaxyAtlasForgeOverlayInformation> GetMobs(
        string? sectorKey)
    {
        return Get(this.mobsBySectorKey, sectorKey);
    }

    public IReadOnlyList<GalaxyAtlasForgeOverlayInformation> GetResources(
        string? sectorKey)
    {
        return Get(this.resourcesBySectorKey, sectorKey);
    }

    public IReadOnlyList<GalaxyAtlasForgeOverlayInformation> GetGravityWells(
        string? sectorKey)
    {
        return Get(this.gravityWellsBySectorKey, sectorKey);
    }

    private static RouteDestination CreateDestination(
        GalaxyNavigationCatalog catalog,
        GalaxySectorDefinition sector,
        float x,
        float y,
        float z)
    {
        GalaxyNavigationCatalogTarget? nearestTarget = null;

        if (catalog.TryGetSector(sector.Key, out var catalogSector))
        {
            nearestTarget = catalogSector.Targets
                .Where(target => target.HasPosition)
                .OrderBy(target => SquaredDistance(target, x, y, z))
                .ThenBy(
                    NavigationDestination.GetTargetDisplayName,
                    StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        if (nearestTarget == null)
        {
            return new RouteDestination(
                NavigationDestination.ForSector(sector),
                string.Concat("Route to ", sector.Name));
        }

        var targetName = NavigationDestination.GetTargetDisplayName(
            nearestTarget);

        return new RouteDestination(
            NavigationDestination.ForTarget(sector, nearestTarget),
            string.Concat("Route via ", targetName));
    }

    private static string DescribeCount(
        long count,
        string singular,
        string plural)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{count:N0} {(count == 1 ? singular : plural)}");
    }

    private static double SquaredDistance(
        GalaxyNavigationCatalogTarget target,
        float x,
        float y,
        float z)
    {
        var deltaX = target.X - x;
        var deltaY = target.Y - y;
        var deltaZ = target.Z - z;

        return (deltaX * deltaX) +
               (deltaY * deltaY) +
               (deltaZ * deltaZ);
    }

    private static ForgeNavigationEncounterFactionBindingKind ResolveMobFactionBindingKind(
        ForgeNavigationMobVariantDocument variant)
    {
        if (variant.FactionBindingKind !=
            ForgeNavigationEncounterFactionBindingKind.Unknown)
        {
            return variant.FactionBindingKind;
        }

        return string.IsNullOrWhiteSpace(variant.FactionIdentifier)
            ? ForgeNavigationEncounterFactionBindingKind.Unknown
            : ForgeNavigationEncounterFactionBindingKind.FactionLinked;
    }

    private static void Add(
        IDictionary<string, List<GalaxyAtlasForgeOverlayInformation>> values,
        string sectorKey,
        GalaxyAtlasForgeOverlayInformation value)
    {
        if (!values.TryGetValue(sectorKey, out var list))
        {
            list = [];
            values.Add(sectorKey, list);
        }

        list.Add(value);
    }

    private static IReadOnlyDictionary<string,
        IReadOnlyList<GalaxyAtlasForgeOverlayInformation>> Build(
        IDictionary<string, List<GalaxyAtlasForgeOverlayInformation>> values)
    {
        return values.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<GalaxyAtlasForgeOverlayInformation>)
            [
                .. pair.Value
                    .OrderBy(value => value.Title, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(value => value.Id, StringComparer.Ordinal),
            ],
            StringComparer.Ordinal);
    }

    private static IReadOnlyList<GalaxyAtlasForgeOverlayInformation> Get(
        IReadOnlyDictionary<string,
            IReadOnlyList<GalaxyAtlasForgeOverlayInformation>> values,
        string? sectorKey)
    {
        return !string.IsNullOrWhiteSpace(sectorKey) &&
               values.TryGetValue(sectorKey, out var result)
            ? result
            : [];
    }

    private sealed record RouteDestination(
        NavigationDestination Destination,
        string Description);
}
