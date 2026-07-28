namespace Net7ClientManager.GalaxyKnowledge;

using System.Collections.ObjectModel;

public sealed record GalaxyKnowledgeSnapshot
{
    public static GalaxyKnowledgeSnapshot Empty { get; } =
        new()
        {
            Provenance = new GalaxyKnowledgeProvenance(),
            Diagnostics = new GalaxyKnowledgeDiagnostics(),
            ItemsByTemplateId =
                new ReadOnlyDictionary<int, GalaxyItemKnowledge>(
                    new Dictionary<int, GalaxyItemKnowledge>()),
            EffectsByIdentity =
                new ReadOnlyDictionary<string, GalaxyEffectKnowledge>(
                    new Dictionary<string, GalaxyEffectKnowledge>(
                        StringComparer.Ordinal)),
        };

    public required GalaxyKnowledgeProvenance Provenance { get; init; }

    public required GalaxyKnowledgeDiagnostics Diagnostics { get; init; }

    public required IReadOnlyDictionary<int, GalaxyItemKnowledge>
        ItemsByTemplateId { get; init; }

    public required IReadOnlyDictionary<string, GalaxyEffectKnowledge>
        EffectsByIdentity { get; init; }

    public IReadOnlyList<GalaxyRefiningRelationshipKnowledge>
        RefiningRelationships { get; init; } = [];

    public IReadOnlyList<GalaxyRecipeKnowledge> Recipes { get; init; } = [];

    public IReadOnlyList<GalaxyMissionKnowledge> Missions { get; init; } = [];

    public bool TryGetItem(
        int itemTemplateId,
        out GalaxyItemKnowledge item)
    {
        return this.ItemsByTemplateId.TryGetValue(
            itemTemplateId,
            out item!);
    }
}

public sealed record GalaxyKnowledgeProvenance
{
    public DateTimeOffset BuiltAtUtc { get; init; }

    public string? CdataPath { get; init; }

    public long CdataFileLength { get; init; }

    public DateTimeOffset? CdataLastWriteTimeUtc { get; init; }

    public string CdataSha256 { get; init; } = "";

    public int CdataIndexEntryCount { get; init; }

    public int CdataParsedTemplateCount { get; init; }

    public string ForgeDatasetEpoch { get; init; } = "";

    public long ForgeRevision { get; init; }

    public int ForgeContractVersion { get; init; }

    public string ForgeSnapshotSha256 { get; init; } = "";

    public bool RecipeReadModelAvailable { get; init; }

    public long RecipeCatalogRevision { get; init; }

    public DateTimeOffset? RecipeCatalogGeneratedAtUtc { get; init; }

    public string RecipeCatalogSha256 { get; init; } = "";

    public bool MissionReadModelAvailable { get; init; }

    public long MissionCatalogRevision { get; init; }

    public DateTimeOffset? MissionCatalogGeneratedAtUtc { get; init; }

    public string MissionCatalogSha256 { get; init; } = "";
}

public sealed record GalaxyKnowledgeDiagnostics
{
    public IReadOnlyList<int> FailedCdataTemplateIds { get; init; } = [];

    public IReadOnlyList<GalaxyUnknownItemCombination>
        UnknownItemCombinations { get; init; } = [];

    public IReadOnlyList<string> DuplicateCdataItemNames { get; init; } = [];

    public int OtherItemCount { get; init; }

    public int ItemsWithForgeSources { get; init; }

    public int ItemsWithoutForgeSources { get; init; }

    public int ItemsWithoutAnyKnownAcquisition { get; init; }

    public int UnresolvedForgeSourceRelationships { get; init; }

    public IReadOnlyList<string> UnresolvedForgeSourceRelationshipIds
    { get; init; } = [];

    public int ResolvedRefiningRelationships { get; init; }

    public int UnresolvedRefiningRelationships { get; init; }

    public IReadOnlyList<string> UnresolvedRefiningTargets { get; init; } = [];

    public int StructuredEffectIdentityCount { get; init; }

    public int RecipeCount { get; init; }

    public int RecipesWithUnresolvedItems { get; init; }

    public IReadOnlyList<int> UnresolvedRecipeItemTemplateIds
    { get; init; } = [];

    public int MissionCount { get; init; }

    public int MissionRewardSourceCount { get; init; }

