namespace Net7ClientManager.Contributions;

internal enum EncounterFactionBindingKind
{
    Unknown = 0,
    Unaffiliated = 1,
    FactionLinked = 2,
}

internal enum EncounterResolvedDisposition
{
    Unknown = 0,
    Hostile = 1,
    Neutral = 2,
    Friendly = 3,
}

internal sealed record ForgeContributorRegistrationRequest
{
    public int ProtocolVersion { get; init; } = 1;

    public required string PublicKey { get; init; }

    public required string LivePilotName { get; init; }

    public string Signature { get; init; } = "";
}

internal sealed record ForgeContributorRegistrationResponse(
    string ContributorId,
    DateTimeOffset RegisteredAtUtc);

internal sealed record ForgeNpcPresenceContributionRequest
{
    public int ProtocolVersion { get; init; } = 1;

    public required string ContributorId { get; init; }

    public required string RequestId { get; init; }

    public DateTimeOffset SubmittedAtUtc { get; init; }

    public DateTimeOffset FirstObservedAtUtc { get; init; }

    public DateTimeOffset LastObservedAtUtc { get; init; }

    public int ObservationCount { get; init; }

    public required string ClientVersion { get; init; }

    public long DatasetRevision { get; init; }

    public required string Attribution { get; init; }

    public required string LivePilotName { get; init; }

    public uint StarbaseId { get; init; }

    public required string StationName { get; init; }

    public required string SectorName { get; init; }

    public uint ActiveSectorNumber { get; init; }

    public IReadOnlyList<ForgeNpcPresenceContributionItem> Npcs { get; init; } = [];

    public string Signature { get; init; } = "";
}

internal sealed record ForgeNpcPresenceContributionItem
{
    public int RoomClass { get; init; }

    public int RoomDefinitionKey { get; init; }

    public int RoomNpcSlot { get; init; }

    public int DefinitionKey { get; init; }

    public int DefinitionSecondaryId { get; init; }

    public required string Name { get; init; }

    // Wire-compatible names retained for the deployed Forge protocol.
    public int Role { get; init; }

    public int Classification { get; init; }
}

internal sealed record ForgeNpcPresenceContributionResponse(
    string RequestId,
    int Received,
    int AlreadyCanonical,
    int EvidenceAccepted,
    int Conflicts,
    long? PublishedRevision,
    string Status);

internal sealed record ForgeNavigationObjectsContributionRequest
{
    public int ProtocolVersion { get; init; } = 2;

    public required string ContributorId { get; init; }

    public required string RequestId { get; init; }

    public DateTimeOffset SubmittedAtUtc { get; init; }

    public DateTimeOffset FirstObservedAtUtc { get; init; }

    public DateTimeOffset LastObservedAtUtc { get; init; }

    public int ObservationCount { get; init; }

    public required string ClientVersion { get; init; }

    public long DatasetRevision { get; init; }

    public required string Attribution { get; init; }

    public required string LivePilotName { get; init; }

    public required string SectorId { get; init; }

    public required string SectorKey { get; init; }

    public required string SectorName { get; init; }

    public required string SystemName { get; init; }

    public uint ActiveSectorNumber { get; init; }

    public IReadOnlyList<ForgeNavigationObjectContributionItem> Targets
    { get; init; } = [];

    public string Signature { get; init; } = "";
}

internal sealed record ForgeNavigationObjectContributionItem
{
    public uint ObjectId { get; init; }

    public required string Name { get; init; }

    public string MapDisplayName { get; init; } = "";

    public float? Signature { get; init; }

    public byte RawObjectType { get; init; }

    public int? NavType { get; init; }

    public bool? IsHuge { get; init; }

    public string SelectionContext { get; init; } = "Navigation";

    public bool HasPosition { get; init; }

    public float X { get; init; }

    public float Y { get; init; }

    public float Z { get; init; }
}

internal sealed record ForgeNavigationObjectsContributionResponse(
    string RequestId,
    int Received,
    int AlreadyCanonical,
    int EvidenceAccepted,
    int Conflicts,
    long? PublishedRevision,
    string Status);

internal sealed record ForgeGravityWellBoundaryContributionRequest
{
    public int ProtocolVersion { get; init; } = 1;

    public required string ContributorId { get; init; }

    public required string RequestId { get; init; }

    public DateTimeOffset SubmittedAtUtc { get; init; }

    public required string ClientVersion { get; init; }

    public long DatasetRevision { get; init; }

    public required string Attribution { get; init; }

    public required string LivePilotName { get; init; }

    public required string SectorId { get; init; }

    public required string SectorKey { get; init; }

    public required string SectorName { get; init; }

    public required string SystemName { get; init; }

    public uint ActiveSectorNumber { get; init; }

    public IReadOnlyList<ForgeGravityWellBoundaryContributionItem> Observations
    { get; init; } = [];

    public string Signature { get; init; } = "";
}

internal sealed record ForgeGravityWellBoundaryContributionItem
{
    public DateTimeOffset ObservedAtUtc { get; init; }

    public required string Direction { get; init; }

    public float X { get; init; }

    public float Y { get; init; }

