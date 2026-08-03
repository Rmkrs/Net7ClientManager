using System.ComponentModel;
// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using System.Globalization;
using System.Text;
using Net7ClientManager.Models;
using Net7ClientManager.Navigation;
using Net7ClientManager.Observations;
using Net7ClientManager.Services;

public sealed partial class MainForm
{
    private void RefreshSlots()
    {
        var profile = this.clientManager.ActiveProfile;
        var slots = profile?.Slots ?? [];
        var signature = this.BuildSlotsSignature(slots);

        SetLabelText(
            this.slotsSummaryLabel,
            profile == null
                ? "No profile selected"
                : slots.Count switch
                {
                    0 => "No slots configured",
                    1 => "1 configured slot",
                    _ => string.Create(
                        CultureInfo.InvariantCulture,
                        $"{slots.Count} configured slots"),
                });

        var structureChanged = !string.Equals(
            this.slotsFlowPanel.Tag as string,
            signature,
            StringComparison.Ordinal);

        if (structureChanged)
        {
            this.slotsFlowPanel.Tag = signature;
            this.slotsFlowPanel.SuspendLayout();
            DisposeChildControls(this.slotsFlowPanel);

            if (profile == null)
            {
                this.slotsFlowPanel.Controls.Add(
                    this.CreateEmptyDashboardCard(
                        "No profile selected",
                        "Managed clients can run without a profile. Create or select one only when you want fixed client slots and layout automation."));
            }
            else if (slots.Count == 0)
            {
                this.slotsFlowPanel.Controls.Add(
                    this.CreateEmptyDashboardCard(
                        "No client slots yet",
                        "Add a slot to define where a client belongs and " +
                        "which account should be used for optional login automation."));
            }
            else
            {
                foreach (var slot in slots)
                {
                    this.slotsFlowPanel.Controls.Add(
                        this.CreateSlotCard(slot));
                }
            }

            this.slotsFlowPanel.ResumeLayout();
            this.ResizeDashboardCards();
        }

        this.UpdateSlotCards(slots);
    }

    private string BuildSlotsSignature(
        IEnumerable<ClientSlot> slots)
    {
        var builder = new StringBuilder();

        _ = builder
            .Append(this.clientManager.ActiveProfile?.Id ?? Guid.Empty)
            .Append('|');

        foreach (var slot in slots)
        {
            var account = this.clientManager.FindConfiguredAccount(slot.AccountId);
            var character = account?.Characters.FirstOrDefault(
                candidate => candidate.Id == slot.CharacterId);

            _ = builder
                .Append(slot.Id)
                .Append('|')
                .Append(slot.Name)
                .Append('|')
                .Append(slot.AccountId)
                .Append('|')
                .Append(slot.CharacterId)
                .Append('|')
                .Append(account?.ToString())
                .Append('|')
                .Append(character?.ToString())
                .Append('|')
                .Append(slot.AutoLogin)
                .Append('|')
                .Append(slot.AutoEnterGame)
                .Append('|')
                .Append(slot.ResolutionPresetName)
                .Append('|')
                .Append(slot.MatchGameResolutionToHost)
                .Append('|')
                .Append(slot.EffectiveTitleBarMode)
                .Append('|')
                .Append(slot.TitleBarHoverDelaySeconds)
                .Append('|')
                .Append(slot.GameResolutionWidth)
                .Append('|')
                .Append(slot.GameResolutionHeight)
                .Append('|')
                .Append(slot.Bounds.Left)
                .Append('|')
                .Append(slot.Bounds.Top)
                .Append('|')
                .Append(slot.Bounds.Width)
                .Append('|')
                .Append(slot.Bounds.Height)
                .Append(';');
        }

        return builder.ToString();
    }

    private void UpdateSlotCards(IEnumerable<ClientSlot> slots)
    {
        var slotsById = slots.ToDictionary(slot => slot.Id);

        foreach (var card in this.slotsFlowPanel.Controls
                     .OfType<DashboardCardPanel>())
        {
            if (card.Tag is not ProfileSlotCardRuntime runtime ||
                !slotsById.TryGetValue(runtime.SlotId, out var slot))
            {
                continue;
            }

            var launchState =
                this.clientManager.GetProfileSlotLaunchStatus(slot);
            var runningClient =
                this.clientManager.GetRunningClientForSlot(slot.Id);
            var statusText = launchState.ButtonText switch
            {
                "Running" => "RUNNING",
                "Starting..." => "STARTING",
                "Restarting..." => "RESTARTING",
                "Retry" => "FAILED",
                _ => "AVAILABLE",
            };
            var statusColor = launchState.ButtonText switch
            {
                "Running" => MainWindowTheme.Success,
                "Starting..." => MainWindowTheme.Accent,
                "Restarting..." => MainWindowTheme.Accent,
                "Retry" => MainWindowTheme.Danger,
                _ => MainWindowTheme.MutedText,
            };
            var accentColor = launchState.ButtonText switch
            {
                "Running" => MainWindowTheme.Success,
                "Starting..." => MainWindowTheme.Accent,
                "Restarting..." => MainWindowTheme.Accent,
                "Retry" => MainWindowTheme.Danger,
                _ => MainWindowTheme.AccentBorder,
            };
            var showLaunchStatus =
                launchState.ButtonText is
                    "Starting..." or "Restarting..." or "Retry";
            var automation = showLaunchStatus
                ? launchState.Status
                : BuildAutomationSummary(slot);
            var automationColor = launchState.ButtonText == "Retry"
                ? MainWindowTheme.Danger
                : MainWindowTheme.MutedText;

            card.AccentColor = accentColor;
            SetLabelText(runtime.StatusLabel, statusText);
            SetControlColor(runtime.StatusLabel, statusColor);
            SetLabelText(runtime.AutomationLabel, automation);
            SetControlColor(runtime.AutomationLabel, automationColor);

            var startButtonText = runningClient == null
                ? launchState.ButtonText
                : "Restart";
            var startButtonEnabled = runningClient != null ||
                                     launchState.CanStart;
            var startButtonDescription = runningClient == null
                ? launchState.Status
                : string.Concat(
                    "Restart ",
                    slot.Name,
                    " using its configured slot settings.");

            if (!string.Equals(
                    runtime.StartButton.Text,
                    startButtonText,
                    StringComparison.Ordinal))
            {
                runtime.StartButton.Text = startButtonText;
            }

            if (runtime.StartButton.Enabled != startButtonEnabled)
            {
                runtime.StartButton.Enabled = startButtonEnabled;
            }

            runtime.StartButton.AccessibleDescription =
                startButtonDescription;
            runtime.ForceCloseButton.Visible = runningClient != null;
            runtime.ForceCloseButton.Enabled = runningClient != null;

            this.dashboardToolTip.SetToolTip(
                runtime.StartButton,
                runningClient == null
                    ? BuildStartActionToolTip(slot.Name, launchState.Status)
                    : BuildRestartActionToolTip(slot.Name),
                delayMilliseconds: 250);

            this.dashboardToolTip.SetToolTip(
                runtime.ForceCloseButton,
                runningClient == null
                    ? null
                    : BuildForceCloseActionToolTip(
                        GetClientDisplayName(runningClient, slot)),
                delayMilliseconds: 250);
        }
    }

