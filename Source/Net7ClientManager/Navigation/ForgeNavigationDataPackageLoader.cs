namespace Net7ClientManager.Navigation;

using System.Globalization;
using System.IO.Compression;
using System.Text.Json;

internal static class ForgeNavigationDataPackageLoader
{
    private const long MaximumPackageBytes = 64L * 1024 * 1024;
    private const int MaximumManifestBytes = 64 * 1024;
    private const int MaximumPayloadBytes = 60 * 1024 * 1024;

    public static ForgeNavigationFullPackage LoadFull(
        string packagePath,
        string? expectedPackageSha256 = null,
        long? expectedPackageSize = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);

        var packageInfo = new FileInfo(packagePath);

        if (!packageInfo.Exists ||
            packageInfo.Length <= 0 ||
            packageInfo.Length > MaximumPackageBytes ||
            expectedPackageSize is not null &&
            packageInfo.Length != expectedPackageSize.Value)
        {
            throw new InvalidOperationException(
                $"Navigation package '{packagePath}' has an invalid size.");
        }

        var packageSha256 = ForgeNavigationHash.ComputeFileSha256(packagePath);

        if (expectedPackageSha256 != null &&
            (!ForgeNavigationHash.IsSha256(expectedPackageSha256) ||
             !string.Equals(
                 packageSha256,
                 expectedPackageSha256,
                 StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                "Navigation package hash does not match the expected hash.");
        }

        using var stream = new FileStream(
            packagePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        using var archive = new ZipArchive(
            stream,
            ZipArchiveMode.Read,
            leaveOpen: false);
        var manifestBytes = ReadEntry(
            archive,
            "net7forge.json",
            MaximumManifestBytes);
        var manifest = JsonSerializer.Deserialize<ForgeNavigationDataPackageManifest>(
                manifestBytes,
                ForgeNavigationDataJson.DistributionReadOptions) ??
            throw new InvalidOperationException(
                "Navigation package manifest is empty.");

        ValidateManifest(manifest, "full");
        ValidateArchiveEntries(archive, manifest.PayloadEntry);
        var snapshotBytes = ReadEntry(
            archive,
            manifest.PayloadEntry,
            MaximumPayloadBytes);
        var snapshotSha256 = ForgeNavigationHash.ComputeSha256(snapshotBytes);

        if (!string.Equals(
                snapshotSha256,
                manifest.PayloadSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Navigation package payload hash does not match its manifest.");
        }

        var snapshot = DeserializeSnapshot(snapshotBytes);
        var document = ForgeNavigationEntityCatalogCodec.ToDataDocument(snapshot);
        ValidateSnapshot(snapshot);
        ValidateDocument(document);
        ValidateManifestCounts(manifest, snapshot);

        if (manifest.BaseRevision != null ||
            manifest.ContractVersion != snapshot.ContractVersion ||
            manifest.DataRevision != snapshot.Revision ||
            !string.Equals(
                manifest.ResultSnapshotSha256,
                manifest.PayloadSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Navigation package manifest and snapshot disagree.");
        }

        return new ForgeNavigationFullPackage(
            packagePath,
            packageSha256,
            packageInfo.Length,
            manifest,
            snapshot,
            document,
            snapshotBytes);
    }

    public static GalaxyDataSet CreateDataSet(
        ForgeNavigationEntitySnapshotDocument snapshot,
        long authorityRevision,
        string datasetEpoch,
        string source,
        string snapshotSha256,
        string packageSha256)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(datasetEpoch);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ValidateSnapshot(snapshot);
        var document = ForgeNavigationEntityCatalogCodec.ToDataDocument(snapshot);
        ValidateDocument(document);

        var topology = new GalaxyTopology(
        [
            .. document.Sectors.Select(sector =>
                new GalaxySectorDefinition
                {
                    Key = sector.Key,
                    Name = sector.Name,
                    SystemName = sector.SystemName,
                    RequiredProfession = sector.RequiredProfession,
                    RequiredFaction = sector.RequiredFaction,
                    MinimumFactionStanding = sector.MinimumFactionStanding,
                    Aliases = sector.Aliases,
                    Connections = sector.Connections,
                }),
        ]);

        var catalogDocument = new GalaxyNavigationCatalogDocument
        {
            SchemaVersion = 2,
            GeneratedAt = document.GeneratedAt,
            Sectors =
            [
                .. document.Sectors
                    .Where(sector =>
                        sector.ActiveSectorNumber != 0 ||
                        sector.Targets.Count != 0 ||
                        sector.Departures.Count != 0)
                    .Select(MapCatalogSector),
            ],
        };
        var catalog = new GalaxyNavigationCatalog(
            topology,
            catalogDocument);

        return new GalaxyDataSet(
            snapshot,
            document,
            authorityRevision,
            datasetEpoch,
            source,
            snapshotSha256,
            packageSha256,
            topology,
            catalog);
    }

    public static ForgeNavigationEntitySnapshotDocument DeserializeSnapshot(
        ReadOnlySpan<byte> snapshotBytes)
    {
        return JsonSerializer.Deserialize<ForgeNavigationEntitySnapshotDocument>(
                snapshotBytes,
                ForgeNavigationDataJson.DistributionReadOptions) ??
            throw new InvalidOperationException(
                "Navigation entity snapshot is empty.");
    }

    public static void ValidateSnapshot(
        ForgeNavigationEntitySnapshotDocument snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.ContractVersion !=
                ForgeNavigationEntitySnapshotDocument.CurrentContractVersion ||
            snapshot.Revision <= 0 ||
            snapshot.GeneratedAt == default ||
            snapshot.Entities == null ||
            snapshot.Entities.Any(entity => entity == null))
        {
            throw new InvalidOperationException(
                "Navigation entity snapshot is invalid or unsupported.");
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entity in snapshot.Entities)
        {
            ValidateEntityIdentity(
                entity.Kind,
                entity.Id,
                entity.Version,
                entity.Document.ValueKind);

            if (!keys.Add(EntityKey(entity.Kind, entity.Id)))
            {
                throw new InvalidOperationException(
                    $"Navigation entity '{entity.Kind}/{entity.Id}' is duplicated.");
            }
        }
    }

    internal static void ValidateDelta(
        ForgeNavigationEntityDeltaDocument delta)
    {
        ArgumentNullException.ThrowIfNull(delta);

        if (delta.ContractVersion !=
                ForgeNavigationEntitySnapshotDocument.CurrentContractVersion ||
            delta.BaseRevision <= 0 ||
            delta.TargetRevision <= delta.BaseRevision ||
            delta.GeneratedAt == default ||
            delta.Changes == null ||
            delta.Changes.Any(change => change == null) ||
            !ForgeNavigationHash.IsSha256(delta.ResultSnapshotSha256))
        {
            throw new InvalidOperationException(
                "Navigation entity delta is invalid or unsupported.");
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var change in delta.Changes)
        {
            var isUpsert = string.Equals(
                change.Operation,
                "upsert",
                StringComparison.Ordinal);
            var isDelete = string.Equals(
                change.Operation,
                "delete",
                StringComparison.Ordinal);

            if ((!isUpsert && !isDelete) ||
                isUpsert && change.Document is null ||
                isDelete && change.Document is not null)
            {
                throw new InvalidOperationException(
                    $"Navigation entity change '{change.Kind}/{change.Id}' is invalid.");
            }

            ValidateEntityIdentity(
                change.Kind,
                change.Id,
                change.Version,
                change.Document?.ValueKind ?? JsonValueKind.Object);

            if (!keys.Add(EntityKey(change.Kind, change.Id)))
            {
                throw new InvalidOperationException(
                    $"Navigation entity change '{change.Kind}/{change.Id}' is duplicated.");
            }
        }
    }

    public static void ValidateDocument(
        ForgeNavigationDataDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.Sectors == null ||
            document.Npcs == null ||
            document.StationFacilities == null ||
            document.VendorItems == null ||
            document.MobVariants == null ||
            document.MobClusters == null ||
            document.MobLoot == null ||
            document.HarvestableVariants == null ||
            document.HarvestableFields == null ||
            document.HarvestableResources == null ||
            document.GravityWells == null ||
            document.Sectors.Any(sector => sector == null) ||
            document.Npcs.Any(npc => npc == null) ||
            document.StationFacilities.Any(facility => facility == null) ||
            document.VendorItems.Any(item => item == null) ||
            document.MobVariants.Any(variant => variant == null) ||
            document.MobClusters.Any(cluster => cluster == null) ||
            document.MobLoot.Any(relationship => relationship == null) ||
            document.HarvestableVariants.Any(variant => variant == null) ||
            document.HarvestableFields.Any(field => field == null) ||
            document.HarvestableResources.Any(resource => resource == null) ||
            document.GravityWells.Any(gravityWell => gravityWell == null))
        {
            throw new InvalidOperationException(
                "Navigation document contains a null sector collection or record.");
        }

        if (document.GeneratedAt == default)
        {
            throw new InvalidOperationException(
                "Navigation document generation time is required.");
        }

        if (document.ContractVersion !=
                ForgeNavigationEntitySnapshotDocument.CurrentContractVersion ||
            document.Revision <= 0)
        {
            throw new InvalidOperationException(
                $"Unsupported navigation contract or revision {document.ContractVersion}/{document.Revision}.");
        }

        EnsureUnique(document.Sectors.Select(sector => sector.Id), "sector ID");
        EnsureUnique(document.Sectors.Select(sector => sector.Key), "sector key");
        EnsureUnique(document.Npcs.Select(npc => npc.Id), "NPC ID");
        EnsureUnique(
            document.StationFacilities.Select(facility => facility.Id),
            "station facility ID");
        EnsureUnique(
            document.VendorItems.Select(item => item.Id),
            "vendor item ID");
        EnsureUnique(
            document.MobVariants.Select(variant => variant.Id),
            "mob variant ID");
        EnsureUnique(
            document.MobClusters.Select(cluster => cluster.Id),
            "mob cluster ID");
        EnsureUnique(
            document.MobLoot.Select(relationship => relationship.Id),
            "mob loot ID");
        EnsureUnique(
            document.HarvestableVariants.Select(variant => variant.Id),
            "harvestable variant ID");
        EnsureUnique(
            document.HarvestableFields.Select(field => field.Id),
            "harvestable field ID");
        EnsureUnique(
            document.HarvestableResources.Select(resource => resource.Id),
            "harvestable resource ID");
        EnsureUnique(
            document.GravityWells.Select(gravityWell => gravityWell.Id),
            "gravity well ID");

        foreach (var npc in document.Npcs)
        {
            ValidateNpc(npc);
        }

        var sectorsByKey = document.Sectors.ToDictionary(
            sector => sector.Key,
            StringComparer.Ordinal);
        var sectorNames = new Dictionary<string, string>(StringComparer.Ordinal);
        var activeSectorNumbers = new HashSet<uint>();
        var targetIds = new HashSet<string>(StringComparer.Ordinal);
        var departureIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var sector in document.Sectors)
        {
            ValidateSectorCollections(sector);
            RequireValue(sector.Id, "Sector ID");
            RequireValue(sector.Key, "Sector key");
            RequireValue(sector.Name, "Sector name");
            RequireValue(sector.SystemName, "Sector system name");
            EnsureUnique(sector.Connections, $"connection in sector {sector.Key}");
            EnsureUnique(
                sector.Aliases.Select(GalaxyTopology.NormalizeName),
                $"alias in sector {sector.Key}");

            if (!string.IsNullOrWhiteSpace(sector.RequiredFaction) &&
                !string.IsNullOrWhiteSpace(sector.RequiredProfession))
            {
                throw new InvalidOperationException(
                    $"Sector '{sector.Key}' cannot require both a faction and profession.");
            }

            foreach (var name in sector.Aliases.Prepend(sector.Name))
            {
                var normalizedName = GalaxyTopology.NormalizeName(name);

                if (sectorNames.TryGetValue(normalizedName, out var existingSectorId) &&
                    !string.Equals(existingSectorId, sector.Id, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Navigation sector name or alias '{name}' is ambiguous.");
                }

                sectorNames[normalizedName] = sector.Id;
            }

            if (sector.ActiveSectorNumber != 0 &&
                !activeSectorNumbers.Add(sector.ActiveSectorNumber))
            {
                throw new InvalidOperationException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Active sector number {sector.ActiveSectorNumber} is duplicated."));
            }

            if (sector.Targets.Select(target => target.Ordinal).Distinct().Count() !=
                sector.Targets.Count)
            {
                throw new InvalidOperationException(
                    $"Sector '{sector.Key}' contains duplicate target ordinals.");
            }

            if (sector.Departures.Select(departure => departure.Ordinal).Distinct().Count() !=
                sector.Departures.Count)
            {
                throw new InvalidOperationException(
                    $"Sector '{sector.Key}' contains duplicate departure ordinals.");
            }

            foreach (var target in sector.Targets)
            {
                ValidateTarget(target);

                if (!targetIds.Add(target.Id))
                {
                    throw new InvalidOperationException(
                        $"Navigation target ID '{target.Id}' is duplicated.");
                }
            }

            foreach (var departure in sector.Departures)
            {
                ValidateDeparture(departure);

                if (!departureIds.Add(departure.Id))
                {
                    throw new InvalidOperationException(
                        $"Navigation departure ID '{departure.Id}' is duplicated.");
                }

                var departureTargetName = GalaxyTopology.NormalizeName(
                    departure.DepartureTargetName);
                var targetExists = sector.Targets.Any(target =>
                    target.RawObjectType == departure.RawObjectType &&
                    (string.Equals(
                         GalaxyTopology.NormalizeName(target.Name),
                         departureTargetName,
                         StringComparison.Ordinal) ||
                     string.Equals(
                         GalaxyTopology.NormalizeName(target.MapDisplayName),
                         departureTargetName,
                         StringComparison.Ordinal)));

                if (!targetExists)
                {
                    throw new InvalidOperationException(
                        $"Departure '{sector.Key}:{departure.DepartureTargetName}' has no matching target.");
                }
            }
        }

        foreach (var npc in document.Npcs)
        {
            ValidateNpcLocation(document.Sectors, npc);
        }

        foreach (var facility in document.StationFacilities)
        {
            ValidateStationFacility(facility);
            ValidateStationFacilityLocation(document.Sectors, facility);
        }

        var npcsById = document.Npcs.ToDictionary(
            npc => npc.Id,
            StringComparer.Ordinal);

        var vendorItemRelations = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in document.VendorItems)
        {
            ValidateVendorItem(item);

            if (!npcsById.TryGetValue(item.VendorNpcId, out var vendor) ||
                vendor.Role is null or <= 1)
            {
                throw new InvalidOperationException(
                    $"Vendor item '{item.Id}' references an unknown non-vendor NPC.");
            }

            var relation = string.Create(
                CultureInfo.InvariantCulture,
                $"{item.VendorNpcId}:{item.ItemTemplateId}");

            if (!vendorItemRelations.Add(relation))
            {
                throw new InvalidOperationException(
                    $"Vendor item relation '{relation}' is duplicated.");
            }
        }