    public int MissionsWithUnresolvedRewardItems { get; init; }

    public IReadOnlyList<int> UnresolvedMissionRewardItemTemplateIds
    { get; init; } = [];

    public int UnresolvedMissionCompletionLocations { get; init; }

    public IReadOnlyList<string> UnresolvedMissionCompletionLocationNames
    { get; init; } = [];
}

public sealed record GalaxyUnknownItemCombination
{
    public int Category { get; init; }

    public int Subcategory { get; init; }

    public int ItemType { get; init; }

    public int Count { get; init; }
}

public sealed record GalaxyItemKnowledge
{
    public int ItemTemplateId { get; init; }

    public string Name { get; init; } = "";

    public GalaxyItemFamily Family { get; init; }

    public int Category { get; init; }

    public int Subcategory { get; init; }

    public int ItemType { get; init; }

    public string TypeDisplayName { get; init; } = "";

    public int TechLevel { get; init; }

    public uint GameBassetId { get; init; }

    public int? ModelBassetId { get; init; }

    public int? IconBassetId { get; init; }

    public uint MaximumStack { get; init; }

    public uint Flags { get; init; }

    public string Manufacturer { get; init; } = "";

    public string Description { get; init; } = "";

    public IReadOnlyList<string> AdditionalText { get; init; } = [];

    public int ProfessionRestrictionMask { get; init; }

    public int RaceRestrictionMask { get; init; }

    public int LoreRestriction { get; init; }

    public int RequiredCombatLevel { get; init; }

    public int RequiredExploreLevel { get; init; }

    public int RequiredTradeLevel { get; init; }

    public int RequiredOverallLevel { get; init; }

    public IReadOnlyList<GalaxyItemAttributeKnowledge> Attributes
    { get; init; } = [];

    public IReadOnlyList<GalaxyItemEffectKnowledge> Effects
    { get; init; } = [];

    public IReadOnlyList<GalaxyItemSourceKnowledge> Sources
    { get; init; } = [];

    public IReadOnlyList<GalaxyRefiningRelationshipKnowledge> RefinesTo
    { get; init; } = [];

    public IReadOnlyList<GalaxyRefiningRelationshipKnowledge> RefinedFrom
    { get; init; } = [];

    public IReadOnlyList<GalaxyRecipeKnowledge> ProducedByRecipes
    { get; init; } = [];

    public IReadOnlyList<GalaxyRecipeKnowledge> UsedByRecipes
    { get; init; } = [];
}

public sealed record GalaxyItemAttributeKnowledge
{
    public uint ItemInfoId { get; init; }

    public uint TypeCode { get; init; }

    public uint RawValue { get; init; }

    public int? Int32Value { get; init; }

    public float? FloatValue { get; init; }

    public string StringValue { get; init; } = "";
}

public enum GalaxyItemEffectTrigger
{
    Activated = 0,
    Equipped = 1,
}

public sealed record GalaxyItemEffectKnowledge
{
    public string Identity { get; init; } = "";

    public GalaxyItemEffectTrigger Trigger { get; init; }

    public int Index { get; init; }

    public string NameFormat { get; init; } = "";

    public string DescriptionFormat { get; init; } = "";

    public IReadOnlyList<float> NameValues { get; init; } = [];

    public IReadOnlyList<float> DescriptionValues { get; init; } = [];

    public uint Field50 { get; init; }

    public uint Field54 { get; init; }
}

public sealed record GalaxyEffectKnowledge
{
    public string Identity { get; init; } = "";

    public IReadOnlyList<GalaxyEffectFormatKnowledge> Formats
    { get; init; } = [];

    public IReadOnlyList<GalaxyEffectProviderKnowledge> Providers
    { get; init; } = [];
}

public sealed record GalaxyEffectFormatKnowledge
{
    public string NameFormat { get; init; } = "";

    public string DescriptionFormat { get; init; } = "";
}

public sealed record GalaxyEffectProviderKnowledge
{
    public int ItemTemplateId { get; init; }

    public GalaxyItemEffectTrigger Trigger { get; init; }

    public int EffectIndex { get; init; }
}

public enum GalaxyItemSourceKind
{
    Vendor = 0,
    MobLoot = 1,
    Harvesting = 2,
    MissionReward = 3,
}

public sealed record GalaxyItemSourceKnowledge
{
    public GalaxyItemSourceKind Kind { get; init; }

