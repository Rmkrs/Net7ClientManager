namespace Net7ClientManager.Navigation;

using Net7ClientManager.Observations.Models;

internal sealed class GalaxyAtlasStationInformationIndex
{
    private static readonly GalaxyAtlasStationInformation emptyInformation =
        new();

    private readonly IReadOnlyDictionary<string, GalaxyAtlasStationInformation>
        informationByKey;

    public GalaxyAtlasStationInformationIndex(GalaxyDataSet dataSet)
    {
        ArgumentNullException.ThrowIfNull(dataSet);

        var accumulators = new Dictionary<string, StationAccumulator>(
            StringComparer.Ordinal);
        var vendorNpcIds = dataSet.Document.VendorItems
            .Select(item => item.VendorNpcId)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var facility in dataSet.Document.StationFacilities)
        {
            foreach (var key in CreateKeys(
                         facility.ActiveSectorNumber,
                         facility.SectorName,
                         facility.StationName))
            {
                GetOrCreate(accumulators, key)
                    .AddFacility(facility.Name);
            }
        }

        foreach (var npc in dataSet.Document.Npcs)
        {
            var vendorDescription = DescribeVendor(
                npc.Role,
                vendorNpcIds.Contains(npc.Id));

            foreach (var key in CreateKeys(
                         npc.ActiveSectorNumber,
                         npc.SectorName,
                         npc.StationName))
            {
                GetOrCreate(accumulators, key)
                    .AddNpc(npc.Name, vendorDescription);
            }
        }

        this.informationByKey = accumulators.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Build(),
            StringComparer.Ordinal);
    }

    public GalaxyAtlasStationInformation Find(
        GalaxySectorDefinition sector,
        GalaxyNavigationCatalogSector catalogSector,
        GalaxyNavigationCatalogTarget target)
    {
        ArgumentNullException.ThrowIfNull(sector);
        ArgumentNullException.ThrowIfNull(catalogSector);
        ArgumentNullException.ThrowIfNull(target);

        var stationNames = new[]
            {
                target.Name,
                target.MapDisplayName,
                NavigationDestination.GetTargetDisplayName(target),
            }
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(Normalize)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        foreach (var stationName in stationNames)
        {
            if (catalogSector.ActiveSectorNumber != 0 &&
                this.informationByKey.TryGetValue(
                    CreateActiveSectorKey(
                        catalogSector.ActiveSectorNumber,
                        stationName),
                    out var activeSectorInformation))
            {
                return activeSectorInformation;
            }

            if (this.informationByKey.TryGetValue(
                    CreateNamedSectorKey(
                        Normalize(sector.Name),
                        stationName),
                    out var namedSectorInformation))
            {
                return namedSectorInformation;
            }
        }

        return emptyInformation;
    }

    private static IEnumerable<string> CreateKeys(
        uint activeSectorNumber,
        string sectorName,
        string stationName)
    {
        var normalizedStationName = Normalize(stationName);

        if (string.IsNullOrWhiteSpace(normalizedStationName))
        {
            yield break;
        }

        if (activeSectorNumber != 0)
        {
            yield return CreateActiveSectorKey(
                activeSectorNumber,
                normalizedStationName);
        }

        var normalizedSectorName = Normalize(sectorName);

        if (!string.IsNullOrWhiteSpace(normalizedSectorName))
        {
            yield return CreateNamedSectorKey(
                normalizedSectorName,
                normalizedStationName);
        }
    }

    private static string CreateActiveSectorKey(
        uint activeSectorNumber,
        string normalizedStationName)
    {
        return string.Concat(
            "active:",
            activeSectorNumber.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            ":",
            normalizedStationName);
    }

    private static string CreateNamedSectorKey(
        string normalizedSectorName,
        string normalizedStationName)
    {
        return string.Concat(
            "named:",
            normalizedSectorName,
            ":",
            normalizedStationName);
    }

    private static StationAccumulator GetOrCreate(
        IDictionary<string, StationAccumulator> accumulators,
        string key)
    {
        if (!accumulators.TryGetValue(key, out var accumulator))
        {
            accumulator = new StationAccumulator();
            accumulators.Add(key, accumulator);
        }

        return accumulator;
    }

    private static string? DescribeVendor(
        int? rawRole,
        bool hasKnownInventory)
    {
        if (!rawRole.HasValue)
        {
            return hasKnownInventory
                ? "Vendor"
                : null;
        }

        var description = ((ClientStarbaseNpcVendorType)rawRole.Value) switch
        {
            ClientStarbaseNpcVendorType.Weapon => "Weapon vendor",
            ClientStarbaseNpcVendorType.System => "System vendor",
            ClientStarbaseNpcVendorType.Core => "Core vendor",
            ClientStarbaseNpcVendorType.Consumable => "Consumables vendor",
            ClientStarbaseNpcVendorType.Junk => "Junk vendor",
            ClientStarbaseNpcVendorType.Component => "Component vendor",
            ClientStarbaseNpcVendorType.Resource => "Resource vendor",
            ClientStarbaseNpcVendorType.BlackMarket => "Black market vendor",
            _ => null,
        };

        return description ?? (hasKnownInventory ? "Vendor" : null);
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? ""
            : GalaxyTopology.NormalizeName(value);
    }

    private sealed class StationAccumulator
    {
        private readonly HashSet<string> facilities =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, NpcAccumulator> npcs =
            new(StringComparer.Ordinal);

        private bool hasFacilityContribution;
        private bool hasNpcContribution;

        public void AddFacility(string? name)
        {
            this.hasFacilityContribution = true;

            if (!string.IsNullOrWhiteSpace(name))
            {
                this.facilities.Add(name.Trim());
            }
        }

        public void AddNpc(
            string? name,
            string? vendorDescription)
        {
            this.hasNpcContribution = true;

            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            var displayName = name.Trim();
            var key = Normalize(displayName);

            if (!this.npcs.TryGetValue(key, out var npc))
            {
                npc = new NpcAccumulator(displayName);
                this.npcs.Add(key, npc);
            }

            npc.AddVendorDescription(vendorDescription);
        }

        public GalaxyAtlasStationInformation Build()
        {
            return new GalaxyAtlasStationInformation
            {
                HasFacilityContribution = this.hasFacilityContribution,
                HasNpcContribution = this.hasNpcContribution,
                Facilities =
                [
                    .. this.facilities.OrderBy(
                        name => name,
                        StringComparer.OrdinalIgnoreCase),
                ],
                Npcs =
                [
                    .. this.npcs.Values
                        .OrderBy(
                            npc => npc.Name,
                            StringComparer.OrdinalIgnoreCase)
                        .Select(npc => npc.Build()),
                ],
            };
        }
    }

    private sealed class NpcAccumulator(string name)
    {
        private readonly HashSet<string> vendorDescriptions =
            new(StringComparer.OrdinalIgnoreCase);

        public string Name { get; } = name;

        public void AddVendorDescription(string? description)
        {
            if (!string.IsNullOrWhiteSpace(description))
            {
                this.vendorDescriptions.Add(description.Trim());
            }
        }

        public GalaxyAtlasStationNpcInformation Build()
        {
            return new GalaxyAtlasStationNpcInformation
            {
                Name = this.Name,
                VendorDescription = this.vendorDescriptions.Count == 0
                    ? null
                    : string.Join(
                        " / ",
                        this.vendorDescriptions.OrderBy(
                            value => value,
                            StringComparer.OrdinalIgnoreCase)),
            };
        }
    }
}