    public float Z { get; init; }

    public int MessageChannel { get; init; }

    public required string RawMessage { get; init; }

    public required string Source { get; init; }
}

internal sealed record ForgeGravityWellBoundaryContributionResponse(
    string RequestId,
    int Received,
    int AlreadyKnown,
    int EvidenceAccepted,
    int RollupsCreated,
    int RollupsUpdated,
    string Status);

internal sealed record ForgeMobSightingsContributionRequest
{
    public int ProtocolVersion { get; init; } = 3;

    public required string ContributorId { get; init; }

    public required string RequestId { get; init; }

    public DateTimeOffset SubmittedAtUtc { get; init; }

    public DateTimeOffset FirstObservedAtUtc { get; init; }

    public DateTimeOffset LastObservedAtUtc { get; init; }

    public int ObservationCount { get; init; }

    public required string ClientVersion { get; init; }

    public long DatasetRevision { get; init; }

    public required string Attribution { get; init; }

    public required string LivePilotName { get; init; }

    public required string SectorId { get; init; }

    public required string SectorKey { get; init; }

    public required string SectorName { get; init; }

    public required string SystemName { get; init; }

    public uint ActiveSectorNumber { get; init; }

    public IReadOnlyList<ForgeMobSightingContributionItem> Sightings
    { get; init; } = [];

    public string Signature { get; init; } = "";
}

internal sealed record ForgeMobSightingContributionItem
{
    public uint ObjectId { get; init; }

    public required string Name { get; init; }

    public byte RawObjectType { get; init; }

    public int CombatLevel { get; init; }

    public string FactionIdentifier { get; init; } = "";

    public EncounterFactionBindingKind FactionBindingKind { get; init; } =
        EncounterFactionBindingKind.Unknown;

    public int? IntrinsicRelationshipRaw { get; init; }

    public EncounterResolvedDisposition? IntrinsicDisposition { get; init; }

    public int? ObservedRelationshipRaw { get; init; }

    public int? ObservedAggressionRaw { get; init; }

    public bool? ObservedActivelyAggressive { get; init; }

    public bool IsOrganic { get; init; }

    public bool? AutoLevel { get; init; }

    public float X { get; init; }

    public float Y { get; init; }

    public float Z { get; init; }

    public DateTimeOffset FirstObservedAtUtc { get; init; }

    public DateTimeOffset LastObservedAtUtc { get; init; }

    public int ObservationCount { get; init; }

    public float Radius { get; init; }
}

internal sealed record ForgeMobSightingsContributionResponse(
    string RequestId,
    int Received,
    int EvidenceAccepted,
    int VariantsCreated,
    int ClustersCreated,
    int ClustersUpdated,
    long? PublishedRevision,
    string Status);

internal sealed record ForgeMobLootContributionRequest
{
    public int ProtocolVersion { get; init; } = 3;

    public required string ContributorId { get; init; }

    public required string RequestId { get; init; }

    public DateTimeOffset SubmittedAtUtc { get; init; }

    public DateTimeOffset FirstObservedAtUtc { get; init; }

    public DateTimeOffset LastObservedAtUtc { get; init; }

    public int ObservationCount { get; init; }

    public required string ClientVersion { get; init; }

    public long DatasetRevision { get; init; }

    public required string Attribution { get; init; }

    public required string LivePilotName { get; init; }

    public IReadOnlyList<ForgeMobLootContributionItem> Drops { get; init; } = [];

    public string Signature { get; init; } = "";
}

internal sealed record ForgeMobLootContributionItem
{
    public required string SectorId { get; init; }

    public required string SectorKey { get; init; }

    public required string SectorName { get; init; }

    public required string SystemName { get; init; }

    public uint ActiveSectorNumber { get; init; }

    public uint MobObjectId { get; init; }

    public uint CorpseObjectId { get; init; }

    public required string MobName { get; init; }

    public byte RawObjectType { get; init; }

    public int CombatLevel { get; init; }

    public string FactionIdentifier { get; init; } = "";

    public EncounterFactionBindingKind FactionBindingKind { get; init; } =
        EncounterFactionBindingKind.Unknown;

    public int? IntrinsicRelationshipRaw { get; init; }

    public EncounterResolvedDisposition? IntrinsicDisposition { get; init; }

    public int? ObservedRelationshipRaw { get; init; }

    public int? ObservedAggressionRaw { get; init; }

    public bool? ObservedActivelyAggressive { get; init; }

    public bool IsOrganic { get; init; }

    public bool? AutoLevel { get; init; }

    public int ItemTemplateId { get; init; }
}

internal sealed record ForgeMobLootContributionResponse(
    string RequestId,
    int Received,
    int AlreadyCanonical,
    int EvidenceAccepted,
    int VariantsCreated,
    int RelationshipsCreated,
    int RelationshipsStrengthened,
    long? PublishedRevision,
    string Status);

internal sealed record ForgeHarvestableResourcesContributionRequest
{
    public int ProtocolVersion { get; init; } = 1;

    public required string ContributorId { get; init; }

    public required string RequestId { get; init; }

    public DateTimeOffset SubmittedAtUtc { get; init; }

