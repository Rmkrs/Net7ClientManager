namespace Net7ClientManager.Observations.Models;

public sealed record ClientRuntimeItemTemplateObservation
{
    public bool IsAvailable { get; init; }

    public string Status { get; init; } = "";

    public int ItemTemplateId { get; init; }

    public int Category { get; init; }

    public int Subcategory { get; init; }

    public int ItemType { get; init; }

    public string Name { get; init; } = "";

    public string Description { get; init; } = "";

    public string Manufacturer { get; init; } = "";

    public string TypeDisplayName { get; init; } = "";

    public uint GameBasset { get; init; }

    public uint TechLevel { get; init; }

    public uint Cost { get; init; }

    public uint MaxStack { get; init; }

    public uint Flags { get; init; }

    public IReadOnlyList<ClientRuntimeItemAttributeObservation> Attributes
    { get; init; } = [];

    public IReadOnlyList<ClientRuntimeItemEffectObservation> ActivatedEffects
    { get; init; } = [];

    public IReadOnlyList<ClientRuntimeItemEffectObservation> EquippedEffects
    { get; init; } = [];
}

public sealed record ClientRuntimeItemAttributeObservation
{
    public uint ItemInfoId { get; init; }

    public uint TypeCode { get; init; }

    public uint RawValue { get; init; }

    public int? Int32Value { get; init; }

    public float? FloatValue { get; init; }

    public string StringValue { get; init; } = "";
}

public sealed record ClientRuntimeItemEffectObservation
{
    public int Index { get; init; }

    public string SourceOrIdentity { get; init; } = "";

    public string NameFormat { get; init; } = "";

    public string DescriptionFormat { get; init; } = "";

    public IReadOnlyList<float> NameValues { get; init; } = [];

    public IReadOnlyList<float> DescriptionValues { get; init; } = [];

    public uint Field50 { get; init; }

    public uint Field54 { get; init; }
}
