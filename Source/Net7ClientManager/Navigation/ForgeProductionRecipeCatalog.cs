namespace Net7ClientManager.Navigation;

internal sealed record ForgeProductionRecipeCatalogResponse
{
    public long Revision { get; init; }

    public DateTimeOffset GeneratedAtUtc { get; init; }

    public IReadOnlyList<ForgeProductionRecipeCatalogItem> Recipes
    { get; init; } = [];
}

internal sealed record ForgeProductionRecipeCatalogItem
{
    public required string Id { get; init; }

    public int Kind { get; init; }

    public int OutputItemTemplateId { get; init; }

    public required string RecipeFingerprint { get; init; }

    public IReadOnlyList<ForgeProductionRecipeIngredient> Ingredients
    { get; init; } = [];

    public required string Confidence { get; init; }

    public bool HasAnonymousReports { get; init; }

    public IReadOnlyList<string> NamedReporters { get; init; } = [];
}

internal sealed record ForgeProductionRecipeIngredient
{
    public int ItemTemplateId { get; init; }

    public int Quantity { get; init; }
}

internal sealed record ForgeProductionRecipeCatalogSnapshot
{
    public static ForgeProductionRecipeCatalogSnapshot Unavailable(
        string status = "Forge recipe data is not available.")
    {
        return new ForgeProductionRecipeCatalogSnapshot
        {
            Status = status,
        };
    }

    public bool IsAvailable { get; init; }

    public string Status { get; init; } = "";

    public long Revision { get; init; }

    public DateTimeOffset GeneratedAtUtc { get; init; }

    public string Sha256 { get; init; } = "";

    public IReadOnlyList<ForgeProductionRecipeCatalogItem> Recipes
    { get; init; } = [];
}