    public string RelationshipId { get; init; } = "";

    public string SourceEntityId { get; init; } = "";

    public string SourceName { get; init; } = "";

    public string LocationName { get; init; } = "";

    public string SectorId { get; init; } = "";

    public string SectorKey { get; init; } = "";

    public string SectorName { get; init; } = "";

    public int? CombatLevel { get; init; }

    public int? TechLevel { get; init; }

    public int? Quantity { get; init; }

    public string Summary { get; init; } = "";

    public string Confidence { get; init; } = "";

    public bool HasAnonymousReports { get; init; }

    public IReadOnlyList<string> NamedReporters { get; init; } = [];

    public IReadOnlyList<GalaxyItemSourceLocationKnowledge> Locations
    { get; init; } = [];
}

public sealed record GalaxyItemSourceLocationKnowledge
{
    public string LocationId { get; init; } = "";

    public string SectorId { get; init; } = "";

    public string SectorKey { get; init; } = "";

    public string SectorName { get; init; } = "";

    public string LocationName { get; init; } = "";

    public float? X { get; init; }

    public float? Y { get; init; }

    public float? Z { get; init; }

    public float? Radius { get; init; }

    public long ObservationCount { get; init; }
}

public sealed record GalaxyMissionKnowledge
{
    public string Identity { get; init; } = "";

    public string SemanticFingerprint { get; init; } = "";

    public string Name { get; init; } = "";

    public string Summary { get; init; } = "";

    public string RewardText { get; init; } = "";

    public string FailureConsequence { get; init; } = "";

    public string IssuingFaction { get; init; } = "";

    public int StageCount { get; init; }

    public bool? IsTimed { get; init; }

    public bool? IsForfeitable { get; init; }

    public string Confidence { get; init; } = "";

    public bool HasAnonymousReports { get; init; }

    public IReadOnlyList<string> NamedReporters { get; init; } = [];

    public IReadOnlyList<GalaxyMissionStageKnowledge> Stages
    { get; init; } = [];

    public IReadOnlyList<GalaxyMissionCompletionLocationKnowledge>
        CompletionLocations { get; init; } = [];

    public IReadOnlyList<GalaxyMissionItemRewardKnowledge> ItemRewards
    { get; init; } = [];
}

public sealed record GalaxyMissionStageKnowledge
{
    public int Index { get; init; }

    public string Text { get; init; } = "";

    public bool? IsTimed { get; init; }
}

public sealed record GalaxyMissionCompletionLocationKnowledge
{
    public string SectorKey { get; init; } = "";

    public string SectorName { get; init; } = "";

    public string StationName { get; init; } = "";
}

public sealed record GalaxyMissionItemRewardKnowledge
{
    public int ItemTemplateId { get; init; }

    public int Quantity { get; init; }
}

public enum GalaxyRefiningRelationshipKind
{
    RefinesTo = 0,
    ReRefinesTo = 1,
}

public sealed record GalaxyRefiningRelationshipKnowledge
{
    public string Identity { get; init; } = "";

    public int InputItemTemplateId { get; init; }

    public int OutputItemTemplateId { get; init; }

    public GalaxyRefiningRelationshipKind Kind { get; init; }

    public string EvidenceText { get; init; } = "";
}

public enum GalaxyRecipeKind
{
    Manufacture = 1,
    Refine = 4,
}

public sealed record GalaxyRecipeKnowledge
{
    public string Identity { get; init; } = "";

    public GalaxyRecipeKind Kind { get; init; }

    public int OutputItemTemplateId { get; init; }

    public string RecipeFingerprint { get; init; } = "";

    public string Confidence { get; init; } = "";

    public bool HasAnonymousReports { get; init; }

    public IReadOnlyList<string> NamedReporters { get; init; } = [];

    public IReadOnlyList<GalaxyRecipeIngredientKnowledge> Ingredients
    { get; init; } = [];
}

public sealed record GalaxyRecipeIngredientKnowledge
{
    public int ItemTemplateId { get; init; }

    public int Quantity { get; init; }
}

public sealed class GalaxyKnowledgeSnapshotChangedEventArgs(
    GalaxyKnowledgeSnapshot snapshot) : EventArgs
{
    public GalaxyKnowledgeSnapshot Snapshot { get; } = snapshot;
}
