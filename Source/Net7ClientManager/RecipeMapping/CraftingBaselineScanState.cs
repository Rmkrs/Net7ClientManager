namespace Net7ClientManager.RecipeMapping;

internal sealed record CraftingBaselineScanState
{
    public static CraftingBaselineScanState Idle { get; } = new();

    public bool IsRunning { get; init; }

    public bool IsPaused { get; init; }

    public string Status { get; init; } = "";

    public int CompletedCategoryCount { get; init; }

    public int TotalCategoryCount { get; init; }

    public int? ActiveCategoryId { get; init; }
}