        var sectorIds = document.Sectors
            .Select(sector => sector.Id)
            .ToHashSet(StringComparer.Ordinal);
        var mobVariantsById = document.MobVariants.ToDictionary(
            variant => variant.Id,
            StringComparer.Ordinal);

        foreach (var variant in document.MobVariants)
        {
            ValidateMobVariant(variant);
        }

        foreach (var cluster in document.MobClusters)
        {
            ValidateMobCluster(cluster);

            if (!mobVariantsById.ContainsKey(cluster.MobVariantId))
            {
                throw new InvalidOperationException(
                    $"Mob cluster '{cluster.Id}' references an unknown variant.");
            }

            if (!sectorIds.Contains(cluster.SectorId))
            {
                throw new InvalidOperationException(
                    $"Mob cluster '{cluster.Id}' references an unknown sector.");
            }
        }

        var mobLootRelations = new HashSet<string>(StringComparer.Ordinal);

        foreach (var relationship in document.MobLoot)
        {
            ValidateMobLoot(relationship);

            if (!mobVariantsById.ContainsKey(relationship.MobVariantId))
            {
                throw new InvalidOperationException(
                    $"Mob loot '{relationship.Id}' references an unknown variant.");
            }

            var relation = string.Create(
                CultureInfo.InvariantCulture,
                $"{relationship.MobVariantId}:{relationship.ItemTemplateId}");

            if (!mobLootRelations.Add(relation))
            {
                throw new InvalidOperationException(
                    $"Mob loot relation '{relation}' is duplicated.");
            }
        }

