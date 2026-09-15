// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using System.Globalization;
using System.Runtime.InteropServices;
using Net7ClientManager.Core;
using Net7ClientManager.Models;
using Net7ClientManager.Services;

internal sealed record CommandPaletteFixedPosition(
    double X,
    double Y);

internal sealed record InGameOptionsValues(
    CommandPaletteShowMode ShowMode,
    CommandPalettePlacementMode PlacementMode,
    Keys HotKey,
    Keys FleetFireAllHotKey,
    CommandPaletteFixedPosition FixedPosition,
    bool MissionWikiEnabled,
    bool RecordMissionHistory,
    bool RecordActivityHistory,
    bool RecordCombatHistory,
    bool UseWormholes,
    bool KeepGalaxyFinderSearchOpen,
    bool ShowVendorCompanion,
    bool ShowBuffDurations,
    bool EnhancedItemToolTipsEnabled,
    int ItemToolTipHorizontalOffset,
    int ItemToolTipVerticalOffset);

internal sealed class InGameOptionsForm : ThemedForm
{
    private readonly InGameOptionsValues initialValues;
    private readonly Func<Keys, CancellationToken, Task<string?>>
        validateHotKeyAsync;
    private readonly Func<Keys, CancellationToken, Task<string?>>
        validateFleetFireAllHotKeyAsync;
    private readonly Func<IWin32Window, CommandPaletteFixedPosition?>
        pickFixedPosition;
    private readonly Action<bool, int, int>
        previewGameItemToolTipSettings;
    private readonly Func<InGameOptionsValues, string?> applySettings;
    private readonly Func<string> createDiagnostics;
    private readonly CancellationTokenSource lifetimeCancellation = new();

    private readonly ComboBox showModeComboBox = new();
    private readonly ComboBox placementComboBox = new();
    private readonly TextBox shortcutTextBox = new();
    private readonly Button changeButton = new();
    private readonly TextBox fleetFireAllShortcutTextBox = new();
    private readonly Button fleetFireAllChangeButton = new();
    private readonly Button fleetFireAllClearButton = new();
    private readonly Button setPositionButton = new();
    private readonly ThemedCheckBox missionWikiCheckBox = new();
    private readonly ThemedCheckBox missionHistoryCheckBox = new();
    private readonly ThemedCheckBox activityHistoryCheckBox = new();
    private readonly ThemedCheckBox combatHistoryCheckBox = new();
    private readonly ThemedCheckBox useWormholesCheckBox = new();
    private readonly ThemedCheckBox keepGalaxyFinderSearchOpenCheckBox = new();
    private readonly ThemedCheckBox showVendorCompanionCheckBox = new();
    private readonly ThemedCheckBox showBuffDurationsCheckBox = new();
    private readonly ThemedCheckBox enhancedItemToolTipsCheckBox = new();
    private readonly TextBox horizontalOffsetTextBox = new();
    private readonly TextBox verticalOffsetTextBox = new();
    private readonly Button horizontalDecreaseButton = new();
    private readonly Button horizontalIncreaseButton = new();
    private readonly Button verticalDecreaseButton = new();
    private readonly Button verticalIncreaseButton = new();
    private readonly Button resetItemToolTipOffsetsButton = new();
    private readonly Button copyDiagnosticsButton = new();
    private readonly Label statusLabel = new();
    private readonly Button applyButton = new();
    private readonly Button cancelButton = new();

    private Keys selectedHotKey;
    private Keys selectedFleetFireAllHotKey;
    private CommandPaletteFixedPosition selectedFixedPosition;
    private int selectedHorizontalOffset;
    private int selectedVerticalOffset;
    private bool capturing;
    private bool capturingFleetFireAllHotKey;
    private bool applying;
    private bool suppressItemToolTipPreview;
    private bool settingsApplied;

