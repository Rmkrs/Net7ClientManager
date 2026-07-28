// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using Net7ClientManager.Core;
using Net7ClientManager.Models;
using Net7ClientManager.Observations;
using Net7ClientManager.Services;
using Net7ClientManager.Social;

public sealed class SocialForm : ThemedForm
{
    private readonly ClientManager clientManager;
    private int ownerProcessId;
    private readonly System.Windows.Forms.Timer refreshTimer;
    private readonly WindowPlacementBinding placementBinding;

    private ComboBox presencePilotCombo = null!;
    private CheckBox publishPresenceCheckBox = null!;
    private ComboBox atlasVisibilityCombo = null!;
    private Button savePresenceButton = null!;
    private TextBox presenceSearchTextBox = null!;
    private TextBox presenceSectorTextBox = null!;
    private ComboBox presenceStateCombo = null!;
    private DataGridView presenceGrid = null!;

    private ComboBox lfgPilotCombo = null!;
    private CheckBox lookingForGuildCheckBox = null!;
    private Label lfgIdentityLabel = null!;
    private CheckedListBox lfgTagsList = null!;
    private CheckedListBox lfgLanguagesList = null!;
    private TextBox lfgOtherLanguageTextBox = null!;
    private ComboBox lfgRegionCombo = null!;
    private TextBox lfgOtherRegionTextBox = null!;
    private TextBox lfgAvailabilityTextBox = null!;
    private TextBox lfgMessageTextBox = null!;
    private Button saveLfgButton = null!;
    private TextBox lfgSearchTextBox = null!;
    private DataGridView lfgGrid = null!;

    private ComboBox guildPublisherCombo = null!;
    private CheckBox guildRecruitingCheckBox = null!;
    private Label guildIdentityLabel = null!;
    private CheckedListBox guildTagsList = null!;
    private CheckedListBox guildProfessionsList = null!;
    private CheckedListBox guildLanguagesList = null!;
    private TextBox guildOtherLanguageTextBox = null!;
    private ComboBox guildRegionCombo = null!;
    private TextBox guildOtherRegionTextBox = null!;
    private TextBox guildActiveTimesTextBox = null!;
    private TextBox guildOtherContactsTextBox = null!;
    private TextBox guildRequirementsTextBox = null!;
    private TextBox guildMessageTextBox = null!;
    private Button saveGuildButton = null!;
    private TextBox guildSearchTextBox = null!;
    private DataGridView guildGrid = null!;

    private Label statusLabel = null!;
    private Button refreshButton = null!;
    private bool loadingControls;
    private long renderedVersion = -1;
    private string localChoiceFingerprint = "";

