namespace Net7ClientManager.Addons.Runtime;

using Net7ClientManager.Addons.Contracts;

public sealed partial class AddonRuntimeCoordinator
{
    /// <summary>
    /// Delivers a host-derived event to the addon runtimes owned by one game
    /// client. The event uses that owner's latest safe snapshot.
    /// </summary>
    public void PublishOwnerEvent(
        int ownerProcessId,
        string eventName,
        IReadOnlyDictionary<string, object?>? data = null,
        DateTimeOffset? occurredAt = null)
    {
        if (this.disposed)
        {
            return;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);

        AddonGameSnapshot? snapshot;

        lock (this.lockObject)
        {
            snapshot = this.owners.GetValueOrDefault(ownerProcessId)?.Snapshot;
        }

        if (snapshot == null)
        {
            return;
        }

        this.EnqueueHostEvent(
            ownerProcessId,
            snapshot,
            eventName,
            data,
            occurredAt);
    }

    /// <summary>
    /// Delivers a character journal event to whichever attached owner is
    /// currently observing that character. Historical or offline characters
    /// intentionally do not receive a runtime event.
    /// </summary>
    public void PublishCharacterEvent(
        uint characterId,
        string eventName,
        IReadOnlyDictionary<string, object?>? data = null,
        DateTimeOffset? occurredAt = null)
    {
        if (this.disposed || characterId == 0)
        {
            return;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);

        (int ProcessId, AddonGameSnapshot Snapshot)[] targets;

        lock (this.lockObject)
        {
            targets =
            [
                .. this.owners
                    .Where(pair =>
                        pair.Value.Snapshot?.Character.Identity?.Id == characterId)
                    .Select(pair =>
                        (pair.Key, pair.Value.Snapshot!)),
            ];
        }

        foreach (var target in targets)
        {
            this.EnqueueHostEvent(
                target.ProcessId,
                target.Snapshot,
                eventName,
                data,
                occurredAt);
        }
    }

    /// <summary>
    /// Delivers an installation-level Client Manager event to every attached
    /// addon owner. The payload should include scope = installation when the
    /// event is not tied to one pilot.
    /// </summary>
    public void PublishGlobalEvent(
        string eventName,
        IReadOnlyDictionary<string, object?>? data = null,
        DateTimeOffset? occurredAt = null)
    {
        if (this.disposed)
        {
            return;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);

        (int ProcessId, AddonGameSnapshot Snapshot)[] targets;

        lock (this.lockObject)
        {
            targets =
            [
                .. this.owners
                    .Where(pair => pair.Value.Snapshot != null)
                    .Select(pair =>
                        (pair.Key, pair.Value.Snapshot!)),
            ];
        }

        foreach (var target in targets)
        {
            this.EnqueueHostEvent(
                target.ProcessId,
                target.Snapshot,
                eventName,
                data,
                occurredAt);
        }
    }

    private void EnqueueHostEvent(
        int ownerProcessId,
        AddonGameSnapshot snapshot,
        string eventName,
        IReadOnlyDictionary<string, object?>? data,
        DateTimeOffset? occurredAt)
    {
        var gameEvent = new AddonGameEvent
        {
            Name = eventName.Trim(),
            Snapshot = snapshot,
            OccurredAt = occurredAt,
            Data = data == null
                ? new Dictionary<string, object?>(StringComparer.Ordinal)
                : new Dictionary<string, object?>(data, StringComparer.Ordinal),
        };

        this.scheduler.EnqueueFireAndForget(
            token => this.RaiseEventCoreAsync(
                ownerProcessId,
                gameEvent,
                token),
            this.LogSchedulerFailure);
    }
}
