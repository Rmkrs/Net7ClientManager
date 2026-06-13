namespace Net7ClientManager.Core;

using System.Diagnostics;
using Net7ClientManager.Forms;
using Net7ClientManager.Models;
using Net7ClientManager.Services;

public sealed class ClientManager : IDisposable
{
    private readonly System.Threading.Lock lockObject = new();
    private readonly Dictionary<int, ClientInstance> clients = [];
    private readonly ClientProcessWatcher clientProcessWatcher;
    private readonly ClientWindowFinder clientWindowFinder = new();
    private readonly ClientDockingService clientDockingService = new();
    private readonly System.Windows.Forms.Timer clientWindowTimer;
    private readonly SettingsStore settingsStore = new();
    private readonly AppSettings settings;

    public ClientManager()
    {
        this.settings = this.settingsStore.Load();

        this.clientProcessWatcher = new ClientProcessWatcher(this.ClientProcessStarted, this.ClientProcessStopped);

        this.clientWindowTimer = new System.Windows.Forms.Timer
        {
            Interval = 1000,
        };

        this.clientWindowTimer.Tick += this.ClientWindowTimer_OnTick;
    }

    public IReadOnlyCollection<ClientInstance> Clients
    {
        get
        {
            lock (this.lockObject)
            {
                return [.. this.clients.Values];
            }
        }
    }

    public LayoutProfile CurrentProfile => this.settings.GetOrCreateCurrentProfile();

    public void Start()
    {
        this.clientProcessWatcher.Start();
        this.clientWindowTimer.Start();
    }

    public void SaveSettings()
    {
        this.settingsStore.Save(this.settings);
    }

    public ClientSlot? GetAssignedSlot(ClientInstance client)
    {
        if (client.AssignedSlotId == null)
        {
            return null;
        }

        return this.CurrentProfile.Slots.FirstOrDefault(slot => slot.Id == client.AssignedSlotId.Value);
    }

    public IReadOnlyList<LayoutProfile> Profiles => this.settings.Profiles;

    public void ReconcileClientsToCurrentProfile()
    {
        this.ReconcileClientAssignments();
        this.SaveSettings();
    }

    public LayoutProfile CreateProfile(string name)
    {
        var profile = this.settings.CreateProfile(name);
        this.ReconcileClientAssignments();
        this.SaveSettings();

        return profile;
    }

    public void SwitchProfile(Guid profileId)
    {
        if (this.settings.CurrentProfileId == profileId)
        {
            return;
        }

        if (this.settings.Profiles.TrueForAll(profile => profile.Id != profileId))
        {
            return;
        }

        this.settings.CurrentProfileId = profileId;
        this.ReconcileClientAssignments();
        this.SaveSettings();
    }

    public void RenameProfile(Guid profileId, string name)
    {
        var profile = this.settings.Profiles.FirstOrDefault(profile => profile.Id == profileId);

        if (profile == null)
        {
            return;
        }

        profile.Name = string.IsNullOrWhiteSpace(name)
            ? "Unnamed Profile"
            : name.Trim();

        this.SaveSettings();
    }

    public void DuplicateProfile(Guid profileId)
    {
        var source = this.settings.Profiles.FirstOrDefault(profile => profile.Id == profileId);

        if (source == null)
        {
            return;
        }

        var profile = new LayoutProfile
        {
            Name = string.Concat(source.Name, " Copy"),
            Slots = [.. source.Slots
                .Select(slot => new ClientSlot
                {
                    Name = slot.Name,
                    AccountName = slot.AccountName,
                    Bounds = new WindowBounds
                    {
                        Left = slot.Bounds.Left,
                        Top = slot.Bounds.Top,
                        Width = slot.Bounds.Width,
                        Height = slot.Bounds.Height,
                    },
                    AutoLogin = slot.AutoLogin,
                })],
        };

        this.settings.Profiles.Add(profile);
        this.settings.CurrentProfileId = profile.Id;

        this.ReconcileClientAssignments();
        this.SaveSettings();
    }

    public void DeleteProfile(Guid profileId)
    {
        if (this.settings.Profiles.Count <= 1)
        {
            return;
        }

        var profile = this.settings.Profiles.FirstOrDefault(profile => profile.Id == profileId);

        if (profile == null)
        {
            return;
        }

        this.settings.Profiles.Remove(profile);

        if (this.settings.CurrentProfileId == profileId)
        {
            this.settings.CurrentProfileId = this.settings.Profiles[0].Id;
        }

        this.ReconcileClientAssignments();
        this.SaveSettings();
    }