    public SocialForm(
        ClientManager clientManager,
        int ownerProcessId)
    {
        ArgumentNullException.ThrowIfNull(clientManager);
        this.clientManager = clientManager;
        this.ownerProcessId = ownerProcessId;

        this.Text = "Social";
        this.Icon = ResourceLoader.Net7ClientManagerIcon;
        this.StartPosition = FormStartPosition.CenterParent;
        this.Size = new Size(1320, 820);
        this.MinimumSize = new Size(1080, 680);
        this.BackColor = MainWindowTheme.Background;
        this.ForeColor = MainWindowTheme.Text;
        this.Font = MainWindowTheme.CreateBodyFont();
        this.ConfigureWindowChrome(
            allowResize: true,
            showMinimizeButton: true,
            showMaximizeButton: true);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12, 50, 12, 12),
            ColumnCount = 1,
            RowCount = 2,
            BackColor = MainWindowTheme.Background,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));

        var tabs = new ThemedTabHost
        {
            Dock = DockStyle.Fill,
        };
        tabs.AddPage(this.CreatePresenceTab());
        tabs.AddPage(this.CreateLookingForGuildTab());
        tabs.AddPage(this.CreateGuildRecruitmentTab());

        var footer = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = MainWindowTheme.Header,
            Padding = new Padding(8, 4, 8, 4),
        };
        this.statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = MainWindowTheme.MutedText,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
        };
        this.refreshButton = CreateButton("Refresh", 90, primary: true);
        this.refreshButton.Dock = DockStyle.Right;
        this.refreshButton.Click += this.RefreshButton_OnClick;
        footer.Controls.Add(this.statusLabel);
        footer.Controls.Add(this.refreshButton);

        root.Controls.Add(tabs, 0, 0);
        root.Controls.Add(footer, 0, 1);
        this.Controls.Add(root);

        this.placementBinding = this.clientManager.BindGlobalWindowPlacement(
            this,
            "social");

        this.refreshTimer = new System.Windows.Forms.Timer
        {
            Interval = 1000,
        };
        this.refreshTimer.Tick += this.RefreshTimer_OnTick;
        this.refreshTimer.Start();

        this.RefreshLocalChoices(force: true);
        this.RenderPublicData(force: true);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.refreshTimer.Stop();
            this.refreshTimer.Tick -= this.RefreshTimer_OnTick;
            this.refreshTimer.Dispose();
            this.placementBinding.Dispose();
        }

        base.Dispose(disposing);
    }

    private ThemedTabPage CreatePresenceTab()
    {
        var tab = CreateTab("Presence");
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(8),
            BackColor = MainWindowTheme.Background,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var settings = CreateSectionPanel();
        var settingsFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(10, 12, 10, 8),
        };
        settingsFlow.Controls.Add(CreateFieldLabel("Pilot", 45));
        this.presencePilotCombo = CreateComboBox(190);
        this.presencePilotCombo.SelectedIndexChanged +=
            this.PresencePilotCombo_OnSelectedIndexChanged;
        settingsFlow.Controls.Add(this.presencePilotCombo);
        this.publishPresenceCheckBox = CreateCheckBox(
            "Publish public presence",
            175);
        this.publishPresenceCheckBox.CheckedChanged +=
            this.PublishPresenceCheckBox_OnCheckedChanged;
        settingsFlow.Controls.Add(this.publishPresenceCheckBox);
        settingsFlow.Controls.Add(CreateFieldLabel("Atlas visibility", 90));
        this.atlasVisibilityCombo = CreateComboBox(155);
        this.atlasVisibilityCombo.Items.AddRange(
            Enum.GetValues<SocialAtlasVisibilityMode>()
                .Cast<object>()
                .ToArray());
        settingsFlow.Controls.Add(this.atlasVisibilityCombo);
        this.savePresenceButton = CreateButton("Save presence", 125, primary: true);
        this.savePresenceButton.Click += this.SavePresenceButton_OnClick;
        settingsFlow.Controls.Add(this.savePresenceButton);
        settings.Controls.Add(settingsFlow);

        var filters = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = MainWindowTheme.Header,
            Padding = new Padding(8, 5, 8, 5),
        };
        filters.Controls.Add(CreateFieldLabel("Find", 35));
        this.presenceSearchTextBox = CreateTextBox(210);
        this.presenceSearchTextBox.TextChanged += (_, _) => this.RenderPresenceGrid();
        filters.Controls.Add(this.presenceSearchTextBox);
        filters.Controls.Add(CreateFieldLabel("Sector", 48));
        this.presenceSectorTextBox = CreateTextBox(175);
        this.presenceSectorTextBox.TextChanged += (_, _) => this.RenderPresenceGrid();
        filters.Controls.Add(this.presenceSectorTextBox);
        filters.Controls.Add(CreateFieldLabel("Show", 40));
        this.presenceStateCombo = CreateComboBox(145);
        this.presenceStateCombo.Items.AddRange(
            ["All", "Online only", "Recently seen", "Offline only"]);
        this.presenceStateCombo.SelectedIndex = 0;
        this.presenceStateCombo.SelectedIndexChanged += (_, _) => this.RenderPresenceGrid();
        filters.Controls.Add(this.presenceStateCombo);

        this.presenceGrid = CreateGrid(
            "Pilot",
            "Status",
            "Last seen",
            "System",
            "Sector",
            "Location",
            "Visibility",
            "LFG");

        root.Controls.Add(settings, 0, 0);
        root.Controls.Add(filters, 0, 1);
        root.Controls.Add(this.presenceGrid, 0, 2);
        tab.Controls.Add(root);
        return tab;
    }

    private ThemedTabPage CreateLookingForGuildTab()
    {
        var tab = CreateTab("Looking for Guild");
        var split = CreateEditorSplit(
            preferredEditorWidth: 520,
            minimumEditorWidth: 460);

        var editor = CreateScrollableEditor();
        this.lfgPilotCombo = AddComboField(editor, "Pilot", 0);
        this.lfgPilotCombo.SelectedIndexChanged +=
            this.LfgPilotCombo_OnSelectedIndexChanged;
        this.lfgIdentityLabel = AddValueLabel(editor, "Current identity", 1);
        this.lookingForGuildCheckBox = AddCheckField(
            editor,
            "Show this pilot as looking for a guild",
            2);
        this.lookingForGuildCheckBox.CheckedChanged +=
            this.LookingForGuildCheckBox_OnCheckedChanged;
        this.lfgTagsList = AddCheckedListField(
            editor,
            "What are you looking for?",
            SocialVocabulary.InterestTags,
            3,
            height: 145);
        this.lfgLanguagesList = AddCheckedListField(
            editor,
            "Languages",
            SocialVocabulary.Languages,
            4,
            height: 110);
        this.lfgOtherLanguageTextBox = AddTextField(
            editor,
            "Other language",
            5,
            maxLength: 96);
        this.lfgRegionCombo = AddComboField(
            editor,
            "Region / timezone",
            6,
            SocialVocabulary.Regions);
        this.lfgRegionCombo.SelectedIndexChanged += (_, _) =>
            this.UpdateLfgEditorEnabledState();
        this.lfgOtherRegionTextBox = AddTextField(
            editor,
            "Other region / timezone",
            7,
            maxLength: 96);
        this.lfgAvailabilityTextBox = AddTextField(
            editor,
            "Usual play time",
            8,
            maxLength: 256);
        this.lfgMessageTextBox = AddTextField(
            editor,
            "Message",
            9,
            maxLength: 1000);
        this.saveLfgButton = CreateButton("Save LFG profile", 145, primary: true);
        AddButtonRow(editor, this.saveLfgButton, 10);
        this.saveLfgButton.Click += this.SaveLfgButton_OnClick;
        split.Panel1.Controls.Add(editor);

        var right = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
        };
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var filter = CreateFilterPanel("Find pilots", out this.lfgSearchTextBox);
        this.lfgSearchTextBox.TextChanged += (_, _) => this.RenderLfgGrid();
        this.lfgGrid = CreateGrid(
            "Pilot",
            "Profession",
            "Level",
            "Interests",
            "Languages",
            "Region",
            "Availability",
            "Message",
            "Updated");
        right.Controls.Add(filter, 0, 0);
        right.Controls.Add(this.lfgGrid, 0, 1);
        split.Panel2.Controls.Add(right);
        tab.Controls.Add(split);
        return tab;
    }

    private ThemedTabPage CreateGuildRecruitmentTab()
    {
        var tab = CreateTab("Guild Recruitment");
        var split = CreateEditorSplit(
            preferredEditorWidth: 560,
            minimumEditorWidth: 500);

        var editor = CreateScrollableEditor();
        this.guildPublisherCombo = AddComboField(editor, "Publishing pilot", 0);
        this.guildPublisherCombo.SelectedIndexChanged +=
            this.GuildPublisherCombo_OnSelectedIndexChanged;
        this.guildIdentityLabel = AddValueLabel(editor, "Guild", 1);
        this.guildRecruitingCheckBox = AddCheckField(
            editor,
            "Show this guild as recruiting",
            2);
        this.guildRecruitingCheckBox.CheckedChanged +=
            this.GuildRecruitingCheckBox_OnCheckedChanged;
        this.guildTagsList = AddCheckedListField(
            editor,
            "Guild focus",
            SocialVocabulary.InterestTags,
            3,
            height: 140);
        this.guildProfessionsList = AddCheckedListField(
            editor,
            "Recruiting",
            SocialVocabulary.Professions,
            4,
            height: 155);
        this.guildLanguagesList = AddCheckedListField(
            editor,
            "Languages",
            SocialVocabulary.Languages,
            5,
            height: 105);
        this.guildOtherLanguageTextBox = AddTextField(
            editor,
            "Other language",
            6,
            maxLength: 96);
        this.guildRegionCombo = AddComboField(
            editor,
            "Region / timezone",
            7,
            SocialVocabulary.Regions);
        this.guildRegionCombo.SelectedIndexChanged += (_, _) =>
            this.UpdateGuildEditorEnabledState();
        this.guildOtherRegionTextBox = AddTextField(
            editor,
            "Other region / timezone",
            8,
            maxLength: 96);
        this.guildActiveTimesTextBox = AddTextField(
            editor,
            "Active times",
            9,
            maxLength: 256);
        this.guildOtherContactsTextBox = AddTextField(
            editor,
            "Other contacts",
            10,
            maxLength: 256);
        this.guildRequirementsTextBox = AddTextField(
            editor,
            "Requirements",
            11,
            maxLength: 500);
        this.guildMessageTextBox = AddTextField(
            editor,
            "Message",
            12,
            maxLength: 1000);
        this.saveGuildButton = CreateButton("Save recruitment", 155, primary: true);
        this.saveGuildButton.Click += this.SaveGuildButton_OnClick;
        AddButtonRow(editor, this.saveGuildButton, 13);
        split.Panel1.Controls.Add(editor);

        var right = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
        };
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var filter = CreateFilterPanel("Find guilds", out this.guildSearchTextBox);
        this.guildSearchTextBox.TextChanged += (_, _) => this.RenderGuildGrid();
        this.guildGrid = CreateGrid(
            "Guild",
            "Contact",
            "Focus",
            "Recruiting",
            "Languages",
            "Region",
            "Active times",
            "Requirements",
            "Message",
            "Updated");
        right.Controls.Add(filter, 0, 0);
        right.Controls.Add(this.guildGrid, 0, 1);
        split.Panel2.Controls.Add(right);
        tab.Controls.Add(split);
        return tab;
    }

    private void RefreshTimer_OnTick(object? sender, EventArgs e)
    {
        if (!this.IsOwnerPilotInGame())
        {
            this.Close();
            return;
        }

        this.RefreshLocalChoices(force: false);
        this.RenderPublicData(force: false);
    }

    private async void RefreshButton_OnClick(object? sender, EventArgs e)
    {
        await this.RunOperationAsync(
            "Refreshing social data...",
            cancellationToken => this.clientManager.RefreshSocialAsync(cancellationToken));
    }

    private void RefreshLocalChoices(bool force)
    {
        var pilots = this.clientManager.GetSocialLocalPilots()
            .Where(pilot => pilot.IsRunning)
            .ToArray();
        var fingerprint = string.Join(
            "|",
            pilots.Select(pilot =>
                $"{pilot.PilotName}:{pilot.GuildName}:{pilot.IsRunning}:{pilot.OverallLevel}"));

        if (!force && string.Equals(
                fingerprint,
                this.localChoiceFingerprint,
                StringComparison.Ordinal))
        {
            return;
        }

        this.localChoiceFingerprint = fingerprint;
        this.loadingControls = true;

        try
        {
            var preferredPilotName = pilots
                .FirstOrDefault(pilot =>
                    pilot.ProcessId == this.ownerProcessId)
                ?.PilotName;
            var presenceSelection =
                this.presencePilotCombo.SelectedItem?.ToString() ??
                preferredPilotName;
            var lfgSelection =
                this.lfgPilotCombo.SelectedItem?.ToString() ??
                preferredPilotName;
            ReplaceItems(
                this.presencePilotCombo,
                pilots.Select(pilot => pilot.PilotName),
                presenceSelection);
            ReplaceItems(
                this.lfgPilotCombo,
                pilots.Select(pilot => pilot.PilotName),
                lfgSelection);

            var preferredGuildChoice = pilots
                .Where(pilot =>
                    pilot.ProcessId == this.ownerProcessId &&
                    !string.IsNullOrWhiteSpace(pilot.GuildName))
                .Select(pilot => $"{pilot.GuildName}|{pilot.PilotName}")
                .FirstOrDefault();
            var guildSelection =
                (this.guildPublisherCombo.SelectedItem as GuildPublisherChoice)?.Key ??
                preferredGuildChoice;
            var guildChoices = pilots
                .Where(pilot => !string.IsNullOrWhiteSpace(pilot.GuildName))
                .Select(pilot => new GuildPublisherChoice(
                    pilot.GuildName!,
                    pilot.PilotName))
                .DistinctBy(choice => choice.Key, StringComparer.OrdinalIgnoreCase)
                .OrderBy(choice => choice.GuildName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(choice => choice.PilotName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            this.guildPublisherCombo.Items.Clear();
            this.guildPublisherCombo.Items.AddRange(guildChoices.Cast<object>().ToArray());
            this.guildPublisherCombo.SelectedItem = guildChoices.FirstOrDefault(choice =>
                string.Equals(choice.Key, guildSelection, StringComparison.OrdinalIgnoreCase));
            if (this.guildPublisherCombo.SelectedIndex < 0 &&
                this.guildPublisherCombo.Items.Count > 0)
            {
                this.guildPublisherCombo.SelectedIndex = 0;
            }
        }
        finally
        {
            this.loadingControls = false;
        }

        this.LoadPresenceSettings();
        this.LoadLfgSettings();
        this.LoadGuildSettings();
    }

    private void RenderPublicData(bool force)
    {
        var snapshot = this.clientManager.GetSocialSnapshot();

        if (!force && snapshot.Version == this.renderedVersion)
        {
            return;
        }

        this.renderedVersion = snapshot.Version;
        this.statusLabel.Text = snapshot.Status;
        this.RenderPresenceGrid();
        this.RenderLfgGrid();
        this.RenderGuildGrid();
    }

    private void PresencePilotCombo_OnSelectedIndexChanged(object? sender, EventArgs e)
    {
        if (!this.loadingControls)
        {
            this.LoadPresenceSettings();
        }
    }

    private void LfgPilotCombo_OnSelectedIndexChanged(object? sender, EventArgs e)
    {
        if (!this.loadingControls)
        {
            this.LoadLfgSettings();
        }
    }

    private void GuildPublisherCombo_OnSelectedIndexChanged(object? sender, EventArgs e)
    {
        if (!this.loadingControls)
        {
            this.LoadGuildSettings();
        }
    }

    private void PublishPresenceCheckBox_OnCheckedChanged(object? sender, EventArgs e)
    {
        if (!this.loadingControls)
        {
            this.UpdatePresenceEditorEnabledState();
        }
    }

    private void LookingForGuildCheckBox_OnCheckedChanged(object? sender, EventArgs e)
    {
        if (!this.loadingControls)
        {
            this.UpdateLfgEditorEnabledState();
        }
    }

    private void GuildRecruitingCheckBox_OnCheckedChanged(object? sender, EventArgs e)
    {
        if (!this.loadingControls)
        {
            this.UpdateGuildEditorEnabledState();
        }
    }

    private void LoadPresenceSettings()
    {
        var pilotName = this.presencePilotCombo.SelectedItem?.ToString();
        var available = !string.IsNullOrWhiteSpace(pilotName);
        this.loadingControls = true;

        try
        {
            if (available)
            {
                var settings = this.clientManager.GetSocialPilotSettings(pilotName!);
                this.publishPresenceCheckBox.Checked = settings.PublishPresence;
                this.atlasVisibilityCombo.SelectedItem = settings.AtlasVisibility;
            }
            else
            {
                this.publishPresenceCheckBox.Checked = false;
                this.atlasVisibilityCombo.SelectedItem = SocialAtlasVisibilityMode.NearNav;
            }
        }
        finally
        {
            this.loadingControls = false;
        }

        this.savePresenceButton.Enabled = available;
        this.publishPresenceCheckBox.Enabled = available;
        this.UpdatePresenceEditorEnabledState();
    }

    private void LoadLfgSettings()
    {
        var pilotName = this.lfgPilotCombo.SelectedItem?.ToString();
        var pilot = this.clientManager.GetSocialLocalPilots().FirstOrDefault(candidate =>
            string.Equals(candidate.PilotName, pilotName, StringComparison.OrdinalIgnoreCase));
        var available = pilot != null;
        this.loadingControls = true;

        try
        {
            if (available)
            {
                var settings = this.clientManager.GetSocialPilotSettings(pilot!.PilotName);
                this.lookingForGuildCheckBox.Checked = settings.IsLookingForGuild;
                this.lfgIdentityLabel.Text = FormatPilotIdentity(pilot);
                SetCheckedItems(this.lfgTagsList, settings.InterestTags);
                SetCheckedItems(this.lfgLanguagesList, settings.Languages);
                this.lfgOtherLanguageTextBox.Text = settings.OtherLanguage ?? "";
                this.lfgRegionCombo.SelectedItem = settings.Region;
                this.lfgOtherRegionTextBox.Text = settings.OtherRegion ?? "";
                this.lfgAvailabilityTextBox.Text = settings.Availability ?? "";
                this.lfgMessageTextBox.Text = settings.Message ?? "";
            }
            else
            {
                this.lookingForGuildCheckBox.Checked = false;
                this.lfgIdentityLabel.Text = "No observed or saved pilot is available.";
                SetCheckedItems(this.lfgTagsList, []);
                SetCheckedItems(this.lfgLanguagesList, []);
                this.lfgOtherLanguageTextBox.Clear();
                this.lfgRegionCombo.SelectedIndex = -1;
                this.lfgOtherRegionTextBox.Clear();
                this.lfgAvailabilityTextBox.Clear();
                this.lfgMessageTextBox.Clear();
            }
        }
        finally
        {
            this.loadingControls = false;
        }

        this.saveLfgButton.Enabled = available;
        this.lookingForGuildCheckBox.Enabled = available;
        this.UpdateLfgEditorEnabledState();
    }

    private void LoadGuildSettings()
    {
        var choice = this.guildPublisherCombo.SelectedItem as GuildPublisherChoice;
        var available = choice != null;
        this.loadingControls = true;

        try
        {
            if (available)
            {
                var settings = this.clientManager.GetSocialGuildSettings(
                    choice!.GuildName,
                    choice.PilotName);
                this.guildRecruitingCheckBox.Checked = settings.IsRecruiting;
                this.guildIdentityLabel.Text =
                    $"{choice.GuildName} · primary contact {settings.PublishingPilotName}";
                SetCheckedItems(this.guildTagsList, settings.FocusTags);
                SetCheckedItems(this.guildProfessionsList, settings.WantedProfessions);
                SetCheckedItems(this.guildLanguagesList, settings.Languages);
                this.guildOtherLanguageTextBox.Text = settings.OtherLanguage ?? "";
                this.guildRegionCombo.SelectedItem = settings.Region;
                this.guildOtherRegionTextBox.Text = settings.OtherRegion ?? "";
                this.guildActiveTimesTextBox.Text = settings.ActiveTimes ?? "";
                this.guildOtherContactsTextBox.Text = settings.OtherContacts ?? "";
                this.guildRequirementsTextBox.Text = settings.Requirements ?? "";
                this.guildMessageTextBox.Text = settings.Message ?? "";
            }
            else
            {
                this.guildRecruitingCheckBox.Checked = false;
                this.guildIdentityLabel.Text = "Log in a guilded pilot to publish recruitment.";
                SetCheckedItems(this.guildTagsList, []);
                SetCheckedItems(this.guildProfessionsList, []);
                SetCheckedItems(this.guildLanguagesList, []);
                this.guildOtherLanguageTextBox.Clear();
                this.guildRegionCombo.SelectedIndex = -1;
                this.guildOtherRegionTextBox.Clear();
                this.guildActiveTimesTextBox.Clear();
                this.guildOtherContactsTextBox.Clear();
                this.guildRequirementsTextBox.Clear();
                this.guildMessageTextBox.Clear();
            }
        }
        finally
        {
            this.loadingControls = false;
        }

        this.saveGuildButton.Enabled = available;
        this.guildRecruitingCheckBox.Enabled = available;
        this.UpdateGuildEditorEnabledState();
    }

    private void UpdatePresenceEditorEnabledState()
    {
        this.atlasVisibilityCombo.Enabled =
            this.publishPresenceCheckBox.Enabled &&
            this.publishPresenceCheckBox.Checked;
    }

    private void UpdateLfgEditorEnabledState()
    {
        var enabled = this.lookingForGuildCheckBox.Enabled &&
                      this.lookingForGuildCheckBox.Checked;
        SetEnabled(
            enabled,
            this.lfgTagsList,
            this.lfgLanguagesList,
            this.lfgAvailabilityTextBox,
            this.lfgMessageTextBox,
            this.lfgRegionCombo);
        this.lfgOtherLanguageTextBox.Enabled = enabled;
        this.lfgOtherRegionTextBox.Enabled = enabled;
    }

    private void UpdateGuildEditorEnabledState()
    {
        var enabled = this.guildRecruitingCheckBox.Enabled &&
                      this.guildRecruitingCheckBox.Checked;
        SetEnabled(
            enabled,
            this.guildTagsList,
            this.guildProfessionsList,
            this.guildLanguagesList,
            this.guildRegionCombo,
            this.guildActiveTimesTextBox,
            this.guildOtherContactsTextBox,
            this.guildRequirementsTextBox,
            this.guildMessageTextBox);
        this.guildOtherLanguageTextBox.Enabled = enabled;
        this.guildOtherRegionTextBox.Enabled = enabled;
    }

    private async void SavePresenceButton_OnClick(object? sender, EventArgs e)
    {
        var pilotName = this.presencePilotCombo.SelectedItem?.ToString();
        if (!this.TryGetRunningPilot(pilotName, out _))
        {
            this.ShowPilotNoLongerAvailableWarning();
            return;
        }

        var settings = this.clientManager.GetSocialPilotSettings(pilotName!);
        settings.PublishPresence = this.publishPresenceCheckBox.Checked;
        settings.AtlasVisibility = this.atlasVisibilityCombo.SelectedItem is
            SocialAtlasVisibilityMode visibility
                ? visibility
                : SocialAtlasVisibilityMode.None;

        await this.RunOperationAsync(
            "Saving presence...",
            cancellationToken => this.clientManager.SaveSocialPilotSettingsAsync(
                pilotName!,
                publishLookingForGuild: false,
                cancellationToken));
    }

    private async void SaveLfgButton_OnClick(object? sender, EventArgs e)
    {
        var pilotName = this.lfgPilotCombo.SelectedItem?.ToString();
        if (!this.TryGetRunningPilot(pilotName, out _))
        {
            this.ShowPilotNoLongerAvailableWarning();
            return;
        }

        var settings = this.clientManager.GetSocialPilotSettings(pilotName!);
        settings.IsLookingForGuild = this.lookingForGuildCheckBox.Checked;
        settings.InterestTags = GetCheckedItems(this.lfgTagsList);
        settings.Languages = GetCheckedItems(this.lfgLanguagesList);
        settings.OtherLanguage = NullIfWhiteSpace(this.lfgOtherLanguageTextBox.Text);
        settings.Region = NullIfWhiteSpace(this.lfgRegionCombo.SelectedItem?.ToString());
        settings.OtherRegion = NullIfWhiteSpace(this.lfgOtherRegionTextBox.Text);
        settings.Availability = NullIfWhiteSpace(this.lfgAvailabilityTextBox.Text);
        settings.Message = NullIfWhiteSpace(this.lfgMessageTextBox.Text);

        await this.RunOperationAsync(
            "Saving Looking for Guild profile...",
            cancellationToken => this.clientManager.SaveSocialPilotSettingsAsync(
                pilotName!,
                publishLookingForGuild: true,
                cancellationToken));
    }

    private async void SaveGuildButton_OnClick(object? sender, EventArgs e)
    {
        var choice = this.guildPublisherCombo.SelectedItem as GuildPublisherChoice;
        if (choice == null ||
            !this.TryGetRunningPilot(choice.PilotName, out var publishingPilot) ||
            !string.Equals(
                publishingPilot.GuildName,
                choice.GuildName,
                StringComparison.OrdinalIgnoreCase))
        {
            this.ShowPilotNoLongerAvailableWarning();
            return;
        }

        var settings = this.clientManager.GetSocialGuildSettings(
            choice.GuildName,
            choice.PilotName);
        settings.PublishingPilotName = choice.PilotName;
        settings.IsRecruiting = this.guildRecruitingCheckBox.Checked;
        settings.FocusTags = GetCheckedItems(this.guildTagsList);
        settings.WantedProfessions = GetCheckedItems(this.guildProfessionsList);
        settings.Languages = GetCheckedItems(this.guildLanguagesList);
        settings.OtherLanguage = NullIfWhiteSpace(this.guildOtherLanguageTextBox.Text);
        settings.Region = NullIfWhiteSpace(this.guildRegionCombo.SelectedItem?.ToString());
        settings.OtherRegion = NullIfWhiteSpace(this.guildOtherRegionTextBox.Text);
        settings.ActiveTimes = NullIfWhiteSpace(this.guildActiveTimesTextBox.Text);
        settings.OtherContacts = NullIfWhiteSpace(this.guildOtherContactsTextBox.Text);
        settings.Requirements = NullIfWhiteSpace(this.guildRequirementsTextBox.Text);
        settings.Message = NullIfWhiteSpace(this.guildMessageTextBox.Text);

        await this.RunOperationAsync(
            "Saving guild recruitment...",
            cancellationToken => this.clientManager.SaveSocialGuildSettingsAsync(
                choice.GuildName,
                cancellationToken));
    }

    private async Task RunOperationAsync(
        string status,
        Func<CancellationToken, Task> operation)
    {
        this.statusLabel.Text = status;
        this.refreshButton.Enabled = false;
        this.savePresenceButton.Enabled = false;
        this.saveLfgButton.Enabled = false;
        this.saveGuildButton.Enabled = false;

        try
        {
            await operation(CancellationToken.None);
            this.RenderPublicData(force: true);
        }
        catch (Exception exception)
        {
            this.statusLabel.Text = exception.Message;
            ThemedMessageDialog.ShowWarning(
                this,
                "Social",
                exception.Message);
        }
        finally
        {
            this.refreshButton.Enabled = true;
            this.LoadPresenceSettings();
            this.LoadLfgSettings();
            this.LoadGuildSettings();
        }
    }

    private void RenderPresenceGrid()
    {
        var snapshot = this.clientManager.GetSocialSnapshot();
        var looking = snapshot.LookingForGuild
            .Select(record => record.PilotName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var search = this.presenceSearchTextBox?.Text.Trim() ?? "";
        var sector = this.presenceSectorTextBox?.Text.Trim() ?? "";
        var stateFilter = this.presenceStateCombo?.SelectedItem?.ToString() ?? "All";
        var rows = snapshot.Presence
            .Where(record => Contains(record.PilotName, search) ||
                             Contains(record.SectorName, search) ||
                             Contains(record.StationName, search) ||
                             Contains(record.NearestNavName, search))
            .Where(record => Contains(record.SectorName, sector) ||
                             Contains(record.SystemName, sector))
            .Select(record => new
            {
                Record = record,
                Freshness = this.clientManager.GetSocialPresenceFreshness(
                    record.UpdatedAtUtc),
            })
            .Where(item => stateFilter switch
            {
                "Online only" => item.Freshness == SocialPresenceFreshness.Online,
                "Recently seen" => item.Freshness ==
                    SocialPresenceFreshness.RecentlySeen,
                "Offline only" => item.Freshness == SocialPresenceFreshness.Offline,
                _ => true,
            })
            .OrderBy(item => item.Freshness)
            .ThenBy(item => item.Record.PilotName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        this.presenceGrid.Rows.Clear();
        foreach (var item in rows)
        {
            this.presenceGrid.Rows.Add(
                item.Record.PilotName,
                FormatFreshness(item.Freshness),
                FormatRelativeTime(item.Record.UpdatedAtUtc),
                item.Record.SystemName ?? "",
                item.Record.SectorName ?? "",
                FormatPresenceLocation(item.Record),
                FormatVisibility(item.Record.AtlasVisibility),
                looking.Contains(item.Record.PilotName) ? "Yes" : "");
        }
    }

    private void RenderLfgGrid()
    {
        var snapshot = this.clientManager.GetSocialSnapshot();
        var search = this.lfgSearchTextBox?.Text.Trim() ?? "";
        var rows = snapshot.LookingForGuild
            .Where(record => MatchesSearch(
                search,
                record.PilotName,
                record.ProfessionName,
                string.Join(" ", record.InterestTags),
                string.Join(" ", record.Languages),
                record.OtherLanguage,
                record.Region,
                record.OtherRegion,
                record.Availability,
                record.Message))
            .OrderByDescending(record => record.UpdatedAtUtc)
            .ToArray();

        this.lfgGrid.Rows.Clear();
        foreach (var record in rows)
        {
            this.lfgGrid.Rows.Add(
                record.PilotName,
                record.ProfessionName ?? "",
                record.OverallLevel?.ToString() ?? "",
                string.Join(", ", record.InterestTags),
                JoinStructuredOther(record.Languages, record.OtherLanguage),
                JoinStructuredOther(
                    string.IsNullOrWhiteSpace(record.Region) ? [] : [record.Region],
                    record.OtherRegion),
                record.Availability ?? "",
                record.Message ?? "",
                FormatRelativeTime(record.UpdatedAtUtc));
        }
    }

    private void RenderGuildGrid()
    {
        var snapshot = this.clientManager.GetSocialSnapshot();
        var search = this.guildSearchTextBox?.Text.Trim() ?? "";
        var rows = snapshot.GuildRecruitment
            .Where(record => MatchesSearch(
                search,
                record.GuildName,
                record.PublishingPilotName,
                record.OtherContacts,
                string.Join(" ", record.FocusTags),
                string.Join(" ", record.WantedProfessions),
                string.Join(" ", record.Languages),
                record.OtherLanguage,
                record.Region,
                record.OtherRegion,
                record.ActiveTimes,
                record.Requirements,
                record.Message))
            .OrderByDescending(record => record.UpdatedAtUtc)
            .ToArray();

        this.guildGrid.Rows.Clear();
        foreach (var record in rows)
        {
            this.guildGrid.Rows.Add(
                record.GuildName,
                string.IsNullOrWhiteSpace(record.OtherContacts)
                    ? record.PublishingPilotName
                    : $"{record.PublishingPilotName}; {record.OtherContacts}",
                string.Join(", ", record.FocusTags),
                string.Join(", ", record.WantedProfessions),
                JoinStructuredOther(record.Languages, record.OtherLanguage),
                JoinStructuredOther(
                    string.IsNullOrWhiteSpace(record.Region) ? [] : [record.Region],
                    record.OtherRegion),
                record.ActiveTimes ?? "",
                record.Requirements ?? "",
                record.Message ?? "",
                FormatRelativeTime(record.UpdatedAtUtc));
        }
    }

    private static ThemedTabPage CreateTab(string text)
    {
        return new ThemedTabPage(text);
    }

    private static SplitContainer CreateEditorSplit(
        int preferredEditorWidth,
        int minimumEditorWidth)
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.Panel1,
            BackColor = MainWindowTheme.Border,
            SplitterWidth = 5,
        };
        split.Panel1.BackColor = MainWindowTheme.Background;
        split.Panel2.BackColor = MainWindowTheme.Background;
        split.Panel1.Padding = new Padding(8);
        split.Panel2.Padding = new Padding(8);

        var initialized = false;
        split.SizeChanged += (_, _) =>
        {
            if (initialized ||
                split.ClientSize.Width <= 0)
            {
                return;
            }

            const int minimumResultsWidth = 430;
            var maximumEditorWidth =
                split.ClientSize.Width -
                minimumResultsWidth -
                split.SplitterWidth;

            if (maximumEditorWidth < minimumEditorWidth)
            {
                return;
            }

            var editorWidth = Math.Clamp(
                preferredEditorWidth,
                minimumEditorWidth,
                maximumEditorWidth);

            // SplitterDistance is clamped against the current control width.
            // Set it only after Dock layout has supplied the real size; setting
            // it in the object initializer leaves Panel1 at the tiny design-time
            // default.
            split.SplitterDistance = editorWidth;
            split.Panel1MinSize = minimumEditorWidth;
            split.Panel2MinSize = minimumResultsWidth;
            initialized = true;
        };

        return split;
    }

    private static Panel CreateSectionPanel()
    {
        return new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = MainWindowTheme.Panel,
            BorderStyle = BorderStyle.FixedSingle,
        };
    }

    private static Button CreateButton(
        string text,
        int width,
        bool primary = false)
    {
        var button = new Button
        {
            Text = text,
            Width = width,
            Height = 30,
            Margin = new Padding(6, 2, 0, 2),
        };
        MainWindowTheme.StyleButton(button, primary);
        return button;
    }

    private static ComboBox CreateComboBox(int width)
    {
        var combo = new ComboBox
        {
            Width = width,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Margin = new Padding(3, 2, 10, 2),
        };
        MainWindowTheme.StyleComboBox(combo);
        return combo;
    }

    private static TextBox CreateTextBox(int width)
    {
        var textBox = new TextBox
        {
            Width = width,
            Margin = new Padding(3, 2, 10, 2),
        };
        MainWindowTheme.StyleTextBox(textBox);
        return textBox;
    }

    private static CheckBox CreateCheckBox(string text, int width)
    {
        return new ThemedCheckBox
        {
            Text = text,
            Width = width,
            Height = 28,
            Margin = new Padding(6, 2, 8, 2),
            ForeColor = MainWindowTheme.Text,
        };
    }

    private static Label CreateFieldLabel(string text, int width)
    {
        return new Label
        {
            Text = text,
            Width = width,
            Height = 28,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = MainWindowTheme.MutedText,
            Margin = new Padding(0, 2, 0, 2),
        };
    }

    private static DataGridView CreateGrid(params string[] columns)
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            BackgroundColor = MainWindowTheme.Background,
            BorderStyle = BorderStyle.None,
            GridColor = MainWindowTheme.Border,
            ForeColor = MainWindowTheme.Text,
            RowHeadersVisible = false,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            EnableHeadersVisualStyles = false,
        };
        grid.ColumnHeadersDefaultCellStyle.BackColor = MainWindowTheme.Header;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = MainWindowTheme.Text;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = MainWindowTheme.Header;
        grid.DefaultCellStyle.BackColor = MainWindowTheme.Panel;
        grid.DefaultCellStyle.ForeColor = MainWindowTheme.Text;
        grid.DefaultCellStyle.SelectionBackColor = MainWindowTheme.ButtonHover;
        grid.DefaultCellStyle.SelectionForeColor = MainWindowTheme.Text;
        grid.AlternatingRowsDefaultCellStyle.BackColor = MainWindowTheme.ElevatedPanel;

        foreach (var column in columns)
        {
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = column,
                HeaderText = column,
                SortMode = DataGridViewColumnSortMode.Automatic,
            });
        }

        return grid;
    }

    private static TableLayoutPanel CreateScrollableEditor()
    {
        var editor = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            ColumnCount = 2,
            RowCount = 20,
            Padding = new Padding(8),
            BackColor = MainWindowTheme.Panel,
        };
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 175));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return editor;
    }

    private static ComboBox AddComboField(
        TableLayoutPanel editor,
        string label,
        int row,
        IReadOnlyList<string>? items = null)
    {
        var combo = CreateComboBox(width: 260);
        combo.Dock = DockStyle.Top;
        if (items != null)
        {
            combo.Items.AddRange(items.Cast<object>().ToArray());
        }
        AddField(editor, label, combo, row, 34);
        return combo;
    }

    private static Label AddValueLabel(
        TableLayoutPanel editor,
        string label,
        int row)
    {
        var value = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = MainWindowTheme.Text,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
        };
        AddField(editor, label, value, row, 36);
        return value;
    }

    private static CheckBox AddCheckField(
        TableLayoutPanel editor,
        string text,
        int row)
    {
        var checkBox = CreateCheckBox(text, 300);
        checkBox.Dock = DockStyle.Fill;
        checkBox.Margin = new Padding(0, 4, 0, 4);
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        editor.Controls.Add(new Label(), 0, row);
        editor.Controls.Add(checkBox, 1, row);
        return checkBox;
    }

    private static CheckedListBox AddCheckedListField(
        TableLayoutPanel editor,
        string label,
        IReadOnlyList<string> items,
        int row,
        int height)
    {
        var list = new CheckedListBox
        {
            Dock = DockStyle.Fill,
            CheckOnClick = true,
            BackColor = MainWindowTheme.ElevatedPanel,
            ForeColor = MainWindowTheme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            IntegralHeight = false,
        };
        list.Items.AddRange(items.Cast<object>().ToArray());
        AddField(editor, label, list, row, height);
        return list;
    }

    private static TextBox AddTextField(
        TableLayoutPanel editor,
        string label,
        int row,
        int maxLength)
    {
        var textBox = CreateTextBox(width: 260);
        textBox.Dock = DockStyle.Fill;
        textBox.Multiline = false;
        textBox.AcceptsReturn = false;
        textBox.ScrollBars = ScrollBars.None;
        textBox.MaxLength = maxLength;
        textBox.KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.KeyCode != Keys.Enter)
            {
                return;
            }

            eventArgs.Handled = true;
            eventArgs.SuppressKeyPress = true;
        };
        textBox.TextChanged += (_, _) => KeepSingleLine(textBox);
        AddField(editor, label, textBox, row, 34);
        return textBox;
    }

    private static void KeepSingleLine(TextBox textBox)
    {
        var normalized = ReplaceControlCharacters(textBox.Text);
        if (textBox.MaxLength > 0 && normalized.Length > textBox.MaxLength)
        {
            normalized = normalized[..textBox.MaxLength];
        }

        if (string.Equals(textBox.Text, normalized, StringComparison.Ordinal))
        {
            return;
        }

        var selectionStart = Math.Min(textBox.SelectionStart, normalized.Length);
        textBox.Text = normalized;
        textBox.SelectionStart = selectionStart;
    }

    private static void AddButtonRow(
        TableLayoutPanel editor,
        Button button,
        int row)
    {
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        editor.Controls.Add(new Label(), 0, row);
        editor.Controls.Add(button, 1, row);
    }

    private static void AddField(
        TableLayoutPanel editor,
        string label,
        Control control,
        int row,
        int height)
    {
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        var compactRow = height <= 42;
        var labelControl = new Label
        {
            Text = label,
            Dock = DockStyle.Fill,
            ForeColor = MainWindowTheme.MutedText,
            TextAlign = compactRow
                ? ContentAlignment.MiddleLeft
                : ContentAlignment.TopLeft,
            Padding = compactRow
                ? new Padding(0, 0, 8, 0)
                : new Padding(0, 7, 8, 0),
            AutoEllipsis = true,
        };
        control.Margin = new Padding(0, 4, 0, 4);
        editor.Controls.Add(labelControl, 0, row);
        editor.Controls.Add(control, 1, row);
    }

    private static Panel CreateFilterPanel(
        string label,
        out TextBox textBox)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = MainWindowTheme.Header,
            Padding = new Padding(8, 5, 8, 5),
        };
        var caption = new Label
        {
            Text = label,
            Dock = DockStyle.Left,
            Width = 85,
            ForeColor = MainWindowTheme.MutedText,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        textBox = CreateTextBox(width: 260);
        textBox.Dock = DockStyle.Fill;
        panel.Controls.Add(textBox);
        panel.Controls.Add(caption);
        return panel;
    }

    private static void ReplaceItems(
        ComboBox combo,
        IEnumerable<string> values,
        string? preferred)
    {
        var items = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        combo.Items.Clear();
        combo.Items.AddRange(items.Cast<object>().ToArray());
        combo.SelectedItem = items.FirstOrDefault(value =>
            string.Equals(value, preferred, StringComparison.OrdinalIgnoreCase));

        if (combo.SelectedIndex < 0 && combo.Items.Count > 0)
        {
            combo.SelectedIndex = 0;
        }
    }

    private static void SetCheckedItems(
        CheckedListBox list,
        IEnumerable<string> selected)
    {
        var values = selected.ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < list.Items.Count; index++)
        {
            list.SetItemChecked(
                index,
                values.Contains(list.Items[index]?.ToString() ?? ""));
        }
    }

    private static List<string> GetCheckedItems(CheckedListBox list)
    {
        return [.. list.CheckedItems
            .Cast<object>()
            .Select(item => item.ToString() ?? "")
            .Where(value => value.Length > 0)];
    }

    private static bool IsChecked(CheckedListBox list, string value)
    {
        return list.CheckedItems.Cast<object>().Any(item =>
            string.Equals(item.ToString(), value, StringComparison.OrdinalIgnoreCase));
    }

    private static void SetEnabled(bool enabled, params Control[] controls)
    {
        foreach (var control in controls)
        {
            control.Enabled = enabled;
        }
    }

    private static string FormatPilotIdentity(SocialLocalPilot pilot)
    {
        var profession = pilot.ProfessionName ?? "Profession unknown";
        var level = pilot.OverallLevel.HasValue
            ? $" · level {pilot.OverallLevel.Value}"
            : "";
        var guild = string.IsNullOrWhiteSpace(pilot.GuildName)
            ? " · no guild"
            : $" · {pilot.GuildName}";
        return $"{profession}{level}{guild}";
    }

    private static string FormatFreshness(SocialPresenceFreshness freshness)
    {
        return freshness switch
        {
            SocialPresenceFreshness.Online => "Online",
            SocialPresenceFreshness.RecentlySeen => "Recently seen",
            _ => "Offline",
        };
    }

    private static string FormatPresenceLocation(
        SocialPresenceRecord record)
    {
        if (!string.IsNullOrWhiteSpace(record.StationName))
        {
            return string.Concat("Docked at ", record.StationName);
        }

        return string.IsNullOrWhiteSpace(record.NearestNavName)
            ? ""
            : string.Concat("Near ", record.NearestNavName);
    }

    private static string FormatVisibility(SocialAtlasVisibilityMode visibility)
    {
        return visibility switch
        {
            SocialAtlasVisibilityMode.SectorOnly => "Sector",
            SocialAtlasVisibilityMode.NearNav => "Near nav",
            SocialAtlasVisibilityMode.ExactPosition => "Exact",
            _ => "Hidden",
        };
    }

    private static string FormatRelativeTime(DateTimeOffset timestamp)
    {
        var age = DateTimeOffset.UtcNow - timestamp;
        if (age < TimeSpan.Zero)
        {
            age = TimeSpan.Zero;
        }

        if (age < TimeSpan.FromMinutes(1))
        {
            return "just now";
        }

        if (age < TimeSpan.FromHours(1))
        {
            return $"{Math.Max(1, (int)age.TotalMinutes)} min ago";
        }

        if (age < TimeSpan.FromDays(1))
        {
            return $"{Math.Max(1, (int)age.TotalHours)} h ago";
        }

        return $"{Math.Max(1, (int)age.TotalDays)} d ago";
    }

    private static bool Contains(string? value, string query)
    {
        return string.IsNullOrWhiteSpace(query) ||
               (!string.IsNullOrWhiteSpace(value) &&
                value.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesSearch(string query, params string?[] values)
    {
        return string.IsNullOrWhiteSpace(query) ||
               values.Any(value => Contains(value, query));
    }

    private static string JoinStructuredOther(
        IReadOnlyList<string> values,
        string? other)
    {
        return string.Join(
            ", ",
            values
                .Concat(string.IsNullOrWhiteSpace(other) ? [] : [other])
                .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static string? NullIfWhiteSpace(string? value)
    {
        return NormalizeSingleLine(value);
    }

    private static string? NormalizeSingleLine(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = ReplaceControlCharacters(value).Trim();
        return normalized.Length == 0 ? null : normalized;
    }

    private static string ReplaceControlCharacters(string value)
    {
        var characters = value.ToCharArray();
        for (var index = 0; index < characters.Length; index++)
        {
            if (char.IsControl(characters[index]))
            {
                characters[index] = ' ';
            }
        }

        return new string(characters);
    }

    public void SelectPilot(int processId)
    {
        this.ownerProcessId = processId;
        this.RefreshLocalChoices(force: true);

        var pilot = this.clientManager.GetSocialLocalPilots()
            .FirstOrDefault(candidate =>
                candidate.IsRunning &&
                candidate.ProcessId == processId);

        if (pilot == null)
        {
            return;
        }

        SelectComboItem(this.presencePilotCombo, pilot.PilotName);
        SelectComboItem(this.lfgPilotCombo, pilot.PilotName);

        if (!string.IsNullOrWhiteSpace(pilot.GuildName))
        {
            for (var index = 0;
                 index < this.guildPublisherCombo.Items.Count;
                 index++)
            {
                if (this.guildPublisherCombo.Items[index] is
                        GuildPublisherChoice choice &&
                    string.Equals(
                        choice.PilotName,
                        pilot.PilotName,
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        choice.GuildName,
                        pilot.GuildName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    this.guildPublisherCombo.SelectedIndex = index;
                    break;
                }
            }
        }
    }

    private bool IsOwnerPilotInGame()
    {
        return this.clientManager.Clients.Any(client =>
            client.ProcessId == this.ownerProcessId &&
            client.LifecycleState == ClientLifecycleState.InGame);
    }

    private bool TryGetRunningPilot(
        string? pilotName,
        out SocialLocalPilot pilot)
    {
        var resolved = this.clientManager.GetSocialLocalPilots()
            .FirstOrDefault(candidate =>
                candidate.IsRunning &&
                string.Equals(
                    candidate.PilotName,
                    pilotName,
                    StringComparison.OrdinalIgnoreCase));

        if (resolved == null)
        {
            pilot = null!;
            return false;
        }

        pilot = resolved;
        return true;
    }

    private void ShowPilotNoLongerAvailableWarning()
    {
        ThemedMessageDialog.ShowWarning(
            this,
            "Social",
            "The selected pilot is no longer in game. Reopen Social from an in-game client.");
        this.RefreshLocalChoices(force: true);
    }

    private static void SelectComboItem(
        ComboBox combo,
        string pilotName)
    {
        for (var index = 0;
             index < combo.Items.Count;
             index++)
        {
            if (string.Equals(
                    combo.Items[index]?.ToString(),
                    pilotName,
                    StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedIndex = index;
                return;
            }
        }
    }

    private sealed record GuildPublisherChoice(
        string GuildName,
        string PilotName)
    {
        public string Key => $"{this.GuildName}|{this.PilotName}";

        public override string ToString()
        {
            return $"{this.GuildName} · {this.PilotName}";
        }
    }
}
