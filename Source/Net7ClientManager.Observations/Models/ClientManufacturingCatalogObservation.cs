namespace Net7ClientManager.Observations.Models;

public sealed record ClientManufacturingCatalogObservation
{
    private const uint AllManufacturingTechLevelsMask = 0x01ff;

    public bool IsAvailable { get; init; }

    public string Status { get; init; } = "";

    public DateTimeOffset ObservedAt { get; init; }

    public uint ManufacturingObjectId { get; init; }

    public uint ClientObjectAddress { get; init; }

    public uint AuxDataAddress { get; init; }

    public uint PanelAddress { get; init; }

    public bool IsManufacturingPanelActive { get; init; }

    public int BrowserStage { get; init; } = -1;

    public int PrimaryIndex { get; init; } = -1;

    public int SecondaryIndex { get; init; } = -1;

    public int LeafIndex { get; init; } = -1;

    public bool ShowingPreviousAttempts { get; init; }

    public int CurrentItemCategoryId { get; init; } = -1;

    public uint TechLevelFilterBitfield { get; init; }

    public int PendingTechFilterRequestCount { get; init; }

    public IReadOnlyList<ClientManufacturingCategoryObservation>
        Categories { get; init; } = [];

    public IReadOnlyList<ClientKnownManufacturingFormulaObservation>
        KnownFormulas { get; init; } = [];

    public string ResultFingerprint { get; init; } = "";

    public bool AllTechLevelsEnabled =>
        (this.TechLevelFilterBitfield & AllManufacturingTechLevelsMask) ==
        AllManufacturingTechLevelsMask;

    public ClientManufacturingCategoryObservation? CurrentCategory =>
        this.Categories.FirstOrDefault(category =>
            category.IsVisible &&
            category.CategoryId == this.CurrentItemCategoryId);

    public bool CanRecordCurrentCategory =>
        this.IsAvailable &&
        this.IsManufacturingPanelActive &&
        this.BrowserStage == 3 &&
        !this.ShowingPreviousAttempts &&
        this.CurrentCategory is { } currentCategory &&
        this.PrimaryIndex == currentCategory.PrimaryIndex &&
        this.SecondaryIndex == currentCategory.SecondaryIndex &&
        this.LeafIndex == currentCategory.LeafIndex &&
        this.AllTechLevelsEnabled &&
        this.PendingTechFilterRequestCount == 0;

    public static ClientManufacturingCatalogObservation Unavailable(
        string status,
        DateTimeOffset? observedAt = null,
        uint manufacturingObjectId = 0,
        uint clientObjectAddress = 0,
        uint auxDataAddress = 0,
        uint panelAddress = 0,
        bool isManufacturingPanelActive = false)
    {
        return new ClientManufacturingCatalogObservation
        {
            Status = status,
            ObservedAt = observedAt ?? DateTimeOffset.MinValue,
            ManufacturingObjectId = manufacturingObjectId,
            ClientObjectAddress = clientObjectAddress,
            AuxDataAddress = auxDataAddress,
            PanelAddress = panelAddress,
            IsManufacturingPanelActive = isManufacturingPanelActive,
        };
    }
}