    private static void SetControlColor(Control control, Color color)
    {
        if (control.ForeColor != color)
        {
            control.ForeColor = color;
        }
    }

    private void ConfirmRestartRunningClient(int processId)
    {
        var client = this.clientManager.Clients.FirstOrDefault(
            candidate => candidate.ProcessId == processId);

        if (client == null)
        {
            this.RefreshAll();
            return;
        }

        var slot = this.clientManager.GetAssignedSlot(client);
        var displayName = GetClientDisplayName(client, slot);

        ThemedMessageDialog.ConfirmModeless(
            this,
            string.Concat("Force restart ", displayName, "?"),
            "This immediately restarts the game client. Use this when the game is stuck.",
            confirmButtonText: "Force restart",
            confirmedAction: () => this.RestartRunningClient(processId));
    }

    private async void RestartRunningClient(int processId)
    {
        await this.RestartClientAndReportAsync(processId);
    }

    private async Task RestartClientAndReportAsync(int processId)
    {
        var restartTask = this.clientManager.RestartClientAsync(
            processId,
            this);

        this.RefreshAll();

        var result = await restartTask;
        this.RefreshAll();

        if (result.Succeeded)
        {
            return;
        }

        ThemedMessageDialog.ShowWarning(
            this,
            "Client could not be restarted",
            result.Status);
    }

    private void ForceCloseSlotClient(ClientSlot slot)
    {
        var client =
            this.clientManager.GetRunningClientForSlot(slot.Id);

        if (client == null)
        {
            this.RefreshAll();
            return;
        }

        this.ConfirmForceCloseRunningClient(client.ProcessId);
    }

    private void ConfirmForceCloseRunningClient(int processId)
    {
        var client = this.clientManager.Clients.FirstOrDefault(
            candidate => candidate.ProcessId == processId);

        if (client == null)
        {
            this.RefreshAll();
            return;
        }

        var slot = this.clientManager.GetAssignedSlot(client);
        var displayName = GetClientDisplayName(client, slot);

        ThemedMessageDialog.ConfirmModeless(
            this,
            string.Concat("Force close ", displayName, "?"),
            "This immediately closes the game client. Use this when the game is stuck.",
            confirmButtonText: "Force close",
            confirmedAction: () => this.ForceCloseRunningClient(processId));
    }

    private void ForceCloseRunningClient(int processId)
    {
        if (!this.clientManager.ForceCloseClient(
                processId,
                out var status))
        {
            ThemedMessageDialog.ShowWarning(
                this,
                "Client could not be closed",
                status);
        }

        this.RefreshAll();
    }

    private static string GetClientDisplayName(
        ClientInstance client,
        ClientSlot? slot)
    {
        var pilotName = client.LiveCharacterIdentity.Name;

        if (!string.IsNullOrWhiteSpace(pilotName))
        {
            return pilotName;
        }

        if (!string.IsNullOrWhiteSpace(slot?.Name))
        {
            return slot.Name;
        }

        return "game client";
    }

    private static ActionToolTipContent BuildStartActionToolTip(
        string slotName,
        string status)
    {
        return new ActionToolTipContent(
        [
            new ActionToolTipParagraph(
            [
                new(
                    string.Concat("Start ", slotName),
                    ActionToolTipTextRole.Accent,
                    Bold: true),
            ],
            ActionToolTipParagraphStyle.Header,
            SpaceAfter: 5),
            new ActionToolTipParagraph(
            [
                new(status, ActionToolTipTextRole.Normal),
            ]),
        ],
        Icon: null);
    }

    private static ActionToolTipContent BuildRestartActionToolTip(
        string slotName)
    {
        return new ActionToolTipContent(
        [
            new ActionToolTipParagraph(
            [
                new(
                    "Restart client",
                    ActionToolTipTextRole.Accent,
                    Bold: true),
            ],
            ActionToolTipParagraphStyle.Header,
            SpaceAfter: 5),
            new ActionToolTipParagraph(
            [
                new(string.Concat(
                    "Immediately closes the current game client and starts ",
                    slotName,
                    " again. Use this when the game is stuck.")),
            ]),
        ],
        Icon: null);
    }

    private static ActionToolTipContent BuildForceCloseActionToolTip(
        string clientName)
    {
        return new ActionToolTipContent(
        [
            new ActionToolTipParagraph(
            [
                new(
                    "Force close client",
                    ActionToolTipTextRole.Danger,
                    Bold: true),
            ],
            ActionToolTipParagraphStyle.Header,
            SpaceAfter: 5),
            new ActionToolTipParagraph(
            [
                new(string.Concat(
                    "Immediately closes ",
                    clientName,
                    ". Use this when the game is stuck.")),
            ]),
        ],
        Icon: null);
    }

