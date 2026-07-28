namespace Net7ClientManager.Addons.Contracts;

public sealed record AddonNavigationTargetSnapshot
{
    public required string Key { get; init; }

    public required string Name { get; init; }

    public required string Type { get; init; }

    public required byte RawObjectType { get; init; }

    public bool HasPosition { get; init; }

    public float X { get; init; }

    public float Y { get; init; }

    public float Z { get; init; }
}
