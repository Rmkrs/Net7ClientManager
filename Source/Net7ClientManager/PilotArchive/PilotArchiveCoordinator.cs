namespace Net7ClientManager.PilotArchive;

using System.Diagnostics;
using System.Threading.Channels;
using Net7ClientManager.Observations;
using Net7ClientManager.Observations.Models;

public sealed class PilotArchiveCoordinator : IDisposable
{
    private readonly PilotArchiveStore store;
    private readonly Channel<ClientObservationSnapshot> snapshots;
    private readonly CancellationTokenSource cancellation = new();
    private readonly Task worker;
    private readonly Lock sessionLock = new();
    private readonly HashSet<SessionSectionKey> observedThisSession = [];
    private readonly Dictionary<CharacterSectionKey, string> appliedFingerprints = [];
    private readonly HashSet<uint> observedPilots = [];
    private readonly HashSet<int> appliedItemTemplateIds = [];
    public PilotArchiveCoordinator(PilotArchiveStore store)
    {
        this.store = store;
        this.snapshots = Channel.CreateBounded<ClientObservationSnapshot>(
            new BoundedChannelOptions(64)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.DropOldest,
                AllowSynchronousContinuations = false,
            });
        this.worker = Task.Run(this.RunAsync);
    }

    public event EventHandler<PilotArchiveChangedEventArgs>? ArchiveChanged;

    public void Observe(ClientObservationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        this.snapshots.Writer.TryWrite(snapshot);
    }

    public void ForgetProcess(int processId)
    {
        lock (this.sessionLock)
        {
            this.observedThisSession.RemoveWhere(
                key => key.ProcessId == processId);
        }
    }

    public void Dispose()
    {
        this.cancellation.Cancel();
        this.snapshots.Writer.TryComplete();

        try
        {
            this.worker.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            this.cancellation.Dispose();
        }
    }

    private async Task RunAsync()
    {
        try
        {
            await foreach (var snapshot in this.snapshots.Reader.ReadAllAsync(
                               this.cancellation.Token))
            {
                try
                {
                    this.ProcessSnapshot(snapshot);
                }
                catch (Exception exception)
                {
                    // One malformed or temporarily inconsistent observation
                    // must not terminate archive capture for the rest of the
                    // application session. The next complete snapshot can
                    // safely retry the section.
                    Debug.WriteLine(
                        string.Concat(
                            "Pilot Archive capture failed: ",
                            exception),
                        "Net7.PilotArchive");
                }
            }
        }
        catch (OperationCanceledException)
            when (this.cancellation.IsCancellationRequested)
        {
        }
    }

    private void ProcessSnapshot(ClientObservationSnapshot snapshot)
    {
        if (snapshot.LifecycleState is ClientLifecycleState.LoginScreen or
            ClientLifecycleState.CharacterSelection)
        {
            this.ForgetProcess(snapshot.ProcessId);
            return;
        }

        var capture = PilotArchiveProjector.Project(snapshot);

        if (capture == null)
        {
            return;
        }

        var observedItemTemplates = EnumerateItemTemplates(snapshot)
            .GroupBy(template => template.ItemTemplateId)
            .Select(group => group.First())
            .ToArray();
        ClientRuntimeItemTemplateObservation[] itemTemplatesToApply;

        lock (this.sessionLock)
        {
            itemTemplatesToApply = observedItemTemplates
                .Where(template =>
                    !this.appliedItemTemplateIds.Contains(
                        template.ItemTemplateId))
                .ToArray();
        }

        var itemTemplatesChanged = this.store.UpsertItemTemplates(
            itemTemplatesToApply,
            snapshot.ObservedAt);

        if (itemTemplatesToApply.Length != 0)
        {
            lock (this.sessionLock)
            {
                foreach (var template in itemTemplatesToApply)
                {
                    this.appliedItemTemplateIds.Add(
                        template.ItemTemplateId);
                }
            }
        }

        List<PilotArchiveSectionCapture> sectionsToApply = [];
        HashSet<string> refreshSections = new(StringComparer.Ordinal);
        bool firstPilotObservation;

        lock (this.sessionLock)
        {
            firstPilotObservation = !this.observedPilots.Contains(
                capture.CharacterId);

            foreach (var section in capture.Sections)
            {
                var characterSectionKey = new CharacterSectionKey(
                    capture.CharacterId,
                    section.Section);
                var sessionSectionKey = new SessionSectionKey(
                    snapshot.ProcessId,
                    capture.CharacterId,
                    section.Section);
                var fingerprintChanged =
                    !this.appliedFingerprints.TryGetValue(
                        characterSectionKey,
                        out var appliedFingerprint) ||
                    !string.Equals(
                        appliedFingerprint,
                        section.Fingerprint,
                        StringComparison.Ordinal);
                var firstSessionObservation =
                    !this.observedThisSession.Contains(sessionSectionKey);

                if (!fingerprintChanged && !firstSessionObservation)
                {
                    continue;
                }

                sectionsToApply.Add(section);

                if (firstSessionObservation)
                {
                    refreshSections.Add(section.Section);
                }
            }
        }

        if (!firstPilotObservation &&
            sectionsToApply.Count == 0 &&
            !itemTemplatesChanged)
        {
            return;
        }

        var filteredCapture = capture with
        {
            Sections = sectionsToApply,
        };
        var contentChanged = this.store.Apply(
            filteredCapture,
            refreshSections);

        lock (this.sessionLock)
        {
            this.observedPilots.Add(capture.CharacterId);

            foreach (var section in sectionsToApply)
            {
                this.appliedFingerprints[
                    new CharacterSectionKey(
                        capture.CharacterId,
                        section.Section)] = section.Fingerprint;

                if (refreshSections.Contains(section.Section))
                {
                    this.observedThisSession.Add(
                        new SessionSectionKey(
                            snapshot.ProcessId,
                            capture.CharacterId,
                            section.Section));
                }
            }
        }

        if (contentChanged ||
            refreshSections.Count != 0 ||
            itemTemplatesChanged)
        {
            this.ArchiveChanged?.Invoke(
                this,
                new PilotArchiveChangedEventArgs(capture.CharacterId));
        }
    }

    private static IEnumerable<ClientRuntimeItemTemplateObservation>
        EnumerateItemTemplates(ClientObservationSnapshot snapshot)
    {
        var inventory = snapshot.LocalPlayer.Inventory;

        if (inventory.IsAvailable)
        {
            foreach (var item in inventory.CargoSlots)
            {
                if (item.Template is { IsAvailable: true } template)
                {
                    yield return template;
                }
            }

            foreach (var item in inventory.EquippedSlots)
            {
                if (item.Template is { IsAvailable: true } template)
                {
                    yield return template;
                }
            }

            foreach (var item in inventory.AmmoSlots)
            {
                if (item.Template is { IsAvailable: true } template)
                {
                    yield return template;
                }
            }
        }

        var secure = snapshot.LocalPlayer.SecureInventory;

        if (secure.IsAvailable)
        {
            foreach (var item in secure.Slots)
            {
                if (item.Template is { IsAvailable: true } template)
                {
                    yield return template;
                }
            }
        }
    }

    private readonly record struct SessionSectionKey(
        int ProcessId,
        uint CharacterId,
        string Section);

    private readonly record struct CharacterSectionKey(
        uint CharacterId,
        string Section);
}
