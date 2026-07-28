namespace Net7ClientManager.Contributions;

public sealed record ForgeContributionStatisticsSnapshot
{
    public bool Enabled { get; init; }

    public bool IdentityRegistered { get; init; }

    public string Status { get; init; } = "Contribution is disabled.";

    public long StationsObserved { get; init; }

    public long NpcsObserved { get; init; }

    public long NpcFactsSubmitted { get; init; }

    public long AlreadyCanonical { get; init; }

    public long EvidenceAccepted { get; init; }

    public long Conflicts { get; init; }

    public long StationFacilitiesObserved { get; init; }

    public long StationFacilityFactsSubmitted { get; init; }

    public long StationFacilitiesAlreadyCanonical { get; init; }

    public long StationFacilityEvidenceAccepted { get; init; }

    public long StationFacilityConflicts { get; init; }

    public long NavigationObjectsObserved { get; init; }

    public long NavigationObjectFactsSubmitted { get; init; }

    public long NavigationObjectsAlreadyCanonical { get; init; }

    public long NavigationObjectEvidenceAccepted { get; init; }

    public long NavigationObjectConflicts { get; init; }

    public long VendorsObserved { get; init; }

    public long VendorItemsObserved { get; init; }

    public long VendorItemFactsSubmitted { get; init; }

    public long VendorItemsAlreadyCanonical { get; init; }

    public long VendorItemEvidenceAccepted { get; init; }

    public long VendorItemConflicts { get; init; }

    public long VendorItemsRemoved { get; init; }

    public long MobSightingsObserved { get; init; }

    public long MobSightingFactsSubmitted { get; init; }

    public long MobSightingEvidenceAccepted { get; init; }

    public long MobVariantsCreated { get; init; }

    public long MobClustersCreated { get; init; }

    public long MobClustersUpdated { get; init; }

    public long LootCorpsesObserved { get; init; }

    public long MobLootRelationshipsObserved { get; init; }

    public long MobLootFactsSubmitted { get; init; }

    public long MobLootAlreadyCanonical { get; init; }

    public long MobLootEvidenceAccepted { get; init; }

    public long MobLootVariantsCreated { get; init; }

    public long MobLootRelationshipsCreated { get; init; }

    public long MobLootRelationshipsStrengthened { get; init; }

    public long HarvestableResourcesObserved { get; init; }

    public long HarvestableFactsSubmitted { get; init; }

    public long HarvestableAlreadyCanonical { get; init; }

    public long HarvestableEvidenceAccepted { get; init; }

    public long HarvestableVariantsCreated { get; init; }

    public long HarvestableFieldsCreated { get; init; }

    public long HarvestableFieldsUpdated { get; init; }

    public long HarvestableRelationshipsCreated { get; init; }

    public long HarvestableRelationshipsStrengthened { get; init; }

    public long ProductionRecipesObserved { get; init; }

    public long ProductionRecipeFactsSubmitted { get; init; }

    public long ProductionRecipesAlreadyCanonical { get; init; }

    public long ProductionRecipeEvidenceAccepted { get; init; }

    public long ProductionRecipeConflicts { get; init; }

    public long ProductionRecipesCreated { get; init; }

    public long MissionsObserved { get; init; }

    public long MissionFactsSubmitted { get; init; }

    public long MissionsAlreadyCanonical { get; init; }

    public long MissionEvidenceAccepted { get; init; }

    public long MissionConflicts { get; init; }

    public long MissionsCreated { get; init; }

    public long MissionsStrengthened { get; init; }

    public long JobOffersObserved { get; init; }

    public long JobOfferFactsSubmitted { get; init; }

    public long JobOffersAlreadyCanonical { get; init; }

    public long JobOfferEvidenceAccepted { get; init; }

    public long JobOfferConflicts { get; init; }

    public long JobOffersCreated { get; init; }

    public long JobOffersStrengthened { get; init; }

    public long SuccessfulBatches { get; init; }

    public long FailedBatches { get; init; }

    public long PublishedRevisions { get; init; }

    public DateTimeOffset? LastSuccessfulContributionUtc { get; init; }

    public DateTimeOffset? LastFailedContributionUtc { get; init; }
}

public sealed class ForgeContributionStatisticsChangedEventArgs : EventArgs
{
    public ForgeContributionStatisticsChangedEventArgs(
        ForgeContributionStatisticsSnapshot session,
        ForgeContributionStatisticsSnapshot lifetime)
    {
        this.Session = session;
        this.Lifetime = lifetime;
    }

    public ForgeContributionStatisticsSnapshot Session { get; }

    public ForgeContributionStatisticsSnapshot Lifetime { get; }
}

public sealed class ForgeContributionRevisionPublishedEventArgs : EventArgs
{
    public ForgeContributionRevisionPublishedEventArgs(long revision)
    {
        this.Revision = revision;
    }

    public long Revision { get; }
}