        var harvestableVariantsById = document.HarvestableVariants.ToDictionary(
            variant => variant.Id,
            StringComparer.Ordinal);
        var harvestableFieldsById = document.HarvestableFields.ToDictionary(
            field => field.Id,
            StringComparer.Ordinal);

        foreach (var variant in document.HarvestableVariants)
        {
            ValidateHarvestableVariant(variant);
        }

        foreach (var field in document.HarvestableFields)
        {
            ValidateHarvestableField(field);

            if (!sectorIds.Contains(field.SectorId))
            {
                throw new InvalidOperationException(
                    $"Harvestable field '{field.Id}' references an unknown sector.");
            }
        }

        var harvestableRelations = new HashSet<string>(StringComparer.Ordinal);

        foreach (var resource in document.HarvestableResources)
        {
            ValidateHarvestableResource(resource);

            if (!harvestableFieldsById.ContainsKey(resource.HarvestableFieldId) ||
                !harvestableVariantsById.ContainsKey(resource.HarvestableVariantId))
            {
                throw new InvalidOperationException(
                    $"Harvestable resource '{resource.Id}' references an unknown field or variant.");
            }

            var relation = string.Create(
                CultureInfo.InvariantCulture,
                $"{resource.HarvestableFieldId}:{resource.HarvestableVariantId}:{resource.ItemTemplateId}");

            if (!harvestableRelations.Add(relation))
            {
                throw new InvalidOperationException(
                    $"Harvestable resource relation '{relation}' is duplicated.");
            }
        }

