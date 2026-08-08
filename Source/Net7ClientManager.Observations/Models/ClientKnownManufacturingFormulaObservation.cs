namespace Net7ClientManager.Observations.Models;

public sealed record ClientKnownManufacturingFormulaObservation
{
    public int Index { get; init; }

    public int ItemTemplateId { get; init; }

    public string ItemName { get; init; } = "";

    public int TechLevel { get; init; }
}
