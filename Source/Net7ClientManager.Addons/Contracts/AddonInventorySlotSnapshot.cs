namespace Net7ClientManager.Addons.Contracts;

public sealed record AddonInventorySlotSnapshot
{
    public required string Collection { get; init; }

    public required int Slot { get; init; }

    public required string State { get; init; }

    public required bool IsUsable { get; init; }

    public int? TemplateId { get; init; }

    public string? Name { get; init; }

    public int? StackCount { get; init; }

    public float? QualityPercent { get; init; }

    public float? StructurePercent { get; init; }

    public float? AverageCost { get; init; }

    public string? BuilderName { get; init; }

    public string? InstanceInfo { get; init; }

    public string? ActivatedEffectInfo { get; init; }

    public string? EquipEffectInfo { get; init; }

    public string? MountBoneName { get; init; }

    public uint? PriceLow { get; init; }

    public uint? PriceHigh { get; init; }

    public string? EquipmentKind { get; init; }

    public int? EquipmentOrdinal { get; init; }

    public IReadOnlyDictionary<string, object?> Operational { get; init; } =
        new Dictionary<string, object?>(StringComparer.Ordinal);

    public bool IsOccupied =>
        string.Equals(this.State, "occupied", StringComparison.Ordinal);
}
