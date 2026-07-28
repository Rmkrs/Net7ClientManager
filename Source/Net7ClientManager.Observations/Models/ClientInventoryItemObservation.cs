namespace Net7ClientManager.Observations.Models;

public sealed record ClientInventoryItemObservation
{
    public ClientInventoryCollectionKind Collection { get; init; }

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

    public string? MountBoneName { get; init; }

    public ClientRuntimeItemTemplateObservation? Template { get; init; }

    public ClientEquippedItemOperationalObservation Operational { get; init; } =
        ClientEquippedItemOperationalObservation.Unavailable(
            "Equipped-item operational state was not observed");

    public uint? PriceLow { get; init; }

    public uint? PriceHigh { get; init; }

    public IReadOnlyDictionary<string, uint> PropertyAddresses
    { get; init; } =
        new Dictionary<string, uint>(
            StringComparer.Ordinal);

    public bool IsOccupied =>
        this.IsPresent &&
        this.IsValid &&
        this.ItemTemplateId is > 0;

    public bool IsUsableEmpty =>
        this.IsPresent &&
        this.IsValid &&
        this.ItemTemplateId == -1;

    public bool IsUnavailable =>
        this.IsPresent &&
        this.IsValid &&
        this.ItemTemplateId == -2;

    public bool IsUsable =>
        this.IsOccupied ||
        this.IsUsableEmpty;

    public float? QualityPercent =>
        this.Quality is >= 0.0f
            ? this.Quality.Value * 100.0f
            : null;

    public float? StructurePercent =>
        this.Structure is >= 0.0f
            ? this.Structure.Value * 100.0f
            : null;
}