    internal InGameOptionsForm(
        InGameOptionsValues initialValues,
        Func<Keys, CancellationToken, Task<string?>> validateHotKeyAsync,
        Func<Keys, CancellationToken, Task<string?>>
            validateFleetFireAllHotKeyAsync,
        Func<IWin32Window, CommandPaletteFixedPosition?>
            pickFixedPosition,
        Action<bool, int, int>
            previewGameItemToolTipSettings,
        Func<InGameOptionsValues, string?> applySettings,
        Func<string> createDiagnostics)
    {
        this.initialValues = initialValues;
        this.selectedHotKey = initialValues.HotKey;
        this.selectedFleetFireAllHotKey =
            initialValues.FleetFireAllHotKey;
        this.selectedFixedPosition = initialValues.FixedPosition;
        this.selectedHorizontalOffset =
            initialValues.ItemToolTipHorizontalOffset;
        this.selectedVerticalOffset =
            initialValues.ItemToolTipVerticalOffset;
        this.validateHotKeyAsync = validateHotKeyAsync;
        this.validateFleetFireAllHotKeyAsync =
            validateFleetFireAllHotKeyAsync;
        this.pickFixedPosition = pickFixedPosition;
        this.previewGameItemToolTipSettings =
            previewGameItemToolTipSettings;
        this.applySettings = applySettings;
        this.createDiagnostics = createDiagnostics;

        this.Text = "In-Game Options";
        this.Icon = ResourceLoader.Net7ClientManagerIcon;
        this.StartPosition = FormStartPosition.CenterParent;
        this.ClientSize = new Size(width: 650, height: 924);
        this.MinimumSize = this.Size;
        this.MaximumSize = this.Size;
        this.BackColor = MainWindowTheme.Background;
        this.ForeColor = MainWindowTheme.Text;
        this.Font = MainWindowTheme.CreateBodyFont();
        this.KeyPreview = true;
        this.ConfigureWindowChrome(
            allowResize: false,
            showMinimizeButton: false,
            showMaximizeButton: false);
        this.ConfigureHelpTopic(HelpTopicIds.InGameTools);
        this.ConfigureHelpTour(this.ShowHelpTour);

        this.BuildUi();
        this.WireEvents();
        this.RefreshState();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        this.showModeComboBox.Focus();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        this.lifetimeCancellation.Cancel();

        if (!this.settingsApplied)
        {
            this.previewGameItemToolTipSettings(
                this.initialValues.EnhancedItemToolTipsEnabled,
                this.initialValues.ItemToolTipHorizontalOffset,
                this.initialValues.ItemToolTipVerticalOffset);
        }

        this.showModeComboBox.SelectedIndexChanged -=
            this.OptionsControl_OnChanged;
        this.placementComboBox.SelectedIndexChanged -=
            this.OptionsControl_OnChanged;
        this.missionWikiCheckBox.CheckedChanged -=
            this.OptionsControl_OnChanged;
        this.missionHistoryCheckBox.CheckedChanged -=
            this.OptionsControl_OnChanged;
        this.activityHistoryCheckBox.CheckedChanged -=
            this.OptionsControl_OnChanged;
        this.combatHistoryCheckBox.CheckedChanged -=
            this.OptionsControl_OnChanged;
        this.useWormholesCheckBox.CheckedChanged -=
            this.OptionsControl_OnChanged;
        this.keepGalaxyFinderSearchOpenCheckBox.CheckedChanged -=
            this.OptionsControl_OnChanged;
        this.showVendorCompanionCheckBox.CheckedChanged -=
            this.OptionsControl_OnChanged;
        this.showBuffDurationsCheckBox.CheckedChanged -=
            this.OptionsControl_OnChanged;
        this.enhancedItemToolTipsCheckBox.CheckedChanged -=
            this.ItemToolTipOption_OnChanged;
        this.horizontalOffsetTextBox.TextChanged -=
            this.ItemToolTipOffsetTextBox_OnTextChanged;
        this.verticalOffsetTextBox.TextChanged -=
            this.ItemToolTipOffsetTextBox_OnTextChanged;
        this.horizontalOffsetTextBox.Leave -=
            this.ItemToolTipOffsetTextBox_OnLeave;
        this.verticalOffsetTextBox.Leave -=
            this.ItemToolTipOffsetTextBox_OnLeave;
        this.horizontalDecreaseButton.Click -=
            this.HorizontalDecreaseButton_OnClick;
        this.horizontalIncreaseButton.Click -=
            this.HorizontalIncreaseButton_OnClick;
        this.verticalDecreaseButton.Click -=
            this.VerticalDecreaseButton_OnClick;
        this.verticalIncreaseButton.Click -=
            this.VerticalIncreaseButton_OnClick;
        this.resetItemToolTipOffsetsButton.Click -=
            this.ResetItemToolTipOffsetsButton_OnClick;
        this.copyDiagnosticsButton.Click -=
            this.CopyDiagnosticsButton_OnClick;
        this.changeButton.Click -= this.ChangeButton_OnClick;
        this.fleetFireAllChangeButton.Click -=
            this.FleetFireAllChangeButton_OnClick;
        this.fleetFireAllClearButton.Click -=
            this.FleetFireAllClearButton_OnClick;
        this.setPositionButton.Click -= this.SetPositionButton_OnClick;
        this.applyButton.Click -= this.ApplyButton_OnClick;
        this.cancelButton.Click -= this.CancelButton_OnClick;
        this.KeyDown -= this.InGameOptionsForm_OnKeyDown;

        this.lifetimeCancellation.Dispose();
        base.OnFormClosed(e);
    }


