// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using System.Globalization;
using Net7ClientManager.Core;
using Net7ClientManager.Models;

internal sealed class LayoutEditorForm : ThemedForm
{
    private readonly ClientManager clientManager;
    private readonly LayoutDesignerControl layoutDesignerControl;
    private readonly Button editSlotButton;
    private readonly Button removeSlotButton;
    private readonly Label selectionLabel;
    private readonly System.Windows.Forms.Timer refreshTimer;

    public LayoutEditorForm(ClientManager clientManager)
    {
        this.clientManager = clientManager;

        this.Text = string.Concat(
            "Layout Editor · ",
            clientManager.CurrentProfile.Name);
        this.Icon = ResourceLoader.Net7ClientManagerIcon;
        this.StartPosition = FormStartPosition.CenterParent;
        this.MinimumSize = new Size(width: 980, height: 660);
        this.Size = new Size(width: 1280, height: 820);
        this.BackColor = MainWindowTheme.Background;
        this.ForeColor = MainWindowTheme.Text;
        this.Font = MainWindowTheme.CreateBodyFont();
        this.ConfigureWindowChrome(
            allowResize: true,
            showMinimizeButton: true,
            showMaximizeButton: true);
        this.ConfigureHelpTopic(HelpTopicIds.Fleet);
        this.ConfigureHelpTour(this.ShowHelpTour);

        this.layoutDesignerControl = new LayoutDesignerControl
        {
            Dock = DockStyle.Fill,
            Profile = clientManager.CurrentProfile,
            Clients = clientManager.Clients,
            Margin = new Padding(all: 0),
        };

        this.layoutDesignerControl.SelectedSlotChanged +=
            this.LayoutDesignerControl_OnSelectedSlotChanged;
        this.layoutDesignerControl.SlotBoundsChanged +=
            this.LayoutDesignerControl_OnSlotBoundsChanged;

        var addSlotButton = new Button
        {
            Text = "+ Add slot",
            Width = 100,
            Height = 32,
        };
        MainWindowTheme.StyleButton(addSlotButton, primary: true);
        addSlotButton.Click += this.AddSlotButton_OnClick;

        this.editSlotButton = new Button
        {
            Text = "Edit selected",
            Width = 112,
            Height = 32,
        };
        MainWindowTheme.StyleButton(this.editSlotButton);
        this.editSlotButton.Click += this.EditSlotButton_OnClick;

        this.removeSlotButton = new Button
        {
            Text = "Remove selected",
            Width = 130,
            Height = 32,
        };
        MainWindowTheme.StyleButton(this.removeSlotButton, danger: true);
        this.removeSlotButton.Click += this.RemoveSlotButton_OnClick;

        var resetViewButton = new Button
        {
            Text = "Reset view",
            Width = 100,
            Height = 32,
        };
        MainWindowTheme.StyleButton(resetViewButton);
        resetViewButton.Click += (_, _) =>
            this.layoutDesignerControl.ResetView();

        var closeButton = new Button
        {
            Text = "Done",
            Width = 88,
            Height = 32,
            DialogResult = DialogResult.OK,
        };
        MainWindowTheme.StyleButton(closeButton, primary: true);

        this.selectionLabel = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            ForeColor = MainWindowTheme.MutedText,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(left: 4, top: 0, right: 8, bottom: 0),
        };