    private Control CreateSlotCard(ClientSlot slot)
    {
        var account = this.clientManager.FindConfiguredAccount(slot.AccountId);
        var character = account?.Characters.FirstOrDefault(
            candidate => candidate.Id == slot.CharacterId);
        var launchState =
            this.clientManager.GetProfileSlotLaunchStatus(slot);
        var runningClient =
            this.clientManager.GetRunningClientForSlot(slot.Id);

        var statusText = launchState.ButtonText switch
        {
            "Running" => "RUNNING",
            "Starting..." => "STARTING",
            "Restarting..." => "RESTARTING",
            "Retry" => "FAILED",
            _ => "AVAILABLE",
        };

        var statusColor = launchState.ButtonText switch
        {
            "Running" => MainWindowTheme.Success,
            "Starting..." => MainWindowTheme.Accent,
            "Restarting..." => MainWindowTheme.Accent,
            "Retry" => MainWindowTheme.Danger,
            _ => MainWindowTheme.MutedText,
        };

        var card = new DashboardCardPanel
        {
            Height = 170,
            Margin = new Padding(left: 0, top: 0, right: 0, bottom: 10),
            Padding = new Padding(left: 16, top: 12, right: 12, bottom: 10),
            AccentColor = launchState.ButtonText switch
            {
                "Running" => MainWindowTheme.Success,
                "Starting..." => MainWindowTheme.Accent,
                "Restarting..." => MainWindowTheme.Accent,
                "Retry" => MainWindowTheme.Danger,
                _ => MainWindowTheme.AccentBorder,
            },
            Cursor = Cursors.Hand,
        };

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Color.Transparent,
        };

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, height: 30));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, height: 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, height: 34));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, height: 100));

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent,
        };

        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, width: 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var titleLabel = new Label
        {
            Text = slot.Name,
            AutoSize = false,
            Dock = DockStyle.Fill,
            Font = MainWindowTheme.CreateHeadingFont(size: 11.0f),
            ForeColor = MainWindowTheme.Text,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
        };

        var statusLabel = new Label
        {
            Text = statusText,
            AutoSize = true,
            Font = MainWindowTheme.CreateBodyFont(size: 8.0f),
            ForeColor = statusColor,
            TextAlign = ContentAlignment.MiddleRight,
            Padding = new Padding(left: 10, top: 7, right: 0, bottom: 0),
        };

        header.Controls.Add(titleLabel, column: 0, row: 0);
        header.Controls.Add(statusLabel, column: 1, row: 0);

        var placementLabel = CreateDashboardLabel(
            BuildSlotPlacementSummary(slot),
            MainWindowTheme.MutedText);

        var accountText = account == null
            ? "Configured account: none"
            : string.Concat(
                "Configured account: ",
                UiObfuscationMode.AccountName(account.ToString()));

        var characterText = character == null
            ? "Configured character: none"
            : string.Concat("Configured character: ", character);

        var configurationPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent,
        };

        configurationPanel.RowStyles.Add(
            new RowStyle(SizeType.Percent, height: 50));
        configurationPanel.RowStyles.Add(
            new RowStyle(SizeType.Percent, height: 50));
        configurationPanel.Controls.Add(
            CreateDashboardLabel(
                accountText,
                MainWindowTheme.Text),
            column: 0,
            row: 0);
        configurationPanel.Controls.Add(
            CreateDashboardLabel(
                characterText,
                MainWindowTheme.Text),
            column: 0,
            row: 1);

        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent,
        };

        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, width: 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var showLaunchStatus =
            launchState.ButtonText is
                "Starting..." or "Restarting..." or "Retry";

        var automation = showLaunchStatus
            ? launchState.Status
            : BuildAutomationSummary(slot);

        var automationLabel = CreateDashboardLabel(
            automation,
            launchState.ButtonText == "Retry"
                ? MainWindowTheme.Danger
                : MainWindowTheme.MutedText);

        var startButton = new Button
        {
            Text = runningClient == null
                ? launchState.ButtonText
                : "Restart",
            Width = 76,
            Height = 28,
            Enabled = runningClient != null || launchState.CanStart,
            Margin = new Padding(left: 0, top: 2, right: 6, bottom: 0),
            AccessibleDescription = runningClient == null
                ? launchState.Status
                : string.Concat(
                    "Restart ",
                    slot.Name,
                    " using its configured slot settings."),
        };
        MainWindowTheme.StyleButton(startButton, primary: true);
        startButton.Click += (_, _) =>
        {
            var currentClient =
                this.clientManager.GetRunningClientForSlot(slot.Id);

            if (currentClient == null)
            {
                _ = this.clientManager.StartProfileSlot(
                    slot.Id,
                    this,
                    out _);
                this.RefreshAll();
                return;
            }

            this.ConfirmRestartRunningClient(
                currentClient.ProcessId);
        };

        var forceCloseButton = new Button
        {
            Text = "Force close",
            Width = 88,
            Height = 28,
            Visible = runningClient != null,
            Enabled = runningClient != null,
            Margin = new Padding(left: 0, top: 2, right: 6, bottom: 0),
            AccessibleDescription =
                "Immediately close this game client.",
        };
        MainWindowTheme.StyleButton(forceCloseButton, danger: true);
        forceCloseButton.Click += (_, _) =>
            this.ForceCloseSlotClient(slot);

        this.dashboardToolTip.SetToolTip(
            startButton,
            runningClient == null
                ? BuildStartActionToolTip(slot.Name, launchState.Status)
                : BuildRestartActionToolTip(slot.Name),
            delayMilliseconds: 250);
        this.dashboardToolTip.SetToolTip(
            forceCloseButton,
            runningClient == null
                ? null
                : BuildForceCloseActionToolTip(
                    GetClientDisplayName(runningClient, slot)),
            delayMilliseconds: 250);

        var editButton = new Button
        {
            Text = "Edit",
            Width = 68,
            Height = 28,
            Margin = new Padding(left: 0, top: 2, right: 6, bottom: 0),
        };
        MainWindowTheme.StyleButton(editButton);
        editButton.Click += (_, _) => this.EditSlot(slot);

        var deleteButton = new Button
        {
            Text = "Delete",
            Width = 68,
            Height = 28,
            Margin = new Padding(left: 0, top: 2, right: 0, bottom: 0),
        };
        MainWindowTheme.StyleButton(deleteButton, danger: true);
        deleteButton.Click += (_, _) => this.RemoveSlot(slot);

        var actionsPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Color.Transparent,
        };
        actionsPanel.Controls.Add(startButton);
        actionsPanel.Controls.Add(forceCloseButton);
        actionsPanel.Controls.Add(editButton);
        actionsPanel.Controls.Add(deleteButton);

        footer.Controls.Add(automationLabel, column: 0, row: 0);
        footer.Controls.Add(actionsPanel, column: 1, row: 0);

        root.Controls.Add(header, column: 0, row: 0);
        root.Controls.Add(configurationPanel, column: 0, row: 1);
        root.Controls.Add(placementLabel, column: 0, row: 2);
        root.Controls.Add(footer, column: 0, row: 3);
        card.Controls.Add(root);
        card.Tag = new ProfileSlotCardRuntime(
            slot.Id,
            card,
            statusLabel,
            automationLabel,
            startButton,
            forceCloseButton,
            editButton,
            deleteButton);

        AttachDoubleClick(
            card,
            () => this.EditSlot(slot),
            excludedControl: actionsPanel);

        return card;
    }

    private void RefreshRunningClients()
    {
        var clients = this.GetOrderedRunningClients();

        var observations = this.clientManager
            .GetClientObservationSnapshots()
            .ToDictionary(snapshot => snapshot.ProcessId);

        var routeSnapshots = clients.ToDictionary(
            client => client.ProcessId,
            client => this.clientManager.GetNavigationRouteSnapshot(
                client.ProcessId));

        SetLabelText(
            this.runningClientsSummaryLabel,
            clients.Count switch
            {
                0 => "No client processes detected",
                1 => "1 client process detected",
                _ => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{clients.Count} client processes detected"),
            });

        var structureSignature = string.Join(
            separator: ";",
            clients.Select(client => client.ProcessId.ToString(
                CultureInfo.InvariantCulture)));

        if (!string.Equals(
                this.runningClientsFlowPanel.Tag as string,
                structureSignature,
                StringComparison.Ordinal))
        {
            this.RebuildRunningClientCards(
                clients,
                structureSignature);
        }

        var cards = this.runningClientsFlowPanel.Controls
            .OfType<RunningClientCardControl>()
            .ToDictionary(card => card.ProcessId);

        foreach (var client in clients)
        {
            if (!cards.TryGetValue(client.ProcessId, out var card))
            {
                continue;
            }

            observations.TryGetValue(
                client.ProcessId,
                out var observation);

            card.ApplyPresentation(
                this.CreateRunningClientCardPresentation(
                    client,
                    this.clientManager.GetAssignedSlot(client),
                    observation,
                    routeSnapshots[client.ProcessId]));
        }
    }

    private List<ClientInstance> GetOrderedRunningClients()
    {
        var clients = this.clientManager.Clients.ToList();
        var activeProcessIds = clients
            .Select(client => client.ProcessId)
            .ToHashSet();

        foreach (var client in clients)
        {
            if (this.runningClientFirstSeenOrders.TryAdd(
                    client.ProcessId,
                    this.nextRunningClientFirstSeenOrder))
            {
                this.nextRunningClientFirstSeenOrder++;
            }
        }

        foreach (var staleProcessId in this.runningClientFirstSeenOrders.Keys
                     .Where(processId => !activeProcessIds.Contains(processId))
                     .ToArray())
        {
            _ = this.runningClientFirstSeenOrders.Remove(staleProcessId);
        }

        var slotIndexes = this.clientManager.CurrentProfile.Slots
            .Select((slot, index) => new
            {
                slot.Id,
                Index = index,
            })
            .ToDictionary(item => item.Id, item => item.Index);

        return clients
            .Select(client => new
            {
                Client = client,
                SlotIndex = client.AssignedSlotId is Guid slotId &&
                            slotIndexes.TryGetValue(slotId, out var slotIndex)
                    ? slotIndex
                    : (int?)null,
                FirstSeenOrder =
                    this.runningClientFirstSeenOrders[client.ProcessId],
            })
            .OrderBy(item => item.SlotIndex.HasValue ? 0 : 1)
            .ThenBy(item => item.SlotIndex ?? int.MaxValue)
            .ThenBy(item => item.FirstSeenOrder)
            .ThenBy(item => item.Client.ProcessId)
            .Select(item => item.Client)
            .ToList();
    }

    private void RebuildRunningClientCards(
        IReadOnlyCollection<ClientInstance> clients,
        string structureSignature)
    {
        this.runningClientsFlowPanel.SuspendLayout();

        try
        {
            DisposeChildControls(this.runningClientsFlowPanel);

            if (clients.Count == 0)
            {
                this.runningClientsFlowPanel.Controls.Add(
                    this.CreateEmptyDashboardCard(
                        "No running clients",
                        "Start a managed client, or use a profile's Create missing action to populate its configured slots."));
            }
            else
            {
                foreach (var client in clients)
                {
                    this.runningClientsFlowPanel.Controls.Add(
                        new RunningClientCardControl(
                            client.ProcessId,
                            this.ConfirmRestartRunningClient,
                            this.ConfirmForceCloseRunningClient,
                            this.dashboardToolTip));
                }
            }

            this.runningClientsFlowPanel.Tag = structureSignature;
        }
        finally
        {
            this.runningClientsFlowPanel.ResumeLayout();
        }

        this.ResizeDashboardCards();
    }

    private RunningClientCardPresentation CreateRunningClientCardPresentation(
        ClientInstance client,
        ClientSlot? assignedSlot,
        ClientObservationSnapshot? observation,
        NavigationRouteSnapshot routeSnapshot)
    {
        var identity = client.LiveCharacterIdentity;
        var observedPilotName = string.IsNullOrWhiteSpace(identity.Name)
            ? null
            : identity.Name;

        var title = observedPilotName
                    ?? assignedSlot?.Name
                    ?? client.ManagedLaunchRequest?.DisplayName
                    ?? "Unassigned client";

        var lifecycleText = FormatLifecycleState(client.LifecycleState);
        var stateText = FormatClientState(client.State);
        var accentColor = client.LifecycleState ==
                          ClientLifecycleState.InGame
            ? MainWindowTheme.Success
            : assignedSlot == null
                ? MainWindowTheme.Warning
                : MainWindowTheme.Accent;

        return new RunningClientCardPresentation(
            title,
            lifecycleText.ToUpperInvariant(),
            accentColor,
            this.BuildConfiguredLaunchSummary(client, assignedSlot),
            BuildObservedIdentitySummary(client),
            observedPilotName == null
                ? MainWindowTheme.MutedText
                : MainWindowTheme.Accent,
            BuildLocationSummary(observation),
            BuildRouteSummary(routeSnapshot),
            routeSnapshot.HasRoute
                ? MainWindowTheme.Success
                : MainWindowTheme.MutedText,
            BuildRunningClientFooter(
                client,
                stateText),
            assignedSlot == null
                ? null
                : BuildRestartActionToolTip(assignedSlot.Name),
            BuildForceCloseActionToolTip(title));
    }

    private Control CreateEmptyDashboardCard(
        string title,
        string description)
    {
        var card = new DashboardCardPanel
        {
            Height = 106,
            Margin = new Padding(left: 0, top: 0, right: 0, bottom: 10),
            Padding = new Padding(left: 18, top: 14, right: 16, bottom: 12),
            AccentColor = MainWindowTheme.Border,
        };

        var titleLabel = new Label
        {
            Text = title,
            Dock = DockStyle.Top,
            Height = 28,
            Font = MainWindowTheme.CreateHeadingFont(size: 10.5f),
            ForeColor = MainWindowTheme.Text,
        };

        var descriptionLabel = new Label
        {
            Text = description,
            Dock = DockStyle.Fill,
            ForeColor = MainWindowTheme.MutedText,
        };

        card.Controls.Add(descriptionLabel);
        card.Controls.Add(titleLabel);

        return card;
    }

    private void AddSlotButton_OnClick(
        object? sender,
        EventArgs e)
    {
        this.AddSlot(SlotEditorGuidanceTarget.None);
    }

    private void AddSlot(SlotEditorGuidanceTarget guidanceTarget)
    {
        var profile = this.clientManager.ActiveProfile;

        if (profile == null)
        {
            return;
        }

        var screenBounds = Screen.PrimaryScreen?.Bounds
                           ?? SystemInformation.VirtualScreen;
        var preset = SlotPlacementDefaults.SelectBestFitResolution(
            this.clientManager.SlotResolutionPresets,
            this.clientManager.DefaultSlotResolutionPreset,
            screenBounds,
            HostedClientWindowMetrics.TitleBarHeight);
        var bounds = SlotPlacementDefaults.CreateForScreen(screenBounds);

        bounds.Width = preset.Width;
        bounds.Height = preset.Height;

        var slot = new ClientSlot
        {
            Name = SlotPlacementDefaults.CreateDefaultName(
                profile.Slots),
            Bounds = bounds,
            ResolutionPresetName = preset.Name,
            MatchGameResolutionToHost = true,
            ShowTitleBar = true,
            TitleBarMode = ClientTitleBarMode.Always,
            TitleBarHoverDelaySeconds = 0.75m,
            GameResolutionWidth = preset.Width,
            GameResolutionHeight = preset.Height,
        };

        using var form = new SlotEditorForm(
            this.clientManager,
            slot,
            isNew: true,
            guidanceTarget: guidanceTarget);

        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        profile.Slots.Add(slot);
        this.clientManager.ReconcileClientsToCurrentProfile();
        this.RefreshAll();
    }

    private void EditSlot(ClientSlot slot)
    {
        this.EditSlot(slot, SlotEditorGuidanceTarget.None);
    }

    private void EditSlot(
        ClientSlot slot,
        SlotEditorGuidanceTarget guidanceTarget)
    {
        if (this.clientManager.ActiveProfile == null)
        {
            return;
        }

        using var form = new SlotEditorForm(
            this.clientManager,
            slot,
            guidanceTarget: guidanceTarget);

        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        this.clientManager.ReconcileClientsToCurrentProfile();
        this.RefreshAll();
    }

    private void RemoveSlot(ClientSlot slot)
    {
        var profile = this.clientManager.ActiveProfile;

        if (profile == null)
        {
            return;
        }

        if (!ThemedMessageDialog.Confirm(
                this,
                "Remove slot",
                string.Concat(
                    "Remove slot '",
                    slot.Name,
                    "' from this profile?"),
                confirmButtonText: "Remove"))
        {
            return;
        }

        profile.Slots.Remove(slot);

        foreach (var client in this.clientManager.Clients.Where(
                     client => client.AssignedSlotId == slot.Id))
        {
            client.AssignedSlotId = null;
            client.HostForm?.SetUnassignedTitle();
        }

        this.clientManager.ReconcileClientsToCurrentProfile();
        this.RefreshAll();
    }

    private void EditLayoutButton_OnClick(
        object? sender,
        EventArgs e)
    {
        this.OpenLayoutEditor(showTour: false);
    }

    private void OpenLayoutEditor(bool showTour)
    {
        if (this.clientManager.ActiveProfile == null)
        {
            return;
        }

        using var form = new LayoutEditorForm(this.clientManager);
        using var windowPlacement =
            this.clientManager.BindGlobalWindowPlacement(
                form,
                Net7ClientManager.Services.WindowPlacementIds.LayoutEditor,
                this);

        if (showTour)
        {
            form.Shown += (_, _) =>
                form.BeginInvoke(() => form.ShowHelpTour());
        }

        _ = form.ShowDialog(this);
        this.RefreshAll();
    }

    private void DashboardFlowPanel_OnSizeChanged(
        object? sender,
        EventArgs e)
    {
        this.ResizeDashboardCards();
    }

    private void ResizeDashboardCards()
    {
        if (this.slotsFlowPanel == null ||
            this.runningClientsFlowPanel == null)
        {
            return;
        }

        ResizeCards(this.slotsFlowPanel);
        ResizeCards(this.runningClientsFlowPanel);
    }

    private static void DisposeChildControls(Control parent)
    {
        var controls = parent.Controls
            .Cast<Control>()
            .ToArray();

        parent.Controls.Clear();

        foreach (var control in controls)
        {
            control.Dispose();
        }
    }

    private static void ResizeCards(FlowLayoutPanel panel)
    {
        if (panel.ClientSize.Width <= 0)
        {
            return;
        }

        var availableWidth = panel.ClientSize.Width -
                             panel.Padding.Horizontal -
                             SystemInformation.VerticalScrollBarWidth -
                             4;

        foreach (Control control in panel.Controls)
        {
            control.Width = Math.Max(220, availableWidth);
        }
    }

    private static Label CreateDashboardLabel(
        string text,
        Color color)
    {
        return new Label
        {
            Text = text,
            AutoSize = false,
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = color,
        };
    }

    private static void AttachDoubleClick(
        Control control,
        Action action,
        Control? excludedControl = null)
    {
        if (ReferenceEquals(control, excludedControl))
        {
            return;
        }

        control.DoubleClick += (_, _) => action();

        foreach (Control child in control.Controls)
        {
            AttachDoubleClick(
                child,
                action,
                excludedControl);
        }
    }

    private string BuildConfiguredLaunchSummary(
        ClientInstance client,
        ClientSlot? slot)
    {
        if (slot != null)
        {
            var account = this.clientManager.FindConfiguredAccount(slot.AccountId);
            var character = account?.Characters.FirstOrDefault(
                candidate => candidate.Id == slot.CharacterId);

            var accountText = account == null
                ? "none"
                : UiObfuscationMode.AccountName(account.ToString());
            var characterText = character?.ToString() ?? "none";

            return string.Concat(
                "Manager slot: ",
                slot.Name,
                " · configured launch: ",
                accountText,
                " / ",
                characterText);
        }

        var launch = client.ManagedLaunchRequest;
        var launchAccount = this.clientManager.FindConfiguredAccount(
            launch?.AccountId);
        var launchCharacter = launchAccount?.Characters.FirstOrDefault(
            candidate => candidate.Id == launch?.CharacterId);

        if (launchAccount != null || launchCharacter != null)
        {
            return string.Concat(
                "Manager assignment: unassigned · configured launch: ",
                launchAccount == null
                    ? "none"
                    : UiObfuscationMode.AccountName(
                        launchAccount.ToString()),
                " / ",
                launchCharacter?.ToString() ?? "none");
        }

        return client.AllowProfileAutoAssignment
            ? "Manager assignment: unassigned · configured launch identity: none"
            : "Manager assignment: intentionally unassigned · configured launch identity: none";
    }

    private static string BuildObservedIdentitySummary(
        ClientInstance client)
    {
        var identity = client.LiveCharacterIdentity;

        if (!identity.IsAvailable ||
            string.IsNullOrWhiteSpace(identity.Name))
        {
            return string.Concat(
                "Live-observed pilot: ",
                string.IsNullOrWhiteSpace(identity.StatusText)
                    ? "not available yet"
                    : identity.StatusText);
        }

        var builder = new StringBuilder("Live-observed pilot: ");
        _ = builder.Append(identity.Name);

        if (!string.IsNullOrWhiteSpace(identity.Profession))
        {
            _ = builder
                .Append(" · ")
                .Append(identity.Profession);
        }

        if (identity.OverallLevel is { } overallLevel)
        {
            _ = builder
                .Append(" · OL ")
                .Append(overallLevel);
        }

        return builder.ToString();
    }

    private static string BuildLocationSummary(
        ClientObservationSnapshot? observation)
    {
        if (observation is not { World.IsAvailable: true })
        {
            return "Location: not observed yet";
        }

        var world = observation.World;
        var location = !string.IsNullOrWhiteSpace(world.CurrentStarbaseName)
            ? world.CurrentStarbaseName
            : !string.IsNullOrWhiteSpace(world.CurrentSectorName)
                ? world.CurrentSectorName
                : "Unknown location";

        if (!string.IsNullOrWhiteSpace(world.CurrentSectorName) &&
            !string.Equals(
                location,
                world.CurrentSectorName,
                StringComparison.OrdinalIgnoreCase))
        {
            location = string.Concat(
                location,
                " · ",
                world.CurrentSectorName);
        }

        if (!string.IsNullOrWhiteSpace(world.CurrentSystemName))
        {
            location = string.Concat(
                location,
                " / ",
                world.CurrentSystemName);
        }

        return string.Concat("Location: ", location);
    }

    private static string BuildRouteSummary(
        NavigationRouteSnapshot routeSnapshot)
    {
        if (routeSnapshot.Route is { } route &&
            routeSnapshot.HasRoute)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"Route: {route.Destination.DisplayName} · {route.RemainingHopCount} hops remaining");
        }

        return string.Concat(
            "Route: ",
            string.IsNullOrWhiteSpace(routeSnapshot.StatusText)
                ? "none"
                : routeSnapshot.StatusText);
    }

    private static string BuildRunningClientFooter(
        ClientInstance client,
        string stateText)
    {
        var managerState = string.IsNullOrWhiteSpace(client.AutomationStatus)
            ? stateText
            : client.AutomationStatus;

        return string.Concat("Status: ", managerState);
    }

    private static string BuildSlotPlacementSummary(ClientSlot slot)
    {
        var gameWidth = slot.MatchGameResolutionToHost ||
                        slot.GameResolutionWidth <= 0
            ? slot.Bounds.Width
            : slot.GameResolutionWidth;
        var gameHeight = slot.MatchGameResolutionToHost ||
                         slot.GameResolutionHeight <= 0
            ? slot.Bounds.Height
            : slot.GameResolutionHeight;

        var hostedHeight =
            HostedClientWindowMetrics.GetHostedWindowHeight(slot);

        return string.Concat(
            "Host ",
            slot.Bounds.Width.ToString(CultureInfo.InvariantCulture),
            "×",
            hostedHeight.ToString(CultureInfo.InvariantCulture),
            " · Game ",
            gameWidth.ToString(CultureInfo.InvariantCulture),
            "×",
            gameHeight.ToString(CultureInfo.InvariantCulture),
            " · ",
            HostedClientWindowMetrics.GetTitleBarModeText(slot),
            slot.EffectiveTitleBarMode == ClientTitleBarMode.OnHover
                ? string.Concat(
                    " after ",
                    slot.TitleBarHoverDelaySeconds.ToString(
                        "0.##",
                        CultureInfo.InvariantCulture),
                    "s")
                : "",
            " · position ",
            slot.Bounds.Left.ToString(CultureInfo.InvariantCulture),
            ", ",
            slot.Bounds.Top.ToString(CultureInfo.InvariantCulture));
    }

    private static string BuildAutomationSummary(ClientSlot slot)
    {
        if (!slot.AutoLogin && !slot.AutoEnterGame)
        {
            return "Launch automation disabled";
        }

        if (slot.AutoLogin && slot.AutoEnterGame)
        {
            return "Auto login and auto enter enabled";
        }

        return slot.AutoLogin
            ? "Auto login enabled"
            : "Auto enter enabled";
    }

    private static string FormatClientState(ClientState state)
    {
        return state switch
        {
            ClientState.WaitingForGameWindow => "Waiting for window",
            ClientState.Docked => "Docked",
            ClientState.WaitingForTos => "Waiting for TOS",
            ClientState.AcceptingTos => "Accepting TOS",
            ClientState.WaitingForIntro => "Waiting for intro",
            ClientState.WaitingForLogin => "Waiting for login",
            ClientState.LoginSubmitted => "Login submitted",
            ClientState.WaitingForCharacterSelect => "Character select",
            ClientState.EnteringGame => "Entering game",
            ClientState.Ready => "Ready",
            ClientState.Closing => "Closing",
            ClientState.Stopped => "Stopped",
            _ => state.ToString(),
        };
    }

    private static string FormatLifecycleState(
        ClientLifecycleState state)
    {
        return state switch
        {
            ClientLifecycleState.ApplicationStarted => "Starting",
            ClientLifecycleState.IntroScene => "Intro",
            ClientLifecycleState.LoginScreen => "Login",
            ClientLifecycleState.CharacterSelection => "Characters",
            ClientLifecycleState.InGame => "In game",
            ClientLifecycleState.Unknown => "Observing",
            _ => state.ToString(),
        };
    }

    private static void SetLabelText(
        Label label,
        string text)
    {
        if (!string.Equals(label.Text, text, StringComparison.Ordinal))
        {
            label.Text = text;
        }
    }

    private sealed record ProfileSlotCardRuntime(
        Guid SlotId,
        Control Card,
        Label StatusLabel,
        Label AutomationLabel,
        Button StartButton,
        Button ForceCloseButton,
        Button EditButton,
        Button DeleteButton);

    private sealed record RunningClientCardPresentation(
        string Title,
        string LifecycleText,
        Color AccentColor,
        string ConfiguredLaunchText,
        string ObservedIdentityText,
        Color ObservedIdentityColor,
        string LocationText,
        string RouteText,
        Color RouteColor,
        string FooterText,
        ActionToolTipContent? RestartToolTip,
        ActionToolTipContent ForceCloseToolTip);

    private sealed class RunningClientCardControl : DashboardCardPanel
    {
        private readonly Label titleLabel;
        private readonly Label lifecycleLabel;
        private readonly Label configuredLaunchLabel;
        private readonly Label observedIdentityLabel;
        private readonly Label locationLabel;
        private readonly Label routeLabel;
        private readonly Label footerLabel;
        private readonly Button restartButton;
        private readonly Button forceCloseButton;
        private readonly ActionToolTip toolTip;

        public RunningClientCardControl(
            int processId,
            Action<int> restartRequested,
            Action<int> forceCloseRequested,
            ActionToolTip toolTip)
        {
            ArgumentNullException.ThrowIfNull(restartRequested);
            ArgumentNullException.ThrowIfNull(forceCloseRequested);
            ArgumentNullException.ThrowIfNull(toolTip);

            this.ProcessId = processId;
            this.toolTip = toolTip;
            this.Height = 200;
            this.Margin = new Padding(
                left: 0,
                top: 0,
                right: 0,
                bottom: 10);
            this.Padding = new Padding(
                left: 16,
                top: 12,
                right: 14,
                bottom: 10);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 6,
                BackColor = Color.Transparent,
            };

            root.RowStyles.Add(new RowStyle(SizeType.Absolute, height: 30));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, height: 26));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, height: 26));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, height: 26));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, height: 26));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, height: 100));

            var header = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.Transparent,
            };

            header.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, width: 100));
            header.ColumnStyles.Add(
                new ColumnStyle(SizeType.Absolute, width: 112));

            this.titleLabel = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = MainWindowTheme.CreateHeadingFont(size: 11.5f),
                ForeColor = MainWindowTheme.Text,
            };

            this.lifecycleLabel = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                Font = MainWindowTheme.CreateBodyFont(size: 8.0f),
                TextAlign = ContentAlignment.MiddleRight,
            };

            this.configuredLaunchLabel = CreateDashboardLabel(
                "",
                MainWindowTheme.MutedText);
            this.observedIdentityLabel = CreateDashboardLabel(
                "",
                MainWindowTheme.MutedText);
            this.locationLabel = CreateDashboardLabel(
                "",
                MainWindowTheme.Text);
            this.routeLabel = CreateDashboardLabel(
                "",
                MainWindowTheme.MutedText);
            this.footerLabel = CreateDashboardLabel(
                "",
                MainWindowTheme.MutedText);

            this.restartButton = new Button
            {
                Text = "Restart",
                Width = 76,
                Height = 28,
                Margin = new Padding(left: 0, top: 2, right: 6, bottom: 0),
                AccessibleDescription =
                    "Restart this game client using its configured slot.",
            };
            MainWindowTheme.StyleButton(this.restartButton, primary: true);
            this.restartButton.Click += (_, _) =>
                restartRequested(this.ProcessId);

            this.forceCloseButton = new Button
            {
                Text = "Force close",
                Width = 88,
                Height = 28,
                Margin = new Padding(left: 0, top: 2, right: 0, bottom: 0),
                AccessibleDescription =
                    "Immediately close this game client.",
            };
            MainWindowTheme.StyleButton(
                this.forceCloseButton,
                danger: true);
            this.forceCloseButton.Click += (_, _) =>
                forceCloseRequested(this.ProcessId);

            var actionsPanel = new FlowLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                BackColor = Color.Transparent,
            };
            actionsPanel.Controls.Add(this.restartButton);
            actionsPanel.Controls.Add(this.forceCloseButton);

            var footer = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                Margin = Padding.Empty,
                RowCount = 1,
                BackColor = Color.Transparent,
            };
            footer.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, width: 100));
            footer.ColumnStyles.Add(
                new ColumnStyle(SizeType.AutoSize));
            footer.Controls.Add(this.footerLabel, column: 0, row: 0);
            footer.Controls.Add(actionsPanel, column: 1, row: 0);

            header.Controls.Add(this.titleLabel, column: 0, row: 0);
            header.Controls.Add(this.lifecycleLabel, column: 1, row: 0);

            root.Controls.Add(header, column: 0, row: 0);
            root.Controls.Add(this.configuredLaunchLabel, column: 0, row: 1);
            root.Controls.Add(this.observedIdentityLabel, column: 0, row: 2);
            root.Controls.Add(this.locationLabel, column: 0, row: 3);
            root.Controls.Add(this.routeLabel, column: 0, row: 4);
            root.Controls.Add(footer, column: 0, row: 5);

            this.Controls.Add(root);
        }

        public int ProcessId { get; }

        public void ApplyPresentation(
            RunningClientCardPresentation presentation)
        {
            this.AccentColor = presentation.AccentColor;
            SetLabelText(this.titleLabel, presentation.Title);
            SetLabelText(this.lifecycleLabel, presentation.LifecycleText);
            SetLabelText(
                this.configuredLaunchLabel,
                presentation.ConfiguredLaunchText);
            SetLabelText(
                this.observedIdentityLabel,
                presentation.ObservedIdentityText);
            SetLabelText(this.locationLabel, presentation.LocationText);
            SetLabelText(this.routeLabel, presentation.RouteText);
            SetLabelText(this.footerLabel, presentation.FooterText);

            this.restartButton.Visible =
                presentation.RestartToolTip != null;
            this.restartButton.Enabled =
                presentation.RestartToolTip != null;
            this.toolTip.SetToolTip(
                this.restartButton,
                presentation.RestartToolTip,
                delayMilliseconds: 250);
            this.toolTip.SetToolTip(
                this.forceCloseButton,
                presentation.ForceCloseToolTip,
                delayMilliseconds: 250);

            SetLabelColor(
                this.lifecycleLabel,
                presentation.AccentColor);
            SetLabelColor(
                this.observedIdentityLabel,
                presentation.ObservedIdentityColor);
            SetLabelColor(
                this.routeLabel,
                presentation.RouteColor);
        }

        private static void SetLabelColor(
            Label label,
            Color color)
        {
            if (label.ForeColor != color)
            {
                label.ForeColor = color;
            }
        }
    }

    private class DashboardCardPanel : Panel
    {
        private Color accentColor = MainWindowTheme.AccentBorder;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color AccentColor
        {
            get => this.accentColor;
            set
            {
                if (this.accentColor == value)
                {
                    return;
                }

                this.accentColor = value;
                this.Invalidate();
            }
        }

        public DashboardCardPanel()
        {
            this.BackColor = MainWindowTheme.ElevatedPanel;
            this.DoubleBuffered = true;
            this.ResizeRedraw = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            using var borderPen = new Pen(MainWindowTheme.Border);
            using var accentBrush = new SolidBrush(this.AccentColor);

            e.Graphics.DrawRectangle(
                borderPen,
                x: 0,
                y: 0,
                width: Math.Max(0, this.ClientSize.Width - 1),
                height: Math.Max(0, this.ClientSize.Height - 1));

            e.Graphics.FillRectangle(
                accentBrush,
                x: 0,
                y: 0,
                width: 4,
                height: this.ClientSize.Height);
        }
    }
}