    internal void ShowHelpTour()
    {
        GuidedTourOverlay.Show(
            this,
            [
                new GuidedTourStep(
                    () => this.showModeComboBox,
                    "Choose how the Client Manager menu opens",
                    "Set whether the in-game menu is always available, appears on hover, or stays hidden until its shortcut is used."),
                new GuidedTourStep(
                    () => this.shortcutTextBox,
                    "Set the Command Palette shortcut",
                    "The Command Palette gives fast keyboard access to Client Manager actions while the game has focus."),
                new GuidedTourStep(
                    () => this.fleetFireAllShortcutTextBox,
                    "Fire the whole fleet with one shortcut",
                    "Fleet Fire All fires the foreground pilot first, then makes eligible followers acquire that pilot's target and fire."),
                new GuidedTourStep(
                    () => this.missionWikiCheckBox,
                    "Turn in-game helpers on or off",
                    "Mission Wiki, Finder behaviour, vendor assistance, buff durations, and enhanced item tooltips can be enabled independently for the active game experience."),
                new GuidedTourStep(
                    () => this.missionHistoryCheckBox,
                    "Choose what Pilot Archive remembers",
                    "Mission, activity, and combat history recording can be controlled here. The resulting histories remain available after the client closes."),
                new GuidedTourStep(
                    () => this.showBuffDurationsCheckBox,
                    "Keep an eye on active buffs",
                    "Buff durations stay visible over the sixteen buff slots. Hover a duration for the effect details Client Manager can resolve."),
                new GuidedTourStep(
                    () => this.enhancedItemToolTipsCheckBox,
                    "Replace cramped native item details",
                    "Enhanced tooltips cover inventory, vault, loot, and equipped items. Use the offsets below to place them comfortably beside the game UI."),
                new GuidedTourStep(
                    () => this.applyButton,
                    "Apply to the active game client",
                    "Apply saves these in-game choices and refreshes the relevant Client Manager surfaces without restarting the game."),
            ]);
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 7,
            BackColor = MainWindowTheme.Background,
            Padding = new Padding(18, 10, 18, 14),
            Margin = Padding.Empty,
        };

        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 280f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 88f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 138f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 94f));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54f));

        root.Controls.Add(
            new Label
            {
                Dock = DockStyle.Fill,
                Text = "Configure features that are used while a game client is running.",
                ForeColor = MainWindowTheme.MutedText,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = Padding.Empty,
            },
            0,
            0);

        root.Controls.Add(this.CreateCommandPalettePanel(), 0, 1);
        root.Controls.Add(this.CreateAddonPanel(), 0, 2);
        root.Controls.Add(this.CreateGalaxyFinderPanel(), 0, 3);
        root.Controls.Add(this.CreateBuffDurationsPanel(), 0, 4);
        root.Controls.Add(this.CreateLowerOptionsPanel(), 0, 5);
        root.Controls.Add(this.CreateFooter(), 0, 6);

        this.Controls.Add(root);
    }

    private Control CreateCommandPalettePanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = MainWindowTheme.Panel,
            Padding = new Padding(18, 10, 18, 10),
            Margin = new Padding(0, 0, 0, 10),
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 7,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };

        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var heading = new Label
        {
            Dock = DockStyle.Fill,
            Text = "COMMANDS & SHORTCUTS",
            Font = MainWindowTheme.CreateHeadingFont(9.0f),
            ForeColor = MainWindowTheme.Accent,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = Padding.Empty,
        };
        layout.SetColumnSpan(heading, 4);
        layout.Controls.Add(heading, 0, 0);

        this.ConfigureComboBox(this.showModeComboBox);
        this.showModeComboBox.Items.AddRange(
        [
            new ShowModeItem(CommandPaletteShowMode.Never, "Never"),
            new ShowModeItem(CommandPaletteShowMode.Always, "Always"),
            new ShowModeItem(CommandPaletteShowMode.Keybinding, "Keybinding"),
        ]);
        this.showModeComboBox.SelectedItem =
            this.showModeComboBox.Items
                .Cast<ShowModeItem>()
                .First(item => item.Value == this.initialValues.ShowMode);

        layout.Controls.Add(this.CreateFieldLabel("Show"), 0, 1);
        layout.Controls.Add(this.showModeComboBox, 1, 1);

        this.ConfigureComboBox(this.placementComboBox);
        this.placementComboBox.Items.AddRange(
        [
            new PlacementModeItem(CommandPalettePlacementMode.Cursor, "On cursor"),
            new PlacementModeItem(CommandPalettePlacementMode.Fixed, "Fixed location"),
        ]);
        this.placementComboBox.SelectedItem =
            this.placementComboBox.Items
                .Cast<PlacementModeItem>()
                .First(item => item.Value == this.initialValues.PlacementMode);

        layout.Controls.Add(this.CreateFieldLabel("Open at"), 0, 2);
        layout.Controls.Add(this.placementComboBox, 1, 2);

        this.setPositionButton.Text = "Set position";
        this.setPositionButton.Dock = DockStyle.Fill;
        this.setPositionButton.Margin = new Padding(8, 4, 0, 4);
        MainWindowTheme.StyleButton(this.setPositionButton);
        layout.Controls.Add(this.setPositionButton, 2, 2);

        this.shortcutTextBox.Dock = DockStyle.Fill;
        this.shortcutTextBox.ReadOnly = true;
        this.shortcutTextBox.TabStop = false;
        this.shortcutTextBox.TextAlign = HorizontalAlignment.Center;
        this.shortcutTextBox.Margin = new Padding(0, 6, 0, 5);
        MainWindowTheme.StyleTextBox(this.shortcutTextBox);

        this.changeButton.Text = "Change";
        this.changeButton.Dock = DockStyle.Fill;
        this.changeButton.Margin = new Padding(8, 4, 0, 4);
        MainWindowTheme.StyleButton(this.changeButton);

        layout.Controls.Add(this.CreateFieldLabel("Shortcut"), 0, 3);
        layout.Controls.Add(this.shortcutTextBox, 1, 3);
        layout.Controls.Add(this.changeButton, 2, 3);

        this.fleetFireAllShortcutTextBox.Dock = DockStyle.Fill;
        this.fleetFireAllShortcutTextBox.ReadOnly = true;
        this.fleetFireAllShortcutTextBox.TabStop = false;
        this.fleetFireAllShortcutTextBox.TextAlign =
            HorizontalAlignment.Center;
        this.fleetFireAllShortcutTextBox.Margin =
            new Padding(0, 6, 0, 5);
        MainWindowTheme.StyleTextBox(
            this.fleetFireAllShortcutTextBox);

        this.fleetFireAllChangeButton.Text = "Change";
        this.fleetFireAllChangeButton.Dock = DockStyle.Fill;
        this.fleetFireAllChangeButton.Margin =
            new Padding(8, 4, 0, 4);
        MainWindowTheme.StyleButton(this.fleetFireAllChangeButton);

        this.fleetFireAllClearButton.Text = "Clear";
        this.fleetFireAllClearButton.Size =
            new Size(width: 72, height: 26);
        this.fleetFireAllClearButton.Anchor = AnchorStyles.Left;
        this.fleetFireAllClearButton.Margin =
            new Padding(8, 4, 0, 4);
        MainWindowTheme.StyleButton(this.fleetFireAllClearButton);

        layout.Controls.Add(
            this.CreateFieldLabel("Fleet fire all"),
            0,
            4);
        layout.Controls.Add(
            this.fleetFireAllShortcutTextBox,
            1,
            4);
        layout.Controls.Add(
            this.fleetFireAllChangeButton,
            2,
            4);
        layout.Controls.Add(
            this.fleetFireAllClearButton,
            3,
            4);

        var behaviorLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Fixed positions are remembered relative to the active game window. In Always mode, drag a section heading to move the palette.",
            ForeColor = MainWindowTheme.MutedText,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Margin = Padding.Empty,
        };
        layout.SetColumnSpan(behaviorLabel, 4);
        layout.Controls.Add(behaviorLabel, 0, 5);

        this.statusLabel.Dock = DockStyle.Fill;
        this.statusLabel.ForeColor = MainWindowTheme.MutedText;
        this.statusLabel.TextAlign = ContentAlignment.TopLeft;
        this.statusLabel.AutoEllipsis = true;
        this.statusLabel.Margin = new Padding(0, 3, 0, 0);
        layout.SetColumnSpan(this.statusLabel, 4);
        layout.Controls.Add(this.statusLabel, 0, 6);

        panel.Controls.Add(layout);
        return panel;
    }

    private Control CreateAddonPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = MainWindowTheme.Panel,
            Padding = new Padding(18, 10, 18, 10),
            Margin = new Padding(0, 0, 0, 10),
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        layout.Controls.Add(
            new Label
            {
                Dock = DockStyle.Fill,
                Text = "ADD-ONS",
                Font = MainWindowTheme.CreateHeadingFont(9.0f),
                ForeColor = MainWindowTheme.Accent,
                TextAlign = ContentAlignment.MiddleLeft,
            },
            0,
            0);

        this.missionWikiCheckBox.Text =
            "Mission Wiki: show mission details and Net-7 Wiki guidance";
        this.missionWikiCheckBox.Checked = this.initialValues.MissionWikiEnabled;
        this.missionWikiCheckBox.Dock = DockStyle.Top;
        layout.Controls.Add(this.missionWikiCheckBox, 0, 1);

        panel.Controls.Add(layout);
        return panel;
    }

    private Control CreateGalaxyFinderPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = MainWindowTheme.Panel,
            Padding = new Padding(18, 10, 18, 10),
            Margin = new Padding(0, 0, 0, 10),
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));

        layout.Controls.Add(
            new Label
            {
                Dock = DockStyle.Fill,
                Text = "NAVIGATION & GALAXY FINDER",
                Font = MainWindowTheme.CreateHeadingFont(9.0f),
                ForeColor = MainWindowTheme.Accent,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = Padding.Empty,
            },
            0,
            0);

        this.useWormholesCheckBox.Text =
            "Use wormholes when planning routes";
        this.useWormholesCheckBox.Checked =
            this.initialValues.UseWormholes;
        this.useWormholesCheckBox.Dock = DockStyle.Fill;
        layout.Controls.Add(
            this.useWormholesCheckBox,
            0,
            1);

        this.keepGalaxyFinderSearchOpenCheckBox.Text =
            "Keep Search open in its own tab";
        this.keepGalaxyFinderSearchOpenCheckBox.Checked =
            this.initialValues.KeepGalaxyFinderSearchOpen;
        this.keepGalaxyFinderSearchOpenCheckBox.Dock = DockStyle.Fill;
        layout.Controls.Add(
            this.keepGalaxyFinderSearchOpenCheckBox,
            0,
            2);

        this.showVendorCompanionCheckBox.Text =
            "Show shopping-list purchases above open vendors";
        this.showVendorCompanionCheckBox.Checked =
            this.initialValues.ShowVendorCompanion;
        this.showVendorCompanionCheckBox.Dock = DockStyle.Fill;
        layout.Controls.Add(
            this.showVendorCompanionCheckBox,
            0,
            3);

        panel.Controls.Add(layout);
        return panel;
    }

    private Control CreateLowerOptionsPanel()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };

        layout.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 50f));
        layout.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 50f));
        layout.RowStyles.Add(
            new RowStyle(SizeType.Percent, 100f));

        var historyPanel = this.CreateHistoryPanel();
        historyPanel.Margin = new Padding(0, 0, 5, 0);

        var itemToolTipsPanel = this.CreateItemToolTipsPanel();
        itemToolTipsPanel.Margin = new Padding(5, 0, 0, 0);

        layout.Controls.Add(historyPanel, 0, 0);
        layout.Controls.Add(itemToolTipsPanel, 1, 0);

        return layout;
    }

    private Control CreateBuffDurationsPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = MainWindowTheme.Panel,
            Padding = new Padding(18, 10, 18, 10),
            Margin = new Padding(0, 0, 0, 10),
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));

        layout.Controls.Add(
            new Label
            {
                Dock = DockStyle.Fill,
                Text = "BUFF DURATIONS",
                Font = MainWindowTheme.CreateHeadingFont(9.0f),
                ForeColor = MainWindowTheme.Accent,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = Padding.Empty,
            },
            0,
            0);

        this.showBuffDurationsCheckBox.Text =
            "Show buff durations";
        this.showBuffDurationsCheckBox.Checked =
            this.initialValues.ShowBuffDurations;
        this.showBuffDurationsCheckBox.Dock = DockStyle.Fill;
        layout.Controls.Add(
            this.showBuffDurationsCheckBox,
            0,
            1);

        panel.Controls.Add(layout);
        return panel;
    }

    private Control CreateHistoryPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = MainWindowTheme.Panel,
            Padding = new Padding(18, 10, 18, 10),
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        layout.Controls.Add(
            new Label
            {
                Dock = DockStyle.Fill,
                Text = "HISTORY",
                Font = MainWindowTheme.CreateHeadingFont(9.0f),
                ForeColor = MainWindowTheme.Accent,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = Padding.Empty,
            },
            0,
            0);

        this.missionHistoryCheckBox.Text = "Record Mission History";
        this.missionHistoryCheckBox.Checked =
            this.initialValues.RecordMissionHistory;
        this.missionHistoryCheckBox.Dock = DockStyle.Fill;
        layout.Controls.Add(this.missionHistoryCheckBox, 0, 1);

        this.activityHistoryCheckBox.Text = "Record Activity History";
        this.activityHistoryCheckBox.Checked =
            this.initialValues.RecordActivityHistory;
        this.activityHistoryCheckBox.Dock = DockStyle.Fill;
        layout.Controls.Add(this.activityHistoryCheckBox, 0, 2);

        this.combatHistoryCheckBox.Text = "Record Combat History";
        this.combatHistoryCheckBox.Checked =
            this.initialValues.RecordCombatHistory;
        this.combatHistoryCheckBox.Dock = DockStyle.Fill;
        layout.Controls.Add(this.combatHistoryCheckBox, 0, 3);

        panel.Controls.Add(layout);
        return panel;
    }

    private Control CreateItemToolTipsPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = MainWindowTheme.Panel,
            Padding = new Padding(18, 10, 18, 10),
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        layout.Controls.Add(
            new Label
            {
                Dock = DockStyle.Fill,
                Text = "ITEM TOOLTIPS",
                Font = MainWindowTheme.CreateHeadingFont(9.0f),
                ForeColor = MainWindowTheme.Accent,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = Padding.Empty,
            },
            0,
            0);

        this.enhancedItemToolTipsCheckBox.Text =
            "Show enhanced item tooltips";
        this.enhancedItemToolTipsCheckBox.Checked =
            this.initialValues.EnhancedItemToolTipsEnabled;
        this.enhancedItemToolTipsCheckBox.Dock = DockStyle.Fill;
        layout.Controls.Add(
            this.enhancedItemToolTipsCheckBox,
            0,
            1);

        this.ConfigureOffsetTextBox(
            this.horizontalOffsetTextBox,
            this.selectedHorizontalOffset);
        this.ConfigureOffsetTextBox(
            this.verticalOffsetTextBox,
            this.selectedVerticalOffset);

        layout.Controls.Add(
            this.CreateOffsetRow(
                "Horizontal",
                this.horizontalDecreaseButton,
                this.horizontalOffsetTextBox,
                this.horizontalIncreaseButton),
            0,
            2);
        layout.Controls.Add(
            this.CreateOffsetRow(
                "Vertical",
                this.verticalDecreaseButton,
                this.verticalOffsetTextBox,
                this.verticalIncreaseButton),
            0,
            3);

        var resetRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = new Padding(0, 5, 0, 0),
        };

        this.resetItemToolTipOffsetsButton.Text = "Reset";
        this.resetItemToolTipOffsetsButton.Size =
            new Size(width: 70, height: 28);
        this.resetItemToolTipOffsetsButton.Margin = Padding.Empty;
        MainWindowTheme.StyleButton(
            this.resetItemToolTipOffsetsButton);
        resetRow.Controls.Add(this.resetItemToolTipOffsetsButton);
        layout.Controls.Add(resetRow, 0, 4);

        panel.Controls.Add(layout);
        return panel;
    }

    private Control CreateOffsetRow(
        string labelText,
        Button decreaseButton,
        TextBox valueTextBox,
        Button increaseButton)
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };

        row.ColumnStyles.Add(
            new ColumnStyle(SizeType.Absolute, 82f));
        row.ColumnStyles.Add(
            new ColumnStyle(SizeType.Absolute, 30f));
        row.ColumnStyles.Add(
            new ColumnStyle(SizeType.Absolute, 64f));
        row.ColumnStyles.Add(
            new ColumnStyle(SizeType.Absolute, 30f));
        row.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 100f));
        row.RowStyles.Add(
            new RowStyle(SizeType.Percent, 100f));

        row.Controls.Add(
            new Label
            {
                Dock = DockStyle.Fill,
                Text = labelText,
                ForeColor = MainWindowTheme.MutedText,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = Padding.Empty,
            },
            0,
            0);

        ConfigureNudgeButton(decreaseButton, "-");
        ConfigureNudgeButton(increaseButton, "+");

        row.Controls.Add(decreaseButton, 1, 0);
        row.Controls.Add(valueTextBox, 2, 0);
        row.Controls.Add(increaseButton, 3, 0);
        row.Controls.Add(
            new Label
            {
                Dock = DockStyle.Fill,
                Text = "px",
                ForeColor = MainWindowTheme.MutedText,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(5, 0, 0, 0),
            },
            4,
            0);

        return row;
    }

    private void ConfigureOffsetTextBox(
        TextBox textBox,
        int value)
    {
        textBox.Dock = DockStyle.Fill;
        textBox.Text = value.ToString(CultureInfo.InvariantCulture);
        textBox.TextAlign = HorizontalAlignment.Center;
        textBox.MaxLength = 4;
        textBox.Margin = new Padding(2, 7, 2, 5);
        MainWindowTheme.StyleTextBox(textBox);
    }

    private static void ConfigureNudgeButton(
        Button button,
        string text)
    {
        button.Text = text;
        button.Dock = DockStyle.Fill;
        button.Margin = new Padding(2, 5, 2, 4);
        MainWindowTheme.StyleButton(button);
    }

    private Control CreateFooter()
    {
        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(0, 10, 0, 8),
            Margin = Padding.Empty,
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var diagnosticsArea = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };

        this.copyDiagnosticsButton.Text = "Copy diagnostics";
        this.copyDiagnosticsButton.Size = new Size(width: 132, height: 34);
        this.copyDiagnosticsButton.Margin = Padding.Empty;
        MainWindowTheme.StyleButton(this.copyDiagnosticsButton);
        diagnosticsArea.Controls.Add(this.copyDiagnosticsButton);

        var actionArea = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };

        this.applyButton.Text = "Apply";
        this.applyButton.Size = new Size(width: 100, height: 34);
        this.applyButton.Margin = new Padding(6, 0, 0, 0);
        MainWindowTheme.StyleButton(this.applyButton, primary: true);

        this.cancelButton.Text = "Cancel";
        this.cancelButton.Size = new Size(width: 100, height: 34);
        this.cancelButton.Margin = Padding.Empty;
        MainWindowTheme.StyleButton(this.cancelButton);

        actionArea.Controls.Add(this.applyButton);
        actionArea.Controls.Add(this.cancelButton);
        footer.Controls.Add(diagnosticsArea, 0, 0);
        footer.Controls.Add(actionArea, 1, 0);
        return footer;
    }

    private void WireEvents()
    {
        this.showModeComboBox.SelectedIndexChanged +=
            this.OptionsControl_OnChanged;
        this.placementComboBox.SelectedIndexChanged +=
            this.OptionsControl_OnChanged;
        this.missionWikiCheckBox.CheckedChanged +=
            this.OptionsControl_OnChanged;
        this.missionHistoryCheckBox.CheckedChanged +=
            this.OptionsControl_OnChanged;
        this.activityHistoryCheckBox.CheckedChanged +=
            this.OptionsControl_OnChanged;
        this.combatHistoryCheckBox.CheckedChanged +=
            this.OptionsControl_OnChanged;
        this.useWormholesCheckBox.CheckedChanged +=
            this.OptionsControl_OnChanged;
        this.keepGalaxyFinderSearchOpenCheckBox.CheckedChanged +=
            this.OptionsControl_OnChanged;
        this.showVendorCompanionCheckBox.CheckedChanged +=
            this.OptionsControl_OnChanged;
        this.showBuffDurationsCheckBox.CheckedChanged +=
            this.OptionsControl_OnChanged;
        this.enhancedItemToolTipsCheckBox.CheckedChanged +=
            this.ItemToolTipOption_OnChanged;
        this.horizontalOffsetTextBox.TextChanged +=
            this.ItemToolTipOffsetTextBox_OnTextChanged;
        this.verticalOffsetTextBox.TextChanged +=
            this.ItemToolTipOffsetTextBox_OnTextChanged;
        this.horizontalOffsetTextBox.Leave +=
            this.ItemToolTipOffsetTextBox_OnLeave;
        this.verticalOffsetTextBox.Leave +=
            this.ItemToolTipOffsetTextBox_OnLeave;
        this.horizontalDecreaseButton.Click +=
            this.HorizontalDecreaseButton_OnClick;
        this.horizontalIncreaseButton.Click +=
            this.HorizontalIncreaseButton_OnClick;
        this.verticalDecreaseButton.Click +=
            this.VerticalDecreaseButton_OnClick;
        this.verticalIncreaseButton.Click +=
            this.VerticalIncreaseButton_OnClick;
        this.resetItemToolTipOffsetsButton.Click +=
            this.ResetItemToolTipOffsetsButton_OnClick;
        this.copyDiagnosticsButton.Click +=
            this.CopyDiagnosticsButton_OnClick;
        this.changeButton.Click += this.ChangeButton_OnClick;
        this.fleetFireAllChangeButton.Click +=
            this.FleetFireAllChangeButton_OnClick;
        this.fleetFireAllClearButton.Click +=
            this.FleetFireAllClearButton_OnClick;
        this.setPositionButton.Click += this.SetPositionButton_OnClick;
        this.applyButton.Click += this.ApplyButton_OnClick;
        this.cancelButton.Click += this.CancelButton_OnClick;
        this.KeyDown += this.InGameOptionsForm_OnKeyDown;
    }

    private void OptionsControl_OnChanged(object? sender, EventArgs e)
    {
        if (this.GetShowMode() != CommandPaletteShowMode.Keybinding)
        {
            this.StopCapturing();
        }

        this.RefreshState();
    }

    private void ItemToolTipOption_OnChanged(
        object? sender,
        EventArgs e)
    {
        this.RefreshState();
        this.PreviewGameItemToolTipSettings();
    }

    private void ItemToolTipOffsetTextBox_OnTextChanged(
        object? sender,
        EventArgs e)
    {
        if (TryParseOffset(
                this.horizontalOffsetTextBox.Text,
                out var horizontalOffset))
        {
            this.selectedHorizontalOffset = horizontalOffset;
        }

        if (TryParseOffset(
                this.verticalOffsetTextBox.Text,
                out var verticalOffset))
        {
            this.selectedVerticalOffset = verticalOffset;
        }

        this.RefreshState();
        this.PreviewGameItemToolTipSettings();
    }

    private void ItemToolTipOffsetTextBox_OnLeave(
        object? sender,
        EventArgs e)
    {
        if (ReferenceEquals(
                sender,
                this.horizontalOffsetTextBox) &&
            !TryParseOffset(
                this.horizontalOffsetTextBox.Text,
                out _))
        {
            this.SetItemToolTipOffsetText(
                this.horizontalOffsetTextBox,
                this.selectedHorizontalOffset);
        }
        else if (ReferenceEquals(
                     sender,
                     this.verticalOffsetTextBox) &&
                 !TryParseOffset(
                     this.verticalOffsetTextBox.Text,
                     out _))
        {
            this.SetItemToolTipOffsetText(
                this.verticalOffsetTextBox,
                this.selectedVerticalOffset);
        }

        this.RefreshState();
    }

    private void HorizontalDecreaseButton_OnClick(
        object? sender,
        EventArgs e)
    {
        this.NudgeItemToolTipOffset(
            horizontalDelta: -1,
            verticalDelta: 0);
    }

    private void HorizontalIncreaseButton_OnClick(
        object? sender,
        EventArgs e)
    {
        this.NudgeItemToolTipOffset(
            horizontalDelta: 1,
            verticalDelta: 0);
    }

    private void VerticalDecreaseButton_OnClick(
        object? sender,
        EventArgs e)
    {
        this.NudgeItemToolTipOffset(
            horizontalDelta: 0,
            verticalDelta: -1);
    }

    private void VerticalIncreaseButton_OnClick(
        object? sender,
        EventArgs e)
    {
        this.NudgeItemToolTipOffset(
            horizontalDelta: 0,
            verticalDelta: 1);
    }

    private void ResetItemToolTipOffsetsButton_OnClick(
        object? sender,
        EventArgs e)
    {
        this.SetItemToolTipOffsets(0, 0);
    }

    private void ChangeButton_OnClick(object? sender, EventArgs e)
    {
        if (this.applying)
        {
            return;
        }

        this.capturing = true;
        this.capturingFleetFireAllHotKey = false;
        this.shortcutTextBox.Text = "Press a shortcut...";
        this.statusLabel.ForeColor = MainWindowTheme.Accent;
        this.statusLabel.Text =
            "Press a key combination. Escape cancels capture.";
        this.changeButton.Text = "Listening...";
        this.Focus();
    }

    private void FleetFireAllChangeButton_OnClick(
        object? sender,
        EventArgs e)
    {
        if (this.applying)
        {
            return;
        }

        this.capturing = false;
        this.capturingFleetFireAllHotKey = true;
        this.fleetFireAllShortcutTextBox.Text =
            "Press a shortcut...";
        this.statusLabel.ForeColor = MainWindowTheme.Accent;
        this.statusLabel.Text =
            "Press a key combination. Escape cancels capture.";
        this.fleetFireAllChangeButton.Text = "Listening...";
        this.Focus();
    }

    private void FleetFireAllClearButton_OnClick(
        object? sender,
        EventArgs e)
    {
        if (this.applying)
        {
            return;
        }

        this.StopCapturing();
        this.selectedFleetFireAllHotKey = Keys.None;
        this.RefreshState();
    }

    private void SetPositionButton_OnClick(object? sender, EventArgs e)
    {
        if (this.applying || this.IsCapturingHotKey)
        {
            return;
        }

        this.Hide();

        try
        {
            var position = this.pickFixedPosition(this);

            if (position != null)
            {
                this.selectedFixedPosition = position;
            }
        }
        finally
        {
            if (!this.IsDisposed && !this.Disposing)
            {
                this.Show();
                this.Activate();
                this.RefreshState();
            }
        }
    }

    private void CopyDiagnosticsButton_OnClick(object? sender, EventArgs e)
    {
        try
        {
            Clipboard.SetText(this.createDiagnostics());
            this.statusLabel.ForeColor = MainWindowTheme.Accent;
            this.statusLabel.Text =
                "Diagnostics copied to the clipboard. Paste them into your support post.";
        }
        catch (ExternalException exception)
        {
            this.ShowError(string.Concat(
                "Diagnostics could not be copied to the clipboard: ",
                exception.Message));
        }
    }

    private async void ApplyButton_OnClick(object? sender, EventArgs e)
    {
        if (this.applying || this.IsCapturingHotKey)
        {
            return;
        }

        this.applying = true;
        this.RefreshState();

        try
        {
            var showMode = this.GetShowMode();

            if (showMode == CommandPaletteShowMode.Keybinding &&
                (this.selectedHotKey != this.initialValues.HotKey ||
                 this.initialValues.ShowMode != CommandPaletteShowMode.Keybinding))
            {
                this.statusLabel.ForeColor = MainWindowTheme.MutedText;
                this.statusLabel.Text = "Checking shortcut...";

                var validationError = await this.validateHotKeyAsync(
                    this.selectedHotKey,
                    this.lifetimeCancellation.Token);

                if (this.lifetimeCancellation.IsCancellationRequested ||
                    this.IsDisposed ||
                    this.Disposing)
                {
                    return;
                }

                if (validationError != null)
                {
                    this.ShowError(validationError);
                    return;
                }
            }

            if (this.selectedFleetFireAllHotKey != Keys.None &&
                this.GetShowMode() == CommandPaletteShowMode.Keybinding &&
                NormalizeHotKey(this.selectedFleetFireAllHotKey) ==
                NormalizeHotKey(this.selectedHotKey))
            {
                this.ShowError(
                    "Fleet Fire All cannot use the same shortcut as the Command Palette.");
                return;
            }

            if (this.selectedFleetFireAllHotKey != Keys.None &&
                this.selectedFleetFireAllHotKey !=
                this.initialValues.FleetFireAllHotKey)
            {
                this.statusLabel.ForeColor = MainWindowTheme.MutedText;
                this.statusLabel.Text =
                    "Checking Fleet Fire All shortcut...";

                var validationError =
                    await this.validateFleetFireAllHotKeyAsync(
                        this.selectedFleetFireAllHotKey,
                        this.lifetimeCancellation.Token);

                if (this.lifetimeCancellation.IsCancellationRequested ||
                    this.IsDisposed ||
                    this.Disposing)
                {
                    return;
                }

                if (validationError != null)
                {
                    this.ShowError(validationError);
                    return;
                }
            }

            if (!this.TryGetItemToolTipOffsets(
                    out var horizontalOffset,
                    out var verticalOffset))
            {
                this.ShowError(
                    "Item tooltip offsets must be whole numbers from -200 to 200.");
                return;
            }

            var values = new InGameOptionsValues(
                showMode,
                this.GetPlacementMode(),
                this.selectedHotKey,
                this.selectedFleetFireAllHotKey,
                this.selectedFixedPosition,
                this.missionWikiCheckBox.Checked,
                this.missionHistoryCheckBox.Checked,
                this.activityHistoryCheckBox.Checked,
                this.combatHistoryCheckBox.Checked,
                this.useWormholesCheckBox.Checked,
                this.keepGalaxyFinderSearchOpenCheckBox.Checked,
                this.showVendorCompanionCheckBox.Checked,
                this.showBuffDurationsCheckBox.Checked,
                this.enhancedItemToolTipsCheckBox.Checked,
                horizontalOffset,
                verticalOffset);

            var applyError = this.applySettings(values);

            if (applyError != null)
            {
                this.ShowError(applyError);
                return;
            }

            this.settingsApplied = true;
            this.DialogResult = DialogResult.OK;
            this.Close();
        }
        catch (OperationCanceledException)
            when (this.lifetimeCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            this.applying = false;

            if (!this.IsDisposed && !this.Disposing)
            {
                this.RefreshState();
            }
        }
    }

    private void CancelButton_OnClick(object? sender, EventArgs e)
    {
        if (this.IsCapturingHotKey)
        {
            this.StopCapturing();
            this.RefreshState();
            return;
        }

        this.DialogResult = DialogResult.Cancel;
        this.Close();
    }

    private void InGameOptionsForm_OnKeyDown(
        object? sender,
        KeyEventArgs e)
    {
        if (!this.IsCapturingHotKey)
        {
            return;
        }

        e.Handled = true;
        e.SuppressKeyPress = true;

        if (e.KeyCode == Keys.Escape)
        {
            this.StopCapturing();
            this.RefreshState();
            return;
        }

        if (IsModifierKey(e.KeyCode))
        {
            return;
        }

        var capturedHotKey = e.KeyCode |
                             (e.Modifiers &
                              (Keys.Control | Keys.Shift | Keys.Alt));

        if (this.capturingFleetFireAllHotKey)
        {
            this.selectedFleetFireAllHotKey = capturedHotKey;
        }
        else
        {
            this.selectedHotKey = capturedHotKey;
        }

        this.StopCapturing();
        this.RefreshState();
    }

    private void StopCapturing()
    {
        this.capturing = false;
        this.capturingFleetFireAllHotKey = false;
        this.changeButton.Text = "Change";
        this.fleetFireAllChangeButton.Text = "Change";
    }

    private bool IsCapturingHotKey =>
        this.capturing ||
        this.capturingFleetFireAllHotKey;

    private void RefreshState()
    {
        var showMode = this.GetShowMode();
        var placementMode = this.GetPlacementMode();
        var keybinding = showMode == CommandPaletteShowMode.Keybinding;
        var fixedPosition = showMode == CommandPaletteShowMode.Always ||
                            (keybinding &&
                             placementMode == CommandPalettePlacementMode.Fixed);

        this.placementComboBox.Enabled = keybinding && !this.applying;
        this.shortcutTextBox.Enabled = keybinding && !this.applying;
        this.changeButton.Enabled = keybinding && !this.applying;
        this.fleetFireAllShortcutTextBox.Enabled = !this.applying;
        this.fleetFireAllChangeButton.Enabled = !this.applying;
        this.fleetFireAllClearButton.Enabled =
            !this.applying &&
            this.selectedFleetFireAllHotKey != Keys.None;
        this.setPositionButton.Enabled = fixedPosition && !this.applying;
        var validItemToolTipOffsets =
            this.TryGetItemToolTipOffsets(out _, out _);
        var itemToolTipsEnabled =
            this.enhancedItemToolTipsCheckBox.Checked &&
            !this.applying;

        this.applyButton.Enabled =
            !this.applying &&
            !this.IsCapturingHotKey &&
            validItemToolTipOffsets;
        this.cancelButton.Enabled = !this.applying;
        this.missionWikiCheckBox.Enabled = !this.applying;
        this.missionHistoryCheckBox.Enabled = !this.applying;
        this.activityHistoryCheckBox.Enabled = !this.applying;
        this.combatHistoryCheckBox.Enabled = !this.applying;
        this.useWormholesCheckBox.Enabled = !this.applying;
        this.keepGalaxyFinderSearchOpenCheckBox.Enabled = !this.applying;
        this.showVendorCompanionCheckBox.Enabled = !this.applying;
        this.showBuffDurationsCheckBox.Enabled = !this.applying;
        this.enhancedItemToolTipsCheckBox.Enabled = !this.applying;
        this.horizontalOffsetTextBox.Enabled = itemToolTipsEnabled;
        this.verticalOffsetTextBox.Enabled = itemToolTipsEnabled;
        this.horizontalDecreaseButton.Enabled = itemToolTipsEnabled;
        this.horizontalIncreaseButton.Enabled = itemToolTipsEnabled;
        this.verticalDecreaseButton.Enabled = itemToolTipsEnabled;
        this.verticalIncreaseButton.Enabled = itemToolTipsEnabled;
        this.resetItemToolTipOffsetsButton.Enabled =
            itemToolTipsEnabled &&
            (this.selectedHorizontalOffset != 0 ||
             this.selectedVerticalOffset != 0);
        this.showModeComboBox.Enabled = !this.applying;

        if (!this.capturing)
        {
            this.shortcutTextBox.Text =
                CommandPaletteHotKeyValidator.FormatHotKey(
                    this.selectedHotKey);
            this.changeButton.Text = "Change";
        }

        if (!this.capturingFleetFireAllHotKey)
        {
            this.fleetFireAllShortcutTextBox.Text =
                this.selectedFleetFireAllHotKey == Keys.None
                    ? "Not set"
                    : CommandPaletteHotKeyValidator.FormatHotKey(
                        this.selectedFleetFireAllHotKey);
            this.fleetFireAllChangeButton.Text = "Change";
        }

        if (this.applying || this.IsCapturingHotKey)
        {
            return;
        }

        if (!validItemToolTipOffsets)
        {
            this.statusLabel.ForeColor = MainWindowTheme.Danger;
            this.statusLabel.Text =
                "Item tooltip offsets must be whole numbers from -200 to 200.";
            return;
        }

        switch (showMode)
        {
            case CommandPaletteShowMode.Never:
                this.statusLabel.ForeColor = MainWindowTheme.MutedText;
                this.statusLabel.Text =
                    "The Command Palette is disabled. Its shortcut and fixed position are retained.";
                break;

            case CommandPaletteShowMode.Always:
                this.statusLabel.ForeColor = MainWindowTheme.MutedText;
                this.statusLabel.Text =
                    "The palette stays attached to the active managed game client and is naturally covered by other applications.";
                break;

            case CommandPaletteShowMode.Keybinding:
                this.SetKeybindingGuidance(placementMode);
                break;

            default:
                this.statusLabel.ForeColor = MainWindowTheme.MutedText;
                this.statusLabel.Text = string.Empty;
                break;
        }
    }

    private void SetKeybindingGuidance(
        CommandPalettePlacementMode placementMode)
    {
        var modifiers = this.selectedHotKey & Keys.Modifiers;
        var hasCursorMovingModifier =
            (modifiers & (Keys.Control | Keys.Shift)) != Keys.None;

        if (placementMode == CommandPalettePlacementMode.Cursor &&
            hasCursorMovingModifier)
        {
            this.statusLabel.ForeColor = MainWindowTheme.Warning;
            this.statusLabel.Text = string.Concat(
                "Earth & Beyond moves the cursor to the center when Ctrl or Shift is used. ",
                "Fixed location is recommended for this shortcut.");
            return;
        }

        this.statusLabel.ForeColor = MainWindowTheme.MutedText;
        this.statusLabel.Text =
            "The shortcut is active only while a managed in-game client is foreground.";
    }

    private void NudgeItemToolTipOffset(
        int horizontalDelta,
        int verticalDelta)
    {
        this.SetItemToolTipOffsets(
            Math.Clamp(
                this.selectedHorizontalOffset + horizontalDelta,
                GameItemToolTipSettings.MinimumOffset,
                GameItemToolTipSettings.MaximumOffset),
            Math.Clamp(
                this.selectedVerticalOffset + verticalDelta,
                GameItemToolTipSettings.MinimumOffset,
                GameItemToolTipSettings.MaximumOffset));
    }

    private void SetItemToolTipOffsets(
        int horizontalOffset,
        int verticalOffset)
    {
        this.selectedHorizontalOffset = horizontalOffset;
        this.selectedVerticalOffset = verticalOffset;
        this.suppressItemToolTipPreview = true;

        try
        {
            this.SetItemToolTipOffsetText(
                this.horizontalOffsetTextBox,
                horizontalOffset);
            this.SetItemToolTipOffsetText(
                this.verticalOffsetTextBox,
                verticalOffset);
        }
        finally
        {
            this.suppressItemToolTipPreview = false;
        }

        this.RefreshState();
        this.PreviewGameItemToolTipSettings();
    }

    private void SetItemToolTipOffsetText(
        TextBox textBox,
        int value)
    {
        var text = value.ToString(CultureInfo.InvariantCulture);

        if (!string.Equals(
                textBox.Text,
                text,
                StringComparison.Ordinal))
        {
            textBox.Text = text;
        }
    }

    private void PreviewGameItemToolTipSettings()
    {
        if (this.suppressItemToolTipPreview ||
            !this.TryGetItemToolTipOffsets(
                out var horizontalOffset,
                out var verticalOffset))
        {
            return;
        }

        this.previewGameItemToolTipSettings(
            this.enhancedItemToolTipsCheckBox.Checked,
            horizontalOffset,
            verticalOffset);
    }

    private bool TryGetItemToolTipOffsets(
        out int horizontalOffset,
        out int verticalOffset)
    {
        horizontalOffset = default;
        verticalOffset = default;

        return TryParseOffset(
                   this.horizontalOffsetTextBox.Text,
                   out horizontalOffset) &&
               TryParseOffset(
                   this.verticalOffsetTextBox.Text,
                   out verticalOffset);
    }

    private static bool TryParseOffset(
        string value,
        out int offset)
    {
        return int.TryParse(
                   value.Trim(),
                   NumberStyles.AllowLeadingSign,
                   CultureInfo.InvariantCulture,
                   out offset) &&
               offset is >= GameItemToolTipSettings.MinimumOffset and
                   <= GameItemToolTipSettings.MaximumOffset;
    }

    private void ShowError(string message)
    {
        this.statusLabel.ForeColor = MainWindowTheme.Danger;
        this.statusLabel.Text = message;
    }

    private CommandPaletteShowMode GetShowMode()
    {
        return this.showModeComboBox.SelectedItem is ShowModeItem item
            ? item.Value
            : CommandPaletteShowMode.Keybinding;
    }

    private CommandPalettePlacementMode GetPlacementMode()
    {
        return this.placementComboBox.SelectedItem is PlacementModeItem item
            ? item.Value
            : CommandPalettePlacementMode.Cursor;
    }

    private void ConfigureComboBox(ComboBox comboBox)
    {
        comboBox.Dock = DockStyle.Fill;
        comboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        comboBox.Margin = new Padding(0, 7, 0, 6);
        MainWindowTheme.StyleComboBox(comboBox);
    }

    private Label CreateFieldLabel(string text)
    {
        return new Label
        {
            Dock = DockStyle.Fill,
            Text = text,
            ForeColor = MainWindowTheme.MutedText,
            TextAlign = ContentAlignment.MiddleLeft,
        };
    }

    private static Keys NormalizeHotKey(Keys hotKey)
    {
        return (hotKey & Keys.KeyCode) |
               (hotKey & (Keys.Control | Keys.Shift | Keys.Alt));
    }

    private static bool IsModifierKey(Keys keyCode)
    {
        return keyCode is
            Keys.ControlKey or
            Keys.LControlKey or
            Keys.RControlKey or
            Keys.ShiftKey or
            Keys.LShiftKey or
            Keys.RShiftKey or
            Keys.Menu or
            Keys.LMenu or
            Keys.RMenu or
            Keys.LWin or
            Keys.RWin;
    }

    private sealed record ShowModeItem(
        CommandPaletteShowMode Value,
        string Text)
    {
        public override string ToString() => this.Text;
    }

    private sealed record PlacementModeItem(
        CommandPalettePlacementMode Value,
        string Text)
    {
        public override string ToString() => this.Text;
    }
}
