namespace Net7ClientManager.Observations.Models;

public sealed record ClientCorpseCargoSlotObservation
{
    public int Slot { get; init; }

    public string PropertyPrefix { get; init; } = "";

    public bool IsPresent { get; init; }

    public bool IsValid { get; init; }

    public string Status { get; init; } = "";

    public int? ItemTemplateId { get; init; }

    public int? StackCount { get; init; }

    public float? Quality { get; init; }

    public float? Structure { get; init; }

    public float? AverageCost { get; init; }

    public string? BuilderName { get; init; }

    public string? InstanceInfo { get; init; }

    public string? InstanceActivatedEffectInfo { get; init; }

    public string? InstanceEquipEffectInfo { get; init; }

    public ClientRawAuxDataValueObservation? PriceRaw { get; init; }

    public IReadOnlyDictionary<string, uint> PropertyAddresses
    { get; init; } =
        new Dictionary<string, uint>(
            StringComparer.Ordinal);

    public bool IsOccupied =>
        this.IsPresent &&
        this.IsValid &&
        this.ItemTemplateId is > 0;

    public float? QualityPercent =>
        this.Quality.HasValue &&
        this.Quality.Value >= 0.0f
            ? this.Quality.Value * 100.0f
            : null;

    public float? StructurePercent =>
        this.Structure.HasValue &&
        this.Structure.Value >= 0.0f
            ? this.Structure.Value * 100.0f
            : null;
}
