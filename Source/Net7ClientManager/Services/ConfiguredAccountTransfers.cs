namespace Net7ClientManager.Services;

using Net7ClientManager.Models;

// Saved launches identify a pilot by both its account and its persistent local ID.
// Only an unambiguous, preserved ID can move a launch to another account.
internal sealed class ConfiguredAccountTransfers
{
    private readonly Dictionary<(Guid AccountId, Guid CharacterId), Guid> destinations = [];

    public ConfiguredAccountTransfers(IEnumerable<GameAccount> before, IEnumerable<GameAccount> after)
    {
        var previous = UniqueOwners(before);
        var current = UniqueOwners(after);
        foreach (var (characterId, accountId) in previous)
        {
            if (current.TryGetValue(characterId, out var destination) && destination != accountId)
            {
                this.destinations.Add((accountId, characterId), destination);
            }
        }
    }

    public bool TryGetDestination(Guid? accountId, Guid? characterId, out Guid destination)
    {
        destination = default;
        return accountId is { } account && characterId is { } character &&
            this.destinations.TryGetValue((account, character), out destination);
    }

    public bool ApplyToProfiles(IEnumerable<LayoutProfile> profiles)
    {
        var changed = false;
        foreach (var slot in profiles.SelectMany(profile => profile.Slots))
        {
            if (this.TryGetDestination(slot.AccountId, slot.CharacterId, out var destination))
            {
                slot.AccountId = destination;
                changed = true;
            }
        }

        return changed;
    }

    public void ApplyToLaunch(ManagedClientLaunchRequest? launch)
    {
        if (launch != null && this.TryGetDestination(launch.AccountId, launch.CharacterId, out var destination))
        {
            launch.AccountId = destination;
        }
    }

    public void ApplyToClient(ClientInstance client, ManagedClientLaunchRequest? effectiveLaunch)
    {
        if (effectiveLaunch != null &&
            this.TryGetDestination(effectiveLaunch.AccountId, effectiveLaunch.CharacterId, out _))
        {
            client.RequiresLoginAfterAccountTransfer = true;
            client.PendingEnterGameAction = null;
            client.EnterGameClickAt = null;
            client.EnterGameSubmittedAt = null;
        }

        this.ApplyToLaunch(client.ManagedLaunchRequest);
    }

    private static Dictionary<Guid, Guid> UniqueOwners(IEnumerable<GameAccount> accounts) =>
        accounts.SelectMany(account => account.Characters.Select(character => (character.Id, AccountId: account.Id)))
            .GroupBy(character => character.Id)
            .Where(group => group.Key != Guid.Empty && group.Take(2).Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single().AccountId);
}
