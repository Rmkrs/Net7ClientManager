namespace Net7ClientManager.Shopping;

using System.Collections.ObjectModel;
using Net7ClientManager.GalaxyKnowledge;

public sealed record ShoppingListDocument
{
    public required string ListId { get; init; }

    public required string Name { get; init; }

    public string Notes { get; init; } = "";

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }

    public IReadOnlyList<ShoppingListRequestedOutput> RequestedOutputs
    { get; init; } = [];

    public IReadOnlyList<ShoppingListRecipeSelection> RecipeSelections
    { get; init; } = [];
}

public sealed record ShoppingListRequestedOutput
{
    public int ItemTemplateId { get; init; }

    public long Quantity { get; init; }
}

public sealed record ShoppingListRecipeSelection
{
    public int OutputItemTemplateId { get; init; }

    public string RecipeIdentity { get; init; } = "";
}

public sealed record ShoppingListSummary
{
    public required string ListId { get; init; }

    public required string Name { get; init; }

    public string Notes { get; init; } = "";

    public DateTimeOffset UpdatedAtUtc { get; init; }

    public int RequestedOutputCount { get; init; }

    public long RequestedUnitCount { get; init; }
}

public sealed record ShoppingOwnershipSnapshot
{
    public uint? ActiveCharacterId { get; init; }

    public string ActivePilotName { get; init; } = "";

    public bool ActivePilotIsLive { get; init; }

    public DateTimeOffset BuiltAtUtc { get; init; }

    public IReadOnlyDictionary<int, ShoppingItemOwnership> ItemsByTemplateId
    { get; init; } =
        new ReadOnlyDictionary<int, ShoppingItemOwnership>(
            new Dictionary<int, ShoppingItemOwnership>());

    public ShoppingItemOwnership GetItem(int itemTemplateId)
    {
        return this.ItemsByTemplateId.TryGetValue(
            itemTemplateId,
            out var ownership)
            ? ownership
            : new ShoppingItemOwnership
            {
                ItemTemplateId = itemTemplateId,
            };
    }
}

public sealed record ShoppingItemOwnership
{
    public int ItemTemplateId { get; init; }

    public long AvailableNow { get; init; }

    public long OwnedElsewhere { get; init; }

    public IReadOnlyList<ShoppingOwnershipLocation> Locations
    { get; init; } = [];
}

public sealed record ShoppingOwnershipLocation
{
    public uint CharacterId { get; init; }

    public string PilotName { get; init; } = "";

    public bool IsActivePilot { get; init; }

    public bool IsLive { get; init; }

    public string Collection { get; init; } = "";

    public long Quantity { get; init; }

    public DateTimeOffset? ObservedAtUtc { get; init; }
}

public sealed record ShoppingPlanSnapshot
{
    public required ShoppingListDocument ShoppingList { get; init; }

    public required ShoppingOwnershipSnapshot Ownership { get; init; }

    public DateTimeOffset BuiltAtUtc { get; init; }

    public string GalaxyKnowledgeIdentity { get; init; } = "";

    public IReadOnlyList<ShoppingPlanLine> Lines { get; init; } = [];

    public IReadOnlyList<ShoppingPlanEdge> Edges { get; init; } = [];

    public IReadOnlyList<ShoppingPlanIssue> Issues { get; init; } = [];
}

public sealed record ShoppingPlanLine
{
    public int ItemTemplateId { get; init; }

    public string ItemName { get; init; } = "";

    public GalaxyItemFamily Family { get; init; }

    public bool IsRequestedOutput { get; init; }

    public long RequestedQuantity { get; init; }

    public long OwnedAvailableNow { get; init; }

    public long OwnedElsewhere { get; init; }

    public long ActiveDemandQuantity { get; init; }

    public long ActiveOwnedApplied { get; init; }

    public long ActiveShortfallQuantity { get; init; }

    public long ActiveProduceQuantity { get; init; }

    public long ActiveAcquireQuantity { get; init; }

    public long ActiveUnresolvedRecipeQuantity { get; init; }

    public long GlobalDemandQuantity { get; init; }

    public long GlobalOwnedApplied { get; init; }

    public long GlobalShortfallQuantity { get; init; }

    public long GlobalProduceQuantity { get; init; }

    public long GlobalAcquireQuantity { get; init; }

    public long GlobalUnresolvedRecipeQuantity { get; init; }

    public string SelectedRecipeIdentity { get; init; } = "";

    public GalaxyRecipeKind? SelectedRecipeKind { get; init; }

    public bool SelectedRecipeOutputQuantityKnown { get; init; }

    public long? SelectedRecipeOutputQuantity { get; init; }

    public IReadOnlyList<string> AlternativeRecipeIdentities
    { get; init; } = [];

    public bool HasKnownAcquisition { get; init; }
}

public sealed record ShoppingPlanEdge
{
    public int ParentItemTemplateId { get; init; }

    public int IngredientItemTemplateId { get; init; }

    public string RecipeIdentity { get; init; } = "";

    public long QuantityPerRecipe { get; init; }

    public long ActiveRequiredQuantity { get; init; }

    public long GlobalRequiredQuantity { get; init; }
}

public enum ShoppingPlanIssueKind
{
    UnknownItem = 0,
    InvalidRecipeSelection = 1,
    RecipeCycle = 2,
    InvalidIngredientQuantity = 3,
    QuantityOverflow = 4,
    UnknownRecipeOutputQuantity = 5,
}

public sealed record ShoppingPlanIssue
{
    public ShoppingPlanIssueKind Kind { get; init; }

    public int ItemTemplateId { get; init; }

    public string RecipeIdentity { get; init; } = "";

    public string Message { get; init; } = "";

    public IReadOnlyList<int> ItemPath { get; init; } = [];
}

public sealed class ShoppingListsChangedEventArgs(
    string? listId) : EventArgs
{
    public string? ListId { get; } = listId;
}
