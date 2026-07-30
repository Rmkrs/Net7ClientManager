// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using System.Globalization;
using Net7ClientManager.Core;
using Net7ClientManager.Services;

public sealed class GameSettingsForm : ThemedForm
{
    private const string PlayerChatOption = "FONT_CHAT_LINE_00";
    private const string MessageChatOption = "FONT_CHAT_LINE_01";
    private const string PrivacyColumn = "Privacy";

    private static readonly Color fixedCellBackground =
        Color.FromArgb(13, 22, 31);

    private static readonly Color editableCellBackground =
        Color.FromArgb(18, 41, 54);

    private static readonly Color editableAlternateCellBackground =
        Color.FromArgb(20, 46, 60);

    private static readonly Color disabledCellBackground =
        Color.FromArgb(15, 27, 36);

    private static readonly object missingChatFontCellMarker = new();

    private static readonly string[] cameraOptions =
    [
        "Camera_Ship",
        "Camera_Loot",
        "Camera_Land",
        "Camera_Bridge",
        "Camera_Warp",
        "Camera_Gate",
        "Camera_Dock",
    ];

    private static readonly string[] numericOptions =
    [
        "Interface_Tooltip_Delay",
        "Graphics_Gamma",
        "Graphics_Land_Poly",
        "Graphics_Land_Detail",
        "Graphics_Space_Poly",
        "Graphics_Texture_Size",
        "Sound_Music",
        "Sound_SFX",
        "Sound_Dialog",
    ];

    private static readonly HashSet<string> numericOptionSet =
        new(numericOptions, StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> booleanOptionSet = new(
        cameraOptions.Concat(["Graphics_Default", "Doppler_Enabled"]),
        StringComparer.OrdinalIgnoreCase);

    private static readonly string[] fontNames =
    [
        "tiny5",
        "small7",
        "small9",
        "small11",
        "small12",
        "small14",
        "small17",
        "med18",
        "med20",
        "med25",
    ];

    private static readonly string[] fontScales =
    [
        "0.5", "0.6", "0.7", "0.8", "0.9", "1.0", "1.1", "1.2",
        "1.3", "1.4", "1.5", "1.6", "1.7", "1.8", "1.9", "2.0",
    ];

    private readonly ClientManager clientManager;
    private readonly WindowPlacementBinding windowPlacement;
    private readonly System.Windows.Forms.Timer onlineStateTimer = new();
    private readonly System.Windows.Forms.Timer autoSaveTimer = new();
    private readonly System.Windows.Forms.Timer statusResetTimer = new();
    private readonly List<GamePilotSettingsRow> rows = [];
    private readonly List<DataGridView> grids = [];

    private readonly Label directoryLabel = new();
    private readonly Label statusLabel = new();
    private readonly Button reloadButton = new();
    private readonly ThemedTabHost tabs = new();
    private readonly ComboBox chatResolutionComboBox = new();
    private ThemedTabPage cameraTab = null!;
    private ThemedTabPage interfaceTab = null!;
    private ThemedTabPage graphicsTab = null!;
    private ThemedTabPage soundTab = null!;
    private ThemedTabPage privacyTab = null!;
    private ThemedTabPage chatFontTab = null!;

    private DataGridView cameraGrid = null!;
    private DataGridView interfaceGrid = null!;
    private DataGridView graphicsGrid = null!;
    private DataGridView soundGrid = null!;
    private DataGridView privacyGrid = null!;
    private DataGridView chatFontGrid = null!;

    private string? outputDirectory;
    private string? onlineSignature;
    private string? transientStatus;
    private DataGridView? sliderDragGrid;
    private int sliderDragRow = -1;
    private int sliderDragColumn = -1;
    private bool isPopulating;
    private bool isSaving;
    private bool reloadAfterAutoSave;
    private ContextMenuStrip? bulkApplyMenu;

    public GameSettingsForm(
        ClientManager clientManager,
        IWin32Window preferredOwner)
    {
        this.clientManager = clientManager;

        this.Text = "Game Settings";
        this.Icon = ResourceLoader.Net7ClientManagerIcon;
        this.StartPosition = FormStartPosition.CenterParent;
        this.Size = new Size(width: 1480, height: 820);
        this.MinimumSize = new Size(width: 1080, height: 680);
        this.BackColor = MainWindowTheme.Background;
        this.ForeColor = MainWindowTheme.Text;
        this.Font = MainWindowTheme.CreateBodyFont();
        this.ConfigureWindowChrome(
            allowResize: true,
            showMinimizeButton: true,
            showMaximizeButton: true);
        this.ConfigureHelpTopic(HelpTopicIds.GameSettings);
        this.windowPlacement = clientManager.BindGlobalWindowPlacement(
            this,
            WindowPlacementIds.GameSettings,
            preferredOwner);

        this.BuildLayout();
        this.ConfigureHelpTour(this.ShowHelpTour);
        this.ConfigureGrids();

        this.Shown += this.GameSettingsForm_OnShown;
        this.FormClosing += this.GameSettingsForm_OnFormClosing;
        this.reloadButton.Click += this.ReloadButton_OnClick;
        this.chatResolutionComboBox.SelectedIndexChanged +=
            this.ChatResolutionComboBox_OnSelectedIndexChanged;

        this.onlineStateTimer.Interval = 1000;
        this.onlineStateTimer.Tick += this.OnlineStateTimer_OnTick;
        this.onlineStateTimer.Start();

        this.autoSaveTimer.Interval = 650;
        this.autoSaveTimer.Tick += this.AutoSaveTimer_OnTick;

        this.statusResetTimer.Interval = 2600;
        this.statusResetTimer.Tick += this.StatusResetTimer_OnTick;
    }


    internal void ShowHelpTour()
    {
        GuidedTourOverlay.Show(
            this,
            [
                new GuidedTourStep(
                    () => this.tabs,
                    "See every pilot's settings together",
                    "Camera, Interface, Graphics, Sound, Privacy, and Chat Font are grouped into tabs. Each row is a game setting and each pilot can be compared without logging characters in and out."),
                new GuidedTourStep(
                    () => this.cameraGrid,
                    "Change one pilot or the whole fleet",
                    "Edit a value in its pilot column to update that character. Right-click a setting to copy the chosen value to every offline pilot, so common preferences stay synchronized.",
                    () => this.tabs.SelectedPage = this.cameraTab),
                new GuidedTourStep(
                    () => this.graphicsGrid,
                    "Keep shared settings consistent",
                    "Use the same fleet-wide action for graphics, interface, sound, and privacy choices. Online pilots are protected until they are safely offline.",
                    () => this.tabs.SelectedPage = this.graphicsTab),
                new GuidedTourStep(
                    () => this.chatResolutionComboBox,
                    "Synchronize chat fonts by resolution",
                    "Choose the resolution record, adjust the chat-font values, then right-click a configured value to copy Player Chat, Game Messages, or both to the offline fleet.",
                    () => this.tabs.SelectedPage = this.chatFontTab),
                new GuidedTourStep(
                    () => this.reloadButton,
                    "Refresh after changing settings in the game",
                    "Reload from game re-reads the settings Earth & Beyond currently has. Client Manager saves supported changes automatically as you make them here."),
            ]);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.Shown -= this.GameSettingsForm_OnShown;
            this.FormClosing -= this.GameSettingsForm_OnFormClosing;
            this.reloadButton.Click -= this.ReloadButton_OnClick;
            this.chatResolutionComboBox.SelectedIndexChanged -=
                this.ChatResolutionComboBox_OnSelectedIndexChanged;
            this.onlineStateTimer.Stop();
            this.onlineStateTimer.Tick -= this.OnlineStateTimer_OnTick;
            this.onlineStateTimer.Dispose();
            this.autoSaveTimer.Stop();
            this.autoSaveTimer.Tick -= this.AutoSaveTimer_OnTick;
            this.autoSaveTimer.Dispose();
            this.statusResetTimer.Stop();
            this.statusResetTimer.Tick -= this.StatusResetTimer_OnTick;
            this.statusResetTimer.Dispose();
            this.bulkApplyMenu?.Dispose();
            this.bulkApplyMenu = null;
            this.windowPlacement.Dispose();
        }

        base.Dispose(disposing);
    }

    private void BuildLayout()
    {
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 112,
            Padding = new Padding(20, 14, 20, 12),
            BackColor = MainWindowTheme.Header,
        };

