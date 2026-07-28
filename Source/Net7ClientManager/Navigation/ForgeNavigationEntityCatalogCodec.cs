namespace Net7ClientManager.Navigation;

using System.Text.Json;

internal static class ForgeNavigationEntityCatalogCodec
{
    public static ForgeNavigationDataDocument ToDataDocument(
        ForgeNavigationEntitySnapshotDocument snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var sectors = Read<ForgeNavigationSectorRecordDocument>(
            snapshot,
            ForgeNavigationEntityKinds.Sector)
            .ToDictionary(sector => sector.Id, StringComparer.Ordinal);
        var targets = Read<ForgeNavigationTargetDeltaRecord>(
            snapshot,
            ForgeNavigationEntityKinds.Target);
        var departures = Read<ForgeNavigationDepartureDeltaRecord>(
            snapshot,
            ForgeNavigationEntityKinds.Departure);
        var targetGroups = targets
            .GroupBy(record => record.SectorId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(record => record.Target)
                    .OrderBy(target => target.Ordinal)
                    .ThenBy(target => target.Id, StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);
        var departureGroups = departures
            .GroupBy(record => record.SectorId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(record => record.Departure)
                    .OrderBy(departure => departure.Ordinal)
                    .ThenBy(departure => departure.Id, StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);

        return new ForgeNavigationDataDocument
        {
            ContractVersion = snapshot.ContractVersion,
            Revision = snapshot.Revision,
            GeneratedAt = snapshot.GeneratedAt,
            Sectors =
            [
                .. sectors.Values
                    .Select(sector => new ForgeNavigationSectorDocument
                    {
                        Id = sector.Id,
                        Key = sector.Key,
                        Name = sector.Name,
                        SystemName = sector.SystemName,
                        RequiredProfession = sector.RequiredProfession,
                        RequiredFaction = sector.RequiredFaction,
                        MinimumFactionStanding = sector.MinimumFactionStanding,
                        Aliases = sector.Aliases,
                        Connections = sector.Connections,
                        ActiveSectorNumber = sector.ActiveSectorNumber,
                        Targets = targetGroups.GetValueOrDefault(sector.Id, []),
                        Departures = departureGroups.GetValueOrDefault(sector.Id, []),
                    })
                    .OrderBy(sector => sector.Key, StringComparer.Ordinal),
            ],
            Npcs = Read<ForgeNavigationNpcDocument>(
                snapshot,
                ForgeNavigationEntityKinds.Npc),
            StationFacilities = Read<ForgeNavigationStationFacilityDocument>(
                snapshot,
                ForgeNavigationEntityKinds.StationFacility),
            VendorItems = Read<ForgeNavigationVendorItemDocument>(
                snapshot,
                ForgeNavigationEntityKinds.VendorItem),
            MobVariants = Read<ForgeNavigationMobVariantDocument>(
                snapshot,
                ForgeNavigationEntityKinds.MobVariant),
            MobClusters = Read<ForgeNavigationMobEncounterClusterDocument>(
                snapshot,
                ForgeNavigationEntityKinds.MobCluster),
            MobLoot = Read<ForgeNavigationMobLootDocument>(
                snapshot,
                ForgeNavigationEntityKinds.MobLoot),
            HarvestableVariants = Read<ForgeNavigationHarvestableVariantDocument>(
                snapshot,
                ForgeNavigationEntityKinds.HarvestableVariant),
            HarvestableFields = Read<ForgeNavigationHarvestableFieldDocument>(
                snapshot,
                ForgeNavigationEntityKinds.HarvestableField),
            HarvestableResources = Read<ForgeNavigationHarvestableResourceDocument>(
                snapshot,
                ForgeNavigationEntityKinds.HarvestableResource),
            GravityWells = Read<ForgeNavigationGravityWellDocument>(
                snapshot,
                ForgeNavigationEntityKinds.GravityWell),
        };
    }

    public static IReadOnlyDictionary<string, int> CountByKind(
        ForgeNavigationEntitySnapshotDocument snapshot)
    {
        return snapshot.Entities
            .GroupBy(entity => entity.Kind, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Count(),
                StringComparer.Ordinal);
    }

    private static IReadOnlyList<T> Read<T>(
        ForgeNavigationEntitySnapshotDocument snapshot,
        string kind)
    {
        List<T> result = [];

        foreach (var entity in snapshot.Entities.Where(entity =>
                     string.Equals(entity.Kind, kind, StringComparison.Ordinal) &&
                     entity.Version == ForgeNavigationEntityKinds.CurrentEntityVersion))
        {
            var document = entity.Document.Deserialize<T>(
                    ForgeNavigationDataJson.DistributionReadOptions) ??
                throw new InvalidOperationException(
                    $"Navigation entity '{kind}/{entity.Id}' is empty.");
            result.Add(document);
        }

        return result;
    }
}