        this.AcceptButton = closeButton;
        this.CancelButton = closeButton;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = MainWindowTheme.Background,
        };

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, height: 82));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, height: 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, height: 48));

        root.Controls.Add(
            this.CreateHeader(
                addSlotButton,
                this.editSlotButton,
                this.removeSlotButton,
                resetViewButton,
                closeButton),
            column: 0,
            row: 0);

        var canvasHost = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(left: 16, top: 0, right: 16, bottom: 10),
            BackColor = MainWindowTheme.Background,
        };
        canvasHost.Controls.Add(this.layoutDesignerControl);
        root.Controls.Add(canvasHost, column: 0, row: 1);

        var footer = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(left: 16, top: 0, right: 16, bottom: 8),
            BackColor = MainWindowTheme.Background,
        };
        footer.Controls.Add(this.selectionLabel);
        root.Controls.Add(footer, column: 0, row: 2);

        this.Controls.Add(root);

        this.refreshTimer = new System.Windows.Forms.Timer
        {
            Interval = 1000,
        };
        this.refreshTimer.Tick += this.RefreshTimer_OnTick;
        this.refreshTimer.Start();

        this.layoutDesignerControl.SelectSlot(
            clientManager.CurrentProfile.Slots.FirstOrDefault());
        this.UpdateSelectionState();
    }


    internal void ShowHelpTour()
    {
        GuidedTourOverlay.Show(
            this,
            [
                new GuidedTourStep(
                    () => this.layoutDesignerControl,
                    "Place the fleet on your monitors",
                    "Each rectangle is one complete hosted client. Drag slots to the screen and position where that window should open every time."),
                new GuidedTourStep(
                    () => this.layoutDesignerControl,
                    "The rectangle matches the real footprint",
                    "Always show includes the Client Manager title bar and snapping keeps the next row clear. Hide and Show on hover use only the Earth & Beyond game area because the hover title bar temporarily overlays it."),
                new GuidedTourStep(
                    () => this.editSlotButton,
                    "Fine-tune the selected client",
                    "Edit selected opens the exact account, character, title-bar mode and hover delay, position, resolution, automatic-login, and automatic-character choices for this slot."),
                new GuidedTourStep(
                    () => this.selectionLabel,
                    "Check the result",
                    "The footer describes the selected slot and its current position. Changes are saved to the active profile as you work."),
            ]);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        this.refreshTimer.Stop();
        this.refreshTimer.Tick -= this.RefreshTimer_OnTick;
        this.refreshTimer.Dispose();

        this.layoutDesignerControl.SelectedSlotChanged -=
            this.LayoutDesignerControl_OnSelectedSlotChanged;
        this.layoutDesignerControl.SlotBoundsChanged -=
            this.LayoutDesignerControl_OnSlotBoundsChanged;

        base.OnFormClosed(e);
    }

    private Control CreateHeader(
        Button addSlotButton,
        Button editSlotButton,
        Button removeSlotButton,
        Button resetViewButton,
        Button closeButton)
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(left: 18, top: 12, right: 18, bottom: 10),
            ColumnCount = 2,
            RowCount = 1,
            BackColor = MainWindowTheme.Header,
        };

        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, width: 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var textPanel = new Panel
        {
            Dock = DockStyle.Fill,
        };

        var titleLabel = new Label
        {
            Text = "Arrange client slots",
            AutoSize = true,
            Font = MainWindowTheme.CreateHeadingFont(size: 15.0f),
            ForeColor = MainWindowTheme.Text,
            Location = new Point(x: 0, y: 0),
        };

        var subtitleLabel = new Label
        {
            Text = string.Concat(
                this.clientManager.CurrentProfile.Name,
                " · drag slots to place them, use the mouse wheel to zoom, and middle-drag to pan"),
            AutoSize = true,
            ForeColor = MainWindowTheme.MutedText,
            Location = new Point(x: 1, y: 32),
        };

        textPanel.Controls.Add(titleLabel);
        textPanel.Controls.Add(subtitleLabel);

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(left: 12, top: 8, right: 0, bottom: 0),
        };

        buttons.Controls.Add(addSlotButton);
        buttons.Controls.Add(editSlotButton);
        buttons.Controls.Add(removeSlotButton);
        buttons.Controls.Add(resetViewButton);
        buttons.Controls.Add(closeButton);

        header.Controls.Add(textPanel, column: 0, row: 0);
        header.Controls.Add(buttons, column: 1, row: 0);

        return header;
    }

    private void RefreshTimer_OnTick(
        object? sender,
        EventArgs e)
    {
        this.layoutDesignerControl.Clients = this.clientManager.Clients;
    }

    private void LayoutDesignerControl_OnSelectedSlotChanged(
        object? sender,
        SelectedSlotChangedEventArgs e)
    {
        this.UpdateSelectionState();
    }

    private void LayoutDesignerControl_OnSlotBoundsChanged(
        object? sender,
        SlotBoundsChangedEventArgs e)
    {
        this.clientManager.SaveSettings();

        var assignedClient = this.clientManager.Clients.FirstOrDefault(
            client => client.AssignedSlotId == e.Slot.Id);

        assignedClient?.HostForm?.ApplySlot(e.Slot);
        this.UpdateSelectionState();
    }

    private void AddSlotButton_OnClick(
        object? sender,
        EventArgs e)
    {
        var screenBounds =
            this.layoutDesignerControl.GetDefaultSlotScreenBounds();
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
                this.clientManager.CurrentProfile.Slots),
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
            isNew: true);

        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        this.clientManager.CurrentProfile.Slots.Add(slot);
        this.clientManager.ReconcileClientsToCurrentProfile();

        this.layoutDesignerControl.Profile =
            this.clientManager.CurrentProfile;
        this.layoutDesignerControl.Clients =
            this.clientManager.Clients;
        this.layoutDesignerControl.SelectSlot(slot);
        this.UpdateSelectionState();
    }

    private void EditSlotButton_OnClick(
        object? sender,
        EventArgs e)
    {
        var slot = this.layoutDesignerControl.SelectedSlot;

        if (slot != null)
        {
            this.EditSlot(slot);
        }
    }

    private void EditSlot(ClientSlot slot)
    {
        using var form = new SlotEditorForm(
            this.clientManager,
            slot);

        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        this.clientManager.ReconcileClientsToCurrentProfile();

        this.layoutDesignerControl.Profile =
            this.clientManager.CurrentProfile;
        this.layoutDesignerControl.Clients =
            this.clientManager.Clients;
        this.layoutDesignerControl.SelectSlot(slot);
        this.UpdateSelectionState();
    }

    private void RemoveSlotButton_OnClick(
        object? sender,
        EventArgs e)
    {
        var slot = this.layoutDesignerControl.SelectedSlot;

        if (slot == null)
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

        this.clientManager.CurrentProfile.Slots.Remove(slot);

        foreach (var client in this.clientManager.Clients.Where(
                     client => client.AssignedSlotId == slot.Id))
        {
            client.AssignedSlotId = null;
            client.HostForm?.SetUnassignedTitle();
        }

        this.clientManager.ReconcileClientsToCurrentProfile();
        this.layoutDesignerControl.Profile =
            this.clientManager.CurrentProfile;
        this.layoutDesignerControl.Clients =
            this.clientManager.Clients;
        this.layoutDesignerControl.SelectSlot(
            this.clientManager.CurrentProfile.Slots.FirstOrDefault());
        this.UpdateSelectionState();
    }

    private void UpdateSelectionState()
    {
        var slot = this.layoutDesignerControl.SelectedSlot;
        var hasSelection = slot != null;

        this.editSlotButton.Enabled = hasSelection;
        this.removeSlotButton.Enabled = hasSelection;

        if (slot == null)
        {
            this.selectionLabel.Text =
                "Select a slot to edit its settings. Double-clicking empty space resets the view.";
            return;
        }

        var hostedHeight =
            HostedClientWindowMetrics.GetHostedWindowHeight(slot);
        var titleBarText =
            HostedClientWindowMetrics.GetTitleBarModeText(slot);

        var hoverDelayText =
            slot.EffectiveTitleBarMode == ClientTitleBarMode.OnHover
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $" after {slot.TitleBarHoverDelaySeconds:0.##}s")
                : "";

        this.selectionLabel.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"Selected: {slot.Name} · {slot.Bounds.Width}×{hostedHeight} · {titleBarText}{hoverDelayText} · at {slot.Bounds.Left}, {slot.Bounds.Top}");
    }
}
