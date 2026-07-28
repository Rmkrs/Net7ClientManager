namespace Net7ClientManager.Observations.Models;

public sealed record ClientCombatActorIdentityObservation
{
    public bool IsResolved { get; init; }

    public uint ObjectId { get; init; }

    public byte RawObjectType { get; init; }

    public string DisplayName { get; init; } = "";

    public string Name { get; init; } = "";

    public string Owner { get; init; } = "";

    public string Title { get; init; } = "";

    public string Rank { get; init; } = "";

    public string Status { get; init; } = "";

    public static ClientCombatActorIdentityObservation Pending(
        uint objectId)
    {
        return new ClientCombatActorIdentityObservation
        {
            ObjectId = objectId,
            Status = "Identity resolution pending",
        };
    }

    public static ClientCombatActorIdentityObservation Unresolved(
        uint objectId,
        string status)
    {
        return new ClientCombatActorIdentityObservation
        {
            ObjectId = objectId,
            Status = string.IsNullOrWhiteSpace(status)
                ? "Identity could not be resolved"
                : status,
        };
    }
}
