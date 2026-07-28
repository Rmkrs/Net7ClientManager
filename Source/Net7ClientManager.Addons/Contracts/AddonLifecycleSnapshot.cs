namespace Net7ClientManager.Addons.Contracts;

public sealed record AddonLifecycleSnapshot
{
    public required string State { get; init; }

    public bool IsInGame { get; init; }

    public bool IsTransitioning { get; init; }
}
