namespace Net7ClientManager.GalaxyKnowledge;

using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;
using Net7ClientManager.Navigation;
using Net7ClientManager.Observations.Models;

internal static partial class GalaxyKnowledgeBuilder
{
    public static GalaxyKnowledgeSnapshot Build(
        ClientItemTemplateCatalogSnapshot catalog,
        GalaxyDataSet dataSet,
        ForgeProductionRecipeCatalogSnapshot recipeCatalog,
        ForgeMissionCatalogSnapshot missionCatalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(dataSet);
        ArgumentNullException.ThrowIfNull(recipeCatalog);
        ArgumentNullException.ThrowIfNull(missionCatalog);

        if (!catalog.IsAvailable)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(catalog.Status)
                    ? "The item catalog is unavailable."
                    : catalog.Status);
        }

        var catalogEntries = catalog.Templates
            .Where(entry =>
                entry.Template.IsAvailable &&
                entry.ItemTemplateId > 0 &&
                !string.IsNullOrWhiteSpace(entry.Template.Name))
            .OrderBy(entry => entry.ItemTemplateId)
            .ToArray();

        var catalogById = catalogEntries.ToDictionary(
            entry => entry.ItemTemplateId);
        var catalogNameGroups = catalogEntries
            .GroupBy(
                entry => entry.Template.Name.Trim(),
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var duplicateCdataItemNames = catalogNameGroups
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var catalogByName = catalogNameGroups
            .Where(group => group.Count() == 1)
            .ToDictionary(
                group => group.Key,
                group => group.Single(),
                StringComparer.OrdinalIgnoreCase);

        var missions = BuildMissions(
            missionCatalog,
            dataSet,
            catalogById,
            out var unresolvedMissionRewardItemTemplateIds,
            out var missionsWithUnresolvedRewardItems,
            out var unresolvedMissionCompletionLocationNames);
        var sourcesByItem = BuildSources(
            dataSet,
            catalogById,
            out var unresolvedSourceRelationshipIds);
        AppendMissionSources(
            sourcesByItem,
            missions,
            catalogById.Keys.ToHashSet());
        var refiningRelationships = BuildRefiningRelationships(
            catalogEntries,
            catalogByName,
            out var unresolvedRefiningTargets);
        var outgoingRefining = refiningRelationships
            .GroupBy(relationship => relationship.InputItemTemplateId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var incomingRefining = refiningRelationships
            .GroupBy(relationship => relationship.OutputItemTemplateId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var recipes = BuildRecipes(
            recipeCatalog,
            catalogById,
            out var unresolvedRecipeItemTemplateIds,
            out var recipesWithUnresolvedItems);
        var recipesByOutput = recipes
            .GroupBy(recipe => recipe.OutputItemTemplateId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var recipesByIngredient = recipes
            .SelectMany(recipe => recipe.Ingredients.Select(ingredient =>
                new
                {
                    ingredient.ItemTemplateId,
                    Recipe = recipe,
                }))
            .GroupBy(entry => entry.ItemTemplateId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(entry => entry.Recipe)
                    .Distinct()
                    .OrderBy(recipe => recipe.Kind)
                    .ThenBy(recipe => recipe.OutputItemTemplateId)
                    .ThenBy(recipe => recipe.Identity, StringComparer.Ordinal)
                    .ToArray());

        var effectProviders = new Dictionary<
            string,
            MutableEffectKnowledge>(
                StringComparer.Ordinal);
        var itemBuilders = new Dictionary<int, GalaxyItemKnowledge>();

        foreach (var entry in catalogEntries)
        {
            var template = entry.Template;
            var family = GalaxyItemFamilyResolver.Resolve(template);
            var effects = BuildEffects(
                template,
                effectProviders,
                entry.ItemTemplateId);

            itemBuilders[entry.ItemTemplateId] =
                new GalaxyItemKnowledge
                {
                    ItemTemplateId = entry.ItemTemplateId,
                    Name = template.Name.Trim(),
                    Family = family,
                    Category = template.Category,
                    Subcategory = template.Subcategory,
                    ItemType = template.ItemType,
                    TypeDisplayName = template.TypeDisplayName.Trim(),
                    TechLevel = checked((int)template.TechLevel),
                    GameBassetId = template.GameBasset,
                    ModelBassetId = entry.ModelBassetId,
                    IconBassetId = entry.IconBassetId,
                    MaximumStack = template.MaxStack,
                    Flags = template.Flags,
                    Manufacturer = template.Manufacturer.Trim(),
                    Description = template.Description.Trim(),
                    AdditionalText =
                        Array.AsReadOnly(entry.AdditionalText.ToArray()),
                    ProfessionRestrictionMask =
                        GetCombinedInt32Attribute(template, 0x03),
                    RaceRestrictionMask =
                        GetCombinedInt32Attribute(template, 0x12),
                    LoreRestriction =
                        GetMaximumInt32Attribute(template, 0x11),
                    RequiredCombatLevel =
                        GetMaximumInt32Attribute(template, 0x04),
                    RequiredExploreLevel =
                        GetMaximumInt32Attribute(template, 0x0c),
                    RequiredTradeLevel =
                        GetMaximumInt32Attribute(template, 0x20),
                    RequiredOverallLevel =
                        GetMaximumInt32Attribute(template, 0x0e),
                    Attributes =
                        Array.AsReadOnly(
                            template.Attributes
                                .Select(attribute =>
                                    new GalaxyItemAttributeKnowledge
                                    {
                                        ItemInfoId = attribute.ItemInfoId,
                                        TypeCode = attribute.TypeCode,
                                        RawValue = attribute.RawValue,
                                        Int32Value = attribute.Int32Value,
                                        FloatValue = attribute.FloatValue,
                                        StringValue = attribute.StringValue,
                                    })
                                .ToArray()),
                    Effects = Array.AsReadOnly(effects),
                    Sources = Array.AsReadOnly(
                        GetValuesOrEmpty(
                            sourcesByItem,
                            entry.ItemTemplateId)),
                    RefinesTo = Array.AsReadOnly(
                        GetValuesOrEmpty(
                            outgoingRefining,
                            entry.ItemTemplateId)),
                    RefinedFrom = Array.AsReadOnly(
                        GetValuesOrEmpty(
                            incomingRefining,
                            entry.ItemTemplateId)),
                    ProducedByRecipes = Array.AsReadOnly(
                        GetValuesOrEmpty(
                            recipesByOutput,
                            entry.ItemTemplateId)),
                    UsedByRecipes = Array.AsReadOnly(
                        GetValuesOrEmpty(
                            recipesByIngredient,
                            entry.ItemTemplateId)),
                };
        }

        var effects2 = effectProviders
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(
                pair => pair.Key,
                pair => pair.Value.ToKnowledge(),
                StringComparer.Ordinal);

        var unknownCombinations = catalogEntries
            .Where(entry =>
                !GalaxyItemFamilyResolver.IsKnownCombination(entry.Template))
            .GroupBy(entry => new
            {
                entry.Template.Category,
                entry.Template.Subcategory,
                entry.Template.ItemType,
            })
            .OrderBy(group => group.Key.Category)
            .ThenBy(group => group.Key.Subcategory)
            .ThenBy(group => group.Key.ItemType)
            .Select(group =>
                new GalaxyUnknownItemCombination
                {
                    Category = group.Key.Category,
                    Subcategory = group.Key.Subcategory,
                    ItemType = group.Key.ItemType,
                    Count = group.Count(),
                })
            .ToArray();

        var itemsWithForgeSources = itemBuilders.Values.Count(
            item => item.Sources.Count != 0);
        var itemsWithoutAnyKnownAcquisition = itemBuilders.Values.Count(
            item =>
                item.Sources.Count == 0 &&
                item.RefinedFrom.Count == 0 &&
                item.ProducedByRecipes.Count == 0);

        return new GalaxyKnowledgeSnapshot
        {
            Provenance = new GalaxyKnowledgeProvenance
            {
                BuiltAtUtc = DateTimeOffset.UtcNow,
                CdataPath = catalog.SourcePath,
                CdataFileLength = catalog.FileLength,
                CdataLastWriteTimeUtc = catalog.LastWriteTimeUtc,
                CdataSha256 = catalog.Sha256,
                CdataIndexEntryCount = catalog.IndexEntryCount,
                CdataParsedTemplateCount = catalogEntries.Length,
                ForgeDatasetEpoch = dataSet.DatasetEpoch,
                ForgeRevision = dataSet.Revision,
                ForgeContractVersion = dataSet.ContractVersion,
                ForgeSnapshotSha256 = dataSet.SnapshotSha256,
                RecipeReadModelAvailable = recipeCatalog.IsAvailable,
                RecipeCatalogRevision = recipeCatalog.Revision,
                RecipeCatalogGeneratedAtUtc = recipeCatalog.IsAvailable
                    ? recipeCatalog.GeneratedAtUtc
                    : null,
                RecipeCatalogSha256 = recipeCatalog.Sha256,
                MissionReadModelAvailable = missionCatalog.IsAvailable,
                MissionCatalogRevision = missionCatalog.Revision,
                MissionCatalogGeneratedAtUtc = missionCatalog.IsAvailable
                    ? missionCatalog.GeneratedAtUtc
                    : null,
                MissionCatalogSha256 = missionCatalog.Sha256,
            },
            Diagnostics = new GalaxyKnowledgeDiagnostics
            {
                FailedCdataTemplateIds =
                    Array.AsReadOnly(
                        catalog.FailedItemTemplateIds.ToArray()),
                UnknownItemCombinations =
                    Array.AsReadOnly(unknownCombinations),
                DuplicateCdataItemNames =
                    Array.AsReadOnly(duplicateCdataItemNames),
                OtherItemCount = itemBuilders.Values.Count(
                    item => item.Family == GalaxyItemFamily.Other),
                ItemsWithForgeSources = itemsWithForgeSources,
                ItemsWithoutForgeSources =
                    itemBuilders.Count - itemsWithForgeSources,
                ItemsWithoutAnyKnownAcquisition =
                    itemsWithoutAnyKnownAcquisition,
                UnresolvedForgeSourceRelationships =
                    unresolvedSourceRelationshipIds.Count,
                UnresolvedForgeSourceRelationshipIds =
                    Array.AsReadOnly(
                        unresolvedSourceRelationshipIds.ToArray()),
                ResolvedRefiningRelationships =
                    refiningRelationships.Count,
                UnresolvedRefiningRelationships =
                    unresolvedRefiningTargets.Count,
                UnresolvedRefiningTargets =
                    Array.AsReadOnly(
                        unresolvedRefiningTargets.ToArray()),
                StructuredEffectIdentityCount = effects2.Count,
                RecipeCount = recipes.Count,
                RecipesWithUnresolvedItems =
                    recipesWithUnresolvedItems,
                UnresolvedRecipeItemTemplateIds =
                    Array.AsReadOnly(
                        unresolvedRecipeItemTemplateIds.ToArray()),
                MissionCount = missions.Count,
                MissionRewardSourceCount = itemBuilders.Values.Sum(
                    item => item.Sources.Count(source =>
                        source.Kind ==
                        GalaxyItemSourceKind.MissionReward)),
                MissionsWithUnresolvedRewardItems =
                    missionsWithUnresolvedRewardItems,
                UnresolvedMissionRewardItemTemplateIds =
                    Array.AsReadOnly(
                        unresolvedMissionRewardItemTemplateIds.ToArray()),
                UnresolvedMissionCompletionLocations =
                    unresolvedMissionCompletionLocationNames.Count,
                UnresolvedMissionCompletionLocationNames =
                    Array.AsReadOnly(
                        unresolvedMissionCompletionLocationNames.ToArray()),
            },
            ItemsByTemplateId =
                new ReadOnlyDictionary<int, GalaxyItemKnowledge>(
                    itemBuilders),
            EffectsByIdentity =
                new ReadOnlyDictionary<string, GalaxyEffectKnowledge>(
                    effects2),
            RefiningRelationships =
                Array.AsReadOnly(refiningRelationships.ToArray()),
            Recipes = Array.AsReadOnly(recipes.ToArray()),
            Missions = Array.AsReadOnly(missions.ToArray()),
        };
    }

    private static List<GalaxyMissionKnowledge> BuildMissions(
        ForgeMissionCatalogSnapshot missionCatalog,
        GalaxyDataSet dataSet,
        IReadOnlyDictionary<int, ClientItemTemplateCatalogEntry> catalogById,
        out List<int> unresolvedRewardItemTemplateIds,
        out int missionsWithUnresolvedRewardItems,
        out List<string> unresolvedCompletionLocationNames)
    {
        unresolvedRewardItemTemplateIds = [];
        unresolvedCompletionLocationNames = [];
        missionsWithUnresolvedRewardItems = 0;

        if (!missionCatalog.IsAvailable)
        {
            return [];
        }

        HashSet<int> unresolvedItemIds = [];
        HashSet<string> unresolvedLocations =
            new(StringComparer.OrdinalIgnoreCase);
        var npcsById = dataSet.Document.Npcs.ToDictionary(
            npc => npc.Id,
            StringComparer.Ordinal);
        List<GalaxyMissionKnowledge> result = [];

        foreach (var mission in missionCatalog.Missions)
        {
            var itemRewards = mission.Reward?.Items
                .Select(reward =>
                    new GalaxyMissionItemRewardKnowledge
                    {
                        ItemTemplateId = reward.ItemTemplateId,
                        Quantity = reward.Quantity,
                    })
                .ToArray() ??
                Array.Empty<GalaxyMissionItemRewardKnowledge>();
            var missionHasUnresolvedReward = false;

            foreach (var reward in itemRewards)
            {
                if (catalogById.ContainsKey(reward.ItemTemplateId))
                {
                    continue;
                }

                missionHasUnresolvedReward = true;
                unresolvedItemIds.Add(reward.ItemTemplateId);
            }

            if (missionHasUnresolvedReward)
            {
                missionsWithUnresolvedRewardItems++;
            }

            var completionLocationCandidates = mission.CompletionLocations
                .Select(location =>
                    new ForgeMissionCompletionLocation
                    {
                        SectorName = location.SectorName,
                        StationName = location.StationName,
                    })
                .Concat(
                    mission.CompletionNpcIds
                        .Where(npcsById.ContainsKey)
                        .Select(completionNpcId =>
                        {
                            var npc = npcsById[completionNpcId];
                            return new ForgeMissionCompletionLocation
                            {
                                SectorName = npc.SectorName,
                                StationName = npc.StationName,
                            };
                        }))
                .ToArray();
            var locations = completionLocationCandidates
                .Select(location =>
                {
                    var sectorName = location.SectorName.Trim();
                    var stationName = location.StationName.Trim();

                    if (dataSet.Topology.TryResolve(
                            sectorName,
                            out var sector))
                    {
                        return new GalaxyMissionCompletionLocationKnowledge
                        {
                            SectorKey = sector.Key,
                            SectorName = sector.Name,
                            StationName = stationName,
                        };
                    }

                    if (sectorName.Length != 0)
                    {
                        unresolvedLocations.Add(
                            string.Concat(
                                mission.Id,
                                ":",
                                sectorName,
                                stationName.Length == 0
                                    ? ""
                                    : string.Concat(":", stationName)));
                    }

                    return new GalaxyMissionCompletionLocationKnowledge
                    {
                        SectorName = sectorName,
                        StationName = stationName,
                    };
                })
                .Where(location =>
                    location.SectorName.Length != 0 ||
                    location.StationName.Length != 0)
                .DistinctBy(location =>
                    string.Join(
                        "|",
                        location.SectorKey,
                        location.SectorName,
                        location.StationName),
                    StringComparer.OrdinalIgnoreCase)
                .OrderBy(
                    location => location.SectorName,
                    StringComparer.OrdinalIgnoreCase)
                .ThenBy(
                    location => location.StationName,
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();

            result.Add(
                new GalaxyMissionKnowledge
                {
                    Identity = mission.Id,
                    SemanticFingerprint =
                        mission.SemanticFingerprint,
                    Name = mission.Name.Trim(),
                    Summary = mission.Summary.Trim(),
                    RewardText = mission.RewardText.Trim(),
                    FailureConsequence =
                        mission.FailureConsequence.Trim(),
                    IssuingFaction = mission.IssuingFaction.Trim(),
                    StageCount = mission.StageCount,
                    IsTimed = mission.IsTimed,
                    IsForfeitable = mission.IsForfeitable,
                    Confidence = mission.Confidence,
                    HasAnonymousReports =
                        mission.HasAnonymousReports,
                    NamedReporters =
                        Array.AsReadOnly(
                            mission.NamedReporters.ToArray()),
                    Stages = Array.AsReadOnly(
                        mission.Stages
                            .Select(stage =>
                                new GalaxyMissionStageKnowledge
                                {
                                    Index = stage.Index,
                                    Text = stage.Text.Trim(),
                                    IsTimed = stage.IsTimed,
                                })
                            .ToArray()),
                    CompletionLocations =
                        Array.AsReadOnly(locations),
                    ItemRewards =
                        Array.AsReadOnly(itemRewards),
                });
        }

        unresolvedRewardItemTemplateIds =
        [
            .. unresolvedItemIds.Order(),
        ];
        unresolvedCompletionLocationNames =
        [
            .. unresolvedLocations.Order(
                StringComparer.OrdinalIgnoreCase),
        ];

        return
        [
            .. result
                .OrderBy(
                    mission => mission.Name,
                    StringComparer.OrdinalIgnoreCase)
                .ThenBy(
                    mission => mission.Identity,
                    StringComparer.Ordinal),
        ];
    }

    private static void AppendMissionSources(
        IDictionary<int, List<GalaxyItemSourceKnowledge>> sourcesByItem,
        IReadOnlyList<GalaxyMissionKnowledge> missions,
        IReadOnlySet<int> knownItemTemplateIds)
    {
        foreach (var mission in missions)
        {
            foreach (var reward in mission.ItemRewards)
            {
                if (!knownItemTemplateIds.Contains(
                        reward.ItemTemplateId))
                {
                    continue;
                }

                var locations = mission.CompletionLocations
                    .Select((location, index) =>
                        new GalaxyItemSourceLocationKnowledge
                        {
                            LocationId = string.Create(
                                CultureInfo.InvariantCulture,
                                $"{mission.Identity}:completion:{index}"),
                            SectorKey = location.SectorKey,
                            SectorName = location.SectorName,
                            LocationName = location.StationName,
                        })
                    .ToArray();
                var firstLocation = locations.FirstOrDefault();

                AddSource(
                    sourcesByItem,
                    reward.ItemTemplateId,
                    new GalaxyItemSourceKnowledge
                    {
                        Kind = GalaxyItemSourceKind.MissionReward,
                        RelationshipId = string.Create(
                            CultureInfo.InvariantCulture,
                            $"{mission.Identity}:reward:{reward.ItemTemplateId}"),
                        SourceEntityId = mission.Identity,
                        SourceName = mission.Name,
                        LocationName =
                            firstLocation?.LocationName ?? "",
                        SectorKey = firstLocation?.SectorKey ?? "",
                        SectorName = firstLocation?.SectorName ?? "",
                        Quantity = reward.Quantity,
                        Summary = mission.Summary.Length != 0
                            ? mission.Summary
                            : mission.RewardText,
                        Confidence = mission.Confidence,
                        HasAnonymousReports =
                            mission.HasAnonymousReports,
                        NamedReporters =
                            mission.NamedReporters,
                        Locations = Array.AsReadOnly(locations),
                    });
            }
        }

        foreach (var sources in sourcesByItem.Values)
        {
            sources.Sort((left, right) =>
            {
                var kind = left.Kind.CompareTo(right.Kind);
                return kind != 0
                    ? kind
                    : StringComparer.OrdinalIgnoreCase.Compare(
                        left.SourceName,
                        right.SourceName);
            });
        }
    }

    private static Dictionary<int, List<GalaxyItemSourceKnowledge>>
        BuildSources(
            GalaxyDataSet dataSet,
            IReadOnlyDictionary<int, ClientItemTemplateCatalogEntry> catalogById,
            out List<string> unresolvedRelationshipIds)
    {
        unresolvedRelationshipIds = [];
        var result = new Dictionary<int, List<GalaxyItemSourceKnowledge>>();
        var document = dataSet.Document;
        var sectorsById = document.Sectors.ToDictionary(
            sector => sector.Id,
            StringComparer.Ordinal);
        var npcsById = document.Npcs.ToDictionary(
            npc => npc.Id,
            StringComparer.Ordinal);
        var mobVariantsById = document.MobVariants.ToDictionary(
            variant => variant.Id,
            StringComparer.Ordinal);
        var mobClustersByVariant = document.MobClusters
            .GroupBy(cluster => cluster.MobVariantId)
            .ToDictionary(
                group => group.Key,
                group => group.ToArray(),
                StringComparer.Ordinal);
        var harvestableVariantsById =
            document.HarvestableVariants.ToDictionary(
                variant => variant.Id,
                StringComparer.Ordinal);
        var harvestableFieldsById =
            document.HarvestableFields.ToDictionary(
                field => field.Id,
                StringComparer.Ordinal);

        foreach (var relationship in document.VendorItems)
        {
            if (!catalogById.ContainsKey(relationship.ItemTemplateId) ||
                !npcsById.TryGetValue(
                    relationship.VendorNpcId,
                    out var vendor))
            {
                unresolvedRelationshipIds.Add(relationship.Id);
                continue;
            }

            var sector = document.Sectors.FirstOrDefault(candidate =>
                string.Equals(
                    candidate.Name,
                    vendor.SectorName,
                    StringComparison.OrdinalIgnoreCase) ||
                candidate.Aliases.Contains(
                    vendor.SectorName,
                    StringComparer.OrdinalIgnoreCase));

            AddSource(
                result,
                relationship.ItemTemplateId,
                new GalaxyItemSourceKnowledge
                {
                    Kind = GalaxyItemSourceKind.Vendor,
                    RelationshipId = relationship.Id,
                    SourceEntityId = vendor.Id,
                    SourceName = vendor.Name?.Trim() ?? "Unknown vendor",
                    LocationName = vendor.StationName.Trim(),
                    SectorId = sector?.Id ?? "",
                    SectorKey = sector?.Key ?? "",
                    SectorName = sector?.Name ?? vendor.SectorName.Trim(),
                    Confidence = relationship.Confidence,
                    HasAnonymousReports =
                        relationship.HasAnonymousReports,
                    NamedReporters =
                        Array.AsReadOnly(
                            relationship.NamedReporters.ToArray()),
                    Locations =
                    [
                        new GalaxyItemSourceLocationKnowledge
                        {
                            LocationId = vendor.Id,
                            SectorId = sector?.Id ?? "",
                            SectorKey = sector?.Key ?? "",
                            SectorName =
                                sector?.Name ?? vendor.SectorName.Trim(),
                        },
                    ],
                });
        }

        foreach (var relationship in document.MobLoot)
        {
            if (!catalogById.ContainsKey(relationship.ItemTemplateId) ||
                !mobVariantsById.TryGetValue(
                    relationship.MobVariantId,
                    out var variant))
            {
                unresolvedRelationshipIds.Add(relationship.Id);
                continue;
            }

            var locations = GetValuesOrEmpty(
                    mobClustersByVariant,
                    variant.Id)
                .Select(cluster =>
                {
                    sectorsById.TryGetValue(
                        cluster.SectorId,
                        out var sector);
                    return new GalaxyItemSourceLocationKnowledge
                    {
                        LocationId = cluster.Id,
                        SectorId = cluster.SectorId,
                        SectorKey = sector?.Key ?? "",
                        SectorName = sector?.Name ?? "",
                        X = cluster.CenterX,
                        Y = cluster.CenterY,
                        Z = cluster.CenterZ,
                        Radius = cluster.Radius,
                        ObservationCount = cluster.SightingCount,
                    };
                })
                .OrderBy(location => location.SectorName)
                .ThenBy(location => location.LocationId)
                .ToArray();

            AddSource(
                result,
                relationship.ItemTemplateId,
                new GalaxyItemSourceKnowledge
                {
                    Kind = GalaxyItemSourceKind.MobLoot,
                    RelationshipId = relationship.Id,
                    SourceEntityId = variant.Id,
                    SourceName = variant.Name.Trim(),
                    CombatLevel = variant.CombatLevel,
                    Confidence = relationship.Confidence,
                    HasAnonymousReports =
                        relationship.HasAnonymousReports,
                    NamedReporters =
                        Array.AsReadOnly(
                            relationship.NamedReporters.ToArray()),
                    Locations = Array.AsReadOnly(locations),
                });
        }

        foreach (var relationship in document.HarvestableResources)
        {
            if (!catalogById.ContainsKey(relationship.ItemTemplateId) ||
                !harvestableVariantsById.TryGetValue(
                    relationship.HarvestableVariantId,
                    out var variant) ||
                !harvestableFieldsById.TryGetValue(
                    relationship.HarvestableFieldId,
                    out var field))
            {
                unresolvedRelationshipIds.Add(relationship.Id);
                continue;
            }

            sectorsById.TryGetValue(field.SectorId, out var sector);

            AddSource(
                result,
                relationship.ItemTemplateId,
                new GalaxyItemSourceKnowledge
                {
                    Kind = GalaxyItemSourceKind.Harvesting,
                    RelationshipId = relationship.Id,
                    SourceEntityId = variant.Id,
                    SourceName = variant.Name.Trim(),
                    LocationName = "Resource field",
                    SectorId = field.SectorId,
                    SectorKey = sector?.Key ?? "",
                    SectorName = sector?.Name ?? "",
                    TechLevel = variant.TechLevel,
                    Confidence = relationship.Confidence,
                    HasAnonymousReports =
                        relationship.HasAnonymousReports,
                    NamedReporters =
                        Array.AsReadOnly(
                            relationship.NamedReporters.ToArray()),
                    Locations =
                    [
                        new GalaxyItemSourceLocationKnowledge
                        {
                            LocationId = field.Id,
                            SectorId = field.SectorId,
                            SectorKey = sector?.Key ?? "",
                            SectorName = sector?.Name ?? "",
                            X = field.CenterX,
                            Y = field.CenterY,
                            Z = field.CenterZ,
                            Radius = field.Radius,
                            ObservationCount = field.ObservationCount,
                        },
                    ],
                });
        }

        foreach (var pair in result)
        {
            pair.Value.Sort((left, right) =>
            {
                var kind = left.Kind.CompareTo(right.Kind);
                return kind != 0
                    ? kind
                    : StringComparer.OrdinalIgnoreCase.Compare(
                        left.SourceName,
                        right.SourceName);
            });
        }

        unresolvedRelationshipIds.Sort(StringComparer.Ordinal);
        return result;
    }

    private static List<GalaxyRecipeKnowledge> BuildRecipes(
        ForgeProductionRecipeCatalogSnapshot recipeCatalog,
        IReadOnlyDictionary<int, ClientItemTemplateCatalogEntry> catalogById,
        out List<int> unresolvedItemTemplateIds,
        out int recipesWithUnresolvedItems)
    {
        unresolvedItemTemplateIds = [];
        recipesWithUnresolvedItems = 0;

        if (!recipeCatalog.IsAvailable)
        {
            return [];
        }

        HashSet<int> unresolvedIds = [];
        List<GalaxyRecipeKnowledge> result = [];

        foreach (var recipe in recipeCatalog.Recipes)
        {
            var kind = recipe.Kind switch
            {
                1 => GalaxyRecipeKind.Manufacture,
                4 => GalaxyRecipeKind.Refine,
                _ => throw new InvalidOperationException(
                    $"Unsupported Forge recipe kind {recipe.Kind}."),
            };
            var hasUnresolvedItem =
                !catalogById.ContainsKey(recipe.OutputItemTemplateId);
            if (!catalogById.ContainsKey(
                    recipe.OutputItemTemplateId))
            {
                unresolvedIds.Add(
                    recipe.OutputItemTemplateId);
            }

            var ingredients = recipe.Ingredients
                .OrderBy(ingredient => ingredient.ItemTemplateId)
                .Select(ingredient =>
                {
                    if (!catalogById.ContainsKey(
                            ingredient.ItemTemplateId))
                    {
                        hasUnresolvedItem = true;
                        unresolvedIds.Add(
                            ingredient.ItemTemplateId);
                    }

                    return new GalaxyRecipeIngredientKnowledge
                    {
                        ItemTemplateId = ingredient.ItemTemplateId,
                        Quantity = ingredient.Quantity,
                    };
                })
                .ToArray();

            if (hasUnresolvedItem)
            {
                recipesWithUnresolvedItems++;
            }

            result.Add(
                new GalaxyRecipeKnowledge
                {
                    Identity = recipe.Id,
                    Kind = kind,
                    OutputItemTemplateId =
                        recipe.OutputItemTemplateId,
                    RecipeFingerprint =
                        recipe.RecipeFingerprint,
                    Confidence = recipe.Confidence,
                    HasAnonymousReports =
                        recipe.HasAnonymousReports,
                    NamedReporters = Array.AsReadOnly(
                        recipe.NamedReporters.ToArray()),
                    Ingredients = Array.AsReadOnly(ingredients),
                });
        }

        unresolvedItemTemplateIds =
        [
            .. unresolvedIds.OrderBy(itemTemplateId =>
                itemTemplateId),
        ];

        return
        [
            .. result
                .OrderBy(recipe => recipe.Kind)
                .ThenBy(recipe => recipe.OutputItemTemplateId)
                .ThenBy(recipe => recipe.Identity, StringComparer.Ordinal),
        ];
    }

    private static List<GalaxyRefiningRelationshipKnowledge>
        BuildRefiningRelationships(
            IReadOnlyList<ClientItemTemplateCatalogEntry> catalogEntries,
            IReadOnlyDictionary<string, ClientItemTemplateCatalogEntry>
                catalogByName,
            out List<string> unresolvedTargets)
    {
        unresolvedTargets = [];
        List<GalaxyRefiningRelationshipKnowledge> result = [];
        HashSet<string> identities = new(StringComparer.Ordinal);

        foreach (var entry in catalogEntries)
        {
            IReadOnlyList<string> evidenceTexts =
            [
                entry.Template.Description,
                .. entry.AdditionalText,
            ];

            foreach (var evidenceText in evidenceTexts.Where(text =>
                         !string.IsNullOrWhiteSpace(text)))
            {
                foreach (Match match in RefiningRelationshipRegex().Matches(
                             evidenceText))
                {
                    var targetName = match.Groups["target"]
                        .Value
                        .Trim()
                        .TrimEnd('.');
                    var label = match.Groups["kind"].Value;
                    var kind = label.StartsWith(
                            "Re-",
                            StringComparison.OrdinalIgnoreCase)
                        ? GalaxyRefiningRelationshipKind.ReRefinesTo
                        : GalaxyRefiningRelationshipKind.RefinesTo;

                    if (!catalogByName.TryGetValue(
                            targetName,
                            out var target))
                    {
                        unresolvedTargets.Add(
                            string.Create(
                                CultureInfo.InvariantCulture,
                                $"{entry.ItemTemplateId}:{label}:{targetName}"));
                        continue;
                    }

                    var identity = string.Create(
                        CultureInfo.InvariantCulture,
                        $"cdata:{entry.ItemTemplateId}:{target.ItemTemplateId}:{(int)kind}");

                    if (!identities.Add(identity))
                    {
                        continue;
                    }

                    result.Add(
                        new GalaxyRefiningRelationshipKnowledge
                        {
                            Identity = identity,
                            InputItemTemplateId = entry.ItemTemplateId,
                            OutputItemTemplateId =
                                target.ItemTemplateId,
                            Kind = kind,
                            EvidenceText = match.Value.Trim(),
                        });
                }
            }
        }

        result.Sort((left, right) =>
        {
            var input = left.InputItemTemplateId.CompareTo(
                right.InputItemTemplateId);
            return input != 0
                ? input
                : left.OutputItemTemplateId.CompareTo(
                    right.OutputItemTemplateId);
        });
        unresolvedTargets.Sort(StringComparer.Ordinal);
        return result;
    }

    private static GalaxyItemEffectKnowledge[] BuildEffects(
        ClientRuntimeItemTemplateObservation template,
        IDictionary<string, MutableEffectKnowledge> effectProviders,
        int itemTemplateId)
    {
        List<GalaxyItemEffectKnowledge> result = [];

        AppendEffects(
            result,
            effectProviders,
            itemTemplateId,
            GalaxyItemEffectTrigger.Activated,
            template.ActivatedEffects);
        AppendEffects(
            result,
            effectProviders,
            itemTemplateId,
            GalaxyItemEffectTrigger.Equipped,
            template.EquippedEffects);

        return result.ToArray();
    }

    private static void AppendEffects(
        ICollection<GalaxyItemEffectKnowledge> target,
        IDictionary<string, MutableEffectKnowledge> effectProviders,
        int itemTemplateId,
        GalaxyItemEffectTrigger trigger,
        IReadOnlyList<ClientRuntimeItemEffectObservation> effects)
    {
        foreach (var effect in effects)
        {
            var identity = ResolveEffectIdentity(effect, trigger);

            var knowledge = new GalaxyItemEffectKnowledge
            {
                Identity = identity,
                Trigger = trigger,
                Index = effect.Index,
                NameFormat = effect.NameFormat,
                DescriptionFormat = effect.DescriptionFormat,
                NameValues = Array.AsReadOnly(
                    effect.NameValues.ToArray()),
                DescriptionValues = Array.AsReadOnly(
                    effect.DescriptionValues.ToArray()),
                Field50 = effect.Field50,
                Field54 = effect.Field54,
            };
            target.Add(knowledge);

            if (!effectProviders.TryGetValue(
                    identity,
                    out var aggregate))
            {
                aggregate = new MutableEffectKnowledge(identity);
                effectProviders.Add(identity, aggregate);
            }

            aggregate.AddFormat(
                effect.NameFormat,
                effect.DescriptionFormat);
            aggregate.Providers.Add(
                new GalaxyEffectProviderKnowledge
                {
                    ItemTemplateId = itemTemplateId,
                    Trigger = trigger,
                    EffectIndex = effect.Index,
                });
        }
    }

    private static string ResolveEffectIdentity(
        ClientRuntimeItemEffectObservation effect,
        GalaxyItemEffectTrigger trigger)
    {
        if (!string.IsNullOrWhiteSpace(effect.SourceOrIdentity))
        {
            return effect.SourceOrIdentity.Trim();
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"anonymous:{trigger}:{effect.NameFormat.Trim()}:{effect.DescriptionFormat.Trim()}");
    }

    private static int GetCombinedInt32Attribute(
        ClientRuntimeItemTemplateObservation template,
        uint itemInfoId)
    {
        var result = 0;

        foreach (var attribute in template.Attributes.Where(attribute =>
                     attribute.ItemInfoId == itemInfoId))
        {
            result |= attribute.Int32Value.GetValueOrDefault();
        }

        return result;
    }

    private static int GetMaximumInt32Attribute(
        ClientRuntimeItemTemplateObservation template,
        uint itemInfoId)
    {
        return template.Attributes
            .Where(attribute => attribute.ItemInfoId == itemInfoId)
            .Select(attribute => attribute.Int32Value.GetValueOrDefault())
            .DefaultIfEmpty()
            .Max();
    }

    private static TValue[] GetValuesOrEmpty<TKey, TValue>(
        IReadOnlyDictionary<TKey, TValue[]> valuesByKey,
        TKey key)
        where TKey : notnull
    {
        return valuesByKey.TryGetValue(key, out var values)
            ? values
            : Array.Empty<TValue>();
    }

    private static TValue[] GetValuesOrEmpty<TKey, TValue>(
        IReadOnlyDictionary<TKey, List<TValue>> valuesByKey,
        TKey key)
        where TKey : notnull
    {
        return valuesByKey.TryGetValue(key, out var values)
            ? values.ToArray()
            : Array.Empty<TValue>();
    }

    private static void AddSource(
        IDictionary<int, List<GalaxyItemSourceKnowledge>> sourcesByItem,
        int itemTemplateId,
        GalaxyItemSourceKnowledge source)
    {
        if (!sourcesByItem.TryGetValue(
                itemTemplateId,
                out var sources))
        {
            sources = [];
            sourcesByItem.Add(itemTemplateId, sources);
        }

        sources.Add(source);
    }

    [GeneratedRegex(
        @"(?im)^\s*(?<kind>Refines\s+to|Re-refines\s+to|Refines\s+into|Refined\s+to)\s*:\s*(?<target>[^\r\n]+?)\s*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex RefiningRelationshipRegex();

    private sealed class MutableEffectKnowledge(
        string identity)
    {
        private readonly HashSet<EffectFormatKey> formats = [];

        public string Identity { get; } = identity;

        public List<GalaxyEffectProviderKnowledge> Providers { get; } = [];

        public void AddFormat(
            string nameFormat,
            string descriptionFormat)
        {
            this.formats.Add(
                new EffectFormatKey(
                    nameFormat,
                    descriptionFormat));
        }

        public GalaxyEffectKnowledge ToKnowledge()
        {
            return new GalaxyEffectKnowledge
            {
                Identity = this.Identity,
                Formats = Array.AsReadOnly(
                    this.formats
                        .OrderBy(
                            format => format.NameFormat,
                            StringComparer.Ordinal)
                        .ThenBy(
                            format => format.DescriptionFormat,
                            StringComparer.Ordinal)
                        .Select(format =>
                            new GalaxyEffectFormatKnowledge
                            {
                                NameFormat = format.NameFormat,
                                DescriptionFormat =
                                    format.DescriptionFormat,
                            })
                        .ToArray()),
                Providers = Array.AsReadOnly(
                    this.Providers
                        .OrderBy(provider => provider.ItemTemplateId)
                        .ThenBy(provider => provider.Trigger)
                        .ThenBy(provider => provider.EffectIndex)
                        .ToArray()),
            };
        }

        private readonly record struct EffectFormatKey(
            string NameFormat,
            string DescriptionFormat);
    }
}
