namespace Net7ClientManager.Observations.Observers;

using Net7ClientManager.Observations.Models;

internal readonly record struct ClientCombatIdentityResolverSnapshot(
    IReadOnlyDictionary<uint, ClientCombatActorIdentityObservation> Identities,
    int PendingCount,
    long ResolutionFailureCount)
{
    public static ClientCombatIdentityResolverSnapshot Empty { get; } =
        new(
            new Dictionary<uint, ClientCombatActorIdentityObservation>(),
            0,
            0);

    public ClientCombatActorIdentityObservation Get(
        uint objectId)
    {
        return this.Identities.TryGetValue(
            objectId,
            out var identity)
            ? identity
            : ClientCombatActorIdentityObservation.Pending(
                objectId);
    }
}
