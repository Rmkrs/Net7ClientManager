// ReSharper disable StringLiteralTypo
// ReSharper disable LocalizableElement
namespace Net7ClientManager.Core;

using System.Diagnostics;
using System.Globalization;
using Net7ClientManager.Forms;
using Net7ClientManager.Models;
using Net7ClientManager.Services;
using Net7ClientManager.Win32;

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
    private static readonly TimeSpan loginFillDelayAfterSizzleInterrupt = TimeSpan.FromSeconds(3);
    private LauncherAutomationSession? launcherSession;
    private DateTimeOffset? expectManagerStartedClientUntil;
    private static readonly TimeSpan managerStartedClientDetectionWindow = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan sizzleEscapeInterval = TimeSpan.FromMilliseconds(500);
    private readonly GameAccountStore gameAccountStore = new();
    private List<GameAccount> accounts;
    private bool createMissingClientsRequested;
    private DateTimeOffset? nextMissingClientStartAllowedAt;
    private IWin32Window? automationOwner;
    private static readonly TimeSpan missingClientStartCooldown = TimeSpan.FromSeconds(2);
    private const string LoginScreenUsernameClickActionName = "Login Screen Username";

    private readonly FleetCommandService fleetCommandService = new();

    public ClientManager()
    {
        this.settings = this.settingsStore.Load();
        this.accounts = this.gameAccountStore.Load();

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

    public IReadOnlyList<GameAccount> Accounts => this.accounts;

    public bool KeepClientsAlive { get; set; }

    public FleetCommandSettings FleetCommandSettings => this.settings.FleetCommands;

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

    public void CreateMissingClients(IWin32Window owner)
    {
        this.automationOwner = owner;
        this.createMissingClientsRequested = true;
        this.TickClientCreationAutomation();
    }

    public void ReconcileClientsToCurrentProfile()
    {
        this.ReconcileClientAssignments();
        this.SaveSettings();
    }

    public void CreateProfile(string name)
    {
        this.settings.CreateProfile(name);
        this.ReconcileClientAssignments();
        this.SaveSettings();
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
                    AccountId = slot.AccountId,
                    CharacterId = slot.CharacterId,
                    AutoEnterGame = slot.AutoEnterGame,
                    Bounds = new WindowBounds
                    {
                        Left = slot.Bounds.Left,
                        Top = slot.Bounds.Top,
                        Width = slot.Bounds.Width,
                        Height = slot.Bounds.Height,
                    },
                    AutoLogin = slot.AutoLogin,
                    ResolutionPresetName = slot.ResolutionPresetName,
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

    public void SaveAccounts(IReadOnlyCollection<GameAccount> gameAccounts)
    {
        this.accounts = [.. gameAccounts
            .OrderBy(account => account.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(account => account.LoginName, StringComparer.OrdinalIgnoreCase)];

        this.gameAccountStore.Save(this.accounts);
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

    public void StartClientFromLauncher(IWin32Window owner)
    {
        this.automationOwner = owner;

        var launcherPath = this.ResolveLauncherPath(owner);

        if (launcherPath == null)
        {
            return;
        }

        var folder = Path.GetDirectoryName(launcherPath);

        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        var process = new Process
        {
            StartInfo = new ProcessStartInfo(launcherPath)
            {
                WorkingDirectory = folder,
                UseShellExecute = true,
                LoadUserProfile = true,
            },
        };

        process.Start();

        this.launcherSession = new LauncherAutomationSession
        {
            ProcessId = process.Id,
        };
    }

    public ClientInstance? FindForegroundHostedClient()
    {
        var foregroundWindowHandle = NativeMethods.GetForegroundWindowHandle();

        if (foregroundWindowHandle == IntPtr.Zero)
        {
            return null;
        }

        lock (this.lockObject)
        {
            return this.clients.Values.FirstOrDefault(client =>
                                                          client.HostForm != null &&
                                                          (
                                                              client.HostForm.Handle == foregroundWindowHandle ||
                                                              NativeMethods.IsChildOrSameWindow(client.HostForm.Handle, foregroundWindowHandle) ||
                                                              client.GameWindowHandle == foregroundWindowHandle ||
                                                              NativeMethods.IsChildOrSameWindow(client.GameWindowHandle, foregroundWindowHandle)
                                                          ));
        }
    }

    public Task AssistMeAsync(FleetCommandInvocationContext invocationContext)
    {
        return this.fleetCommandService.AssistMeAsync(
            invocationContext,
            this.Clients,
            this.CurrentProfile,
            this.settings.FleetCommands,
            this.BuildFleetCommandPilotName(invocationContext.ActiveClient));
    }

    private string BuildFleetCommandPilotName(ClientInstance client)
    {
        var slot = this.GetAssignedSlot(client);

        if (slot == null)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"PID {client.ProcessId}");
        }

        var character = this.FindCharacter(slot.AccountId, slot.CharacterId);

        if (!string.IsNullOrWhiteSpace(character?.Name))
        {
            return character.Name;
        }

        if (!string.IsNullOrWhiteSpace(slot.Name))
        {
            return slot.Name;
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"PID {client.ProcessId}");
    }

    public void CancelFleetCommand()
    {
        this.fleetCommandService.Cancel();
    }

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

        if (this.IsExpectedManagerStartedClient())
        {
            client.StartedByManager = true;
            client.StartedByManagerAt = DateTimeOffset.UtcNow;
            client.State = ClientState.WaitingForTos;
            client.AutomationStatus = "Waiting for TOS";

            this.expectManagerStartedClientUntil = null;
            this.launcherSession = null;
        }

        lock (this.lockObject)
        {
            this.clients[processId] = client;
        }
    }

    private bool IsExpectedManagerStartedClient()
    {
        return this.expectManagerStartedClientUntil != null
               && DateTimeOffset.UtcNow <= this.expectManagerStartedClientUntil.Value;
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
        this.TickLauncherAutomation();

        foreach (var client in this.Clients)
        {
            switch (client.State)
            {
                case ClientState.WaitingForGameWindow:
                    this.TryDockWaitingClient(client);
                    break;

                case ClientState.WaitingForTos:
                case ClientState.AcceptingTos:
                    this.TickStartedClientAutomation(client);
                    break;

                case ClientState.Docked:
                    this.StartAutomationIfEnabled(client);
                    break;

                case ClientState.WaitingForSizzle:
                case ClientState.WaitingForLogin:
                case ClientState.WaitingForCharacterSelect:
                    this.TickClientAutomation(client);
                    break;

                case ClientState.LoginNameFilled:
                case ClientState.EnteringGame:
                case ClientState.Closing:
                case ClientState.Stopped:
                default:
                    break;
            }
        }

        this.TickClientCreationAutomation();
    }

    private void TickClientCreationAutomation()
    {
        if (!this.createMissingClientsRequested && !this.KeepClientsAlive)
        {
            return;
        }

        if (this.automationOwner == null)
        {
            return;
        }

        if (this.launcherSession != null)
        {
            return;
        }

        if (this.IsWaitingForExpectedManagerStartedClient())
        {
            return;
        }

        if (this.nextMissingClientStartAllowedAt != null &&
            DateTimeOffset.UtcNow < this.nextMissingClientStartAllowedAt.Value)
        {
            return;
        }

        if (this.HasClientStillStarting())
        {
            return;
        }

        ClientSlot? missingSlot;

        lock (this.lockObject)
        {
            missingSlot = this.CurrentProfile.Slots.FirstOrDefault(slot => !this.IsSlotSatisfied(slot));
        }

        if (missingSlot == null)
        {
            this.createMissingClientsRequested = false;
            return;
        }

        this.StartClientFromLauncher(this.automationOwner);
        this.nextMissingClientStartAllowedAt = DateTimeOffset.UtcNow + missingClientStartCooldown;
    }

    private bool HasClientStillStarting()
    {
        lock (this.lockObject)
        {
            return this.clients.Values.Any(client =>
                                               client.State is ClientState.WaitingForGameWindow
                                                   or ClientState.WaitingForTos
                                                   or ClientState.AcceptingTos
                                                   or ClientState.WaitingForSizzle
                                                   or ClientState.WaitingForLogin
                                                   or ClientState.WaitingForCharacterSelect);
        }
    }

    private void TryDockWaitingClient(ClientInstance client)
    {
        var gameWindowHandle = this.clientWindowFinder.FindGameWindow(client.ProcessId);

        if (gameWindowHandle is not { } resolvedGameWindowHandle)
        {
            return;
        }

        client.GameWindowHandle = resolvedGameWindowHandle;
        this.DockClient(client);
    }

    private void DockClient(ClientInstance client)
    {
        if (client.HostForm != null)
        {
            return;
        }

        var hostForm = new ClientHostForm(
            client,
            this.clientDockingService,
            this.CloseClient,
            slot => this.FindAccount(slot.AccountId),
            slot => this.FindCharacter(slot.AccountId, slot.CharacterId));

        client.HostForm = hostForm;
        client.State = ClientState.Docked;
        client.DockedAt = DateTimeOffset.UtcNow;
        client.SizzleInterruptedAt = null;
        client.SizzleFilePath = null;
        client.SizzleSeen = false;
        client.LastSizzleEscapeSentAt = null;
        client.LoginNameFilled = false;

        this.AutoAssignSlotsIfEnabled();

        var slot = this.GetAssignedSlot(client);

        if (slot != null)
        {
            hostForm.ApplySlot(slot);
        }

        hostForm.Show();

        this.StartAutomationIfEnabled(client);
    }

    private void StartAutomationIfEnabled(ClientInstance client)
    {
        if (client.State != ClientState.Docked)
        {
            client.AutomationStatus = $"Not docked: {client.State}";

            return;
        }

        var slot = this.GetAssignedSlot(client);

        if (slot == null)
        {
            client.AutomationStatus = "No assigned slot";
            return;
        }

        if (!slot.AutoLogin)
        {
            client.AutomationStatus = "Auto login off";
            return;
        }

        if (slot.AccountId == null)
        {
            client.AutomationStatus = "No account";
            return;
        }

        client.DockedAt ??= DateTimeOffset.UtcNow;
        client.SizzleInterruptedAt = null;
        client.SizzleFilePath = null;
        client.SizzleSeen = false;
        client.LastSizzleEscapeSentAt = null;
        client.LoginNameFilled = false;
        client.AutomationStatus = "Waiting for sizzle";
        client.State = ClientState.WaitingForSizzle;
    }

    private bool IsWaitingForExpectedManagerStartedClient()
    {
        if (this.expectManagerStartedClientUntil == null)
        {
            return false;
        }

        if (DateTimeOffset.UtcNow <= this.expectManagerStartedClientUntil.Value)
        {
            return true;
        }

        this.expectManagerStartedClientUntil = null;
        return false;
    }

    private void TickClientAutomation(ClientInstance client)
    {
        if (client.GameWindowHandle == IntPtr.Zero)
        {
            return;
        }

        if (client.Process.HasExited)
        {
            return;
        }

        var slot = this.GetAssignedSlot(client);

        if (slot is not { AutoLogin: true } || slot.AccountId == null)
        {
            client.State = ClientState.Docked;
            return;
        }

        switch (client.State)
        {
            case ClientState.WaitingForSizzle:
                this.WaitForSizzle(client);
                return;

            case ClientState.WaitingForLogin:
                this.TryFillLoginName(client, slot);
                return;


            case ClientState.WaitingForCharacterSelect:
                this.TryEnterGame(client);
                break;

            case ClientState.WaitingForGameWindow:
            case ClientState.Docked:
            case ClientState.WaitingForTos:
            case ClientState.AcceptingTos:
            case ClientState.LoginNameFilled:
            case ClientState.EnteringGame:
            case ClientState.Closing:
            case ClientState.Stopped:
            default:
                return;
        }
    }

    private void WaitForSizzle(ClientInstance client)
    {
        client.SizzleFilePath ??= TryResolveSizzleFilePath(client);

        if (client.SizzleFilePath == null)
        {
            client.AutomationStatus = "Cannot find EB_Sizzle.bik";
            return;
        }

        var canReadSizzle = CanReadExclusive(client.SizzleFilePath);

        if (!canReadSizzle)
        {
            client.SizzleSeen = true;
            client.AutomationStatus = "Skipping intro";

            if (client.LastSizzleEscapeSentAt == null
                || DateTimeOffset.UtcNow - client.LastSizzleEscapeSentAt.Value >= sizzleEscapeInterval)
            {
                NativeMethods.FocusWindow(client.GameWindowHandle);
                NativeMethods.SendEscape(client.GameWindowHandle);

                client.LastSizzleEscapeSentAt = DateTimeOffset.UtcNow;
            }

            return;
        }

        if (!client.SizzleSeen)
        {
            client.AutomationStatus = "Waiting for intro";
            return;
        }

        client.AutomationStatus = "Intro skipped";
        client.SizzleInterruptedAt = DateTimeOffset.UtcNow;
        client.State = ClientState.WaitingForLogin;
    }

    private void TryFillLoginName(ClientInstance client, ClientSlot slot)
    {
        if (client.LoginNameFilled)
        {
            return;
        }

        if (client.SizzleInterruptedAt == null)
        {
            return;
        }

        if (DateTimeOffset.UtcNow - client.SizzleInterruptedAt.Value < loginFillDelayAfterSizzleInterrupt)
        {
            client.AutomationStatus = "Waiting for login";
            return;
        }

        var account = this.FindAccount(slot.AccountId);

        if (account == null)
        {
            client.AutomationStatus = "Missing account";
            return;
        }

        if (string.IsNullOrWhiteSpace(account.LoginName))
        {
            client.AutomationStatus = "Missing login name";
            return;
        }

        var password = PasswordProtector.Unprotect(account.ProtectedPassword);

        if (string.IsNullOrEmpty(password))
        {
            client.AutomationStatus = "Missing password";
            return;
        }

        if (!this.TryClickNamedInputAction(client, LoginScreenUsernameClickActionName))
        {
            client.AutomationStatus = "Missing login screen username target";
            return;
        }

        SendKeys.SendWait("^a");
        SendKeys.SendWait(EscapeSendKeysText(account.LoginName));

        SendKeys.SendWait("{TAB}");

        SendKeys.SendWait("^a");
        SendKeys.SendWait(EscapeSendKeysText(password));

        SendKeys.SendWait("{ENTER}");

        client.LoginNameFilled = true;
        client.PasswordFilled = true;
        client.LoginSubmitted = true;
        client.AutomationStatus = "Login submitted";

        client.State = slot.AutoEnterGame
            ? ClientState.WaitingForCharacterSelect
            : ClientState.LoginNameFilled;

        client.CharacterSelectReadyAt = DateTimeOffset.UtcNow.AddSeconds(6);
    }

    private void TryEnterGame(ClientInstance client)
    {
        if (client.AssignedSlotId == null)
        {
            return;
        }

        var slot = this.GetAssignedSlot(client);

        if (slot == null || !slot.AutoEnterGame)
        {
            return;
        }

        if (client.CharacterSelectReadyAt == null ||
            DateTimeOffset.UtcNow < client.CharacterSelectReadyAt.Value)
        {
            client.AutomationStatus = "Waiting for character select";
            return;
        }

        var account = this.FindAccount(slot.AccountId);
        var character = this.FindCharacter(slot.AccountId, slot.CharacterId);

        if (account == null || character == null)
        {
            client.AutomationStatus = "Missing character";
            return;
        }

        var characterSelectClickActionName = GetCharacterSelectClickActionName(character.CharacterSlotNumber);

        var clickActions = new InputActionStore().LoadClickActions();

        var characterSlotAction = clickActions.FirstOrDefault(action =>
                                                                  string.Equals(
                                                                      action.Name,
                                                                      characterSelectClickActionName,
                                                                      StringComparison.OrdinalIgnoreCase));

        var enterGameAction = clickActions.FirstOrDefault(action =>
                                                              string.Equals(
                                                                  action.Name,
                                                                  "Character Screen Enter Game",
                                                                  StringComparison.OrdinalIgnoreCase));

        if (characterSlotAction == null || enterGameAction == null)
        {
            client.AutomationStatus = "Missing character screen actions";
            return;
        }

        if (client.PendingEnterGameAction != null)
        {
            if (client.EnterGameClickAt == null ||
                DateTimeOffset.UtcNow < client.EnterGameClickAt.Value)
            {
                client.AutomationStatus = "Waiting to enter game";
                return;
            }

            if (!this.ClickInputAction(client, client.PendingEnterGameAction))
            {
                return;
            }

            client.PendingEnterGameAction = null;
            client.EnterGameClickAt = null;
            client.AutomationStatus = $"Entering game as {character.Name}";
            client.State = ClientState.EnteringGame;

            return;
        }

        if (!this.ClickInputAction(client, characterSlotAction))
        {
            return;
        }

        client.PendingEnterGameAction = enterGameAction;
        client.EnterGameClickAt = DateTimeOffset.UtcNow.AddMilliseconds(1000);
        client.AutomationStatus = $"Selected character {character.Name}";
    }

    private static string GetCharacterSelectClickActionName(int characterSlotNumber)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"Character Screen Slot {characterSlotNumber}");
    }

    private bool TryClickNamedInputAction(ClientInstance client, string actionName)
    {
        var clickActions = new InputActionStore().LoadClickActions();

        var action = clickActions.FirstOrDefault(action =>
                                                     string.Equals(
                                                         action.Name,
                                                         actionName,
                                                         StringComparison.OrdinalIgnoreCase));

        if (action == null)
        {
            client.AutomationStatus = $"Missing click target: {actionName}";
            return false;
        }

        return this.ClickInputAction(client, action);
    }

    private bool ClickInputAction(ClientInstance client, InputActionDefinition action)
    {
        if (action.Kind != InputActionKind.MouseClick)
        {
            client.AutomationStatus = $"Action is not a click: {action.Name}";
            return false;
        }

        if (client.GameWindowHandle == IntPtr.Zero)
        {
            client.AutomationStatus = "Missing game window";
            return false;
        }

        if (!NativeMethods.TryGetClientSize(client.GameWindowHandle, out var clientSize))
        {
            client.AutomationStatus = "Could not get client size";
            return false;
        }

        var x = (int)Math.Round(action.BaseX * clientSize.Width / action.BaseWidth);
        var y = (int)Math.Round(action.BaseY * clientSize.Height / action.BaseHeight);

        var clicked = NativeMethods.ForegroundLeftClick(
            client.GameWindowHandle,
            x,
            y);

        if (!clicked)
        {
            client.AutomationStatus = $"Click failed: {action.Name}";
            return false;
        }

        client.AutomationStatus = $"Clicked {action.Name}";
        return true;
    }

    private static string EscapeSendKeysText(string value)
    {
        return value
            .Replace("{", "{{}", StringComparison.Ordinal)
            .Replace("}", "{}}", StringComparison.Ordinal)
            .Replace("+", "{+}", StringComparison.Ordinal)
            .Replace("^", "{^}", StringComparison.Ordinal)
            .Replace("%", "{%}", StringComparison.Ordinal)
            .Replace("~", "{~}", StringComparison.Ordinal)
            .Replace("(", "{(}", StringComparison.Ordinal)
            .Replace(")", "{)}", StringComparison.Ordinal);
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

            // First pass: keep all existing valid unique assignments.
            // Existing running clients must never be displaced just because a new process appeared.
            foreach (var client in this.clients.Values)
            {
                if (client.AssignedSlotId is not { } assignedSlotId)
                {
                    continue;
                }

                if (!activeSlots.Exists(slot => slot.Id == assignedSlotId))
                {
                    client.AssignedSlotId = null;
                    continue;
                }

                if (!assignedSlotIds.Add(assignedSlotId))
                {
                    client.AssignedSlotId = null;
                }
            }

            // Second pass: assign only currently unassigned clients to free slots.
            foreach (var client in this.clients.Values
                         .Where(client => client.AssignedSlotId == null)
                         .OrderBy(client => client.StartedByManagerAt ?? DateTimeOffset.MaxValue)
                         .ThenBy(client => client.ProcessId))
            {
                var nextSlot = activeSlots.FirstOrDefault(slot => !assignedSlotIds.Contains(slot.Id));

                if (nextSlot == null)
                {
                    client.HostForm?.SetUnassignedTitle();
                    continue;
                }

                client.AssignedSlotId = nextSlot.Id;
                assignedSlotIds.Add(nextSlot.Id);
            }

            // Third pass: apply the final assignments.
            foreach (var client in this.clients.Values)
            {
                if (client.AssignedSlotId is not { } assignedSlotId)
                {
                    client.HostForm?.SetUnassignedTitle();
                    continue;
                }

                var slot = activeSlots.FirstOrDefault(slot => slot.Id == assignedSlotId);

                if (slot == null)
                {
                    client.AssignedSlotId = null;
                    client.HostForm?.SetUnassignedTitle();
                    continue;
                }

                client.HostForm?.ApplySlot(slot);
            }
        }
    }

    private string? ResolveLauncherPath(IWin32Window owner)
    {
        if (IsValidLauncherPath(this.settings.PathToNet7Launcher))
        {
            return this.settings.PathToNet7Launcher;
        }

        using var dialog = new OpenFileDialog();
        dialog.Title = "Select LaunchNet7.exe";
        dialog.FileName = "LaunchNet7.exe";
        dialog.Filter = "Net7 Launcher|LaunchNet7.exe|Executable files|*.exe|All files|*.*";
        dialog.CheckFileExists = true;

        if (dialog.ShowDialog(owner) != DialogResult.OK)
        {
            return null;
        }

        if (!IsValidLauncherPath(dialog.FileName))
        {
            return null;
        }

        this.settings.PathToNet7Launcher = dialog.FileName;
        this.SaveSettings();

        return dialog.FileName;
    }

    private static bool IsValidLauncherPath(string? path)
    {
        return !string.IsNullOrWhiteSpace(path)
               && File.Exists(path)
               && string.Equals(
                   Path.GetFileName(path),
                   "LaunchNet7.exe",
                   StringComparison.OrdinalIgnoreCase);
    }

    private void TickLauncherAutomation()
    {
        if (this.launcherSession == null || this.launcherSession.State == LauncherAutomationState.Stopped)
        {
            return;
        }

        Process launcherProcess;

        try
        {
            launcherProcess = Process.GetProcessById(this.launcherSession.ProcessId);
        }
        catch (ArgumentException)
        {
            this.launcherSession.State = LauncherAutomationState.Stopped;
            this.launcherSession = null;
            return;
        }

        if (launcherProcess.HasExited)
        {
            this.launcherSession.State = LauncherAutomationState.Stopped;
            this.launcherSession = null;
            return;
        }

        switch (this.launcherSession.State)
        {
            case LauncherAutomationState.WaitingForPlayButton:
                if (!NativeMethods.IsLauncherPlayButtonDisplayed(launcherProcess.MainWindowHandle))
                {
                    return;
                }

                this.launcherSession.State = LauncherAutomationState.ClickedPlayButton;
                return;

            case LauncherAutomationState.ClickedPlayButton:
                if (!NativeMethods.ClickLauncherPlayButton(launcherProcess.MainWindowHandle))
                {
                    return;
                }

                this.expectManagerStartedClientUntil = DateTimeOffset.UtcNow + managerStartedClientDetectionWindow;
                this.launcherSession.State = LauncherAutomationState.WaitingForClientProcess;
                return;
            case LauncherAutomationState.WaitingForClientProcess:
            case LauncherAutomationState.Stopped:
            default:
                return;
        }
    }

    private void TickStartedClientAutomation(ClientInstance client)
    {
        if (!client.StartedByManager)
        {
            client.State = ClientState.WaitingForGameWindow;
            return;
        }

        switch (client.State)
        {
            case ClientState.WaitingForTos:
                if (!NativeMethods.IsTosWindowDisplayed(client.ProcessId))
                {
                    return;
                }

                client.State = ClientState.AcceptingTos;
                client.AutomationStatus = "Accepting TOS";
                return;

            case ClientState.AcceptingTos:
                if (!NativeMethods.AcceptTos(client.ProcessId))
                {
                    return;
                }

                client.AutomationStatus = "Waiting for window";
                client.State = ClientState.WaitingForGameWindow;
                return;
            case ClientState.WaitingForGameWindow:
            case ClientState.Docked:
            case ClientState.WaitingForSizzle:
            case ClientState.WaitingForLogin:
            case ClientState.LoginNameFilled:
            case ClientState.Closing:
            case ClientState.Stopped:
            case ClientState.WaitingForCharacterSelect:
            case ClientState.EnteringGame:
            default:
                return;
        }
    }

    private static string? TryResolveSizzleFilePath(ClientInstance client)
    {
        string? clientExecutablePath;

        try
        {
            client.Process.Refresh();
            clientExecutablePath = client.Process.MainModule?.FileName;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(clientExecutablePath))
        {
            return null;
        }

        var releaseDirectory = Path.GetDirectoryName(clientExecutablePath);

        if (string.IsNullOrWhiteSpace(releaseDirectory))
        {
            return null;
        }

        var gameRoot = Directory.GetParent(releaseDirectory)?.FullName;

        if (string.IsNullOrWhiteSpace(gameRoot))
        {
            return null;
        }

        var sizzleFilePath = Path.Combine(
            gameRoot,
            "Data",
            "client",
            "mixfiles",
            "EB_Sizzle.bik");

        return File.Exists(sizzleFilePath)
            ? sizzleFilePath
            : null;
    }

    private static bool CanReadExclusive(string filePath)
    {
        try
        {
            using var stream = File.Open(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.None);

            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private bool IsSlotSatisfied(ClientSlot slot)
    {
        var client = this.clients.Values.FirstOrDefault(client => client.AssignedSlotId == slot.Id);

        if (client == null)
        {
            return false;
        }

        if (client.State is ClientState.Closing or ClientState.Stopped)
        {
            return false;
        }

        if (!slot.AutoLogin)
        {
            return client.State is ClientState.Docked
                or ClientState.WaitingForSizzle
                or ClientState.WaitingForLogin
                or ClientState.LoginNameFilled
                or ClientState.WaitingForCharacterSelect
                or ClientState.EnteringGame;
        }

        if (!slot.AutoEnterGame)
        {
            return client.State is ClientState.LoginNameFilled
                or ClientState.WaitingForCharacterSelect
                or ClientState.EnteringGame;
        }

        return client.State == ClientState.EnteringGame;
    }

    public GameAccount? FindAccount(Guid? accountId)
    {
        if (accountId == null)
        {
            return null;
        }

        return this.accounts.FirstOrDefault(account => account.Id == accountId.Value);
    }

    public GameCharacter? FindCharacter(Guid? accountId, Guid? characterId)
    {
        var account = this.FindAccount(accountId);

        if (account == null || characterId == null)
        {
            return null;
        }

        return account.Characters.FirstOrDefault(character => character.Id == characterId.Value);
    }

    private sealed class LauncherAutomationSession
    {
        public required int ProcessId { get; init; }

        public LauncherAutomationState State { get; set; } = LauncherAutomationState.WaitingForPlayButton;
    }

    private enum LauncherAutomationState
    {
        WaitingForPlayButton,
        ClickedPlayButton,
        WaitingForClientProcess,
        Stopped,
    }
}
