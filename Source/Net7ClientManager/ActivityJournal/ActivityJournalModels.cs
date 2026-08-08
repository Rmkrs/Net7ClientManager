namespace Net7ClientManager.ActivityJournal;

[Flags]
public enum ActivityJournalCategory
{
    None = 0,
    Navigation = 1,
    Missions = 2,
    Reputation = 4,
    Credits = 8,
    Loot = 16,
    Combat = 32,
    Crafting = 64,
}

public enum ActivityJournalKind
{
    EnteredSector = 1,
    Docked = 2,
    Undocked = 3,
    MissionAccepted = 100,
    MissionProgressed = 101,
    MissionCompleted = 102,
    MissionForfeited = 103,
    MissionFailed = 104,
    MissionExpired = 105,
    MissionNoLongerActive = 106,
    ReputationChanged = 200,
    CreditsGained = 300,
    CreditsSpent = 301,
    VendorPurchased = 302,
    VendorSold = 303,
    Looted = 400,
    CombatKilled = 500,
    CombatDied = 501,
    CombatDisengaged = 502,
    CombatInterrupted = 503,
    CraftingRecipeScan = 600,
    CraftingAnalyzeFailed = 610,
    CraftingAnalyzeSucceeded = 611,
    CraftingAnalyzeCritical = 612,
    CraftingDismantleFailed = 620,
    CraftingDismantled = 621,
    CraftingDismantleCritical = 622,
    CraftingManufactureFailed = 630,
    CraftingManufactured = 631,
    CraftingManufactureCritical = 632,
}

public sealed record ActivityJournalEntry
{
    public long EventId { get; init; }

    public required uint CharacterId { get; init; }

    public required string PilotName { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }

    public required ActivityJournalCategory Category { get; init; }

    public required ActivityJournalKind Kind { get; init; }

    public required string Summary { get; init; }

    public string Details { get; init; } = "";

    public string SystemName { get; init; } = "";

    public string SectorName { get; init; } = "";

    public string StarbaseName { get; init; } = "";

    public string NearestNavName { get; init; } = "";

    public string RelatedMissionEpisodeId { get; init; } = "";

    public string RelatedCombatEncounterId { get; init; } = "";

    public string RelatedLootSessionId { get; init; } = "";

    public int PayloadVersion { get; init; } = 1;

    public string PayloadJson { get; init; } = "{}";

    public string CategoryDisplay
    {
        get
        {
            List<string> parts = [];

            if (this.Category.HasFlag(ActivityJournalCategory.Navigation))
            {
                parts.Add("Navigation");
            }

            if (this.Category.HasFlag(ActivityJournalCategory.Missions))
            {
                parts.Add("Missions");
            }

            if (this.Category.HasFlag(ActivityJournalCategory.Reputation))
            {
                parts.Add("Reputation");
            }

            if (this.Category.HasFlag(ActivityJournalCategory.Credits))
            {
                parts.Add("Credits");
            }

            if (this.Category.HasFlag(ActivityJournalCategory.Loot))
            {
                parts.Add("Loot");
            }

            if (this.Category.HasFlag(ActivityJournalCategory.Combat))
            {
                parts.Add("Combat");
            }

            if (this.Category.HasFlag(ActivityJournalCategory.Crafting))
            {
                parts.Add("Crafting");
            }

            return parts.Count == 0
                ? this.Category.ToString()
                : string.Join(" · ", parts);
        }
    }
}

public sealed record ReputationJournalEntry
{
    public long ReputationEventId { get; init; }

    public long ActivityEventId { get; init; }

    public required uint CharacterId { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }

    public required string FactionKey { get; init; }

    public required string DisplayName { get; init; }

    public required float PreviousReaction { get; init; }

    public required float CurrentReaction { get; init; }

    public float Delta => this.CurrentReaction - this.PreviousReaction;

    public string SystemName { get; init; } = "";

    public string SectorName { get; init; } = "";

    public string StarbaseName { get; init; } = "";

    public string NearestNavName { get; init; } = "";

    public string Reason { get; init; } = "";

    public string RelatedMissionEpisodeId { get; init; } = "";

    public string RelatedCombatEncounterId { get; init; } = "";
}

public sealed record LootJournalItem
{
    public int Ordinal { get; init; }

    public int? ItemTemplateId { get; init; }

    public required string Name { get; init; }

    public int Quantity { get; init; } = 1;

    public float? QualityPercent { get; init; }
}

public sealed record LootJournalSession
{
    public required string SessionId { get; init; }

    public long ActivityEventId { get; init; }

    public required uint CharacterId { get; init; }

    public required string PilotName { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public required DateTimeOffset LastUpdatedAt { get; init; }

    public uint SourceObjectId { get; init; }

    public string SourceName { get; init; } = "";

    public long Credits { get; init; }

    public string SystemName { get; init; } = "";

    public string SectorName { get; init; } = "";

    public string StarbaseName { get; init; } = "";

    public string NearestNavName { get; init; } = "";

    public IReadOnlyList<LootJournalItem> Items { get; init; } = [];
}

public sealed class ActivityJournalChangedEventArgs(uint characterId) : EventArgs
{
    public uint CharacterId { get; } = characterId;
}

internal sealed class ActivityJournalEntryRecordedEventArgs(
    ActivityJournalEntry activity,
    ReputationJournalEntry? reputation = null,
    LootJournalSession? loot = null) : EventArgs
{
    public ActivityJournalEntry Activity { get; } = activity;

    public ReputationJournalEntry? Reputation { get; } = reputation;

    public LootJournalSession? Loot { get; } = loot;
}
