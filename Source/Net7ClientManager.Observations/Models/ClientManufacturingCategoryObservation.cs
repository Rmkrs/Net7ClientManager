namespace Net7ClientManager.Observations.Models;

public sealed record ClientManufacturingCategoryObservation
{
    public int PrimaryIndex { get; init; }

    public int SecondaryIndex { get; init; }

    public int LeafIndex { get; init; }

    public string PrimaryName { get; init; } = "";

    public string SecondaryName { get; init; } = "";

    public string LeafName { get; init; } = "";

    public int CategoryId { get; init; }

    public bool IsVisible { get; init; }

    public string DisplayPath => string.Join(
        " / ",
        new[]
        {
            this.PrimaryName,
            this.SecondaryName,
            this.LeafName,
        }
        .Where(value => !string.IsNullOrWhiteSpace(value)));
}
