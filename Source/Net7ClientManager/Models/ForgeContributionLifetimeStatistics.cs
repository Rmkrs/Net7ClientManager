namespace Net7ClientManager.Models;

public sealed class ForgeContributionLifetimeStatistics
{
    public const int CurrentObservationCounterVersion = 11;

    public int ObservationCounterVersion { get; set; }

    public List<string> ObservedStationKeys { get; set; } = [];

    public List<string> ObservedNpcKeys { get; set; } = [];

    public List<string> ObservedStationFacilityKeys { get; set; } = [];

    public List<string> ObservedNavigationObjectKeys { get; set; } = [];

    public List<string> ObservedVendorKeys { get; set; } = [];

    public List<string> ObservedVendorItemKeys { get; set; } = [];

    public List<string> ObservedMobSightingKeys { get; set; } = [];

    public List<string> ObservedMobLootRelationshipKeys { get; set; } = [];

    public List<string> ObservedHarvestableResourceKeys { get; set; } = [];

    public List<string> ObservedProductionRecipeKeys { get; set; } = [];

    public List<string> ObservedMissionKeys { get; set; } = [];

    public List<string> ObservedJobOfferKeys { get; set; } = [];

    public long StationsObserved { get; set; }

    public long NpcsObserved { get; set; }

    public long NpcFactsSubmitted { get; set; }

    public long AlreadyCanonical { get; set; }

    public long EvidenceAccepted { get; set; }

    public long Conflicts { get; set; }

    public long StationFacilitiesObserved { get; set; }

    public long StationFacilityFactsSubmitted { get; set; }

    public long StationFacilitiesAlreadyCanonical { get; set; }

    public long StationFacilityEvidenceAccepted { get; set; }

    public long StationFacilityConflicts { get; set; }

    public long NavigationObjectsObserved { get; set; }

    public long NavigationObjectFactsSubmitted { get; set; }

    public long NavigationObjectsAlreadyCanonical { get; set; }

    public long NavigationObjectEvidenceAccepted { get; set; }

    public long NavigationObjectConflicts { get; set; }

    public long VendorsObserved { get; set; }

    public long VendorItemsObserved { get; set; }

    public long VendorItemFactsSubmitted { get; set; }

    public long VendorItemsAlreadyCanonical { get; set; }

    public long VendorItemEvidenceAccepted { get; set; }

    public long VendorItemConflicts { get; set; }

    public long VendorItemsRemoved { get; set; }

    public long MobSightingsObserved { get; set; }

    public long MobSightingFactsSubmitted { get; set; }

    public long MobSightingEvidenceAccepted { get; set; }

    public long MobVariantsCreated { get; set; }

    public long MobClustersCreated { get; set; }

    public long MobClustersUpdated { get; set; }

    public long LootCorpsesObserved { get; set; }

    public long MobLootRelationshipsObserved { get; set; }

    public long MobLootFactsSubmitted { get; set; }

    public long MobLootAlreadyCanonical { get; set; }

    public long MobLootEvidenceAccepted { get; set; }

    public long MobLootVariantsCreated { get; set; }

    public long MobLootRelationshipsCreated { get; set; }

    public long MobLootRelationshipsStrengthened { get; set; }

    public long HarvestableResourcesObserved { get; set; }

    public long HarvestableFactsSubmitted { get; set; }

    public long HarvestableAlreadyCanonical { get; set; }

    public long HarvestableEvidenceAccepted { get; set; }

    public long HarvestableVariantsCreated { get; set; }

    public long HarvestableFieldsCreated { get; set; }

    public long HarvestableFieldsUpdated { get; set; }

    public long HarvestableRelationshipsCreated { get; set; }

    public long HarvestableRelationshipsStrengthened { get; set; }

    public long ProductionRecipesObserved { get; set; }

    public long ProductionRecipeFactsSubmitted { get; set; }

    public long ProductionRecipesAlreadyCanonical { get; set; }

    public long ProductionRecipeEvidenceAccepted { get; set; }

    public long ProductionRecipeConflicts { get; set; }

    public long ProductionRecipesCreated { get; set; }

    public long MissionsObserved { get; set; }

    public long MissionFactsSubmitted { get; set; }

    public long MissionsAlreadyCanonical { get; set; }

    public long MissionEvidenceAccepted { get; set; }

    public long MissionConflicts { get; set; }

    public long MissionsCreated { get; set; }

    public long MissionsStrengthened { get; set; }

    public long JobOffersObserved { get; set; }

    public long JobOfferFactsSubmitted { get; set; }

    public long JobOffersAlreadyCanonical { get; set; }

    public long JobOfferEvidenceAccepted { get; set; }

    public long JobOfferConflicts { get; set; }

    public long JobOffersCreated { get; set; }

    public long JobOffersStrengthened { get; set; }

    public long SuccessfulBatches { get; set; }

    public long FailedBatches { get; set; }

    public long PublishedRevisions { get; set; }

    public DateTimeOffset? LastSuccessfulContributionUtc { get; set; }

