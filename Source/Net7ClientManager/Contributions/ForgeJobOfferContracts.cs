namespace Net7ClientManager.Contributions;

internal sealed record ForgeJobOfferContributionRequest
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
    public string SystemName { get; init; } = "";
    public uint ActiveSectorNumber { get; init; }
    public int FacilitySlot { get; init; } = -1;
    public IReadOnlyList<ForgeJobOfferContributionItem> Offers { get; init; } = [];
    public string Signature { get; init; } = "";
}

internal sealed record ForgeJobOfferContributionItem
{
    public required string FamilyFingerprint { get; init; }
    public required string SemanticFingerprint { get; init; }
    public uint ObservedJobId { get; init; }
    public int CatalogueGeneration { get; init; }
    public int Category { get; init; }
    public int Level { get; init; }
    public required string Type { get; init; }
    public required string Sponsor { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required string AdvertisedReward { get; init; }
    public string ObjectiveSummary { get; init; } = "";
    public bool StillAvailable { get; init; } = true;
}

internal sealed record ForgeJobOfferContributionResponse(
    string RequestId,
    int Received,
    int AlreadyCanonical,
    int EvidenceAccepted,
    int Conflicts,
    int Created,
    int Strengthened,
    long CatalogRevision,
    string Status);