    public DateTimeOffset FirstObservedAtUtc { get; init; }

    public DateTimeOffset LastObservedAtUtc { get; init; }

    public int ObservationCount { get; init; }

    public required string ClientVersion { get; init; }

    public long DatasetRevision { get; init; }

    public required string Attribution { get; init; }

    public required string LivePilotName { get; init; }

    public required string SectorId { get; init; }

    public required string SectorKey { get; init; }

    public required string SectorName { get; init; }

    public required string SystemName { get; init; }

    public uint ActiveSectorNumber { get; init; }

    public IReadOnlyList<ForgeHarvestableResourceContributionItem> Harvestables
    { get; init; } = [];

    public string Signature { get; init; } = "";
}

internal sealed record ForgeHarvestableResourceContributionItem
{
    public uint ObjectId { get; init; }

    public required string Name { get; init; }

    public byte RawObjectType { get; init; }

    public int TechLevel { get; init; }

    public float X { get; init; }

    public float Y { get; init; }

    public float Z { get; init; }

    public DateTimeOffset FirstObservedAtUtc { get; init; }

    public DateTimeOffset LastObservedAtUtc { get; init; }

    public int ObservationCount { get; init; }

    public IReadOnlyList<int> ItemTemplateIds { get; init; } = [];
}

internal sealed record ForgeHarvestableResourcesContributionResponse(
    string RequestId,
    int Received,
    int AlreadyCanonical,
    int EvidenceAccepted,
    int VariantsCreated,
    int FieldsCreated,
    int FieldsUpdated,
    int RelationshipsCreated,
    int RelationshipsStrengthened,
    IReadOnlyList<string> AcceptedRelationshipIds,
    long? PublishedRevision,
    string Status);

internal sealed record ForgeStationServicesContributionRequest
{
    public int ProtocolVersion { get; init; } = 1;

    public required string ContributorId { get; init; }

    public required string RequestId { get; init; }

    public DateTimeOffset SubmittedAtUtc { get; init; }

    public DateTimeOffset FirstObservedAtUtc { get; init; }

    public DateTimeOffset LastObservedAtUtc { get; init; }

    public int ObservationCount { get; init; }

    public required string ClientVersion { get; init; }

    public long DatasetRevision { get; init; }

    public required string Attribution { get; init; }

    public required string LivePilotName { get; init; }

    public uint StarbaseId { get; init; }

    public uint StarbaseDefinitionId { get; init; }

    public required string StationName { get; init; }

    public required string SectorName { get; init; }

    public uint ActiveSectorNumber { get; init; }

    public int RoomCount { get; init; }

    public int FacilityDefinitionCount { get; init; }

    public IReadOnlyList<ForgeStationServiceContributionItem> Facilities
    { get; init; } = [];

    public string Signature { get; init; } = "";
}

internal sealed record ForgeStationServiceContributionItem
{
    public int RoomClass { get; init; }

    public int RoomDefinitionKey { get; init; }

    public int RoomFacilitySlot { get; init; }

    public int DefinitionSlot { get; init; }

    public int FacilityType { get; init; }

    public required string Name { get; init; }

    public uint ReservedValue { get; init; }
}

internal sealed record ForgeStationServicesContributionResponse(
    string RequestId,
    int Received,
    int AlreadyCanonical,
    int EvidenceAccepted,
    int Conflicts,
    long? PublishedRevision,
    string Status);

internal sealed record ForgeVendorInventoryContributionRequest
{
    public int ProtocolVersion { get; init; } = 1;

    public required string ContributorId { get; init; }

    public required string RequestId { get; init; }

    public DateTimeOffset SubmittedAtUtc { get; init; }

    public DateTimeOffset FirstObservedAtUtc { get; init; }

    public DateTimeOffset LastObservedAtUtc { get; init; }

    public int ObservationCount { get; init; }

    public required string ClientVersion { get; init; }

    public long DatasetRevision { get; init; }

    public required string Attribution { get; init; }

    public required string LivePilotName { get; init; }

    public uint StarbaseId { get; init; }

    public required string StationName { get; init; }

    public required string SectorName { get; init; }

    public uint ActiveSectorNumber { get; init; }

    public int RoomClass { get; init; }

    public int RoomDefinitionKey { get; init; }

    public int RoomNpcSlot { get; init; }

    public int DefinitionKey { get; init; }

    public int DefinitionSecondaryId { get; init; }

    public required string VendorName { get; init; }

    public int VendorType { get; init; }

    public int AmbientType { get; init; }

    public required string CatalogFingerprint { get; init; }

    public IReadOnlyList<ForgeVendorInventoryContributionItem> Items
    { get; init; } = [];

    public string Signature { get; init; } = "";
}

internal sealed record ForgeVendorInventoryContributionItem
{
    public int Slot { get; init; }

    public int ItemTemplateId { get; init; }
}

internal sealed record ForgeVendorInventoryContributionResponse(
    string RequestId,
    int Received,
    int AlreadyCanonical,
    int EvidenceAccepted,
    int Conflicts,
    int Removed,
    long? PublishedRevision,
    string Status);
