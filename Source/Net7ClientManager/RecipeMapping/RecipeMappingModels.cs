namespace Net7ClientManager.RecipeMapping;

internal enum RecipeMappingBaselineStatus
{
    NotStarted,
    InProgress,
    Complete,
}

internal enum RecipeMappingItemKnowledge
{
    NotApplicable,
    Mapped,
    Missing,
    NotScanned,
    SkillNotLearned,
    SkillNotAvailable,
    NotManufacturable,
}

internal sealed record RecipeMappingBuildSkillProgress
{
    public string Name { get; init; } = "";

    public int CurrentRank { get; init; }

    public bool RequiresBaseline { get; init; }

    public bool IsComplete { get; init; }

    public int CompletedCategoryCount { get; init; }

    public int TotalCategoryCount { get; init; }
}

internal sealed record RecipeMappingCategoryProgress
{
    public int CategoryId { get; init; }

    public int PrimaryIndex { get; init; }

    public int SecondaryIndex { get; init; }

    public int LeafIndex { get; init; }

    public string Path { get; init; } = "";

    public string DisplayName { get; init; } = "";

    public string BuildSkillName { get; init; } = "";

    public bool IsVisited { get; init; }

    public int FormulaCount { get; init; }

    public DateTimeOffset? LastObservedAtUtc { get; init; }
}

internal sealed record RecipeMappingRecipeRow
{
    public int ItemTemplateId { get; init; }

    public string Name { get; init; } = "";

    public int? TechLevel { get; init; }

    public int? CategoryId { get; init; }

    public string PrimaryCategory { get; init; } = "";

    public string SecondaryCategory { get; init; } = "";

    public string Category { get; init; } = "";

    public IReadOnlyList<string> ApplicableBuildSkillNames { get; init; } = [];
}

internal sealed record RecipeMappingPresentation
{
    public static RecipeMappingPresentation Unavailable(
        string status)
    {
        return new RecipeMappingPresentation
        {
            Status = status,
        };
    }

    public bool IsAvailable { get; init; }

    public string Status { get; init; } = "";

    public uint CharacterId { get; init; }

    public string PilotName { get; init; } = "";

    public RecipeMappingBaselineStatus BaselineStatus { get; init; }

    public bool IsBaselineComplete =>
        this.BaselineStatus == RecipeMappingBaselineStatus.Complete;

    public bool BuildSkillsAvailable { get; init; }

    public string BuildSkillObservationStatus { get; init; } = "";

    public int RequiredBuildSkillCount { get; init; }

    public int KnownZeroBuildSkillCount { get; init; }

    public int CompletedCategoryCount { get; init; }

    public int TotalCategoryCount { get; init; }

    public int KnownRecipeCount { get; init; }

    public int? CurrentCategoryId { get; init; }

    public bool CurrentCategoryIsRequired { get; init; }

    public string CurrentCategoryPath { get; init; } = "";

    public string CurrentCategoryDisplayName { get; init; } = "";

    public string NextUnvisitedCategoryPath { get; init; } = "";

    public string NextUnvisitedCategoryDisplayName { get; init; } = "";

    public bool ManufacturingPanelActive { get; init; }

    public bool ManufacturingCatalogAvailable { get; init; }

    public bool ManufacturingReadyForAutomatedScan { get; init; }

    public string ManufacturingObservationStatus { get; init; } = "";

    public bool AllTechLevelsEnabled { get; init; }

    public IReadOnlyList<RecipeMappingBuildSkillProgress>
        BuildSkills { get; init; } = [];

    public IReadOnlyList<RecipeMappingCategoryProgress>
        Categories { get; init; } = [];

    public IReadOnlyList<RecipeMappingRecipeRow>
        Recipes { get; init; } = [];
}

internal sealed record RecipeMappingRestrictionLine
{
    public string Text { get; init; } = "";

    public bool? IsCharacterEligible { get; init; }
}

internal sealed record RecipeMappingItemPresentation
{
    public bool? IsManufacturable { get; init; }

    public RecipeMappingItemKnowledge Knowledge { get; init; }

    public string Text { get; init; } = "";

    public string Detail { get; init; } = "";

    public string RestrictionText { get; init; } = "";

    public bool? IsCharacterEligible { get; init; }

    public IReadOnlyList<RecipeMappingRestrictionLine> RestrictionLines
    { get; init; } = [];