    public DateTimeOffset? LastFailedContributionUtc { get; set; }

    public void EnsureDefaults()
    {
        this.ObservedStationKeys ??= [];
        this.ObservedNpcKeys ??= [];
        this.ObservedStationFacilityKeys ??= [];
        this.ObservedNavigationObjectKeys ??= [];
        this.ObservedVendorKeys ??= [];
        this.ObservedVendorItemKeys ??= [];
        this.ObservedMobSightingKeys ??= [];
        this.ObservedMobLootRelationshipKeys ??= [];
        this.ObservedHarvestableResourceKeys ??= [];
        this.ObservedProductionRecipeKeys ??= [];
        this.ObservedMissionKeys ??= [];
        this.ObservedJobOfferKeys ??= [];

        if (this.ObservationCounterVersion < 2)
        {
            // Earlier builds counted every submission attempt as another
            // station, another full roster, and another locally covered set.
            // Those aggregates cannot be converted into unique observations,
            // so reset them while preserving accepted/submitted evidence,
            // conflicts, batch outcomes, and published revisions.
            this.ObservedStationKeys.Clear();
            this.ObservedNpcKeys.Clear();
            this.AlreadyCanonical = 0;
        }

        if (this.ObservationCounterVersion < 3)
        {
            this.ObservedStationFacilityKeys.Clear();
        }

        if (this.ObservationCounterVersion < 4)
        {
            this.ObservedVendorKeys.Clear();
            this.ObservedVendorItemKeys.Clear();
        }

        if (this.ObservationCounterVersion < 5)
        {
            this.ObservedNavigationObjectKeys.Clear();
        }

        if (this.ObservationCounterVersion < 6)
        {
            this.ObservedMobSightingKeys.Clear();
        }

        if (this.ObservationCounterVersion < 7)
        {
            this.ObservedMobLootRelationshipKeys.Clear();
        }

        if (this.ObservationCounterVersion < 8)
        {
            this.ObservedHarvestableResourceKeys.Clear();
        }

        if (this.ObservationCounterVersion < 9)
        {
            this.ObservedProductionRecipeKeys.Clear();
        }

        if (this.ObservationCounterVersion < 10)
        {
            this.ObservedMissionKeys.Clear();
        }

        if (this.ObservationCounterVersion < 11)
        {
            this.ObservedJobOfferKeys.Clear();
        }

        this.ObservationCounterVersion = CurrentObservationCounterVersion;

        this.ObservedStationKeys =
        [
            .. this.ObservedStationKeys
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.Ordinal),
        ];
        this.ObservedNpcKeys =
        [
            .. this.ObservedNpcKeys
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.Ordinal),
        ];
        this.ObservedStationFacilityKeys =
        [
            .. this.ObservedStationFacilityKeys
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.Ordinal),
        ];
        this.ObservedNavigationObjectKeys =
        [
            .. this.ObservedNavigationObjectKeys
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.Ordinal),
        ];
        this.ObservedVendorKeys =
        [
            .. this.ObservedVendorKeys
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.Ordinal),
        ];
        this.ObservedVendorItemKeys =
        [
            .. this.ObservedVendorItemKeys
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.Ordinal),
        ];
        this.ObservedMobSightingKeys =
        [
            .. this.ObservedMobSightingKeys
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.Ordinal),
        ];
        this.ObservedMobLootRelationshipKeys =
        [
            .. this.ObservedMobLootRelationshipKeys
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.Ordinal),
        ];
        this.ObservedHarvestableResourceKeys =
        [
            .. this.ObservedHarvestableResourceKeys
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.Ordinal),
        ];
        this.ObservedProductionRecipeKeys =
        [
            .. this.ObservedProductionRecipeKeys
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.Ordinal),
        ];
        this.ObservedMissionKeys =
        [
            .. this.ObservedMissionKeys
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.Ordinal),
        ];
        this.ObservedJobOfferKeys =
        [
            .. this.ObservedJobOfferKeys
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.Ordinal),
        ];
        this.StationsObserved = this.ObservedStationKeys.Count;
        this.NpcsObserved = this.ObservedNpcKeys.Count;
        this.StationFacilitiesObserved =
            this.ObservedStationFacilityKeys.Count;
        this.NavigationObjectsObserved =
            this.ObservedNavigationObjectKeys.Count;
        this.VendorsObserved = this.ObservedVendorKeys.Count;
        this.VendorItemsObserved = this.ObservedVendorItemKeys.Count;
        this.MobSightingsObserved = this.ObservedMobSightingKeys.Count;
        this.MobLootRelationshipsObserved =
            this.ObservedMobLootRelationshipKeys.Count;
        this.HarvestableResourcesObserved =
            this.ObservedHarvestableResourceKeys.Count;
        this.ProductionRecipesObserved =
            this.ObservedProductionRecipeKeys.Count;
        this.MissionsObserved = this.ObservedMissionKeys.Count;
        this.JobOffersObserved = this.ObservedJobOfferKeys.Count;
    }
}
