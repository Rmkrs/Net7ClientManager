namespace Net7ClientManager.Navigation;

using System.Text.Json.Serialization;

public sealed record GalaxyNavigationCatalogTarget
{
    public required string Name { get; init; }

    public string MapDisplayName { get; init; } = "";

    public float? Signature { get; init; }

    public required byte RawObjectType { get; init; }

    [JsonIgnore]
    public GalaxyNavigationTargetKind Kind =>
        GalaxyNavigationTargetKinds.FromRawObjectType(
            this.RawObjectType);

    public int? NavType { get; init; }

    public bool? IsHuge { get; init; }

    public GalaxyNavigationTargetSelectionContext SelectionContext
    { get; init; } = GalaxyNavigationTargetSelectionContext.Navigation;

    public bool HasPosition { get; init; }

    public float X { get; init; }

    public float Y { get; init; }

    public float Z { get; init; }
}
