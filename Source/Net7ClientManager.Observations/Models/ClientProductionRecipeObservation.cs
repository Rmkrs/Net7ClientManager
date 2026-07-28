namespace Net7ClientManager.Observations.Models;

public sealed record ClientProductionRecipeObservation
{
    public bool IsAvailable { get; init; }

    public string Status { get; init; } = "";

    public uint ManufacturingObjectId { get; init; }

    public uint ClientObjectAddress { get; init; }

    public uint AuxDataAddress { get; init; }

    public ClientProductionRecipeKind Kind { get; init; }

    public int OutputItemTemplateId { get; init; }

    public IReadOnlyList<ClientProductionRecipeIngredientObservation>
        Ingredients { get; init; } = [];

    public string RecipeFingerprint { get; init; } = "";

    public static ClientProductionRecipeObservation Unavailable(
        string status,
        uint manufacturingObjectId = 0,
        uint clientObjectAddress = 0,
        uint auxDataAddress = 0)
    {
        return new ClientProductionRecipeObservation
        {
            Status = status,
            ManufacturingObjectId = manufacturingObjectId,
            ClientObjectAddress = clientObjectAddress,
            AuxDataAddress = auxDataAddress,
        };
    }
}