    public IReadOnlyList<string> MappedOnPilotNames { get; init; } = [];
}


internal enum RecipeMappingHistoryEventKind
{
    RecipeLearnedByScan = 1,
    AnalyzeFailed = 10,
    AnalyzeFailedDamaged = 11,
    AnalyzeSucceeded = 12,
    AnalyzeCriticalSucceeded = 13,
    DismantleFailed = 20,
    DismantleFailedDamaged = 21,
    DismantleSucceeded = 22,
    DismantleCriticalSucceeded = 23,
    ManufactureFailed = 30,
    ManufactureFailedDamaged = 31,
    ManufactureSucceeded = 32,
    ManufactureCriticalSucceeded = 33,
    RecipeScanCompleted = 40,
}

internal sealed record RecipeMappingHistoryItem
{
    public int ItemTemplateId { get; init; }

    public int Quantity { get; init; }

    public float? QualityPercent { get; init; }
}

internal sealed record RecipeMappingHistoryEventRecord
{
    public long EventId { get; init; }

    public uint CharacterId { get; init; }

    public int ItemTemplateId { get; init; }

    public RecipeMappingHistoryEventKind Kind { get; init; }

    public int ResultValidity { get; init; }

    public DateTimeOffset ObservedAtUtc { get; init; }

    public float? OutcomeQualityPercent { get; init; }

    public long? CreditsSpent { get; init; }

    public float? SuccessProbabilityPercent { get; init; }

    public float? CriticalSuccessProbabilityPercent { get; init; }

    public int OutputQuantity { get; init; }

    public IReadOnlyList<RecipeMappingHistoryItem> ResultItems { get; init; } = [];

    public int RecipeCount { get; init; }

    public int NewRecipeCount { get; init; }
}

internal sealed record RecipeMappingRecipeComponent
{
    public int ItemTemplateId { get; init; }

    public string Name { get; init; } = "";

    public int Quantity { get; init; }
}

internal sealed record RecipeMappingRecipeDetailsPresentation
{
    public int ItemTemplateId { get; init; }

    public DateTimeOffset? FirstObservedAtUtc { get; init; }

    public string AcquisitionSource { get; init; } = "";

    public IReadOnlyList<RecipeMappingRecipeComponent> Components { get; init; } = [];

    public string ComponentsSource { get; init; } = "";

    public IReadOnlyList<RecipeMappingHistoryEventRecord> History { get; init; } = [];
}

internal sealed record RecipeMappingObservationResult
{
    public RecipeMappingPresentation? Presentation { get; init; }

    public IReadOnlyList<RecipeMappingHistoryEventRecord> RecordedEvents { get; init; } = [];

    public bool RecipeDetailsChanged { get; init; }
}

internal sealed class RecipeMappingDocument
{
    public List<RecipeMappingCharacterRecord> Characters { get; set; } = [];
}

internal sealed class RecipeMappingCharacterRecord
{
    public uint CharacterId { get; set; }

    public string PilotName { get; set; } = "";

    public bool BaselineObservedComplete { get; set; }

    public DateTimeOffset? BaselineCompletedAtUtc { get; set; }

    public DateTimeOffset? UpdatedAtUtc { get; set; }

    // Every known build-skill name is initialized from a reliable skill
    // observation. Skills that are not learned at that point are tracked as a
    // known-zero baseline until the game later reports them as learned. That
    // transition reopens only the newly relevant crafting categories.
    public List<string> BaselineInitializedBuildSkillNames { get; set; } = [];

    public List<string> KnownZeroBuildSkillNames { get; set; } = [];

    public List<int> KnownRecipeItemTemplateIds { get; set; } = [];

    // Store the complete terminal schema for diagnostics and forward
    // compatibility. Presentation/completeness only use categories relevant to
    // the build skills that currently require a baseline.
    public List<RecipeMappingCategoryRecord> Categories { get; set; } = [];
}

internal sealed class RecipeMappingCategoryRecord
{
    public int CategoryId { get; set; }

    public int PrimaryIndex { get; set; }

    public int SecondaryIndex { get; set; }

    public int LeafIndex { get; set; }

    public string Path { get; set; } = "";

    public bool IsVisited { get; set; }

    public DateTimeOffset? LastObservedAtUtc { get; set; }

    public List<int> FormulaItemTemplateIds { get; set; } = [];
}
