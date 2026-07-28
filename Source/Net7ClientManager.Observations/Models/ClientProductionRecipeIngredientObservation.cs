namespace Net7ClientManager.Observations.Models;

public sealed record ClientProductionRecipeIngredientObservation
{
    public int ItemTemplateId { get; init; }

    public int Quantity { get; init; }
}