        foreach (var gravityWell in document.GravityWells)
        {
            ValidateGravityWell(gravityWell);

            if (!sectorIds.Contains(gravityWell.SectorId))
            {
                throw new InvalidOperationException(
                    $"Gravity well '{gravityWell.Id}' references an unknown sector.");
            }
        }

        foreach (var sector in document.Sectors)
        {
            foreach (var connectionKey in sector.Connections)
            {
                if (!sectorsByKey.TryGetValue(connectionKey, out var connectedSector) ||
                    !connectedSector.Connections.Contains(
                        sector.Key,
                        StringComparer.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Topology connection '{sector.Key}' to '{connectionKey}' is invalid.");
                }
            }

            foreach (var departure in sector.Departures.Where(candidate =>
                         string.Equals(
                             candidate.Status,
                             "Verified",
                             StringComparison.Ordinal)))
            {
                if (string.IsNullOrWhiteSpace(departure.ToSectorKey) ||
                    !sectorsByKey.ContainsKey(departure.ToSectorKey) ||
                    !sector.Connections.Contains(
                        departure.ToSectorKey,
                        StringComparer.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Verified departure '{sector.Key}' to '{departure.ToSectorKey}' is not in the topology.");
                }
            }
        }
    }

    internal static void ValidateManifest(
        ForgeNavigationDataPackageManifest manifest,
        string expectedMode)
    {
        if (manifest.PackageFormatVersion !=
                ForgeNavigationDataPackageManifest.CurrentPackageFormatVersion ||
            manifest.ContractVersion !=
                ForgeNavigationEntitySnapshotDocument.CurrentContractVersion ||
            manifest.DataRevision <= 0 ||
            manifest.SourceAuthorityRevision <= 0 ||
            !string.Equals(manifest.Component, "navigation", StringComparison.Ordinal) ||
            !string.Equals(manifest.Mode, expectedMode, StringComparison.Ordinal) ||
            manifest.CreatedAt == default ||
            string.IsNullOrWhiteSpace(manifest.Origin) ||
            !ForgeNavigationHash.IsSha256(manifest.PayloadSha256) ||
            !ForgeNavigationHash.IsSha256(manifest.ResultSnapshotSha256) ||
            manifest.EntityCounts == null ||
            manifest.EntityCounts.Any(pair =>
                string.IsNullOrWhiteSpace(pair.Key) || pair.Value < 0))
        {
            throw new InvalidOperationException(
                "Navigation package manifest is invalid or unsupported.");
        }

        var expectedEntry = string.Equals(expectedMode, "full", StringComparison.Ordinal)
            ? "navigation.snapshot.json"
            : "navigation.delta.json";

        if (!string.Equals(
                manifest.PayloadEntry,
                expectedEntry,
                StringComparison.Ordinal) ||
            string.Equals(expectedMode, "full", StringComparison.Ordinal) &&
            manifest.BaseRevision != null ||
            string.Equals(expectedMode, "delta", StringComparison.Ordinal) &&
            (manifest.BaseRevision == null ||
             manifest.BaseRevision <= 0 ||
             manifest.BaseRevision >= manifest.DataRevision))
        {
            throw new InvalidOperationException(
                "Navigation package payload or revision metadata is invalid.");
        }
    }

    internal static void ValidateManifestCounts(
        ForgeNavigationDataPackageManifest manifest,
        ForgeNavigationEntitySnapshotDocument snapshot)
    {
        var actual = ForgeNavigationEntityCatalogCodec.CountByKind(snapshot);

        if (manifest.EntityCounts.Count != actual.Count ||
            actual.Any(pair =>
                !manifest.EntityCounts.TryGetValue(pair.Key, out var count) ||
                count != pair.Value))
        {
            throw new InvalidOperationException(
                "Navigation package entity counts do not match its snapshot.");
        }
    }

    private static void ValidateEntityIdentity(
        string? kind,
        string? id,
        int version,
        JsonValueKind documentKind)
    {
        if (string.IsNullOrWhiteSpace(kind) || kind.Length > 64 ||
            string.IsNullOrWhiteSpace(id) || id.Length > 256 ||
            version <= 0 ||
            documentKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                "Navigation entity identity or document is invalid.");
        }
    }

    private static string EntityKey(string kind, string id)
    {
        return string.Concat(kind, "\u001f", id);
    }

    internal static byte[] ReadEntry(
        ZipArchive archive,
        string entryName,
        int maximumBytes)
    {
        var entry = archive.GetEntry(entryName) ??
            throw new InvalidOperationException(
                $"Navigation package entry '{entryName}' is missing.");

        if (entry.Length < 0 || entry.Length > maximumBytes)
        {
            throw new InvalidOperationException(
                $"Navigation package entry '{entryName}' is too large.");
        }

        using var stream = entry.Open();
        using var memory = new MemoryStream(checked((int)entry.Length));
        stream.CopyTo(memory);

        if (memory.Length != entry.Length)
        {
            throw new InvalidOperationException(
                $"Navigation package entry '{entryName}' was truncated.");
        }

        return memory.ToArray();
    }

