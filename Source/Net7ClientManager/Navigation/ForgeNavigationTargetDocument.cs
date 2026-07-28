namespace Net7ClientManager.Navigation;

using System.Text.Json.Serialization;

internal sealed record ForgeNavigationTargetDocument
{
    public required string Id { get; init; }

    public int Ordinal { get; init; }

    public required string Name { get; init; }

    public string MapDisplayName { get; init; } = "";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? Signature { get; init; }

    public byte RawObjectType { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? NavType { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsHuge { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SelectionContext { get; init; }

    public bool HasPosition { get; init; }

    public float X { get; init; }

    public float Y { get; init; }

    public float Z { get; init; }
}