        var headerLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
        };
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));

        var headingPanel = new Panel { Dock = DockStyle.Fill };
        headingPanel.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "Pilot game settings",
            ForeColor = MainWindowTheme.Text,
            Font = MainWindowTheme.CreateHeadingFont(18.0f),
            Location = new Point(0, 0),
        });
        headingPanel.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "Compare and update settings already created by Earth & Beyond. Changes save automatically.",
            ForeColor = MainWindowTheme.MutedText,
            Location = new Point(1, 34),
        });
        this.directoryLabel.AutoSize = false;
        this.directoryLabel.AutoEllipsis = true;
        this.directoryLabel.ForeColor = MainWindowTheme.MutedText;
        this.directoryLabel.Location = new Point(1, 59);
        this.directoryLabel.Size = new Size(760, 22);
        headingPanel.Controls.Add(this.directoryLabel);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 18, 0, 0),
        };

        this.reloadButton.Text = "Reload from game";
        this.reloadButton.Width = 132;
        this.reloadButton.Height = 34;
        this.reloadButton.Margin = new Padding(8, 0, 0, 0);
        MainWindowTheme.StyleButton(this.reloadButton);

        actions.Controls.Add(this.reloadButton);

        headerLayout.Controls.Add(headingPanel, 0, 0);
        headerLayout.Controls.Add(actions, 1, 0);
        header.Controls.Add(headerLayout);

        var footer = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 36,
            Padding = new Padding(18, 8, 18, 8),
            BackColor = MainWindowTheme.Header,
        };
        this.statusLabel.Dock = DockStyle.Fill;
        this.statusLabel.ForeColor = MainWindowTheme.MutedText;
        this.statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        footer.Controls.Add(this.statusLabel);

        this.tabs.Dock = DockStyle.Fill;

        this.cameraGrid = this.CreateGrid();
        this.interfaceGrid = this.CreateGrid();
        this.graphicsGrid = this.CreateGrid();
        this.soundGrid = this.CreateGrid();
        this.privacyGrid = this.CreateGrid();
        this.chatFontGrid = this.CreateGrid();

        this.cameraTab = this.CreateGridTab("Camera", this.cameraGrid);
        this.interfaceTab = this.CreateGridTab("Interface", this.interfaceGrid);
        this.graphicsTab = this.CreateGridTab("Graphics", this.graphicsGrid);
        this.soundTab = this.CreateGridTab("Sound", this.soundGrid);
        this.privacyTab = this.CreateGridTab("Privacy", this.privacyGrid);
        this.chatFontTab = this.CreateChatFontTab();
        this.tabs.AddPage(this.cameraTab);
        this.tabs.AddPage(this.interfaceTab);
        this.tabs.AddPage(this.graphicsTab);
        this.tabs.AddPage(this.soundTab);
        this.tabs.AddPage(this.privacyTab);
        this.tabs.AddPage(this.chatFontTab);

        this.Controls.Add(this.tabs);
        this.Controls.Add(footer);
        this.Controls.Add(header);
    }

    private ThemedTabPage CreateGridTab(string title, DataGridView grid)
    {
        var tab = new ThemedTabPage(title)
        {
            BackColor = MainWindowTheme.Background,
            ForeColor = MainWindowTheme.Text,
            Padding = new Padding(12),
        };

        var hintPanel = this.CreateBulkHintPanel();
        tab.Controls.Add(grid);
        tab.Controls.Add(hintPanel);
        grid.Dock = DockStyle.Fill;
        return tab;
    }

    private Panel CreateBulkHintPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 34,
            BackColor = MainWindowTheme.Background,
        };
        panel.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "Drag sliders or click values to edit. Right-click a setting to apply that value to every offline pilot.",
            ForeColor = MainWindowTheme.MutedText,
            Location = new Point(0, 8),
        });
        return panel;
    }

    private ThemedTabPage CreateChatFontTab()
    {
        var tab = new ThemedTabPage("Chat Font")
        {
            BackColor = MainWindowTheme.Background,
            ForeColor = MainWindowTheme.Text,
            Padding = new Padding(12),
        };

        var resolutionPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 70,
            BackColor = MainWindowTheme.Background,
        };
        resolutionPanel.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "Resolution",
            ForeColor = MainWindowTheme.MutedText,
            Location = new Point(0, 14),
        });
        this.chatResolutionComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        this.chatResolutionComboBox.Location = new Point(82, 9);
        this.chatResolutionComboBox.Width = 150;
        MainWindowTheme.StyleComboBox(this.chatResolutionComboBox);
        resolutionPanel.Controls.Add(this.chatResolutionComboBox);
        resolutionPanel.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "Only existing resolution records are editable; unavailable rows are marked as not configured.",
            ForeColor = MainWindowTheme.MutedText,
            Location = new Point(250, 14),
        });
        resolutionPanel.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "Right-click any configured chat-font value to copy the complete Player Chat setup, Game Message setup, or both. Missing resolution records are created.",
            ForeColor = MainWindowTheme.MutedText,
            Location = new Point(0, 43),
        });

        this.chatFontGrid.Dock = DockStyle.Fill;
        tab.Controls.Add(this.chatFontGrid);
        tab.Controls.Add(resolutionPanel);
        return tab;
    }

    private DataGridView CreateGrid()
    {
        var grid = new DataGridView
        {
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AutoGenerateColumns = false,
            BackgroundColor = MainWindowTheme.Panel,
            BorderStyle = BorderStyle.None,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single,
            ColumnHeadersHeight = 38,
            EditMode = DataGridViewEditMode.EditOnEnter,
            EnableHeadersVisualStyles = false,
            GridColor = MainWindowTheme.Border,
            MultiSelect = false,
            RowHeadersVisible = false,
            RowTemplate = { Height = 32 },
            SelectionMode = DataGridViewSelectionMode.CellSelect,
            ShowCellToolTips = true,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = MainWindowTheme.Panel,
                ForeColor = MainWindowTheme.Text,
                SelectionBackColor = MainWindowTheme.ButtonHover,
                SelectionForeColor = MainWindowTheme.Text,
                NullValue = "",
            },
            AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = MainWindowTheme.ElevatedPanel,
                ForeColor = MainWindowTheme.Text,
                SelectionBackColor = MainWindowTheme.ButtonHover,
                SelectionForeColor = MainWindowTheme.Text,
            },
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = MainWindowTheme.Header,
                ForeColor = MainWindowTheme.Text,
                SelectionBackColor = MainWindowTheme.Header,
                SelectionForeColor = MainWindowTheme.Text,
                Font = MainWindowTheme.CreateHeadingFont(9.0f),
                Alignment = DataGridViewContentAlignment.MiddleCenter,
                WrapMode = DataGridViewTriState.True,
            },
        };

        grid.CellValueChanged += this.Grid_OnCellValueChanged;
        grid.CurrentCellDirtyStateChanged +=
            this.Grid_OnCurrentCellDirtyStateChanged;
        grid.CellBeginEdit += this.Grid_OnCellBeginEdit;
        grid.CellMouseDown += this.Grid_OnCellMouseDown;
        grid.CellMouseClick += this.Grid_OnCellMouseClick;
        grid.CellPainting += this.Grid_OnCellPainting;
        grid.DataError += this.Grid_OnDataError;
        grid.EditingControlShowing += this.Grid_OnEditingControlShowing;
        grid.KeyDown += this.Grid_OnKeyDown;
        grid.MouseMove += this.Grid_OnMouseMove;
        grid.MouseUp += this.Grid_OnMouseUp;
        this.grids.Add(grid);
        return grid;
    }

    private void ConfigureGrids()
    {
        this.AddIdentityColumns(this.cameraGrid);
        this.AddBooleanColumn(this.cameraGrid, "Camera_Ship", "Keep ship visible", 132);
        this.AddBooleanColumn(this.cameraGrid, "Camera_Loot", "Looting", 100);
        this.AddBooleanColumn(this.cameraGrid, "Camera_Land", "Landing", 100);
        this.AddBooleanColumn(this.cameraGrid, "Camera_Bridge", "Bridge", 100);
        this.AddBooleanColumn(this.cameraGrid, "Camera_Warp", "Warping", 100);
        this.AddBooleanColumn(this.cameraGrid, "Camera_Gate", "Gating", 100);
        this.AddBooleanColumn(this.cameraGrid, "Camera_Dock", "Docking", 100);

        this.AddIdentityColumns(this.interfaceGrid);
        this.AddNumericColumn(
            this.interfaceGrid,
            "Interface_Tooltip_Delay",
            "Tooltip delay",
            320);

        this.AddIdentityColumns(this.graphicsGrid);
        this.AddBooleanColumn(
            this.graphicsGrid,
            "Graphics_Default",
            "Recommended",
            116);
        this.AddNumericColumn(this.graphicsGrid, "Graphics_Gamma", "Gamma", 188);
        this.AddNumericColumn(this.graphicsGrid, "Graphics_Land_Poly", "Land poly", 188);
        this.AddNumericColumn(this.graphicsGrid, "Graphics_Land_Detail", "Land detail", 188);
        this.AddNumericColumn(this.graphicsGrid, "Graphics_Space_Poly", "Space poly", 188);
        this.AddNumericColumn(this.graphicsGrid, "Graphics_Texture_Size", "Texture size", 188);

        this.AddIdentityColumns(this.soundGrid);
        this.AddBooleanColumn(this.soundGrid, "Doppler_Enabled", "Doppler", 100);
        this.AddNumericColumn(this.soundGrid, "Sound_Music", "Music", 220);
        this.AddNumericColumn(this.soundGrid, "Sound_SFX", "Effects", 220);
        this.AddNumericColumn(this.soundGrid, "Sound_Dialog", "Dialog", 220);

        this.AddIdentityColumns(this.privacyGrid);
        this.privacyGrid.Columns.Add(new DataGridViewComboBoxColumn
        {
            Name = PrivacyColumn,
            HeaderText = "Who can see me online?",
            Width = 190,
            FlatStyle = FlatStyle.Flat,
            DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
            DisplayStyleForCurrentCellOnly = true,
            Items = { "Everyone", "Friends only" },
            SortMode = DataGridViewColumnSortMode.NotSortable,
            ToolTipText = "Choose who can see this pilot online. Right-click to apply the value to every offline pilot.",
        });

        this.AddIdentityColumns(this.chatFontGrid);
        this.AddComboColumn(this.chatFontGrid, "PlayerFont", "Player font", 110, fontNames);
        this.AddComboColumn(this.chatFontGrid, "PlayerScale", "Player scale", 90, fontScales);
        this.AddComboColumn(this.chatFontGrid, "PlayerLetter", "Player letter", 92, Enumerable.Range(-3, 8).Cast<object>());
        this.AddComboColumn(this.chatFontGrid, "PlayerLine", "Player line", 92, Enumerable.Range(-12, 18).Cast<object>());
        this.AddComboColumn(this.chatFontGrid, "MessageFont", "Message font", 110, fontNames);
        this.AddComboColumn(this.chatFontGrid, "MessageScale", "Message scale", 98, fontScales);
        this.AddComboColumn(this.chatFontGrid, "MessageLetter", "Message letter", 98, Enumerable.Range(-3, 8).Cast<object>());
        this.AddComboColumn(this.chatFontGrid, "MessageLine", "Message line", 98, Enumerable.Range(-12, 18).Cast<object>());
    }

    private void AddIdentityColumns(DataGridView grid)
    {
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Pilot",
            HeaderText = "Pilot",
            Width = 180,
            Frozen = true,
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.Automatic,
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Status",
            HeaderText = "Status",
            Width = 112,
            Frozen = true,
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable,
        });
    }

    private void AddBooleanColumn(
        DataGridView grid,
        string name,
        string title,
        int width)
    {
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = title,
            Width = width,
            ReadOnly = false,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            ToolTipText = "Click to toggle. Right-click to apply this value to every offline pilot.",
        });
    }

    private void AddNumericColumn(
        DataGridView grid,
        string name,
        string title,
        int width)
    {
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = title,
            Width = width,
            ReadOnly = false,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            ToolTipText = "Drag to change. Arrow keys adjust by 1; Page Up/Down by 10. Right-click to apply this value to every offline pilot.",
            DefaultCellStyle = new DataGridViewCellStyle
            {
                Alignment = DataGridViewContentAlignment.MiddleCenter,
            },
        });
    }

    private void AddComboColumn(
        DataGridView grid,
        string name,
        string title,
        int width,
        IEnumerable<object> items)
    {
        var column = new DataGridViewComboBoxColumn
        {
            Name = name,
            HeaderText = title,
            Width = width,
            FlatStyle = FlatStyle.Flat,
            DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
            DisplayStyleForCurrentCellOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            ToolTipText = "Choose a value. Right-click to apply it to every offline pilot.",
        };
        column.Items.AddRange(items.ToArray());
        grid.Columns.Add(column);
    }

    private void GameSettingsForm_OnShown(object? sender, EventArgs e)
    {
        this.RefreshObservedOutputDirectory(loadWhenChanged: true);
    }

    private void GameSettingsForm_OnFormClosing(
        object? sender,
        FormClosingEventArgs e)
    {
        this.autoSaveTimer.Stop();

        if (this.HasDirtyChanges())
        {
            this.SaveChanges(showErrors: true);
        }

        if (!this.HasDirtyChanges())
        {
            return;
        }

        e.Cancel = MessageBox.Show(
            this,
            "Some game-setting changes could not be saved. Close this window anyway?",
            "Game Settings",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning) != DialogResult.Yes;
    }

    private void ReloadButton_OnClick(object? sender, EventArgs e)
    {
        this.autoSaveTimer.Stop();

        if (this.HasDirtyChanges())
        {
            this.SaveChanges(showErrors: true);

            if (this.HasDirtyChanges())
            {
                return;
            }
        }

        if (!this.RefreshObservedOutputDirectory(loadWhenChanged: true) &&
            this.outputDirectory != null)
        {
            this.LoadRows();
        }
    }

    private void AutoSaveTimer_OnTick(object? sender, EventArgs e)
    {
        this.autoSaveTimer.Stop();
        this.SaveChanges(showErrors: true);
    }

    private void StatusResetTimer_OnTick(object? sender, EventArgs e)
    {
        this.statusResetTimer.Stop();
        this.transientStatus = null;
        this.UpdateStatus();
    }

    private void ChatResolutionComboBox_OnSelectedIndexChanged(
        object? sender,
        EventArgs e)
    {
        if (this.isPopulating)
        {
            return;
        }

        var resolution = this.chatResolutionComboBox.SelectedItem as string;
        this.clientManager.SetGameSettingsChatFontResolution(resolution);
        this.PopulateChatFontGrid();
    }

    private void OnlineStateTimer_OnTick(object? sender, EventArgs e)
    {
        if (this.RefreshObservedOutputDirectory(loadWhenChanged: true))
        {
            return;
        }

        var identities = GamePilotSettingsCatalog.ResolveOnlineIdentities(
            this.clientManager);
        var signature = string.Join(
            ",",
            identities.OrderBy(identity => identity));

        if (string.Equals(signature, this.onlineSignature, StringComparison.Ordinal))
        {
            return;
        }

        this.onlineSignature = signature;
        var becameOffline = false;

        foreach (var row in this.rows)
        {
            var wasOnline = row.File.IsOnline;
            row.File.IsOnline = identities.Contains(row.File.Identity);
            becameOffline |= wasOnline && !row.File.IsOnline;
        }

        if (becameOffline && !this.HasDirtyChanges())
        {
            this.LoadRows();
            return;
        }

        if (becameOffline)
        {
            this.reloadAfterAutoSave = true;
            this.ScheduleAutoSave();
        }

        this.ApplyAllRowStates();
        this.UpdateStatus();
    }

    private bool RefreshObservedOutputDirectory(bool loadWhenChanged)
    {
        var observedDirectory = this.clientManager.LocateGameOutputDirectory();

        if (string.Equals(
                this.outputDirectory,
                observedDirectory,
                StringComparison.OrdinalIgnoreCase))
        {
            if (this.outputDirectory == null && this.rows.Count == 0)
            {
                this.SetEmptyState(
                    "Launch an Earth & Beyond client once so N7CM can locate its per-pilot settings.");
            }

            return false;
        }

        if (this.HasDirtyChanges())
        {
            this.ScheduleAutoSave();
            this.transientStatus =
                "A different installation was observed; saving current changes before switching.";
            this.UpdateStatus();
            return false;
        }

        this.outputDirectory = observedDirectory;

        if (!loadWhenChanged)
        {
            return true;
        }

        if (this.outputDirectory == null)
        {
            this.SetEmptyState(
                "The previously observed Earth & Beyond installation is unavailable. Launch a client from the current installation once.");
        }
        else
        {
            this.LoadRows();
        }

        return true;
    }

    private void LoadRows()
    {
        this.autoSaveTimer.Stop();

        if (this.outputDirectory == null ||
            !Directory.Exists(this.outputDirectory))
        {
            this.SetEmptyState(
                "The previously observed Earth & Beyond installation is unavailable. Launch a client from the current installation once.");
            return;
        }

        this.rows.Clear();
        var files = GamePilotSettingsCatalog.Load(
            this.outputDirectory,
            this.clientManager);

        foreach (var file in files)
        {
            if (!GameOptionsDocument.TryRead(
                    file.FilePath,
                    out var document,
                    out _))
            {
                continue;
            }

            var row = new GamePilotSettingsRow
            {
                File = file,
                Document = document,
            };
            this.LoadValues(row);
            this.rows.Add(row);
        }

        this.onlineSignature = string.Join(
            ",",
            this.rows
                .Where(row => row.File.IsOnline)
                .Select(row => row.File.Identity)
                .OrderBy(identity => identity));
        this.directoryLabel.Text = this.outputDirectory;
        this.PopulateResolutionChoices();
        this.PopulateAllGrids();
        this.UpdateStatus();
    }

    private void SetEmptyState(string message)
    {
        this.autoSaveTimer.Stop();
        this.statusResetTimer.Stop();
        this.transientStatus = null;
        this.rows.Clear();
        this.directoryLabel.Text = this.outputDirectory ??
            "Waiting for the first witnessed Earth & Beyond client launch.";
        this.isPopulating = true;

        try
        {
            foreach (var grid in this.grids)
            {
                grid.Rows.Clear();
            }

            this.chatResolutionComboBox.Items.Clear();
        }
        finally
        {
            this.isPopulating = false;
        }

        this.statusLabel.Text = message;
        this.reloadButton.Enabled = this.outputDirectory != null &&
            Directory.Exists(this.outputDirectory);
    }

    private void LoadValues(GamePilotSettingsRow row)
    {
        foreach (var option in cameraOptions)
        {
            row.Values[option] = row.Document.GetBoolean(option);
        }

        row.Values["Graphics_Default"] =
            row.Document.GetBoolean("Graphics_Default");
        row.Values["Doppler_Enabled"] =
            row.Document.GetBoolean("Doppler_Enabled");
        row.Values["RestrictedStatus"] =
            row.Document.GetBoolean("RestrictedStatus");

        foreach (var option in numericOptions)
        {
            row.Values[option] = row.Document.GetDecimal(option);
        }

        foreach (var pair in ChatFontValueCodec.Parse(
                     row.Document.GetValue(PlayerChatOption)))
        {
            row.PlayerChatFonts[pair.Key] = pair.Value;
        }

        foreach (var pair in ChatFontValueCodec.Parse(
                     row.Document.GetValue(MessageChatOption)))
        {
            row.MessageChatFonts[pair.Key] = pair.Value;
        }
    }

    private void PopulateResolutionChoices()
    {
        var counts = this.rows
            .SelectMany(row => row.PlayerChatFonts.Keys
                .Concat(row.MessageChatFonts.Keys))
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Count(),
                StringComparer.OrdinalIgnoreCase);
        var choices = counts.Keys
            .OrderBy(value => ResolutionSortKey(value))
            .ThenBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var saved = this.clientManager.GameSettingsEditorSettings
            .ChatFontResolution;
        var selected = choices.FirstOrDefault(choice => string.Equals(
                           choice,
                           saved,
                           StringComparison.OrdinalIgnoreCase))
                       ?? choices
                           .OrderByDescending(choice => counts[choice])
                           .ThenBy(choice => ResolutionSortKey(choice))
                           .FirstOrDefault();

        this.isPopulating = true;

        try
        {
            this.chatResolutionComboBox.Items.Clear();
            this.chatResolutionComboBox.Items.AddRange(choices);
            this.chatResolutionComboBox.SelectedItem = selected;
        }
        finally
        {
            this.isPopulating = false;
        }

        if (selected != null &&
            !string.Equals(saved, selected, StringComparison.OrdinalIgnoreCase))
        {
            this.clientManager.SetGameSettingsChatFontResolution(selected);
        }
    }

    private void PopulateAllGrids()
    {
        this.isPopulating = true;

        try
        {
            this.PopulateOrdinaryGrid(
                this.cameraGrid,
                cameraOptions);
            this.PopulateOrdinaryGrid(
                this.interfaceGrid,
                ["Interface_Tooltip_Delay"]);
            this.PopulateOrdinaryGrid(
                this.graphicsGrid,
                [
                    "Graphics_Default",
                    "Graphics_Gamma",
                    "Graphics_Land_Poly",
                    "Graphics_Land_Detail",
                    "Graphics_Space_Poly",
                    "Graphics_Texture_Size",
                ]);
            this.PopulateOrdinaryGrid(
                this.soundGrid,
                [
                    "Doppler_Enabled",
                    "Sound_Music",
                    "Sound_SFX",
                    "Sound_Dialog",
                ]);
            this.PopulatePrivacyGrid();
            this.PopulateChatFontGridCore();
        }
        finally
        {
            this.isPopulating = false;
        }

        this.ApplyAllRowStates();
    }

    private void PopulateOrdinaryGrid(
        DataGridView grid,
        IReadOnlyList<string> options)
    {
        grid.Rows.Clear();

        foreach (var model in this.rows)
        {
            var index = grid.Rows.Add();
            var row = grid.Rows[index];
            row.Tag = model;
            this.PopulateIdentityCells(row, model);

            foreach (var option in options)
            {
                var value = model.Values.GetValueOrDefault(option);
                row.Cells[option].Value = value;
            }
        }
    }

    private void PopulatePrivacyGrid()
    {
        this.privacyGrid.Rows.Clear();

        foreach (var model in this.rows)
        {
            var index = this.privacyGrid.Rows.Add();
            var row = this.privacyGrid.Rows[index];
            row.Tag = model;
            this.PopulateIdentityCells(row, model);
            row.Cells[PrivacyColumn].Value =
                model.Values.GetValueOrDefault("RestrictedStatus") switch
                {
                    true => "Friends only",
                    false => "Everyone",
                    _ => null,
                };
        }
    }

    private void PopulateChatFontGrid()
    {
        this.isPopulating = true;

        try
        {
            this.PopulateChatFontGridCore();
        }
        finally
        {
            this.isPopulating = false;
        }

        this.ApplyAllRowStates();
    }

    private void PopulateChatFontGridCore()
    {
        this.chatFontGrid.Rows.Clear();
        var resolution = this.SelectedChatResolution;

        foreach (var model in this.rows)
        {
            var index = this.chatFontGrid.Rows.Add();
            var row = this.chatFontGrid.Rows[index];
            row.Tag = model;
            this.PopulateIdentityCells(row, model);

            if (resolution != null &&
                model.PlayerChatFonts.TryGetValue(
                    resolution,
                    out var player))
            {
                row.Cells["PlayerFont"].Value = player.Font;
                row.Cells["PlayerScale"].Value =
                    player.Scale.ToString("0.0", CultureInfo.InvariantCulture);
                row.Cells["PlayerLetter"].Value = player.LetterSpacing;
                row.Cells["PlayerLine"].Value = player.LineSpacing;
            }

            if (resolution != null &&
                model.MessageChatFonts.TryGetValue(
                    resolution,
                    out var message))
            {
                row.Cells["MessageFont"].Value = message.Font;
                row.Cells["MessageScale"].Value =
                    message.Scale.ToString("0.0", CultureInfo.InvariantCulture);
                row.Cells["MessageLetter"].Value = message.LetterSpacing;
                row.Cells["MessageLine"].Value = message.LineSpacing;
            }
        }
    }

    private void PopulateIdentityCells(
        DataGridViewRow gridRow,
        GamePilotSettingsRow model)
    {
        gridRow.Cells["Pilot"].Value = model.File.DisplayName;
        gridRow.Cells["Pilot"].Style = new DataGridViewCellStyle
        {
            BackColor = fixedCellBackground,
            ForeColor = MainWindowTheme.Text,
            SelectionBackColor = fixedCellBackground,
            SelectionForeColor = MainWindowTheme.Text,
            Padding = new Padding(6, 0, 4, 0),
        };
        gridRow.Cells["Pilot"].ToolTipText = string.Concat(
            model.File.SourceName == null
                ? "No pilot name was found in Pilot Archive or shortcut.ini."
                : model.File.SourceName,
            Environment.NewLine,
            "Settings identity: ",
            model.File.Identity.ToString(CultureInfo.InvariantCulture),
            Environment.NewLine,
            Path.GetFileName(model.File.FilePath));
        gridRow.Cells["Status"].Value = model.File.IsOnline
            ? "Online · read-only"
            : "Offline";
        gridRow.Cells["Status"].Style = new DataGridViewCellStyle
        {
            BackColor = fixedCellBackground,
            ForeColor = model.File.IsOnline
                ? MainWindowTheme.Warning
                : MainWindowTheme.MutedText,
            SelectionBackColor = fixedCellBackground,
            SelectionForeColor = model.File.IsOnline
                ? MainWindowTheme.Warning
                : MainWindowTheme.MutedText,
            Alignment = DataGridViewContentAlignment.MiddleCenter,
        };
    }

    private void ApplyAllRowStates()
    {
        foreach (var grid in this.grids)
        {
            foreach (DataGridViewRow gridRow in grid.Rows)
            {
                if (gridRow.Tag is GamePilotSettingsRow model)
                {
                    this.ApplyRowState(grid, gridRow, model);
                }
            }
        }
    }

    private void ApplyRowState(
        DataGridView grid,
        DataGridViewRow gridRow,
        GamePilotSettingsRow model)
    {
        gridRow.Cells["Status"].Value = model.File.IsOnline
            ? "Online · read-only"
            : "Offline";
        gridRow.Cells["Pilot"].Style.ForeColor = model.File.IsOnline
            ? MainWindowTheme.DisabledText
            : MainWindowTheme.Text;
        gridRow.Cells["Pilot"].Style.SelectionForeColor =
            gridRow.Cells["Pilot"].Style.ForeColor;
        gridRow.Cells["Status"].Style.ForeColor = model.File.IsOnline
            ? MainWindowTheme.Warning
            : MainWindowTheme.MutedText;
        gridRow.Cells["Status"].Style.SelectionForeColor =
            gridRow.Cells["Status"].Style.ForeColor;

        foreach (DataGridViewCell cell in gridRow.Cells)
        {
            if (cell.OwningColumn.Name is "Pilot" or "Status")
            {
                cell.ReadOnly = true;
                continue;
            }

            var missing = false;

            if (grid == this.chatFontGrid)
            {
                var resolution = this.SelectedChatResolution;
                var isPlayer = cell.OwningColumn.Name.StartsWith(
                    "Player",
                    StringComparison.Ordinal);
                missing = resolution == null ||
                          (isPlayer
                              ? !model.PlayerChatFonts.ContainsKey(resolution)
                              : !model.MessageChatFonts.ContainsKey(resolution));

                if (missing)
                {
                    cell.ToolTipText = resolution == null
                        ? "No chat-font resolution records were found."
                        : string.Concat(
                            "No ",
                            resolution,
                            " record exists in this file. Copy a complete setup from another pilot, or open Chat Font in-game at that resolution to create it.");
                }
            }
            else if (grid == this.privacyGrid)
            {
                missing = model.Values.GetValueOrDefault(
                    "RestrictedStatus") == null;
            }
            else
            {
                missing = model.Values.GetValueOrDefault(
                    cell.OwningColumn.Name) == null;
            }

            cell.ReadOnly = model.File.IsOnline || missing;
            cell.Tag = grid == this.chatFontGrid && missing
                ? missingChatFontCellMarker
                : null;

            if (cell is DataGridViewComboBoxCell comboCell)
            {
                comboCell.DisplayStyle = cell.ReadOnly
                    ? DataGridViewComboBoxDisplayStyle.Nothing
                    : DataGridViewComboBoxDisplayStyle.DropDownButton;
                comboCell.DisplayStyleForCurrentCellOnly = !cell.ReadOnly;
            }

            var editableBackground = gridRow.Index % 2 == 0
                ? editableCellBackground
                : editableAlternateCellBackground;
            cell.Style.BackColor = cell.ReadOnly
                ? disabledCellBackground
                : editableBackground;
            cell.Style.ForeColor = cell.ReadOnly
                ? MainWindowTheme.DisabledText
                : MainWindowTheme.Text;
            cell.Style.SelectionBackColor = cell.ReadOnly
                ? disabledCellBackground
                : MainWindowTheme.ButtonHover;
            cell.Style.SelectionForeColor = cell.ReadOnly
                ? MainWindowTheme.DisabledText
                : MainWindowTheme.Text;

            if (!missing)
            {
                cell.ToolTipText = cell.OwningColumn.ToolTipText;
            }

            if (missing && string.IsNullOrWhiteSpace(cell.ToolTipText))
            {
                cell.ToolTipText = "This setting is not present in the pilot's options file.";
            }
        }
    }

    private void Grid_OnCurrentCellDirtyStateChanged(
        object? sender,
        EventArgs e)
    {
        if (sender is DataGridView { IsCurrentCellDirty: true } grid)
        {
            grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        }
    }

    private void Grid_OnCellBeginEdit(
        object? sender,
        DataGridViewCellCancelEventArgs e)
    {
        if (sender is not DataGridView grid ||
            e.ColumnIndex < 0)
        {
            return;
        }

        var columnName = grid.Columns[e.ColumnIndex].Name;

        if (numericOptionSet.Contains(columnName) ||
            booleanOptionSet.Contains(columnName))
        {
            e.Cancel = true;
        }
    }

    private void Grid_OnCellMouseDown(
        object? sender,
        DataGridViewCellMouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left ||
            sender is not DataGridView grid ||
            e.RowIndex < 0 ||
            e.ColumnIndex < 0 ||
            !numericOptionSet.Contains(grid.Columns[e.ColumnIndex].Name) ||
            grid.Rows[e.RowIndex].Cells[e.ColumnIndex].ReadOnly)
        {
            return;
        }

        grid.CurrentCell = grid.Rows[e.RowIndex].Cells[e.ColumnIndex];
        this.sliderDragGrid = grid;
        this.sliderDragRow = e.RowIndex;
        this.sliderDragColumn = e.ColumnIndex;
        grid.Capture = true;
        var bounds = grid.GetCellDisplayRectangle(
            e.ColumnIndex,
            e.RowIndex,
            cutOverflow: false);
        this.SetSliderValueFromClientX(
            grid,
            e.RowIndex,
            e.ColumnIndex,
            bounds.Left + e.X);
    }

    private void Grid_OnCellMouseClick(
        object? sender,
        DataGridViewCellMouseEventArgs e)
    {
        if (sender is not DataGridView grid ||
            e.RowIndex < 0 ||
            e.ColumnIndex < 0)
        {
            return;
        }

        var cell = grid.Rows[e.RowIndex].Cells[e.ColumnIndex];
        var columnName = grid.Columns[e.ColumnIndex].Name;

        if (e.Button == MouseButtons.Left &&
            booleanOptionSet.Contains(columnName) &&
            !cell.ReadOnly)
        {
            grid.CurrentCell = cell;
            cell.Value = cell.Value is not true;
            grid.InvalidateCell(cell);
            return;
        }

        if (e.Button == MouseButtons.Right &&
            columnName is not "Pilot" and not "Status" &&
            cell.Value != null)
        {
            this.ShowBulkApplyMenu(grid, e.RowIndex, e.ColumnIndex);
        }
    }

    private void Grid_OnCellPainting(
        object? sender,
        DataGridViewCellPaintingEventArgs e)
    {
        if (sender is not DataGridView grid ||
            e.RowIndex < 0 ||
            e.ColumnIndex < 0)
        {
            return;
        }

        var columnName = grid.Columns[e.ColumnIndex].Name;
        var cell = grid.Rows[e.RowIndex].Cells[e.ColumnIndex];

        if (grid == this.chatFontGrid &&
            ReferenceEquals(cell.Tag, missingChatFontCellMarker))
        {
            this.PaintMissingChatFontCell(grid, e);
            return;
        }

        if (booleanOptionSet.Contains(columnName))
        {
            this.PaintBooleanCell(grid, e);
            return;
        }

        if (numericOptionSet.Contains(columnName))
        {
            this.PaintSliderCell(grid, e);
        }
    }

    private void Grid_OnEditingControlShowing(
        object? sender,
        DataGridViewEditingControlShowingEventArgs e)
    {
        if (e.Control is ComboBox comboBox)
        {
            MainWindowTheme.StyleComboBox(comboBox);
            comboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        }
    }

    private void Grid_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not DataGridView grid ||
            grid.CurrentCell == null ||
            grid.CurrentCell.RowIndex < 0 ||
            grid.CurrentCell.ColumnIndex < 0 ||
            grid.CurrentCell.ReadOnly)
        {
            return;
        }

        var columnName = grid.CurrentCell.OwningColumn.Name;

        if (booleanOptionSet.Contains(columnName) && e.KeyCode == Keys.Space)
        {
            grid.CurrentCell.Value = grid.CurrentCell.Value is not true;
            grid.InvalidateCell(grid.CurrentCell);
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }

        if (!numericOptionSet.Contains(columnName))
        {
            return;
        }

        if (!TryConvertToDecimal(grid.CurrentCell.Value, out var current))
        {
            current = 0;
        }

        current = NormalizeSliderValue(current);

        decimal? next = e.KeyCode switch
        {
            Keys.Left or Keys.Down => current - 1,
            Keys.Right or Keys.Up => current + 1,
            Keys.PageDown => current - 10,
            Keys.PageUp => current + 10,
            Keys.Home => 0,
            Keys.End => 99,
            _ => null,
        };

        if (!next.HasValue)
        {
            return;
        }

        this.SetNumericCellValue(
            grid,
            grid.CurrentCell.RowIndex,
            grid.CurrentCell.ColumnIndex,
            Math.Clamp(next.Value, 0, 99));
        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    private void Grid_OnMouseMove(object? sender, MouseEventArgs e)
    {
        if (sender is not DataGridView grid ||
            this.sliderDragGrid != grid ||
            this.sliderDragRow < 0 ||
            this.sliderDragColumn < 0 ||
            e.Button != MouseButtons.Left)
        {
            return;
        }

        this.SetSliderValueFromClientX(
            grid,
            this.sliderDragRow,
            this.sliderDragColumn,
            e.X);
    }

    private void Grid_OnMouseUp(object? sender, MouseEventArgs e)
    {
        var wasDragging = this.sliderDragGrid != null;

        if (this.sliderDragGrid != null)
        {
            this.sliderDragGrid.Capture = false;
        }

        this.sliderDragGrid = null;
        this.sliderDragRow = -1;
        this.sliderDragColumn = -1;

        if (wasDragging && this.HasDirtyChanges())
        {
            this.ScheduleAutoSave();
        }
    }

    private void PaintMissingChatFontCell(
        DataGridView grid,
        DataGridViewCellPaintingEventArgs e)
    {
        e.PaintBackground(e.CellBounds, true);
        e.Paint(e.CellBounds, DataGridViewPaintParts.Border);
        var columnName = grid.Columns[e.ColumnIndex].Name;
        var text = columnName is "PlayerFont" or "MessageFont"
            ? "Not configured"
            : "—";
        TextRenderer.DrawText(
            e.Graphics,
            text,
            e.CellStyle.Font ?? grid.Font,
            e.CellBounds,
            MainWindowTheme.DisabledText,
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPadding |
            TextFormatFlags.SingleLine);
        e.Handled = true;
    }

    private void PaintBooleanCell(
        DataGridView grid,
        DataGridViewCellPaintingEventArgs e)
    {
        e.PaintBackground(e.CellBounds, true);
        e.Paint(e.CellBounds, DataGridViewPaintParts.Border);
        var cell = grid.Rows[e.RowIndex].Cells[e.ColumnIndex];
        var boxSize = 16;
        var box = new Rectangle(
            e.CellBounds.Left + ((e.CellBounds.Width - boxSize) / 2),
            e.CellBounds.Top + ((e.CellBounds.Height - boxSize) / 2),
            boxSize,
            boxSize);
        var enabled = !cell.ReadOnly;
        var borderColor = enabled
            ? MainWindowTheme.AccentBorder
            : MainWindowTheme.DisabledText;
        var hasValue = cell.Value is bool;
        var checkedValue = cell.Value is true;

        if (!hasValue)
        {
            TextRenderer.DrawText(
                e.Graphics,
                "—",
                e.CellStyle.Font ?? grid.Font,
                e.CellBounds,
                MainWindowTheme.DisabledText,
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPadding |
                TextFormatFlags.SingleLine);
            e.Handled = true;
            return;
        }

        using (var background = new SolidBrush(checkedValue
                   ? Color.FromArgb(24, 72, 91)
                   : Color.FromArgb(11, 23, 31)))
        {
            e.Graphics.FillRectangle(background, box);
        }

        using (var border = new Pen(borderColor))
        {
            e.Graphics.DrawRectangle(border, box);
        }

        if (checkedValue)
        {
            var oldSmoothing = e.Graphics.SmoothingMode;
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var check = new Pen(
                enabled ? MainWindowTheme.Accent : MainWindowTheme.DisabledText,
                2.1f)
            {
                StartCap = System.Drawing.Drawing2D.LineCap.Round,
                EndCap = System.Drawing.Drawing2D.LineCap.Round,
            };
            e.Graphics.DrawLines(
                check,
                [
                    new Point(box.Left + 3, box.Top + 8),
                    new Point(box.Left + 7, box.Top + 12),
                    new Point(box.Left + 13, box.Top + 4),
                ]);
            e.Graphics.SmoothingMode = oldSmoothing;
        }

        e.Handled = true;
    }

    private void PaintSliderCell(
        DataGridView grid,
        DataGridViewCellPaintingEventArgs e)
    {
        e.PaintBackground(e.CellBounds, true);
        e.Paint(e.CellBounds, DataGridViewPaintParts.Border);
        var cell = grid.Rows[e.RowIndex].Cells[e.ColumnIndex];

        if (!TryConvertToDecimal(cell.Value, out var value))
        {
            TextRenderer.DrawText(
                e.Graphics,
                "—",
                e.CellStyle.Font ?? grid.Font,
                e.CellBounds,
                MainWindowTheme.DisabledText,
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPadding |
                TextFormatFlags.SingleLine);
            e.Handled = true;
            return;
        }

        var trackLeft = e.CellBounds.Left + 12;
        var valueWidth = 48;
        var trackRight = Math.Max(
            trackLeft + 1,
            e.CellBounds.Right - valueWidth - 12);
        var trackWidth = Math.Max(1, trackRight - trackLeft);
        var centerY = e.CellBounds.Top + (e.CellBounds.Height / 2);
        var ratio = Math.Clamp(value, 0, 99) / 99m;
        var thumbX = trackLeft + (int)Math.Round(
            trackWidth * (double)ratio,
            MidpointRounding.AwayFromZero);
        var enabled = !cell.ReadOnly;

        using (var trackBrush = new SolidBrush(enabled
                   ? Color.FromArgb(35, 67, 82)
                   : Color.FromArgb(30, 43, 51)))
        {
            e.Graphics.FillRectangle(
                trackBrush,
                new Rectangle(trackLeft, centerY - 2, trackWidth, 4));
        }

        var filledWidth = Math.Max(0, thumbX - trackLeft);

        if (filledWidth > 0)
        {
            using var fillBrush = new SolidBrush(enabled
                ? MainWindowTheme.AccentBorder
                : MainWindowTheme.DisabledText);
            e.Graphics.FillRectangle(
                fillBrush,
                new Rectangle(trackLeft, centerY - 2, filledWidth, 4));
        }

        var thumb = new Rectangle(thumbX - 5, centerY - 8, 10, 16);
        using (var thumbBrush = new SolidBrush(enabled
                   ? Color.FromArgb(27, 91, 111)
                   : disabledCellBackground))
        {
            e.Graphics.FillRectangle(thumbBrush, thumb);
        }

        using (var thumbPen = new Pen(enabled
                   ? MainWindowTheme.Accent
                   : MainWindowTheme.DisabledText))
        {
            e.Graphics.DrawRectangle(thumbPen, thumb);
        }

        var valueBounds = new Rectangle(
            trackRight + 8,
            e.CellBounds.Top,
            valueWidth - 8,
            e.CellBounds.Height);
        TextRenderer.DrawText(
            e.Graphics,
            FormatDecimal(value),
            e.CellStyle.Font ?? grid.Font,
            valueBounds,
            enabled ? MainWindowTheme.Accent : MainWindowTheme.DisabledText,
            TextFormatFlags.Right |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPadding |
            TextFormatFlags.SingleLine);
        e.Handled = true;
    }

    private void SetSliderValueFromClientX(
        DataGridView grid,
        int rowIndex,
        int columnIndex,
        int clientX)
    {
        if (rowIndex < 0 ||
            columnIndex < 0 ||
            rowIndex >= grid.Rows.Count ||
            columnIndex >= grid.Columns.Count ||
            grid.Rows[rowIndex].Cells[columnIndex].ReadOnly)
        {
            return;
        }

        var bounds = grid.GetCellDisplayRectangle(
            columnIndex,
            rowIndex,
            cutOverflow: false);
        var trackLeft = bounds.Left + 12;
        var trackRight = Math.Max(trackLeft + 1, bounds.Right - 60);
        var ratio = Math.Clamp(
            (double)(clientX - trackLeft) / (trackRight - trackLeft),
            0.0,
            1.0);
        var value = (decimal)Math.Round(
            ratio * 99.0,
            MidpointRounding.AwayFromZero);
        this.SetNumericCellValue(grid, rowIndex, columnIndex, value);
    }

    private void SetNumericCellValue(
        DataGridView grid,
        int rowIndex,
        int columnIndex,
        decimal value)
    {
        var cell = grid.Rows[rowIndex].Cells[columnIndex];

        if (cell.ReadOnly ||
            TryConvertToDecimal(cell.Value, out var current) && current == value)
        {
            return;
        }

        cell.Value = value;
        grid.InvalidateCell(cell);
    }

    private void ShowBulkApplyMenu(
        DataGridView grid,
        int rowIndex,
        int columnIndex)
    {
        var sourceCell = grid.Rows[rowIndex].Cells[columnIndex];
        var columnName = grid.Columns[columnIndex].Name;

        if (this.bulkApplyMenu is { IsDisposed: false } previousMenu)
        {
            if (previousMenu.Visible)
            {
                previousMenu.Close();
            }
            else
            {
                previousMenu.Dispose();
            }
        }

        var menu = new ContextMenuStrip
        {
            BackColor = Color.FromArgb(15, 24, 33),
            ForeColor = MainWindowTheme.Text,
            ShowImageMargin = false,
            ShowCheckMargin = false,
            Padding = new Padding(1),
            Renderer = new GalaxyAtlasContextMenuRenderer(),
        };

        if (grid == this.chatFontGrid)
        {
            if (grid.Rows[rowIndex].Tag is not GamePilotSettingsRow sourceModel ||
                this.SelectedChatResolution is not { } resolution)
            {
                menu.Dispose();
                return;
            }

            var hasPlayer = sourceModel.PlayerChatFonts.ContainsKey(resolution);
            var hasMessage = sourceModel.MessageChatFonts.ContainsKey(resolution);

            if (hasPlayer)
            {
                menu.Items.Add(this.CreateBulkApplyMenuItem(
                    string.Concat(
                        "Copy complete Player Chat setup for ",
                        resolution,
                        " to all offline pilots"),
                    () => this.ApplyCompleteChatFontSetupToAll(
                        sourceModel,
                        isPlayer: true,
                        resolution: resolution)));
            }

            if (hasMessage)
            {
                menu.Items.Add(this.CreateBulkApplyMenuItem(
                    string.Concat(
                        "Copy complete Game Message setup for ",
                        resolution,
                        " to all offline pilots"),
                    () => this.ApplyCompleteChatFontSetupToAll(
                        sourceModel,
                        isPlayer: false,
                        resolution: resolution)));
            }

            if (hasPlayer && hasMessage)
            {
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add(this.CreateBulkApplyMenuItem(
                    string.Concat(
                        "Copy both chat font setups for ",
                        resolution,
                        " to all offline pilots"),
                    () => this.ApplyBothChatFontSetupsToAll(
                        sourceModel,
                        resolution)));
            }
        }
        else
        {
            var displayValue = FormatBulkValue(columnName, sourceCell.Value);
            menu.Items.Add(this.CreateBulkApplyMenuItem(
                string.Concat(
                    "Apply ",
                    displayValue,
                    " to all offline pilots"),
                () => this.ApplyValueToAll(
                    grid,
                    columnName,
                    sourceCell.Value)));
        }

        if (menu.Items.Count == 0)
        {
            menu.Dispose();
            return;
        }

        this.bulkApplyMenu = menu;
        menu.Closed += (_, _) => this.DisposeBulkApplyMenuAfterClose(menu);
        menu.Show(Cursor.Position);
    }

    private ToolStripMenuItem CreateBulkApplyMenuItem(
        string text,
        Action action)
    {
        var item = new ToolStripMenuItem
        {
            Text = text,
            AutoSize = true,
            ForeColor = MainWindowTheme.Text,
            BackColor = Color.FromArgb(15, 24, 33),
            DisplayStyle = ToolStripItemDisplayStyle.Text,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        item.Click += (_, _) => action();
        return item;
    }

    private void DisposeBulkApplyMenuAfterClose(ContextMenuStrip menu)
    {
        if (!this.IsHandleCreated || this.IsDisposed || this.Disposing)
        {
            return;
        }

        try
        {
            this.BeginInvoke(new Action(() =>
            {
                if (!menu.IsDisposed)
                {
                    menu.Dispose();
                }

                if (ReferenceEquals(this.bulkApplyMenu, menu))
                {
                    this.bulkApplyMenu = null;
                }
            }));
        }
        catch (InvalidOperationException)
        {
            // The form is closing; its normal disposal path owns the menu now.
        }
    }

    private void ApplyValueToAll(
        DataGridView grid,
        string columnName,
        object? value)
    {
        var changed = 0;

        foreach (DataGridViewRow row in grid.Rows)
        {
            var cell = row.Cells[columnName];

            if (cell.ReadOnly || ValuesEqual(cell.Value, value))
            {
                continue;
            }

            cell.Value = value;
            changed++;
        }

        if (changed == 0)
        {
            this.ShowTransientStatus("Every eligible offline pilot already has that value.");
            return;
        }

        this.ScheduleAutoSave();
        this.ShowTransientStatus(string.Concat(
            "Applied to ",
            changed.ToString(CultureInfo.InvariantCulture),
            changed == 1 ? " pilot. Saving automatically…" : " pilots. Saving automatically…"));
        grid.Invalidate();
    }

    private void ApplyCompleteChatFontSetupToAll(
        GamePilotSettingsRow sourceModel,
        bool isPlayer,
        string resolution)
    {
        var sourceRecords = isPlayer
            ? sourceModel.PlayerChatFonts
            : sourceModel.MessageChatFonts;

        if (!sourceRecords.TryGetValue(resolution, out var source))
        {
            return;
        }

        var changed = 0;

        foreach (var model in this.rows)
        {
            if (model.File.IsOnline)
            {
                continue;
            }

            var records = isPlayer
                ? model.PlayerChatFonts
                : model.MessageChatFonts;

            if (records.TryGetValue(resolution, out var existing) &&
                ChatFontRecordsEqual(existing, source))
            {
                continue;
            }

            records[resolution] = CloneChatFontRecord(source);

            if (isPlayer)
            {
                model.DirtyPlayerChatFontResolutions.Add(resolution);
            }
            else
            {
                model.DirtyMessageChatFontResolutions.Add(resolution);
            }

            changed++;
        }

        if (changed == 0)
        {
            this.ShowTransientStatus(
                "Every eligible offline pilot already has that complete font setup.");
            return;
        }

        this.PopulateChatFontGrid();
        this.ScheduleAutoSave();
        this.ShowTransientStatus(string.Concat(
            "Copied complete ",
            isPlayer ? "Player Chat" : "Game Message",
            " setup to ",
            changed.ToString(CultureInfo.InvariantCulture),
            changed == 1
                ? " pilot. Saving automatically…"
                : " pilots. Saving automatically…"));
    }

    private void ApplyBothChatFontSetupsToAll(
        GamePilotSettingsRow sourceModel,
        string resolution)
    {
        if (!sourceModel.PlayerChatFonts.TryGetValue(
                resolution,
                out var playerSource) ||
            !sourceModel.MessageChatFonts.TryGetValue(
                resolution,
                out var messageSource))
        {
            return;
        }

        var changed = 0;

        foreach (var model in this.rows)
        {
            if (model.File.IsOnline)
            {
                continue;
            }

            var playerChanged = !model.PlayerChatFonts.TryGetValue(
                                    resolution,
                                    out var existingPlayer) ||
                                !ChatFontRecordsEqual(
                                    existingPlayer,
                                    playerSource);
            var messageChanged = !model.MessageChatFonts.TryGetValue(
                                     resolution,
                                     out var existingMessage) ||
                                 !ChatFontRecordsEqual(
                                     existingMessage,
                                     messageSource);

            if (!playerChanged && !messageChanged)
            {
                continue;
            }

            if (playerChanged)
            {
                model.PlayerChatFonts[resolution] =
                    CloneChatFontRecord(playerSource);
                model.DirtyPlayerChatFontResolutions.Add(resolution);
            }

            if (messageChanged)
            {
                model.MessageChatFonts[resolution] =
                    CloneChatFontRecord(messageSource);
                model.DirtyMessageChatFontResolutions.Add(resolution);
            }

            changed++;
        }

        if (changed == 0)
        {
            this.ShowTransientStatus(
                "Every eligible offline pilot already has both chat font setups.");
            return;
        }

        this.PopulateChatFontGrid();
        this.ScheduleAutoSave();
        this.ShowTransientStatus(string.Concat(
            "Copied both chat font setups to ",
            changed.ToString(CultureInfo.InvariantCulture),
            changed == 1
                ? " pilot. Saving automatically…"
                : " pilots. Saving automatically…"));
    }

    private static ChatFontRecord CloneChatFontRecord(ChatFontRecord source)
    {
        return new ChatFontRecord
        {
            Resolution = source.Resolution,
            Font = source.Font,
            Scale = source.Scale,
            LetterSpacing = source.LetterSpacing,
            LineSpacing = source.LineSpacing,
        };
    }

    private static bool ChatFontRecordsEqual(
        ChatFontRecord left,
        ChatFontRecord right)
    {
        return string.Equals(
                   left.Resolution,
                   right.Resolution,
                   StringComparison.OrdinalIgnoreCase) &&
               string.Equals(
                   left.Font,
                   right.Font,
                   StringComparison.OrdinalIgnoreCase) &&
               left.Scale == right.Scale &&
               left.LetterSpacing == right.LetterSpacing &&
               left.LineSpacing == right.LineSpacing;
    }

    private static bool ValuesEqual(object? left, object? right)
    {
        if (TryConvertToDecimal(left, out var leftNumber) &&
            TryConvertToDecimal(right, out var rightNumber))
        {
            return leftNumber == rightNumber;
        }

        return Equals(left, right);
    }

    private static string FormatBulkValue(string columnName, object? value)
    {
        if (booleanOptionSet.Contains(columnName))
        {
            return value is true ? "On" : "Off";
        }

        if (numericOptionSet.Contains(columnName) &&
            TryConvertToDecimal(value, out var number))
        {
            return FormatDecimal(number);
        }

        var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        return $"“{text}”";
    }

    private static string FormatDecimal(decimal value)
    {
        return NormalizeSliderValue(value)
            .ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static decimal NormalizeSliderValue(decimal value)
    {
        var nearestInteger = Math.Round(value, 0);
        return Math.Abs(value - nearestInteger) <= 0.00001m
            ? nearestInteger
            : value;
    }

    private void Grid_OnDataError(
        object? sender,
        DataGridViewDataErrorEventArgs e)
    {
        e.ThrowException = false;
    }

    private void Grid_OnCellValueChanged(
        object? sender,
        DataGridViewCellEventArgs e)
    {
        if (this.isPopulating ||
            sender is not DataGridView grid ||
            e.RowIndex < 0 ||
            e.ColumnIndex < 0 ||
            grid.Rows[e.RowIndex].Tag is not GamePilotSettingsRow model ||
            model.File.IsOnline)
        {
            return;
        }

        var columnName = grid.Columns[e.ColumnIndex].Name;
        var cellValue = grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value;

        if (columnName is "Pilot" or "Status")
        {
            return;
        }

        if (grid == this.chatFontGrid)
        {
            this.ApplyChatFontCellChange(
                grid.Rows[e.RowIndex],
                model,
                columnName,
                cellValue);
        }
        else if (grid == this.privacyGrid)
        {
            model.Values["RestrictedStatus"] = string.Equals(
                Convert.ToString(cellValue, CultureInfo.InvariantCulture),
                "Friends only",
                StringComparison.Ordinal);
            model.DirtyOptions.Add("RestrictedStatus");
        }
        else if (numericOptionSet.Contains(columnName))
        {
            if (TryConvertToDecimal(cellValue, out var number))
            {
                model.Values[columnName] = number;
                model.DirtyOptions.Add(columnName);
            }
        }
        else
        {
            model.Values[columnName] = cellValue is true;
            model.DirtyOptions.Add(columnName);
        }

        this.ScheduleAutoSave();
    }

    private void ApplyChatFontCellChange(
        DataGridViewRow gridRow,
        GamePilotSettingsRow model,
        string columnName,
        object? cellValue)
    {
        var resolution = this.SelectedChatResolution;

        if (resolution == null)
        {
            return;
        }

        var isPlayer = columnName.StartsWith(
            "Player",
            StringComparison.Ordinal);
        var records = isPlayer
            ? model.PlayerChatFonts
            : model.MessageChatFonts;

        if (!records.TryGetValue(resolution, out var record))
        {
            return;
        }

        var suffix = isPlayer
            ? columnName["Player".Length..]
            : columnName["Message".Length..];

        switch (suffix)
        {
            case "Font":
                record.Font = Convert.ToString(
                                  cellValue,
                                  CultureInfo.InvariantCulture)
                              ?? record.Font;
                var minimum = ChatFontValueCodec.MinimumLineSpacing(record.Font);
                var clamped = Math.Clamp(record.LineSpacing, minimum, 5);

                if (clamped != record.LineSpacing)
                {
                    record.LineSpacing = clamped;
                    this.isPopulating = true;

                    try
                    {
                        gridRow.Cells[isPlayer ? "PlayerLine" : "MessageLine"]
                            .Value = clamped;
                    }
                    finally
                    {
                        this.isPopulating = false;
                    }
                }

                break;

            case "Scale":
                if (decimal.TryParse(
                        Convert.ToString(cellValue, CultureInfo.InvariantCulture),
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var scale))
                {
                    record.Scale = scale;
                }

                break;

            case "Letter":
                if (ConvertToInt(cellValue, out var letterSpacing))
                {
                    record.LetterSpacing = Math.Clamp(letterSpacing, -3, 4);
                }

                break;

            case "Line":
                if (ConvertToInt(cellValue, out var lineSpacing))
                {
                    record.LineSpacing = Math.Clamp(
                        lineSpacing,
                        ChatFontValueCodec.MinimumLineSpacing(record.Font),
                        5);
                    this.isPopulating = true;

                    try
                    {
                        gridRow.Cells[columnName].Value = record.LineSpacing;
                    }
                    finally
                    {
                        this.isPopulating = false;
                    }
                }

                break;
        }

        if (isPlayer)
        {
            model.DirtyPlayerChatFontResolutions.Add(resolution);
        }
        else
        {
            model.DirtyMessageChatFontResolutions.Add(resolution);
        }
    }

    private void SaveChanges(bool showErrors)
    {
        if (this.isSaving)
        {
            return;
        }

        this.autoSaveTimer.Stop();
        var dirtyRows = this.rows
            .Where(row => row.HasDirtyChanges)
            .ToArray();

        if (dirtyRows.Length == 0)
        {
            this.transientStatus = null;
            this.UpdateStatus();
            return;
        }

        this.isSaving = true;
        this.transientStatus = "Saving changes…";
        this.UpdateStatus();
        var shouldReloadAfterSave = false;

        try
        {
            var onlineIdentities =
                GamePilotSettingsCatalog.ResolveOnlineIdentities(
                    this.clientManager);

            foreach (var row in this.rows)
            {
                row.File.IsOnline = onlineIdentities.Contains(
                    row.File.Identity);
            }

            this.ApplyAllRowStates();
            var savedCount = 0;
            List<string> errors = [];

            foreach (var row in dirtyRows)
            {
                if (onlineIdentities.Contains(row.File.Identity))
                {
                    errors.Add(string.Concat(
                        row.File.DisplayName,
                        ": the pilot came online before the change could be saved."));
                    row.File.IsOnline = true;
                    continue;
                }

                if (!GameOptionsDocument.TryRead(
                        row.File.FilePath,
                        out var document,
                        out var readError))
                {
                    errors.Add(string.Concat(
                        row.File.DisplayName,
                        ": ",
                        readError));
                    continue;
                }

                try
                {
                    foreach (var option in row.DirtyOptions)
                    {
                        var value = row.Values.GetValueOrDefault(option);
                        var updated = value switch
                        {
                            bool boolean => document.SetBoolean(option, boolean),
                            decimal number => document.SetFloat(option, number),
                            int integer => document.SetFloat(option, integer),
                            _ => false,
                        };

                        if (!updated)
                        {
                            throw new InvalidDataException(string.Concat(
                                "Option ",
                                option,
                                " is no longer present."));
                        }
                    }

                    foreach (var resolution in row.DirtyPlayerChatFontResolutions)
                    {
                        var current = document.GetValue(PlayerChatOption);
                        var replacement = ChatFontValueCodec.ReplaceOrAdd(
                            current,
                            row.PlayerChatFonts[resolution]);

                        if (!document.SetValue(PlayerChatOption, replacement))
                        {
                            throw new InvalidDataException(
                                "Player chat font option is no longer present.");
                        }
                    }

                    foreach (var resolution in row.DirtyMessageChatFontResolutions)
                    {
                        var current = document.GetValue(MessageChatOption);
                        var replacement = ChatFontValueCodec.ReplaceOrAdd(
                            current,
                            row.MessageChatFonts[resolution]);

                        if (!document.SetValue(MessageChatOption, replacement))
                        {
                            throw new InvalidDataException(
                                "Game message font option is no longer present.");
                        }
                    }

                    document.SaveAtomic(
                        row.File.FilePath,
                        GetBackupDirectory());
                    row.Document = document;
                    row.DirtyOptions.Clear();
                    row.DirtyPlayerChatFontResolutions.Clear();
                    row.DirtyMessageChatFontResolutions.Clear();
                    savedCount++;
                }
                catch (Exception ex) when (
                    ex is IOException or
                    UnauthorizedAccessException or
                    InvalidDataException or
                    ArgumentException or
                    NotSupportedException)
                {
                    errors.Add(string.Concat(
                        row.File.DisplayName,
                        ": ",
                        ex.Message));
                }
            }

            if (errors.Count == 0)
            {
                this.ShowTransientStatus(string.Concat(
                    "Saved ",
                    savedCount.ToString(CultureInfo.InvariantCulture),
                    savedCount == 1 ? " pilot." : " pilots."));
            }
            else
            {
                this.transientStatus = string.Concat(
                    "Saved ",
                    savedCount.ToString(CultureInfo.InvariantCulture),
                    "; ",
                    errors.Count.ToString(CultureInfo.InvariantCulture),
                    " failed.");
                this.UpdateStatus();

                if (showErrors)
                {
                    MessageBox.Show(
                        this,
                        string.Join(Environment.NewLine, errors),
                        "Some settings could not be saved",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }
        }
        finally
        {
            this.isSaving = false;
            shouldReloadAfterSave =
                this.reloadAfterAutoSave && !this.HasDirtyChanges();
            this.reloadAfterAutoSave &= !shouldReloadAfterSave;
        }

        if (shouldReloadAfterSave)
        {
            this.LoadRows();
            return;
        }

        this.UpdateStatus();
    }

    private void ScheduleAutoSave()
    {
        if (this.isPopulating || this.isSaving)
        {
            return;
        }

        this.statusResetTimer.Stop();
        this.transientStatus = this.sliderDragGrid == null
            ? "Saving automatically…"
            : "Release the slider to save…";
        this.autoSaveTimer.Stop();

        if (this.sliderDragGrid == null)
        {
            this.autoSaveTimer.Start();
        }

        this.UpdateStatus();
    }

    private void ShowTransientStatus(string text)
    {
        this.transientStatus = text;
        this.statusResetTimer.Stop();
        this.statusResetTimer.Start();
        this.UpdateStatus();
    }

    private void UpdateStatus()
    {
        var dirtyPilotCount = this.rows.Count(row => row.HasDirtyChanges);
        var onlineCount = this.rows.Count(row => row.File.IsOnline);
        var baseText = string.Concat(
            this.rows.Count.ToString(CultureInfo.InvariantCulture),
            " settings files · ",
            onlineCount.ToString(CultureInfo.InvariantCulture),
            " online/read-only · Changes save automatically");
        var state = this.transientStatus;

        if (string.IsNullOrWhiteSpace(state) && dirtyPilotCount > 0)
        {
            state = "Waiting to save…";
        }

        this.statusLabel.Text = string.IsNullOrWhiteSpace(state)
            ? baseText
            : string.Concat(baseText, " · ", state);
        this.reloadButton.Enabled = this.outputDirectory != null &&
            Directory.Exists(this.outputDirectory);
    }

    private bool HasDirtyChanges()
    {
        return this.rows.Any(row => row.HasDirtyChanges);
    }

    private string? SelectedChatResolution =>
        this.chatResolutionComboBox.SelectedItem as string;

    private static string GetBackupDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData),
            "Net7ClientManager",
            "game-settings-backups");
    }

    private static bool ConvertToInt(object? value, out int result)
    {
        if (value is int integer)
        {
            result = integer;
            return true;
        }

        return int.TryParse(
            Convert.ToString(value, CultureInfo.InvariantCulture),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out result);
    }

    private static bool TryConvertToDecimal(
        object? value,
        out decimal result)
    {
        if (value is decimal number)
        {
            result = number;
            return true;
        }

        if (value is int integer)
        {
            result = integer;
            return true;
        }

        return decimal.TryParse(
            Convert.ToString(value, CultureInfo.InvariantCulture),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out result);
    }

    private static long ResolutionSortKey(string resolution)
    {
        var parts = resolution.Split('x', '×');

        if (parts.Length == 2 &&
            int.TryParse(parts[0], out var width) &&
            int.TryParse(parts[1], out var height))
        {
            return ((long)width << 32) | (uint)height;
        }

        return long.MaxValue;
    }
}