    public void Dispose()
    {
        this.clientWindowTimer.Stop();
        this.clientWindowTimer.Tick -= this.ClientWindowTimer_OnTick;
        this.clientWindowTimer.Dispose();

        this.clientProcessWatcher.Dispose();

        foreach (var client in this.Clients)
        {
            this.CloseClient(client, CloseReason.ApplicationExit);
        }
    }

    public IReadOnlyList<SlotResolutionPreset> SlotResolutionPresets => this.settings.SlotResolutionPresets;

    public SlotResolutionPreset DefaultSlotResolutionPreset =>
        this.settings.SlotResolutionPresets.FirstOrDefault(
            preset => string.Equals(preset.Name, this.settings.DefaultSlotResolutionPresetName, StringComparison.Ordinal))
        ?? this.settings.SlotResolutionPresets[0];

    private void ClientProcessStarted(int processId)
    {
        Process process;

        try
        {
            process = Process.GetProcessById(processId);
        }
        catch (ArgumentException)
        {
            return;
        }

        var client = new ClientInstance(processId, process);

        lock (this.lockObject)
        {
            this.clients[processId] = client;
        }
    }

    private void ClientProcessStopped(int processId)
    {
        ClientInstance? client;

        lock (this.lockObject)
        {
            if (!this.clients.Remove(processId, out client))
            {
                return;
            }
        }

        client.HostForm?.CloseFromManager();
    }

    private void ClientWindowTimer_OnTick(object? sender, EventArgs e)
    {
        foreach (var client in this.Clients)
        {
            if (client.State != ClientState.WaitingForGameWindow)
            {
                continue;
            }

            var gameWindowHandle = this.clientWindowFinder.FindGameWindow(client.ProcessId);

            if (gameWindowHandle is not { } resolvedGameWindowHandle)
            {
                continue;
            }

            client.GameWindowHandle = resolvedGameWindowHandle;
            this.DockClient(client);
        }
    }

    private void DockClient(ClientInstance client)
    {
        if (client.HostForm != null)
        {
            return;
        }

        var hostForm = new ClientHostForm(client, this.clientDockingService, this.CloseClient);
        client.HostForm = hostForm;
        client.State = ClientState.Docked;

        this.AutoAssignSlotsIfEnabled();

        var slot = this.GetAssignedSlot(client);

        if (slot != null)
        {
            hostForm.ApplySlot(slot);
        }

        hostForm.Show();
    }

    private void CloseClient(ClientInstance client, CloseReason reason)
    {
        if (client.State is ClientState.Closing or ClientState.Stopped)
        {
            return;
        }

        client.State = ClientState.Closing;

        try
        {
            switch (reason)
            {
                case CloseReason.UserRequested:
                    this.KillClientProcess(client);
                    break;

                case CloseReason.ApplicationExit:
                    this.KillClientProcess(client);
                    break;

                case CloseReason.ProcessExited:
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(reason), reason, message: null);
            }
        }
        finally
        {
            client.State = ClientState.Stopped;

            lock (this.lockObject)
            {
                this.clients.Remove(client.ProcessId);
            }

            client.HostForm?.CloseFromManager();
        }
    }

    private void KillClientProcess(ClientInstance client)
    {
        try
        {
            if (client.Process.HasExited)
            {
                return;
            }

            client.Process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
        catch (Exception)
        {
            // Later: write to app log.
        }
    }

    private void AutoAssignSlotsIfEnabled()
    {
        if (!this.settings.AutoAssignNewClients)
        {
            return;
        }

        this.ReconcileClientAssignments();
        this.SaveSettings();
    }

    private void ReconcileClientAssignments()
    {
        lock (this.lockObject)
        {
            var activeSlots = this.CurrentProfile.Slots;
            var assignedSlotIds = new HashSet<Guid>();

            foreach (var client in this.clients.Values.OrderBy(client => client.ProcessId))
            {
                if (client.AssignedSlotId is { } assignedSlotId
                    && activeSlots.Exists(slot => slot.Id == assignedSlotId)
                    && assignedSlotIds.Add(assignedSlotId))
                {
                    var existingSlot = activeSlots.First(slot => slot.Id == assignedSlotId);
                    client.HostForm?.ApplySlot(existingSlot);
                    continue;
                }

                client.AssignedSlotId = null;

                var nextSlot = activeSlots.FirstOrDefault(slot => !assignedSlotIds.Contains(slot.Id));

                if (nextSlot == null)
                {
                    client.HostForm?.SetUnassignedTitle();
                    continue;
                }

                client.AssignedSlotId = nextSlot.Id;
                assignedSlotIds.Add(nextSlot.Id);
                client.HostForm?.ApplySlot(nextSlot);
            }
        }
    }
}