    internal static void ValidateArchiveEntries(
        ZipArchive archive,
        string payloadEntryName)
    {
        if (archive.Entries.Count != 2 ||
            archive.GetEntry("net7forge.json") == null ||
            archive.GetEntry(payloadEntryName) == null)
        {
            throw new InvalidOperationException(
                "Navigation package contains unexpected archive entries.");
        }

        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.IndexOfAny(['/', '\\']) >= 0 ||
                !string.Equals(entry.Name, entry.FullName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Navigation package contains an unsafe archive entry.");
            }
        }
    }


    private static void ValidateSectorCollections(
        ForgeNavigationSectorDocument sector)
    {
        if (sector.Aliases == null ||
            sector.Connections == null ||
            sector.Targets == null ||
            sector.Departures == null ||
            sector.Aliases.Any(alias => alias == null) ||
            sector.Connections.Any(connection => connection == null) ||
            sector.Targets.Any(target => target == null) ||
            sector.Departures.Any(departure => departure == null))
        {
            throw new InvalidOperationException(
                $"Navigation sector '{sector.Key}' contains a null collection or record.");
        }
    }

    private static GalaxyNavigationCatalogSector MapCatalogSector(
        ForgeNavigationSectorDocument sector)
    {
        return new GalaxyNavigationCatalogSector
        {
            SectorKey = sector.Key,
            ActiveSectorNumber = sector.ActiveSectorNumber,
            Targets =
            [
                .. sector.Targets.Select(target =>
                    new GalaxyNavigationCatalogTarget
                    {
                        Name = target.Name,
                        MapDisplayName = target.MapDisplayName,
                        Signature = target.Signature,
                        RawObjectType = target.RawObjectType,
                        NavType = target.NavType,
                        IsHuge = target.IsHuge,
                        SelectionContext = ParseTargetSelectionContext(
                            target.SelectionContext),
                        HasPosition = target.HasPosition,
                        X = target.X,
                        Y = target.Y,
                        Z = target.Z,
                    }),
            ],
            Departures =
            [
                .. sector.Departures.Select(departure =>
                    new GalaxyNavigationCatalogDeparture
                    {
                        ToSectorKey = departure.ToSectorKey,
                        DestinationName = departure.DestinationName,
                        DepartureTargetName = departure.DepartureTargetName,
                        RawObjectType = departure.RawObjectType,
                        Status = Enum.Parse<GalaxyNavigationDepartureStatus>(
                            departure.Status,
                            ignoreCase: false),
                        HasExpectedPosition = departure.HasExpectedPosition,
                        ExpectedX = departure.ExpectedX,
                        ExpectedY = departure.ExpectedY,
                        ExpectedZ = departure.ExpectedZ,
                        DestinationSectorNumber = departure.DestinationSectorNumber,
                        VerificationCount = departure.VerificationCount,
                        AccessRequirement = departure.AccessRequirement,
                        Note = departure.Note,
                    }),
            ],
        };
    }

    private static void ValidateNpcLocation(
        IReadOnlyList<ForgeNavigationSectorDocument> sectors,
        ForgeNavigationNpcDocument npc)
    {
        var normalizedSectorName = GalaxyTopology.NormalizeName(npc.SectorName);
        // ActiveSectorNumber identifies the loaded station interior. It is
        // intentionally different from the surrounding space sector number,
        // so resolve the containing sector exclusively by its name or alias.
        var matchingSectors = sectors
            .Where(sector =>
                string.Equals(
                    GalaxyTopology.NormalizeName(sector.Name),
                    normalizedSectorName,
                    StringComparison.Ordinal) ||
                sector.Aliases.Any(alias =>
                    string.Equals(
                        GalaxyTopology.NormalizeName(alias),
                        normalizedSectorName,
                        StringComparison.Ordinal)))
            .ToArray();

        if (matchingSectors.Length != 1)
        {
            throw new InvalidOperationException(
                $"NPC '{npc.Id}' does not identify one known sector.");
        }

        var sector = matchingSectors[0];

        var normalizedStationName = GalaxyTopology.NormalizeName(npc.StationName);
        var stationExists = sector.Targets.Any(target =>
            target.RawObjectType is 3 or 12 &&
            (string.Equals(
                 GalaxyTopology.NormalizeName(target.Name),
                 normalizedStationName,
                 StringComparison.Ordinal) ||
             string.Equals(
                 GalaxyTopology.NormalizeName(target.MapDisplayName),
                 normalizedStationName,
                 StringComparison.Ordinal)));

        if (!stationExists)
        {
            throw new InvalidOperationException(
                $"NPC '{npc.Id}' references an unknown station.");
        }
    }

    private static void ValidateNpc(ForgeNavigationNpcDocument npc)
    {
        RequireValue(npc.Id, "NPC ID");
        RequireValue(npc.StationName, "NPC station name");
        RequireValue(npc.SectorName, "NPC sector name");
        ValidateNpcIdentity(npc.DefinitionKey, npc.DefinitionSecondaryId, npc.Name);
        ValidateOptionalValue(npc.Name, 128, "NPC name");
        RequireValue(npc.Confidence, "NPC confidence");

        if (npc.RoomClass < 0 ||
            npc.RoomDefinitionKey < 0 ||
            npc.RoomNpcSlot is < 0 or > 4095)
        {
            throw new InvalidOperationException(
                "NPC room location is invalid.");
        }

        if (!string.Equals(npc.Confidence, "Observed", StringComparison.Ordinal) &&
            !string.Equals(npc.Confidence, "Corroborated", StringComparison.Ordinal) &&
            !string.Equals(npc.Confidence, "Verified", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"NPC confidence '{npc.Confidence}' is invalid.");
        }

        if (npc.NamedReporters == null ||
            npc.NamedReporters.Any(name => string.IsNullOrWhiteSpace(name)))
        {
            throw new InvalidOperationException(
                "NPC named reporters contain a null or empty value.");
        }

        EnsureUnique(
            npc.NamedReporters.Select(GalaxyTopology.NormalizeName),
            $"named reporter for NPC {npc.Id}");
    }

    private static void ValidateStationFacilityLocation(
        IReadOnlyList<ForgeNavigationSectorDocument> sectors,
        ForgeNavigationStationFacilityDocument facility)
    {
        var normalizedSectorName = GalaxyTopology.NormalizeName(facility.SectorName);
        var matchingSectors = sectors
            .Where(sector =>
                string.Equals(
                    GalaxyTopology.NormalizeName(sector.Name),
                    normalizedSectorName,
                    StringComparison.Ordinal) ||
                sector.Aliases.Any(alias =>
                    string.Equals(
                        GalaxyTopology.NormalizeName(alias),
                        normalizedSectorName,
                        StringComparison.Ordinal)))
            .ToArray();

        if (matchingSectors.Length != 1)
        {
            throw new InvalidOperationException(
                $"Station facility '{facility.Id}' does not identify one known sector.");
        }

        var normalizedStationName = GalaxyTopology.NormalizeName(facility.StationName);
        var stationExists = matchingSectors[0].Targets.Any(target =>
            target.RawObjectType is 3 or 12 &&
            (string.Equals(
                 GalaxyTopology.NormalizeName(target.Name),
                 normalizedStationName,
                 StringComparison.Ordinal) ||
             string.Equals(
                 GalaxyTopology.NormalizeName(target.MapDisplayName),
                 normalizedStationName,
                 StringComparison.Ordinal)));

        if (!stationExists)
        {
            throw new InvalidOperationException(
                $"Station facility '{facility.Id}' references an unknown station.");
        }
    }

    private static void ValidateStationFacility(
        ForgeNavigationStationFacilityDocument facility)
    {
        RequireValue(facility.Id, "Station facility ID");
        RequireValue(facility.StationName, "Station facility station name");
        RequireValue(facility.SectorName, "Station facility sector name");
        RequireValue(facility.Name, "Station facility name");
        RequireValue(facility.Confidence, "Station facility confidence");

        if (facility.RoomClass < 0 ||
            facility.RoomDefinitionKey < 0 ||
            facility.RoomFacilitySlot is < 0 or > 4095 ||
            facility.DefinitionSlot is < 0 or > 4095 ||
            facility.FacilityType is < 0 or > 65535)
        {
            throw new InvalidOperationException(
                "Station facility location or type is invalid.");
        }

        if (!string.Equals(facility.Confidence, "Observed", StringComparison.Ordinal) &&
            !string.Equals(facility.Confidence, "Corroborated", StringComparison.Ordinal) &&
            !string.Equals(facility.Confidence, "Verified", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Station facility confidence '{facility.Confidence}' is invalid.");
        }

        if (facility.NamedReporters == null ||
            facility.NamedReporters.Any(name => string.IsNullOrWhiteSpace(name)))
        {
            throw new InvalidOperationException(
                "Station facility named reporters contain a null or empty value.");
        }

        EnsureUnique(
            facility.NamedReporters.Select(GalaxyTopology.NormalizeName),
            $"named reporter for station facility {facility.Id}");
    }

    private static void ValidateTarget(
        ForgeNavigationTargetDocument target)
    {
        RequireValue(target.Id, "Target ID");
        RequireValue(target.Name, "Target name");

        if (target.MapDisplayName == null)
        {
            throw new InvalidOperationException(
                "Target map display name cannot be null.");
        }

        if (target.Ordinal < 0)
        {
            throw new InvalidOperationException(
                "Target ordinal cannot be negative.");
        }

        var selectionContext = ParseTargetSelectionContext(
            target.SelectionContext);

        if (selectionContext ==
            GalaxyNavigationTargetSelectionContext.Navigation)
        {
            if (!target.Signature.HasValue ||
                !target.NavType.HasValue ||
                !target.IsHuge.HasValue)
            {
                throw new InvalidOperationException(
                    "Navigation-context targets require complete navigation metadata.");
            }

            RequireFinite(target.Signature.Value, "Target signature");

            if (target.NavType.Value is < 0 or > 16)
            {
                throw new InvalidOperationException(
                    "Target navigation type is invalid.");
            }
        }
        else if (target.Signature.HasValue ||
                 target.NavType.HasValue ||
                 target.IsHuge.HasValue ||
                 !target.HasPosition)
        {
            throw new InvalidOperationException(
                "Object-context targets require a fixed position and cannot claim unavailable navigation metadata.");
        }

        RequireFinite(target.X, "Target X");
        RequireFinite(target.Y, "Target Y");
        RequireFinite(target.Z, "Target Z");
    }

    private static GalaxyNavigationTargetSelectionContext
        ParseTargetSelectionContext(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            string.Equals(
                value,
                "Navigation",
                StringComparison.Ordinal))
        {
            return GalaxyNavigationTargetSelectionContext.Navigation;
        }

        if (string.Equals(
                value,
                "Object",
                StringComparison.Ordinal))
        {
            return GalaxyNavigationTargetSelectionContext.Object;
        }

        throw new InvalidOperationException(
            $"Target selection context '{value}' is invalid.");
    }

    internal static void ValidateVendorItem(
        ForgeNavigationVendorItemDocument item)
    {
        RequireValue(item.Id, "Vendor item ID");
        RequireValue(item.VendorNpcId, "Vendor item vendor NPC ID");
        RequireValue(item.Confidence, "Vendor item confidence");

        if (item.ItemTemplateId <= 0)
        {
            throw new InvalidOperationException(
                "Vendor item template identity is invalid.");
        }

        if (!string.Equals(item.Confidence, "Observed", StringComparison.Ordinal) &&
            !string.Equals(item.Confidence, "Corroborated", StringComparison.Ordinal) &&
            !string.Equals(item.Confidence, "Verified", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Vendor item confidence '{item.Confidence}' is invalid.");
        }

        if (item.NamedReporters == null ||
            item.NamedReporters.Any(name => string.IsNullOrWhiteSpace(name)))
        {
            throw new InvalidOperationException(
                "Vendor item named reporters contain a null or empty value.");
        }

        EnsureUnique(
            item.NamedReporters.Select(GalaxyTopology.NormalizeName),
            $"named reporter for vendor item {item.Id}");
    }

    internal static void ValidateMobVariant(
        ForgeNavigationMobVariantDocument variant)
    {
        RequireValue(variant.Id, "Mob variant ID");
        RequireValue(variant.Name, "Mob variant name");

        if (variant.Name.Length > 128 || variant.Name.Any(char.IsControl) ||
            variant.RawObjectType is not (0 or 2 or 35) ||
            variant.CombatLevel is < 0 or > 255 ||
            variant.FactionIdentifier == null ||
            variant.FactionIdentifier.Length > 128 ||
            variant.FactionIdentifier.Any(char.IsControl))
        {
            throw new InvalidOperationException(
                $"Mob variant '{variant.Id}' is invalid.");
        }

        if (!Enum.IsDefined(typeof(ForgeNavigationEncounterFactionBindingKind), variant.FactionBindingKind) ||
            variant.IntrinsicRelationshipRaw is < 0 or > 3 ||
            variant.IntrinsicDisposition.HasValue &&
            !Enum.IsDefined(typeof(ForgeNavigationEncounterResolvedDisposition), variant.IntrinsicDisposition.Value))
        {
            throw new InvalidOperationException(
                $"Mob variant '{variant.Id}' has invalid disposition metadata.");
        }

        if (variant.FactionBindingKind ==
                ForgeNavigationEncounterFactionBindingKind.FactionLinked &&
            string.IsNullOrWhiteSpace(variant.FactionIdentifier))
        {
            throw new InvalidOperationException(
                $"Mob variant '{variant.Id}' is faction-linked but has no faction identifier.");
        }

        if (variant.FactionBindingKind !=
                ForgeNavigationEncounterFactionBindingKind.Unaffiliated &&
            (variant.IntrinsicRelationshipRaw.HasValue ||
             variant.IntrinsicDisposition.HasValue))
        {
            throw new InvalidOperationException(
                $"Mob variant '{variant.Id}' stores intrinsic disposition without an explicit unaffiliated binding.");
        }
    }

    internal static void ValidateMobCluster(
        ForgeNavigationMobEncounterClusterDocument cluster)
    {
        RequireValue(cluster.Id, "Mob cluster ID");
        RequireValue(cluster.MobVariantId, "Mob cluster variant ID");
        RequireValue(cluster.SectorId, "Mob cluster sector ID");
        RequireFinite(cluster.CenterX, "Mob cluster center X");
        RequireFinite(cluster.CenterY, "Mob cluster center Y");
        RequireFinite(cluster.CenterZ, "Mob cluster center Z");
        RequireFinite(cluster.Radius, "Mob cluster radius");

        if (cluster.Radius is < 0 or > 12500 ||
            cluster.SightingCount <= 0 ||
            cluster.FirstSeenAtUtc == default ||
            cluster.LastSeenAtUtc == default ||
            cluster.FirstSeenAtUtc > cluster.LastSeenAtUtc)
        {
            throw new InvalidOperationException(
                $"Mob cluster '{cluster.Id}' is invalid.");
        }
    }

    internal static void ValidateMobLoot(
        ForgeNavigationMobLootDocument relationship)
    {
        RequireValue(relationship.Id, "Mob loot ID");
        RequireValue(relationship.MobVariantId, "Mob loot variant ID");
        RequireValue(relationship.Confidence, "Mob loot confidence");

        if (relationship.ItemTemplateId <= 0)
        {
            throw new InvalidOperationException(
                "Mob loot item template identity is invalid.");
        }

        if (!string.Equals(relationship.Confidence, "Observed", StringComparison.Ordinal) &&
            !string.Equals(relationship.Confidence, "Corroborated", StringComparison.Ordinal) &&
            !string.Equals(relationship.Confidence, "Verified", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Mob loot confidence '{relationship.Confidence}' is invalid.");
        }

        if (relationship.NamedReporters == null ||
            relationship.NamedReporters.Any(name => string.IsNullOrWhiteSpace(name)))
        {
            throw new InvalidOperationException(
                "Mob loot named reporters contain a null or empty value.");
        }

        EnsureUnique(
            relationship.NamedReporters.Select(GalaxyTopology.NormalizeName),
            $"named reporter for mob loot {relationship.Id}");
    }

    internal static void ValidateHarvestableVariant(
        ForgeNavigationHarvestableVariantDocument variant)
    {
        RequireValue(variant.Id, "Harvestable variant ID");
        RequireValue(variant.Name, "Harvestable variant name");
        RequireValue(variant.Category, "Harvestable variant category");

        if (variant.Name.Length > 128 ||
            variant.Name.Any(char.IsControl) ||
            variant.RawObjectType != 0x26 ||
            variant.TechLevel is < 1 or > 255 ||
            variant.Category is not ("Ore" or "Gas" or "Hydrocarbon" or "Hulk" or "Unknown"))
        {
            throw new InvalidOperationException(
                $"Harvestable variant '{variant.Id}' is invalid.");
        }
    }

    internal static void ValidateHarvestableField(
        ForgeNavigationHarvestableFieldDocument field)
    {
        RequireValue(field.Id, "Harvestable field ID");
        RequireValue(field.SectorId, "Harvestable field sector ID");
        RequireFinite(field.CenterX, "Harvestable field center X");
        RequireFinite(field.CenterY, "Harvestable field center Y");
        RequireFinite(field.CenterZ, "Harvestable field center Z");
        RequireFinite(field.Radius, "Harvestable field radius");

        if (field.Radius is < 0 or > 30000 ||
            field.ObservationCount <= 0 ||
            field.FirstSeenAtUtc == default ||
            field.LastSeenAtUtc == default ||
            field.FirstSeenAtUtc > field.LastSeenAtUtc)
        {
            throw new InvalidOperationException(
                $"Harvestable field '{field.Id}' is invalid.");
        }
    }

    internal static void ValidateHarvestableResource(
        ForgeNavigationHarvestableResourceDocument resource)
    {
        RequireValue(resource.Id, "Harvestable resource ID");
        RequireValue(resource.HarvestableFieldId, "Harvestable resource field ID");
        RequireValue(resource.HarvestableVariantId, "Harvestable resource variant ID");
        RequireValue(resource.Confidence, "Harvestable resource confidence");

        if (resource.ItemTemplateId <= 0 ||
            resource.Confidence is not ("Observed" or "Corroborated" or "Verified") ||
            resource.NamedReporters == null ||
            resource.NamedReporters.Any(name => string.IsNullOrWhiteSpace(name)))
        {
            throw new InvalidOperationException(
                $"Harvestable resource '{resource.Id}' is invalid.");
        }

        EnsureUnique(
            resource.NamedReporters.Select(GalaxyTopology.NormalizeName),
            $"named reporter for harvestable resource {resource.Id}");
    }

    internal static void ValidateGravityWell(
        ForgeNavigationGravityWellDocument gravityWell)
    {
        RequireValue(gravityWell.Id, "Gravity well ID");
        RequireValue(gravityWell.SectorId, "Gravity well sector ID");
        RequireFinite(gravityWell.CenterX, "Gravity well center X");
        RequireFinite(gravityWell.CenterY, "Gravity well center Y");
        RequireFinite(gravityWell.CenterZ, "Gravity well center Z");
        RequireFinite(gravityWell.Radius, "Gravity well radius");
        RequireFinite(gravityWell.AngularCoverage, "Gravity well angular coverage");
        RequireFinite(gravityWell.Confidence, "Gravity well confidence");

        if (gravityWell.Radius is < 0 or > 100000 ||
            gravityWell.BoundaryObservationCount <= 0 ||
            gravityWell.EnteringCount < 0 ||
            gravityWell.LeavingCount < 0 ||
            gravityWell.EnteringCount + gravityWell.LeavingCount !=
                gravityWell.BoundaryObservationCount ||
            gravityWell.AngularCoverage is < 0 or > 1 ||
            gravityWell.Confidence is < 0 or > 1 ||
            gravityWell.FirstObservedAtUtc == default ||
            gravityWell.LastObservedAtUtc == default ||
            gravityWell.UpdatedAtUtc == default ||
            gravityWell.FirstObservedAtUtc > gravityWell.LastObservedAtUtc)
        {
            throw new InvalidOperationException(
                $"Gravity well '{gravityWell.Id}' is invalid.");
        }
    }

    private static void ValidateDeparture(
        ForgeNavigationDepartureDocument departure)
    {
        RequireValue(departure.Id, "Departure ID");
        RequireValue(departure.DestinationName, "Departure destination name");

        if (departure.Ordinal < 0)
        {
            throw new InvalidOperationException(
                "Departure ordinal cannot be negative.");
        }
        RequireValue(departure.DepartureTargetName, "Departure target name");

        if (departure.AccessRequirement == null || departure.Note == null)
        {
            throw new InvalidOperationException(
                "Departure access requirement and note cannot be null.");
        }

        if (!Enum.TryParse<GalaxyNavigationDepartureStatus>(
                departure.Status,
                ignoreCase: false,
                out _))
        {
            throw new InvalidOperationException(
                $"Departure status '{departure.Status}' is invalid.");
        }

        RequireFinite(departure.ExpectedX, "Departure expected X");
        RequireFinite(departure.ExpectedY, "Departure expected Y");
        RequireFinite(departure.ExpectedZ, "Departure expected Z");
    }

    private static void RequireFinite(float value, string name)
    {
        if (!float.IsFinite(value))
        {
            throw new InvalidOperationException($"{name} must be finite.");
        }
    }

    private static void ValidateNpcIdentity(
        int definitionKey,
        int definitionSecondaryId,
        string? name)
    {
        if (definitionKey == 0 &&
            definitionSecondaryId == 0 &&
            string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException(
                "NPC requires a native definition identity or a name.");
        }
    }

    private static void ValidateOptionalValue(
        string? value,
        int maximumLength,
        string name)
    {
        if (value == null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"{name} must be null or contain a value.");
        }

        if (value.Length > maximumLength || value.Any(char.IsControl))
        {
            throw new InvalidOperationException(
                $"{name} is invalid.");
        }
    }

    private static void RequireValue(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{name} is required.");
        }
    }

    private static void EnsureUnique(
        IEnumerable<string> values,
        string description)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var value in values)
        {
            RequireValue(value, description);

            if (!seen.Add(value))
            {
                throw new InvalidOperationException(
                    $"Duplicate {description} '{value}'.");
            }
        }
    }
}
