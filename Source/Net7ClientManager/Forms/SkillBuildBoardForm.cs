// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using System.Diagnostics;
using System.Globalization;
using Net7ClientManager.Core;
using Net7ClientManager.Contributions;
using Net7ClientManager.Models;
using Net7ClientManager.Observations.Models;
using Net7ClientManager.Observations.Observers;
using Net7ClientManager.SkillPlanning;
using Net7ClientManager.Win32;

/// <summary>
/// Shared build board. The same board is editable while authoring and read-only
/// while a character is following a build.
/// </summary>
internal sealed partial class SkillBuildBoardForm : ThemedForm
{
    private const string PlacementAddonId = "net7.builds";
    private const string PlacementWidgetId = "build-board-v2";
    private static readonly Size DefaultClientSize = new(1280, 930);
    private static readonly Color CombatColor = Color.FromArgb(4, 168, 152);
    private static readonly Color ExploreColor = Color.FromArgb(82, 148, 255);
    private static readonly Color TradeColor = Color.FromArgb(211, 88, 235);

    private readonly SkillBuildLocalWorkspace workspace;
    private readonly ForgeContributionCoordinator forgeContributionCoordinator;
    private readonly Func<string, string, AddonWindowPlacement?>
        resolveWindowPlacement;
    private readonly Action<string, string, AddonWindowPlacement>
        saveWindowPlacement;
    private readonly Func<int?, Size, Image?> resolveItemIcon;
    private readonly Panel contentPanel = new();
    private readonly ActionToolTip itemToolTip = new();
    private readonly HashSet<ContextMenuStrip> transientMenus = [];
    private readonly HashSet<Control> darkThemeControls = [];
    private readonly Dictionary<ItemIconKey, Image?> itemIcons = [];
    private readonly Dictionary<int, SkillRowBinding> skillRows = [];
    private readonly Dictionary<SkillBuildEquipmentKind, Panel>
        equipmentGroupHosts = [];

    private SkillBuildBoardPresentation presentation =
        SkillBuildBoardPresentation.Hidden;
    private SkillBuildLocalLibrary library = new();
    private string presentationFingerprint = "";
    private string? selectedBuildId;
    private SkillBuildBoardDraft? draft;
    private bool editMode;
    private bool placementRestored;
    private Rectangle placementOwnerBounds;
    private Panel? skillsHost;
    private Panel? equipmentHost;
    private Control? levelsHost;
    private LevelStripBinding? levelStripBinding;
    private ComboBox? localBuildPicker;
    private FlowLayoutPanel? localPublicationHost;
    private bool suppressWorkspaceRefresh;

    public SkillBuildBoardForm(
        SkillBuildLocalWorkspace workspace,
        ForgeContributionCoordinator forgeContributionCoordinator,
        Func<string, string, AddonWindowPlacement?> resolveWindowPlacement,
        Action<string, string, AddonWindowPlacement> saveWindowPlacement,
        Func<int?, Size, Image?> resolveItemIcon)
    {
        this.workspace = workspace ??
            throw new ArgumentNullException(nameof(workspace));
        this.forgeContributionCoordinator = forgeContributionCoordinator ??
            throw new ArgumentNullException(nameof(forgeContributionCoordinator));
        this.resolveWindowPlacement = resolveWindowPlacement ??
            throw new ArgumentNullException(nameof(resolveWindowPlacement));
        this.saveWindowPlacement = saveWindowPlacement ??
            throw new ArgumentNullException(nameof(saveWindowPlacement));
        this.resolveItemIcon = resolveItemIcon ??
            throw new ArgumentNullException(nameof(resolveItemIcon));

        this.Text = "Builds";
        this.Icon = ResourceLoader.Net7ClientManagerIcon;
        this.StartPosition = FormStartPosition.Manual;
        this.AutoScaleMode = AutoScaleMode.None;
        this.ClientSize = DefaultClientSize;
        this.MinimumSize = new Size(1050, 720);
        this.BackColor = MainWindowTheme.Background;
        this.ForeColor = MainWindowTheme.Text;
        this.ConfigureWindowChrome(
            allowResize: true,
            showMinimizeButton: true,
            showMaximizeButton: true);
        this.ConfigureHelpTopic(HelpTopicIds.Builds);
        this.ConfigureHelpTour(this.ShowHelpTour);

        this.ConfigureContent();
        this.Controls.Add(this.contentPanel);
        this.RegisterDarkScrollbarTheme(this);

        this.FormClosing += this.SkillBuildBoardForm_OnFormClosing;
        this.workspace.Changed += this.Workspace_OnChanged;
    }


    internal void ShowHelpTour()
    {
        GuidedTourOverlay.Show(
            this,
            [
                new GuidedTourStep(
                    () => (Control?)this.localBuildPicker ?? this.contentPanel,
                    "Choose a build for this pilot",
                    "A build is a step-by-step equipment and skill guide. Use the picker to switch between saved builds, and Use to make one the active guide for this pilot.",
                    this.ShowLocalBuildsForTour),
                new GuidedTourStep(
                    () => (Control?)this.forgeSearchTextBox ?? this.contentPanel,
                    "Find community builds in the Forge",
                    "Open Forge and search by build name, purpose, notes, or publisher. Sort by stars, newest, name, or relevance to find a guide that suits the pilot.",
                    this.OpenForgeBrowser),
                new GuidedTourStep(
                    () => this.forgeSearchResultsHost ?? this.contentPanel,
                    "Preview before you use it",
                    "Open a result to inspect its equipment, skill plan, notes, profession, level milestones, and available versions. Use the version you actually choose; Client Manager never swaps it behind your back."),
                new GuidedTourStep(
                    () => this.levelsHost ?? this.contentPanel,
                    "See when the build becomes possible",
                    "The level strip shows the Combat, Explore, Trade, Overall, and hull milestones needed by the planned equipment and skills.",
                    this.ShowLocalBuildsForTour),
                new GuidedTourStep(
                    () => this.equipmentHost ?? this.contentPanel,
                    "See what you already own and what is missing",
                    "Equipment cards compare the guide with the selected pilot. They show equipped, in cargo, in vault, or missing items. Hover an item for its details and requirements."),
                new GuidedTourStep(
                    () => this.skillsHost ?? this.contentPanel,
                    "Follow the skill plan",
                    "Skill rows compare the current rank with the build target. Hover them to see requirements and prerequisites, including ranks the build needs before later milestones."),
            ]);
    }

    internal void ShowForgeHelpTour()
    {
        this.OpenForgeBrowser();
        GuidedTourOverlay.Show(
            this,
            [
                new GuidedTourStep(
                    () => (Control?)this.forgeSearchTextBox ?? this.contentPanel,
                    "Search for a build guide",
                    "Search by build name, purpose, notes, or publisher. Sort the results to discover popular, recent, or closely matching guides."),
                new GuidedTourStep(
                    () => this.forgeSearchResultsHost ?? this.contentPanel,
                    "Open a result to inspect the plan",
                    "A published guide can contain equipment, skill targets, milestone levels, notes, and multiple versions. Open one before deciding to use it."),
                new GuidedTourStep(
                    () => this.forgeSearchResultsHost ?? this.contentPanel,
                    "Choose the guide you want to follow",
                    "Use a build to add that exact version to your local library. The Build Board then compares it with the selected pilot and shows what is owned, missing, or still needs training."),
            ]);
    }

    private void ShowLocalBuildsForTour()
    {
        this.CancelForgeSearch();
        this.forgeMode = false;
        this.forgeDetails = null;
        this.forgeVersion = null;
        this.RebuildContent();
    }

    public void RestorePlacement(Rectangle ownerBounds)
    {
        if (this.placementRestored)
        {
            return;
        }

        this.placementOwnerBounds = ownerBounds;
        var placement = this.resolveWindowPlacement(
            PlacementAddonId,
            PlacementWidgetId);
        var size = placement is { Width: > 0, Height: > 0 }
            ? new Size(
                Math.Max(this.MinimumSize.Width, placement.Width),
                Math.Max(this.MinimumSize.Height, placement.Height))
            : DefaultClientSize;
        var defaultLocation = new Point(
            ownerBounds.Left + Math.Max(24, (ownerBounds.Width - size.Width) / 2),
            ownerBounds.Top + Math.Max(24, (ownerBounds.Height - size.Height) / 2));
        var location = placement == null
            ? defaultLocation
            : new Point(
                defaultLocation.X + placement.OffsetX,
                defaultLocation.Y + placement.OffsetY);

        this.Bounds = ClampToWorkingArea(new Rectangle(location, size));
        this.placementRestored = true;
    }

    public void SetPresentation(
        SkillBuildBoardPresentation nextPresentation)
    {
        ArgumentNullException.ThrowIfNull(nextPresentation);
        this.RunBuildCommand(
            "refresh the Build Board",
            () => this.ApplyPresentation(nextPresentation),
            hideOnFailure: false);
    }

    private void ApplyPresentation(
        SkillBuildBoardPresentation nextPresentation)
    {
        var changed = !string.Equals(
            this.presentationFingerprint,
            nextPresentation.Fingerprint,
            StringComparison.Ordinal);
        var characterChanged =
            this.presentation.CharacterId != nextPresentation.CharacterId ||
            this.presentation.Baseline?.ProfessionIndex !=
            nextPresentation.Baseline?.ProfessionIndex;

        this.presentation = nextPresentation;
        this.presentationFingerprint = nextPresentation.Fingerprint;
        this.Text = nextPresentation.HasBuildContext
            ? string.Create(
                CultureInfo.CurrentCulture,
                $"Builds · {nextPresentation.PilotName}")
            : "Builds";

        if (characterChanged)
        {
            this.draft = null;
            this.editMode = false;
            this.selectedBuildId = null;
            this.ResetForgeBrowser();
        }

        this.ReloadLibrary();
        if (changed && !this.editMode)
        {
            if (characterChanged || !this.TryRefreshReadOnlyBoard())
            {
                this.RebuildContent();
            }
        }

        this.QueueForgeKnowledgeRefresh();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.FormClosing -= this.SkillBuildBoardForm_OnFormClosing;
            this.workspace.Changed -= this.Workspace_OnChanged;
            this.DisposeTransientMenus();
            this.CancelForgeSearch();
            this.CancelForgeOperation();
            this.CancelForgeKnowledgeRefresh();
            this.itemToolTip.Dispose();
            foreach (var control in this.darkThemeControls)
            {
                control.HandleCreated -=
                    this.DarkScrollbarControl_OnHandleCreated;
                control.ControlAdded -=
                    this.DarkScrollbarControl_OnControlAdded;
                control.Disposed -=
                    this.DarkScrollbarControl_OnDisposed;
            }
            this.darkThemeControls.Clear();
            this.itemIcons.Clear();
        }

        base.Dispose(disposing);
    }

    private void ConfigureContent()
    {
        this.contentPanel.Dock = DockStyle.Fill;
        this.contentPanel.AutoScroll = false;
        this.contentPanel.Padding = new Padding(10);
        this.contentPanel.Margin = Padding.Empty;
        this.contentPanel.BackColor = MainWindowTheme.Background;
    }

    private void ReloadLibrary()
    {
        if (!this.presentation.HasBuildContext ||
            this.presentation.Baseline == null ||
            this.presentation.CharacterId == 0)
        {
            this.library = new SkillBuildLocalLibrary();
            this.selectedBuildId = null;
            return;
        }

        this.library = this.workspace.GetLibrary(
            this.presentation.CharacterId,
            this.presentation.Baseline.ProfessionIndex);

        if (this.selectedBuildId != null &&
            this.library.Builds.Any(value => string.Equals(
                value.BuildId,
                this.selectedBuildId,
                StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        this.selectedBuildId =
            this.library.ActiveBuildId ??
            this.library.Builds.FirstOrDefault()?.BuildId;
    }

    private void RebuildContent()
    {
        var suppressRedraw = this.contentPanel.IsHandleCreated;
        if (suppressRedraw)
        {
            NativeMethods.SetWindowRedraw(
                this.contentPanel.Handle,
                enabled: false);
        }

        this.contentPanel.SuspendLayout();
        try
        {
            this.itemToolTip.Remove(this.contentPanel);
            this.contentPanel.Controls.Clear();
            this.skillsHost = null;
            this.equipmentHost = null;
            this.levelsHost = null;
            this.levelStripBinding = null;
            this.localBuildPicker = null;
            this.localPublicationHost = null;
            this.ResetForgeViewBindings();
            this.skillRows.Clear();
            this.equipmentGroupHosts.Clear();

            if (!this.presentation.HasBuildContext ||
                this.presentation.Baseline == null)
            {
                this.contentPanel.Controls.Add(
                    CreateMessageBody(
                        this.presentation.StatusText,
                        MainWindowTheme.MutedText));
                return;
            }

            if (this.forgeMode)
            {
                this.BuildForgeView();
                return;
            }

            if (this.editMode && this.draft != null)
            {
                this.BuildEditor();
                return;
            }

            this.BuildReadOnlyView();
        }
        finally
        {
            this.contentPanel.ResumeLayout(performLayout: true);
            if (suppressRedraw)
            {
                NativeMethods.SetWindowRedraw(
                    this.contentPanel.Handle,
                    enabled: true);
            }
        }
    }

    private void BuildReadOnlyView()
    {
        if (this.library.Builds.Count == 0)
        {
            this.BuildEmptyLibrary();
            return;
        }

        var build = this.GetSelectedBuild() ?? this.library.Builds[0];
        this.selectedBuildId = build.BuildId;
        var analysis = this.workspace.Board.Analyze(
            build,
            this.presentation.Baseline,
            this.presentation.EquipmentBaseline);

        var root = CreateBoardRoot(commandHeight: 70);
        root.Controls.Add(this.CreateReadOnlyCommandBar(build), 0, 0);
        this.AddBoard(root, analysis, editable: false, rowOffset: 1);
        this.contentPanel.Controls.Add(root);
    }

    private bool TryRefreshReadOnlyBoard()
    {
        if (this.levelStripBinding == null ||
            this.skillsHost == null ||
            this.equipmentHost == null)
        {
            return false;
        }

        var build = this.GetSelectedBuild();
        if (build == null)
        {
            return false;
        }

        var analysis = this.workspace.Board.Analyze(
            build,
            this.presentation.Baseline,
            this.presentation.EquipmentBaseline);
        this.UpdateLevelStrip(analysis, editable: false);
        this.UpdateSkillRows(analysis, editable: false);
        this.RefreshEquipmentGroups(
            analysis,
            editable: false,
            kind: null);
        return true;
    }

    private void BuildEmptyLibrary()
    {
        var page = CreateMessagePage();
        var currentEquipmentLabel = this.presentation.IsArchivedContext
            ? "Create from stored equipment"
            : "Create from current equipment";
        var fromCurrent = CreateActionButton(
            currentEquipmentLabel,
            primary: true,
            width: 208);
        fromCurrent.Click += (_, _) => this.QueueBuildCommand(
            this.presentation.IsArchivedContext
                ? "create a build from stored equipment"
                : "create a build from current equipment",
            this.BeginCreateFromCurrent);

        var empty = CreateActionButton(
            "Start empty",
            width: 110);
        empty.Click += (_, _) => this.QueueBuildCommand(
            "create an empty build",
            this.BeginCreateEmpty);

        var browseForge = CreateActionButton(
            "Browse Forge",
            width: 124);
        browseForge.Click += (_, _) => this.OpenForgeBrowser();

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 42,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Color.Transparent,
        };
        buttons.Controls.Add(fromCurrent);
        buttons.Controls.Add(empty);
        buttons.Controls.Add(browseForge);

        page.Controls.Add(buttons);
        page.Controls.Add(
            CreateMessageBody(
                "Create a local equipment-centred build for this profession.",
                MainWindowTheme.MutedText));
        page.Controls.Add(CreateMessageHeading("No local builds yet"));
        this.contentPanel.Controls.Add(page);
    }

    private void BuildEditor()
    {
        var document = this.draft!.ToDocument();
        var analysis = this.workspace.Board.Analyze(
            document,
            this.presentation.Baseline,
            equipped: null);

        var root = CreateBoardRoot(commandHeight: 82);
        root.Controls.Add(this.CreateEditorCommandBar(), 0, 0);
        this.AddBoard(root, analysis, editable: true, rowOffset: 1);
        this.contentPanel.Controls.Add(root);
    }

    private static TableLayoutPanel CreateBoardRoot(int commandHeight)
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Color.Transparent,
            GrowStyle = TableLayoutPanelGrowStyle.FixedSize,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100.0f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, commandHeight));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 80.0f));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100.0f));
        return root;
    }

    private void AddBoard(
        TableLayoutPanel root,
        SkillBuildAnalysis analysis,
        bool editable,
        int rowOffset)
    {
        this.levelsHost = this.CreateLevelStrip(analysis, editable);
        root.Controls.Add(this.levelsHost, 0, rowOffset);

        var board = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Color.Transparent,
        };
        board.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38.0f));
        board.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62.0f));
        board.RowStyles.Add(new RowStyle(SizeType.Percent, 100.0f));

        this.skillsHost = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 4, 0),
            BackColor = MainWindowTheme.Background,
        };
        this.equipmentHost = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(4, 0, 0, 0),
            BackColor = MainWindowTheme.Background,
        };

        this.skillsHost.Controls.Add(
            this.CreateSkillsSection(analysis, editable));
        this.equipmentHost.Controls.Add(
            this.CreateEquipmentSection(analysis, editable));
        board.Controls.Add(this.skillsHost, 0, 0);
        board.Controls.Add(this.equipmentHost, 1, 0);

        var boardHost = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(0, 8, 0, 0),
            BackColor = MainWindowTheme.Background,
        };
        boardHost.Controls.Add(board);
        root.Controls.Add(boardHost, 0, rowOffset + 1);
    }

    private Control CreateReadOnlyCommandBar(SkillBuildDocument build)
    {
        var bar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = new Padding(0, 2, 0, 6),
            BackColor = Color.Transparent,
        };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100.0f));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76.0f));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92.0f));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70.0f));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70.0f));
        bar.RowStyles.Add(new RowStyle(SizeType.Absolute, 38.0f));
        bar.RowStyles.Add(new RowStyle(SizeType.Percent, 100.0f));

        var picker = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Margin = new Padding(0, 0, 6, 0),
            DisplayMember = nameof(BuildChoice.Text),
        };
        MainWindowTheme.StyleComboBox(picker);
        ConfigureBuildPicker(picker);
        foreach (var value in this.library.Builds)
        {
            picker.Items.Add(this.CreateLocalBuildChoice(value));
        }
        this.localBuildPicker = picker;
        picker.SelectedIndex = Math.Max(
            0,
            this.library.Builds
                .Select((value, index) => (value, index))
                .FirstOrDefault(value => string.Equals(
                    value.value.BuildId,
                    build.BuildId,
                    StringComparison.OrdinalIgnoreCase))
                .index);
        picker.Enabled = !this.forgeBusy;
        picker.SelectedIndexChanged += (_, _) =>
        {
            if (picker.SelectedItem is not BuildChoice choice ||
                string.Equals(
                    choice.Build.BuildId,
                    this.selectedBuildId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            this.QueueBuildCommand(
                "open the selected build",
                () =>
                {
                    this.selectedBuildId = choice.Build.BuildId;
                    this.RebuildContent();
                });
        };

        var active = string.Equals(
            build.BuildId,
            this.library.ActiveBuildId,
            StringComparison.OrdinalIgnoreCase);
        var useButton = CreateActionButton(
            active ? "Active" : "Use",
            primary: !active,
            width: 70);
        useButton.Enabled = !active && !this.forgeBusy;
        useButton.Click += (_, _) => this.QueueBuildCommand(
            "activate the build",
            () => this.workspace.SetActiveBuild(
                this.presentation.CharacterId,
                build.BuildId));

        var forgeLink = this.library.GetForgeLink(build.BuildId);
        var editButton = CreateActionButton(
            forgeLink is { IsPublisherSource: false }
                ? "Copy & edit"
                : "Edit",
            width: 86);
        editButton.Enabled = !this.forgeBusy;
        editButton.Click += (_, _) => this.QueueBuildCommand(
            forgeLink is { IsPublisherSource: false }
                ? "copy the immutable Forge build"
                : "edit the build",
            () =>
            {
                if (forgeLink is { IsPublisherSource: false })
                {
                    this.BeginCopy(build);
                }
                else
                {
                    this.BeginEdit(build);
                }
            });

        var forgeButton = CreateActionButton("Forge", width: 64);
        forgeButton.Enabled = !this.forgeBusy;
        forgeButton.Click += (_, _) => this.OpenForgeBrowser();

        var newButton = CreateActionButton("New", width: 64);
        newButton.Enabled = !this.forgeBusy;
        newButton.Click += (_, _) => this.RunBuildCommand(
            "open the new Build menu",
            () => this.ShowNewBuildMenu(newButton),
            hideOnFailure: false);

        bar.Controls.Add(picker, 0, 0);
        bar.Controls.Add(useButton, 1, 0);
        bar.Controls.Add(editButton, 2, 0);
        bar.Controls.Add(forgeButton, 3, 0);
        bar.Controls.Add(newButton, 4, 0);
        var metadata = this.CreateReadOnlyBuildMetadata(build);
        bar.Controls.Add(metadata, 0, 1);
        bar.SetColumnSpan(metadata, 5);
        return bar;
    }

    private Control CreateReadOnlyBuildMetadata(SkillBuildDocument build)
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = new Padding(2, 1, 0, 0),
            BackColor = Color.Transparent,
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100.0f));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var purpose = new Label
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Text = BuildReadOnlyMetadataText(
                this.presentation.ContextText,
                build.Summary),
            TextAlign = ContentAlignment.MiddleLeft,
            Font = MainWindowTheme.CreateBodyFont(8.5f),
            ForeColor = MainWindowTheme.MutedText,
            BackColor = Color.Transparent,
            AutoEllipsis = true,
        };
        row.Controls.Add(purpose, 0, 0);

        var publication = new FlowLayoutPanel
        {
            Name = "BuildPublicationMetadataHost",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Color.Transparent,
            AccessibleName = "Build publication metadata",
        };
        this.localPublicationHost = publication;
        this.PopulateLocalPublicationControls(publication, build);
        row.Controls.Add(publication, 1, 0);
        return row;
    }

    private static string BuildReadOnlyMetadataText(
        string context,
        string summary)
    {
        var normalizedContext = context?.Trim() ?? "";
        var normalizedSummary = summary?.Trim() ?? "";

        if (normalizedContext.Length == 0)
        {
            return normalizedSummary.Length == 0
                ? ""
                : string.Concat("Purpose: ", normalizedSummary);
        }

        return normalizedSummary.Length == 0
            ? normalizedContext
            : string.Concat(
                normalizedContext,
                "  ·  Purpose: ",
                normalizedSummary);
    }

    private Control CreateEditorCommandBar()
    {
        var bar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = new Padding(0, 2, 0, 6),
            BackColor = Color.Transparent,
        };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70.0f));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100.0f));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82.0f));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96.0f));
        bar.RowStyles.Add(new RowStyle(SizeType.Absolute, 37.0f));
        bar.RowStyles.Add(new RowStyle(SizeType.Absolute, 37.0f));

        var nameLabel = CreateFieldLabel("Name");
        var name = new TextBox
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            Text = this.draft!.Title,
            MaxLength = 120,
            Margin = new Padding(0, 4, 6, 4),
        };
        MainWindowTheme.StyleTextBox(name);
        name.TextChanged += (_, _) => this.draft.Title = name.Text;

        var summaryLabel = CreateFieldLabel("Purpose");
        var summary = new TextBox
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            Text = this.draft.Summary,
            MaxLength = 500,
            Margin = new Padding(0, 4, 0, 4),
        };
        MainWindowTheme.StyleTextBox(summary);
        summary.TextChanged += (_, _) => this.draft.Summary = summary.Text;

        var cancel = CreateActionButton("Cancel", width: 76);
        cancel.Dock = DockStyle.Fill;
        cancel.Margin = new Padding(2, 2, 2, 2);
        cancel.Click += (_, _) => this.QueueBuildCommand(
            "cancel editing",
            () =>
            {
                this.draft = null;
                this.editMode = false;
                this.RebuildContent();
            });

        var save = CreateActionButton(
            "Save locally",
            primary: true,
            width: 90);
        save.Dock = DockStyle.Fill;
        save.Margin = new Padding(2, 2, 0, 2);
        save.Click += (_, _) => this.QueueBuildCommand(
            "save the build",
            this.SaveDraft);

        bar.Controls.Add(nameLabel, 0, 0);
        bar.Controls.Add(name, 1, 0);
        bar.Controls.Add(cancel, 2, 0);
        bar.Controls.Add(save, 3, 0);
        bar.Controls.Add(summaryLabel, 0, 1);
        bar.Controls.Add(summary, 1, 1);
        bar.SetColumnSpan(summary, 3);
        return bar;
    }

    private Control CreateLevelStrip(
        SkillBuildAnalysis analysis,
        bool editable)
    {
        var strip = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 6,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = new Padding(0, 0, 0, 8),
            BackColor = Color.Transparent,
        };
        strip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 13.0f));
        strip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 13.0f));
        strip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 13.0f));
        strip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 13.0f));
        strip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 13.0f));
        strip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35.0f));

        var levels = editable
            ? analysis.GenericLevels
            : analysis.EffectiveLevels;
        var reasons = editable
            ? analysis.GenericReasons
            : analysis.EffectiveReasons;

        var overall = this.CreateLevelCard(
            "Overall",
            levels.CurrentOverall,
            levels.Overall,
            MainWindowTheme.Accent,
            reasons.Overall);
        var hull = this.CreateHullCard(analysis, editable, reasons.Hull);
        var combat = this.CreateLevelCard(
            "Combat",
            levels.CurrentCombat,
            levels.Combat,
            CombatColor,
            reasons.Combat);
        var explore = this.CreateLevelCard(
            "Explore",
            levels.CurrentExplore,
            levels.Explore,
            ExploreColor,
            reasons.Explore);
        var trade = this.CreateLevelCard(
            "Trade",
            levels.CurrentTrade,
            levels.Trade,
            TradeColor,
            reasons.Trade);
        var skillPoints = this.CreateSkillPointCard(
            analysis,
            editable,
            reasons.SkillPoints);
        skillPoints.Card.Margin = Padding.Empty;

        strip.Controls.Add(overall.Card, 0, 0);
        strip.Controls.Add(hull.Card, 1, 0);
        strip.Controls.Add(combat.Card, 2, 0);
        strip.Controls.Add(explore.Card, 3, 0);
        strip.Controls.Add(trade.Card, 4, 0);
        strip.Controls.Add(skillPoints.Card, 5, 0);

        this.levelStripBinding = new LevelStripBinding(
            overall,
            hull,
            combat,
            explore,
            trade,
            skillPoints);
        return strip;
    }

    private BoundLevelCard CreateHullCard(
        SkillBuildAnalysis analysis,
        bool editable,
        IReadOnlyList<string> reasons)
    {
        if (!analysis.HasSupportedEquipmentSlots)
        {
            var maximum = this.workspace.HullCatalog.GetMaximum(
                analysis.Build.ProfessionIndex);
            return this.CreateTextCard(
                "Hull",
                "Too many slots",
                MainWindowTheme.Danger,
                MainWindowTheme.Danger,
                [
                    string.Create(
                        CultureInfo.CurrentCulture,
                        $"This profession supports at most {maximum.WeaponSlots} weapons and {maximum.DeviceSlots} devices."),
                ]);
        }

        var currentTier = editable
            ? null
            : this.presentation.Baseline?.HullTier;

        return this.CreateLevelCard(
            "Hull",
            currentTier,
            analysis.RequiredHull.Tier,
            MainWindowTheme.Accent,
            reasons);
    }

    private BoundLevelCard CreateTextCard(
        string name,
        string text,
        Color labelColor,
        Color valueColor,
        IReadOnlyList<string> reasons)
    {
        var card = CreateLevelCardShell(name, labelColor, out var value);
        value.Text = text;
        value.ForeColor = valueColor;
        this.SetRequirementToolTip(card, reasons);
        return new BoundLevelCard(card, value, FormatPostCap: false);
    }

    private BoundLevelCard CreateLevelCard(
        string name,
        int? current,
        int target,
        Color labelColor,
        IReadOnlyList<string> reasons)
    {
        var card = CreateLevelCardShell(name, labelColor, out var value);
        var formatPostCap = string.Equals(
            name,
            "Overall",
            StringComparison.Ordinal);
        UpdateLevelValue(value, current, target, formatPostCap);
        this.SetRequirementToolTip(
            card,
            current.HasValue && current.Value >= target
                ? []
                : reasons);
        return new BoundLevelCard(card, value, formatPostCap);
    }

    private static TableLayoutPanel CreateLevelCardShell(
        string name,
        Color labelColor,
        out Label value)
    {
        var card = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 0, 4, 0),
            Padding = new Padding(8, 3, 8, 5),
            BackColor = MainWindowTheme.Panel,
        };
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 42.0f));
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 58.0f));
        card.Controls.Add(
            new Label
            {
                Dock = DockStyle.Fill,
                Text = name,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = MainWindowTheme.CreateBodyFont(8.5f),
                ForeColor = labelColor,
                BackColor = Color.Transparent,
            },
            0,
            0);
        value = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = MainWindowTheme.CreateHeadingFont(10.5f),
            ForeColor = MainWindowTheme.Text,
            BackColor = Color.Transparent,
            AutoEllipsis = true,
            Padding = new Padding(0, 0, 0, 4),
        };
        card.Controls.Add(value, 0, 1);
        return card;
    }

    private static void UpdateLevelValue(
        Label value,
        int? current,
        int target,
        bool formatPostCap = false)
    {
        var complete = current.HasValue && current.Value >= target;
        var targetText = formatPostCap && target > 150
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"150 + {target - 150}")
            : target.ToString(CultureInfo.InvariantCulture);
        value.Text = current.HasValue
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{current.Value} / {targetText}")
            : targetText;
        value.Font = MainWindowTheme.CreateHeadingFont(12.0f);
        value.ForeColor = complete
            ? MainWindowTheme.Success
            : current.HasValue
                ? MainWindowTheme.Accent
                : MainWindowTheme.Text;
    }

    private BoundLevelCard CreateSkillPointCard(
        SkillBuildAnalysis analysis,
        bool editable,
        IReadOnlyList<string> reasons)
    {
        var card = CreateLevelCardShell(
            "Skill points",
            MainWindowTheme.Warning,
            out var value);
        UpdateSkillPointValue(value, analysis, editable);
        this.SetRequirementToolTip(card, reasons);
        return new BoundLevelCard(card, value, FormatPostCap: false);
    }

    private void UpdateSkillPointValue(
        Label value,
        SkillBuildAnalysis analysis,
        bool editable)
    {
        if (editable)
        {
            value.Text = string.Create(
                CultureInfo.InvariantCulture,
                $"{analysis.GenericRequiredSkillPoints} total needed");
            value.ForeColor = MainWindowTheme.Text;
            return;
        }

        var needed = Math.Max(0, analysis.RemainingSkillPointCost);

        var available = Math.Max(0, analysis.AvailableSkillPoints);
        var deficit = Math.Max(0, needed - available);
        value.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"{available} available / {needed} needed");
        value.ForeColor = deficit == 0
            ? MainWindowTheme.Success
            : MainWindowTheme.Accent;
    }

    private void SetRequirementToolTip(
        Control card,
        IReadOnlyList<string> reasons)
    {
        var controls = card.Controls
            .Cast<Control>()
            .Prepend(card)
            .ToArray();
        if (reasons.Count == 0)
        {
            foreach (var control in controls)
            {
                this.itemToolTip.Remove(control);
            }
            return;
        }

        var title = card.Controls
            .Cast<Control>()
            .OfType<Label>()
            .FirstOrDefault()?.Text ?? "Requirement";
        List<ActionToolTipParagraph> paragraphs =
        [
            new ActionToolTipParagraph(
                [new(title, ActionToolTipTextRole.Accent, Bold: true)],
                ActionToolTipParagraphStyle.Header,
                SpaceAfter: 7),
        ];
        foreach (var reason in reasons)
        {
            paragraphs.Add(
                new ActionToolTipParagraph(
                    [new(reason)],
                    SpaceAfter: 2));
        }
        var content = new ActionToolTipContent(paragraphs, Icon: null);
        foreach (var control in controls)
        {
            this.itemToolTip.SetToolTip(
                control,
                content,
                SystemInformation.MouseHoverTime);
        }
    }

    private Control CreateSkillsSection(
        SkillBuildAnalysis analysis,
        bool editable)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = Padding.Empty,
            BackColor = MainWindowTheme.Background,
        };
        var heading = CreateSkillLegend(editable);

        var list = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = MainWindowTheme.Background,
        };
        list.SizeChanged += (_, _) => ResizeFlowChildren(list);

        foreach (var skill in analysis.Skills)
        {
            list.Controls.Add(
                this.CreateSkillRow(skill, editable));
        }

        panel.Controls.Add(list);
        panel.Controls.Add(heading);
        return panel;
    }

    private static Control CreateSkillLegend(bool editable)
    {
        var legend = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 28,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = new Padding(8, 0, 8, 0),
            BackColor = MainWindowTheme.Panel,
        };
        legend.Controls.Add(CreateLegendLabel("Skills  ·  ", MainWindowTheme.Text));
        legend.Controls.Add(CreateLegendLabel(
            editable ? "gold required" : "gold trained",
            MainWindowTheme.Warning));
        legend.Controls.Add(CreateLegendLabel(", ", MainWindowTheme.Text));
        legend.Controls.Add(CreateLegendLabel(
            editable ? "blue recommended" : "blue missing",
            MainWindowTheme.Accent));
        return legend;
    }

    private static Label CreateLegendLabel(string text, Color color) =>
        new()
        {
            AutoSize = true,
            Height = 28,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            Text = text,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = MainWindowTheme.CreateHeadingFont(9.0f),
            ForeColor = color,
            BackColor = Color.Transparent,
        };

    private Control CreateSkillRow(
        SkillBuildSkillAnalysis skill,
        bool editable)
    {
        var row = new TableLayoutPanel
        {
            Height = 42,
            Width = 300,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 1),
            Padding = new Padding(10, 3, 8, 3),
            BackColor = MainWindowTheme.ElevatedPanel,
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100.0f));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 225.0f));
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100.0f));

        var name = new Label
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Text = skill.Skill.Name,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = MainWindowTheme.CreateHeadingFont(10.0f),
            ForeColor = MainWindowTheme.Text,
            BackColor = Color.Transparent,
            AutoEllipsis = true,
        };
        SkillPipsControlBase pips;
        if (editable)
        {
            var editablePips = new EditableSkillRankPipsControl(
                skill.EquipmentMinimumRank,
                skill.TargetRank,
                skill.EquipmentMinimumRank,
                skill.MaximumRank)
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(4, 0, 0, 0),
            };
            editablePips.TargetRankChanged += (_, e) =>
                this.QueueBuildCommand(
                    "change a recommended skill rank",
                    () => this.ChangeRecommendedRank(
                        skill.Skill.Id,
                        e.TargetRank));
            pips = editablePips;
        }
        else
        {
            pips = new RankComparisonPipsControl(
                skill.CurrentRank.GetValueOrDefault(),
                skill.TargetRank,
                skill.MaximumRank)
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(4, 0, 0, 0),
            };
        }

        row.Controls.Add(name, 0, 0);
        row.Controls.Add(pips, 1, 0);
        this.RegisterSkillToolTip(skill, row, name, pips);
        this.skillRows[skill.Skill.Id] = new SkillRowBinding(
            row,
            name,
            pips,
            editable);
        return row;
    }

    private void RegisterSkillToolTip(
        SkillBuildSkillAnalysis skill,
        params Control[] controls)
    {
        if (string.IsNullOrWhiteSpace(skill.Skill.Description))
        {
            return;
        }

        var content = new ActionToolTipContent(
            [
                new ActionToolTipParagraph(
                    [
                        new(
                            skill.Skill.Name,
                            ActionToolTipTextRole.Accent,
                            Bold: true),
                    ],
                    ActionToolTipParagraphStyle.Header,
                    SpaceAfter: 7),
                new ActionToolTipParagraph(
                    [new(skill.Skill.Description.Trim())]),
            ],
            Icon: null);
        foreach (var control in controls)
        {
            this.itemToolTip.SetToolTip(
                control,
                content,
                SystemInformation.MouseHoverTime);
        }
    }

    private Control CreateEquipmentSection(
        SkillBuildAnalysis analysis,
        bool editable)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = Padding.Empty,
            BackColor = MainWindowTheme.Background,
        };
        var heading = new Label
        {
            Dock = DockStyle.Top,
            Height = 28,
            Padding = new Padding(8, 0, 8, 0),
            Text = "Equipment",
            TextAlign = ContentAlignment.MiddleLeft,
            Font = MainWindowTheme.CreateHeadingFont(9.0f),
            ForeColor = MainWindowTheme.Text,
            BackColor = MainWindowTheme.Panel,
        };

        var scroll = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = MainWindowTheme.Background,
        };
        var board = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 3,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = new Padding(0, 0, 0, 6),
            BackColor = MainWindowTheme.Background,
        };
        board.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34.0f));
        board.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32.0f));
        board.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34.0f));

        board.Controls.Add(
            this.CreateEquipmentGroupHost(
                SkillBuildEquipmentKind.Weapon,
                analysis,
                editable),
            0,
            0);

        var middle = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(4, 0, 4, 0),
            Padding = Padding.Empty,
            BackColor = Color.Transparent,
        };
        middle.Controls.Add(
            this.CreateEquipmentGroupHost(
                SkillBuildEquipmentKind.Shield,
                analysis,
                editable),
            0,
            0);
        middle.Controls.Add(
            this.CreateEquipmentGroupHost(
                SkillBuildEquipmentKind.Reactor,
                analysis,
                editable),
            0,
            1);
        middle.Controls.Add(
            this.CreateEquipmentGroupHost(
                SkillBuildEquipmentKind.Engine,
                analysis,
                editable),
            0,
            2);
        board.Controls.Add(middle, 1, 0);

        board.Controls.Add(
            this.CreateEquipmentGroupHost(
                SkillBuildEquipmentKind.Device,
                analysis,
                editable),
            2,
            0);

        var notes = this.CreateBuildNotesSection(
            editable ? this.draft?.Notes ?? "" : analysis.Build.Notes,
            editable);

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = notes == null ? 1 : 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = MainWindowTheme.Background,
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100.0f));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.Controls.Add(board, 0, 0);
        if (notes != null)
        {
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, notes.Height));
            content.Controls.Add(notes, 0, 1);
        }

        scroll.Controls.Add(content);
        panel.Controls.Add(scroll);
        panel.Controls.Add(heading);
        return panel;
    }

    private Control? CreateBuildNotesSection(
        string notes,
        bool editable)
    {
        if (!editable && string.IsNullOrWhiteSpace(notes))
        {
            return null;
        }

        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = editable ? 132 : 112,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = new Padding(0, 8, 0, 0),
            BackColor = MainWindowTheme.Background,
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100.0f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26.0f));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100.0f));
        panel.Controls.Add(CreateEquipmentGroupHeading("Notes"), 0, 0);

        var text = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            AcceptsReturn = true,
            ReadOnly = !editable,
            Text = notes,
            MaxLength = 8000,
            Margin = Padding.Empty,
        };
        MainWindowTheme.StyleTextBox(text);
        if (!editable)
        {
            text.TabStop = false;
            text.BackColor = MainWindowTheme.ElevatedPanel;
        }
        else
        {
            text.TextChanged += (_, _) =>
            {
                if (this.draft != null)
                {
                    this.draft.Notes = text.Text;
                }
            };
        }
        panel.Controls.Add(text, 0, 1);
        return panel;
    }

    private Panel CreateEquipmentGroupHost(
        SkillBuildEquipmentKind kind,
        SkillBuildAnalysis analysis,
        bool editable)
    {
        var host = new Panel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = MainWindowTheme.Background,
        };
        host.Controls.Add(this.CreateEquipmentGroup(kind, analysis, editable));
        this.equipmentGroupHosts[kind] = host;
        return host;
    }

    private Control CreateEquipmentGroup(
        SkillBuildEquipmentKind kind,
        SkillBuildAnalysis analysis,
        bool editable)
    {
        var title = kind switch
        {
            SkillBuildEquipmentKind.Weapon => "Weapons",
            SkillBuildEquipmentKind.Shield => "Shield",
            SkillBuildEquipmentKind.Reactor => "Reactor",
            SkillBuildEquipmentKind.Engine => "Engine",
            SkillBuildEquipmentKind.Device => "Devices",
            _ => "Equipment",
        };
        return this.CreateEquipmentGroup(
            title,
            kind,
            analysis,
            editable,
            allowMany: kind is SkillBuildEquipmentKind.Weapon or
                SkillBuildEquipmentKind.Device);
    }

    private Control CreateEquipmentGroup(
        string title,
        SkillBuildEquipmentKind kind,
        SkillBuildAnalysis analysis,
        bool editable,
        bool allowMany)
    {
        var group = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = MainWindowTheme.Background,
        };
        group.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100.0f));

        var heading = CreateEquipmentGroupHeading(title);
        heading.Height = 26;
        group.Controls.Add(heading, 0, 0);

        var values = analysis.Equipment
            .Where(value => value.Requirement.Kind == kind)
            .OrderBy(value => value.Requirement.Order)
            .ToArray();
        var maximumHull = this.workspace.HullCatalog.GetMaximum(
            analysis.Build.ProfessionIndex);
        var maximumCount = kind switch
        {
            SkillBuildEquipmentKind.Weapon => maximumHull.WeaponSlots,
            SkillBuildEquipmentKind.Device => maximumHull.DeviceSlots,
            _ => 1,
        };
        var row = 1;
        foreach (var value in values)
        {
            group.RowCount++;
            group.Controls.Add(
                this.CreateEquipmentCard(value, editable),
                0,
                row++);
        }

        if (editable && (allowMany || values.Length == 0))
        {
            group.RowCount++;
            var canAdd = values.Length < maximumCount;
            var add = CreateActionButton(
                canAdd
                    ? allowMany
                        ? $"+ Add {title.TrimEnd('s').ToLowerInvariant()}"
                        : "+ Choose"
                    : string.Create(
                        CultureInfo.CurrentCulture,
                        $"Maximum {maximumCount}"),
                width: 120);
            add.Dock = DockStyle.Top;
            add.Height = 30;
            add.Margin = new Padding(0, 3, 0, 5);
            add.Enabled = canAdd;
            if (canAdd)
            {
                add.Click += (_, _) => this.QueueBuildCommand(
                    $"add {title.TrimEnd('s').ToLowerInvariant()}",
                    () => this.AddEquipmentRequirement(kind));
            }
            group.Controls.Add(add, 0, row);
        }

        return group;
    }

    private Control CreateEquipmentCard(
        SkillBuildEquipmentRequirementAnalysis analysis,
        bool editable)
    {
        var alternativeCount = analysis.Requirement.Alternatives.Count;
        var additionalAlternativeCount = Math.Max(0, alternativeCount - 1);
        var showSecondLine = !editable || additionalAlternativeCount > 0;
        var card = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = showSecondLine ? 62 : 48,
            ColumnCount = editable ? 3 : 2,
            RowCount = showSecondLine ? 2 : 1,
            Margin = new Padding(0, 0, 0, 3),
            Padding = new Padding(6, 4, 5, 4),
            BackColor = MainWindowTheme.ElevatedPanel,
        };
        if (showSecondLine)
        {
            card.RowStyles.Add(new RowStyle(SizeType.Percent, 55.0f));
            card.RowStyles.Add(new RowStyle(SizeType.Percent, 45.0f));
        }
        else
        {
            card.RowStyles.Add(new RowStyle(SizeType.Percent, 100.0f));
        }
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 36.0f));
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100.0f));
        if (editable)
        {
            card.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34.0f));
        }

        var preferredChoice =
            analysis.Requirement.Alternatives.FirstOrDefault();
        var effectiveChoice = analysis.EffectiveChoice;
        var choice = editable
            ? effectiveChoice ?? preferredChoice
            : preferredChoice;
        var nameText = choice == null
            ? "Choose equipment"
            : string.IsNullOrWhiteSpace(choice.ItemName)
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"Item #{choice.ItemTemplateId}")
                : choice.ItemName;
        var icon = new PictureBox
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 1, 5, 1),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent,
            Image = choice == null
                ? null
                : this.GetItemIcon(
                    choice.ItemTemplateId,
                    new Size(30, 30)),
            Cursor = editable ? Cursors.Hand : Cursors.Default,
        };
        var name = new Label
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Text = nameText,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = MainWindowTheme.CreateHeadingFont(9.5f),
            ForeColor = MainWindowTheme.Text,
            BackColor = Color.Transparent,
            AutoEllipsis = true,
            Cursor = editable ? Cursors.Hand : Cursors.Default,
        };
        if (editable)
        {
            EventHandler replace = (_, _) => this.QueueBuildCommand(
                "replace equipment",
                () => this.ReplacePreferredEquipment(
                    analysis.Requirement.RequirementId));
            name.Click += replace;
            icon.Click += replace;
        }
        card.Controls.Add(icon, 0, 0);
        card.SetRowSpan(icon, showSecondLine ? 2 : 1);
        card.Controls.Add(name, 1, 0);

        Label? status = null;
        if (showSecondLine)
        {
            var availabilityText = analysis.Availability switch
            {
                SkillBuildEquipmentAvailability.Equipped => "Equipped",
                SkillBuildEquipmentAvailability.Inventory => "In inventory",
                SkillBuildEquipmentAvailability.Vault => "In vault",
                _ => "Missing",
            };
            var usesAcceptedAlternative =
                !editable &&
                preferredChoice != null &&
                effectiveChoice != null &&
                preferredChoice.ItemTemplateId != effectiveChoice.ItemTemplateId;
            var statusText = editable
                ? string.Create(
                    CultureInfo.CurrentCulture,
                    $"{additionalAlternativeCount} {(additionalAlternativeCount == 1 ? "alternative" : "alternatives")}")
                : usesAcceptedAlternative
                    ? string.Concat(availabilityText, " · ",
                        string.IsNullOrWhiteSpace(effectiveChoice!.ItemName)
                            ? string.Create(
                                CultureInfo.InvariantCulture,
                                $"Item #{effectiveChoice.ItemTemplateId}")
                            : effectiveChoice.ItemName)
                    : additionalAlternativeCount > 0
                        ? string.Create(
                            CultureInfo.CurrentCulture,
                            $"{availabilityText} · +{additionalAlternativeCount} {(additionalAlternativeCount == 1 ? "alternative" : "alternatives")}")
                        : availabilityText;
            status = new Label
            {
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                Text = statusText,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = MainWindowTheme.CreateBodyFont(8.5f),
                ForeColor = editable
                    ? MainWindowTheme.MutedText
                    : analysis.Availability switch
                    {
                        SkillBuildEquipmentAvailability.Equipped =>
                            MainWindowTheme.Success,
                        SkillBuildEquipmentAvailability.Inventory =>
                            MainWindowTheme.Warning,
                        SkillBuildEquipmentAvailability.Vault =>
                            MainWindowTheme.Accent,
                        _ => MainWindowTheme.Danger,
                    },
                BackColor = Color.Transparent,
                AutoEllipsis = true,
            };
            card.Controls.Add(status, 1, 1);
        }

        if (editable)
        {
            var choices = CreateCompactButton("⋯");
            choices.Click += (_, _) => this.RunBuildCommand(
                "open equipment choices",
                () => this.ShowEquipmentChoicesMenu(
                    choices,
                    analysis.Requirement.RequirementId),
                hideOnFailure: false);
            card.Controls.Add(choices, 2, 0);
            card.SetRowSpan(choices, showSecondLine ? 2 : 1);
        }

        if (choice != null)
        {
            this.RegisterItemToolTip(
                choice.ItemTemplateId,
                nameText,
                icon.Image,
                card,
                icon,
                name);

            if (status != null)
            {
                var statusChoice =
                    !editable &&
                    effectiveChoice != null &&
                    effectiveChoice.ItemTemplateId != choice.ItemTemplateId
                        ? effectiveChoice
                        : choice;
                var statusName = string.IsNullOrWhiteSpace(statusChoice.ItemName)
                    ? string.Create(
                        CultureInfo.InvariantCulture,
                        $"Item #{statusChoice.ItemTemplateId}")
                    : statusChoice.ItemName;
                this.RegisterItemToolTip(
                    statusChoice.ItemTemplateId,
                    statusName,
                    this.GetItemIcon(
                        statusChoice.ItemTemplateId,
                        new Size(30, 30)),
                    status);
            }
        }

        return card;
    }

    private void RefreshEditorBoard(
        bool refreshLevels,
        bool refreshSkills,
        bool refreshEquipment,
        SkillBuildEquipmentKind? equipmentKind = null)
    {
        if (!this.editMode || this.draft == null)
        {
            return;
        }

        var analysis = this.workspace.Board.Analyze(
            this.draft.ToDocument(),
            this.presentation.Baseline,
            equipped: null);

        if (refreshLevels)
        {
            this.UpdateLevelStrip(analysis, editable: true);
        }
        if (refreshSkills)
        {
            this.UpdateSkillRows(analysis, editable: true);
        }
        if (refreshEquipment)
        {
            this.RefreshEquipmentGroups(
                analysis,
                editable: true,
                equipmentKind);
        }
    }

    private void RefreshEquipmentGroups(
        SkillBuildAnalysis analysis,
        bool editable,
        SkillBuildEquipmentKind? kind)
    {
        if (this.equipmentHost == null || this.equipmentHost.IsDisposed)
        {
            Debug.WriteLine("[Builds] Equipment host was unavailable during a localized refresh.");
            return;
        }

        if (this.equipmentGroupHosts.Count == 0)
        {
            this.equipmentGroupHosts.Clear();
            ReplaceHostContent(
                this.equipmentHost,
                this.CreateEquipmentSection(analysis, editable),
                preserveScroll: true);
            this.ApplyDarkScrollbarTheme(this.equipmentHost);
            return;
        }

        var scrollable = FindScrollableChild(this.equipmentHost);
        var scroll = scrollable?.AutoScrollPosition ?? Point.Empty;
        SkillBuildEquipmentKind[] kinds = kind.HasValue
            ? [kind.Value]
            : new[]
            {
                SkillBuildEquipmentKind.Weapon,
                SkillBuildEquipmentKind.Shield,
                SkillBuildEquipmentKind.Reactor,
                SkillBuildEquipmentKind.Engine,
                SkillBuildEquipmentKind.Device,
            };

        foreach (var refreshKind in kinds)
        {
            if (!this.equipmentGroupHosts.TryGetValue(
                    refreshKind,
                    out var host) ||
                host.IsDisposed)
            {
                this.equipmentGroupHosts.Clear();
                ReplaceHostContent(
                    this.equipmentHost,
                    this.CreateEquipmentSection(analysis, editable),
                    preserveScroll: true);
                this.ApplyDarkScrollbarTheme(this.equipmentHost);
                return;
            }

            ReplaceHostContent(
                host,
                this.CreateEquipmentGroup(
                    refreshKind,
                    analysis,
                    editable),
                preserveScroll: false);
            this.ApplyDarkScrollbarTheme(host);
        }

        RestoreScroll(scrollable, scroll);
    }

    private void ChangeRecommendedRank(
        int skillId,
        int targetRank)
    {
        if (this.draft == null)
        {
            return;
        }

        var analysis = this.workspace.Board.Analyze(
            this.draft.ToDocument(),
            this.presentation.Baseline,
            equipped: null);
        var skill = analysis.Skills.FirstOrDefault(value =>
            value.Skill.Id == skillId);
        if (skill == null)
        {
            return;
        }

        this.draft.SetRecommendedRank(
            skillId,
            targetRank,
            skill.EquipmentMinimumRank);
        this.RefreshEditorBoard(
            refreshLevels: true,
            refreshSkills: true,
            refreshEquipment: false);
    }

    private void UpdateLevelStrip(
        SkillBuildAnalysis analysis,
        bool editable)
    {
        if (this.levelStripBinding == null)
        {
            Debug.WriteLine("[Builds] Level strip binding was unavailable during a localized refresh.");
            return;
        }

        var levels = editable
            ? analysis.GenericLevels
            : analysis.EffectiveLevels;
        var reasons = editable
            ? analysis.GenericReasons
            : analysis.EffectiveReasons;
        UpdateBoundLevelCard(
            this.levelStripBinding.Overall,
            levels.CurrentOverall,
            levels.Overall,
            reasons.Overall);
        this.UpdateHullCard(
            this.levelStripBinding.Hull,
            analysis,
            editable,
            reasons.Hull);
        UpdateBoundLevelCard(
            this.levelStripBinding.Combat,
            levels.CurrentCombat,
            levels.Combat,
            reasons.Combat);
        UpdateBoundLevelCard(
            this.levelStripBinding.Explore,
            levels.CurrentExplore,
            levels.Explore,
            reasons.Explore);
        UpdateBoundLevelCard(
            this.levelStripBinding.Trade,
            levels.CurrentTrade,
            levels.Trade,
            reasons.Trade);
        UpdateSkillPointValue(
            this.levelStripBinding.SkillPoints.Value,
            analysis,
            editable);
        this.SetRequirementToolTip(
            this.levelStripBinding.SkillPoints.Card,
            reasons.SkillPoints);
    }

    private void UpdateHullCard(
        BoundLevelCard binding,
        SkillBuildAnalysis analysis,
        bool editable,
        IReadOnlyList<string> reasons)
    {
        if (!analysis.HasSupportedEquipmentSlots)
        {
            binding.Value.Text = "Too many slots";
            binding.Value.ForeColor = MainWindowTheme.Danger;
        }
        else
        {
            var current = editable
                ? null
                : this.presentation.Baseline?.HullTier;
            UpdateLevelValue(
                binding.Value,
                current,
                analysis.RequiredHull.Tier,
                binding.FormatPostCap);
            if (current.HasValue && current.Value >= analysis.RequiredHull.Tier)
            {
                reasons = [];
            }
        }
        this.SetRequirementToolTip(binding.Card, reasons);
    }

    private void UpdateBoundLevelCard(
        BoundLevelCard binding,
        int? current,
        int target,
        IReadOnlyList<string> reasons)
    {
        UpdateLevelValue(
            binding.Value,
            current,
            target,
            binding.FormatPostCap);
        this.SetRequirementToolTip(
            binding.Card,
            current.HasValue && current.Value >= target
                ? []
                : reasons);
    }

    private void UpdateSkillRows(
        SkillBuildAnalysis analysis,
        bool editable)
    {
        if (this.skillsHost == null || this.skillsHost.IsDisposed)
        {
            Debug.WriteLine("[Builds] Skills host was unavailable during a localized refresh.");
            return;
        }

        if (analysis.Skills.Count != this.skillRows.Count ||
            analysis.Skills.Any(value => !this.skillRows.ContainsKey(value.Skill.Id)))
        {
            this.skillRows.Clear();
            ReplaceHostContent(
                this.skillsHost!,
                this.CreateSkillsSection(analysis, editable),
                preserveScroll: true);
            this.ApplyDarkScrollbarTheme(this.skillsHost!);
            return;
        }

        foreach (var skill in analysis.Skills)
        {
            this.skillRows[skill.Skill.Id].Update(skill, editable);
        }
    }

    private static void ReplaceHostContent(
        Panel host,
        Control replacement,
        bool preserveScroll)
    {
        var scroll = preserveScroll
            ? FindScrollableChild(host)?.AutoScrollPosition ?? Point.Empty
            : Point.Empty;

        host.SuspendLayout();
        try
        {
            foreach (Control child in host.Controls.Cast<Control>().ToArray())
            {
                host.Controls.Remove(child);
                child.Dispose();
            }
            host.Controls.Add(replacement);
        }
        finally
        {
            host.ResumeLayout(performLayout: true);
        }

        if (preserveScroll)
        {
            RestoreScroll(FindScrollableChild(host), scroll);
        }
    }

    private void AddEquipmentRequirement(SkillBuildEquipmentKind kind)
    {
        var maximumHull = this.workspace.HullCatalog.GetMaximum(
            this.draft!.ProfessionIndex);
        var maximumCount = kind switch
        {
            SkillBuildEquipmentKind.Weapon => maximumHull.WeaponSlots,
            SkillBuildEquipmentKind.Device => maximumHull.DeviceSlots,
            _ => 1,
        };
        var currentCount = this.draft.Equipment.Count(value =>
            value.Kind == kind);
        if (currentCount >= maximumCount)
        {
            MessageBox.Show(
                this,
                string.Create(
                    CultureInfo.CurrentCulture,
                    $"This profession supports at most {maximumCount} {FormatEquipmentKindPlural(kind)}."),
                "Hull limit",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var selection = this.PickEquipment(
            kind,
            SkillBuildEquipmentPickerMode.Choose);
        if (selection == null)
        {
            return;
        }

        this.draft!.AddRequirement(
            kind,
            ToAlternative(selection.Choice));
        this.RefreshEditorBoard(
            refreshLevels: true,
            refreshSkills: true,
            refreshEquipment: true,
            equipmentKind: kind);
    }

    private void ReplacePreferredEquipment(string requirementId)
    {
        var requirement = this.draft!.Equipment.FirstOrDefault(value =>
            string.Equals(
                value.RequirementId,
                requirementId,
                StringComparison.Ordinal));
        if (requirement == null)
        {
            return;
        }

        var selection = this.PickEquipment(
            requirement.Kind,
            SkillBuildEquipmentPickerMode.ReplaceOrAlternative);
        if (selection == null)
        {
            return;
        }

        requirement = this.FindDraftRequirement(requirementId);
        if (requirement == null)
        {
            return;
        }

        if (selection.Action == SkillBuildEquipmentPickerAction.AddAlternative)
        {
            if (requirement.Alternatives.All(value =>
                    value.ItemTemplateId != selection.Choice.ItemTemplateId))
            {
                requirement.Alternatives.Add(ToAlternative(selection.Choice));
            }
        }
        else
        {
            this.draft.ReplacePrimaryAlternative(
                requirementId,
                ToAlternative(selection.Choice));
        }
        this.RefreshEditorBoard(
            refreshLevels: true,
            refreshSkills: true,
            refreshEquipment: true,
            equipmentKind: requirement.Kind);
    }

    private void ShowEquipmentChoicesMenu(
        Control owner,
        string requirementId)
    {
        var requirement = this.draft!.Equipment.FirstOrDefault(value =>
            string.Equals(
                value.RequirementId,
                requirementId,
                StringComparison.Ordinal));
        if (requirement == null)
        {
            return;
        }

        var menu = this.CreateTransientContextMenu(showImages: true);
        menu.MouseMove += this.EquipmentMenu_OnMouseMove;
        menu.MouseLeave += this.EquipmentMenu_OnMouseLeave;

        for (var index = 0; index < requirement.Alternatives.Count; index++)
        {
            var alternative = requirement.Alternatives[index];
            var item = new ToolStripMenuItem(alternative.ItemName)
            {
                Image = this.GetItemIcon(
                    alternative.ItemTemplateId,
                    new Size(24, 24)),
                Tag = alternative.ItemTemplateId,
            };
            var itemTemplateId = alternative.ItemTemplateId;
            if (index > 0)
            {
                item.DropDownItems.Add(
                    "Make primary",
                    null,
                    (_, _) => this.QueueBuildCommand(
                        "change the primary equipment choice",
                        () =>
                        {
                            var current = this.FindDraftRequirement(requirementId);
                            var currentIndex = current?.Alternatives.FindIndex(value =>
                                value.ItemTemplateId == itemTemplateId) ?? -1;
                            if (current == null || currentIndex <= 0)
                            {
                                return;
                            }

                            var selected = current.Alternatives[currentIndex];
                            current.Alternatives.RemoveAt(currentIndex);
                            current.Alternatives.Insert(0, selected);
                            this.RefreshEditorBoard(
                                refreshLevels: true,
                                refreshSkills: true,
                                refreshEquipment: true,
                                equipmentKind: current.Kind);
                        }));
            }
            if (requirement.Alternatives.Count > 1)
            {
                item.DropDownItems.Add(
                    "Remove alternative",
                    null,
                    (_, _) => this.QueueBuildCommand(
                        "remove an equipment alternative",
                        () =>
                        {
                            var current = this.FindDraftRequirement(requirementId);
                            if (current == null || current.Alternatives.Count <= 1)
                            {
                                return;
                            }

                            current.Alternatives.RemoveAll(value =>
                                value.ItemTemplateId == itemTemplateId);
                            this.RefreshEditorBoard(
                                refreshLevels: true,
                                refreshSkills: true,
                                refreshEquipment: true,
                                equipmentKind: current.Kind);
                        }));
            }
            menu.Items.Add(item);
        }

        if (menu.Items.Count > 0)
        {
            menu.Items.Add(new ToolStripSeparator());
        }
        menu.Items.Add(
            "Add alternative",
            null,
            (_, _) => this.QueueBuildCommand(
                "add an equipment alternative",
                () =>
                {
                    var current = this.FindDraftRequirement(requirementId);
                    if (current == null)
                    {
                        return;
                    }

                    var selection = this.PickEquipment(
                        current.Kind,
                        SkillBuildEquipmentPickerMode.Alternative);
                    current = this.FindDraftRequirement(requirementId);
                    if (selection == null ||
                        current == null ||
                        current.Alternatives.Any(value =>
                            value.ItemTemplateId == selection.Choice.ItemTemplateId))
                    {
                        return;
                    }

                    current.Alternatives.Add(
                        ToAlternative(selection.Choice));
                    this.RefreshEditorBoard(
                        refreshLevels: false,
                        refreshSkills: false,
                        refreshEquipment: true,
                        equipmentKind: current.Kind);
                }));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(
            "Remove from build",
            null,
            (_, _) => this.QueueBuildCommand(
                "remove equipment from the build",
                () =>
                {
                    var current = this.FindDraftRequirement(requirementId);
                    if (current == null)
                    {
                        return;
                    }

                    var kind = current.Kind;
                    this.draft!.RemoveRequirement(requirementId);
                    this.RefreshEditorBoard(
                        refreshLevels: true,
                        refreshSkills: true,
                        refreshEquipment: true,
                        equipmentKind: kind);
                }));
        this.ShowTransientContextMenu(menu, owner);
    }

    private void EquipmentMenu_OnMouseMove(
        object? sender,
        MouseEventArgs e)
    {
        if (sender is not ContextMenuStrip menu ||
            menu.GetItemAt(e.Location)?.Tag is not int itemTemplateId)
        {
            if (sender is Control control)
            {
                this.itemToolTip.Remove(control);
            }
            return;
        }

        var choice = this.workspace.EquipmentCatalog.Find(itemTemplateId);
        var itemName = choice?.Name ?? string.Create(
            CultureInfo.InvariantCulture,
            $"Item #{itemTemplateId}");
        ClientRuntimeItemTemplateObservation? template = null;
        if (ClientItemTemplateCatalog.TryGetRuntimeObservation(
                itemTemplateId,
                out var observed))
        {
            template = observed;
        }
        var content = PilotArchiveItemToolTipBuilder.BuildCatalogItem(
            itemTemplateId,
            itemName,
            template,
            this.GetItemIcon(itemTemplateId, new Size(36, 36)));
        this.itemToolTip.SetHoveredToolTip(
            menu,
            content,
            SystemInformation.MouseHoverTime);
    }

    private void EquipmentMenu_OnMouseLeave(
        object? sender,
        EventArgs e)
    {
        if (sender is Control control)
        {
            this.itemToolTip.Remove(control);
        }
    }

    private EquipmentPickerSelection? PickEquipment(
        SkillBuildEquipmentKind kind,
        SkillBuildEquipmentPickerMode mode)
    {
        using var picker = new SkillBuildEquipmentPickerForm(
            this.workspace.EquipmentCatalog,
            kind,
            this.draft!.ProfessionIndex,
            this.resolveItemIcon,
            mode);
        return picker.ShowDialog(this) == DialogResult.OK &&
               picker.SelectedChoice != null
            ? new EquipmentPickerSelection(
                picker.SelectedChoice,
                picker.SelectedAction)
            : null;
    }

    private void BeginCreateFromCurrent()
    {
        var baseline = this.presentation.Baseline!;
        var title = string.Create(
            CultureInfo.InvariantCulture,
            $"{this.presentation.PilotName} build");
        this.draft = SkillBuildBoardDraft.CreateFromCurrent(
            baseline.ProfessionIndex,
            title,
            this.presentation.EquipmentBaseline);
        this.editMode = true;
        this.RebuildContent();
    }

    private void BeginCreateEmpty()
    {
        var baseline = this.presentation.Baseline!;
        this.draft = SkillBuildBoardDraft.CreateEmpty(
            baseline.ProfessionIndex,
            "New build");
        this.editMode = true;
        this.RebuildContent();
    }

    private void BeginEdit(SkillBuildDocument build)
    {
        this.draft = SkillBuildBoardDraft.FromDocument(build);
        this.editMode = true;
        this.RebuildContent();
    }

    private void BeginCopy(SkillBuildDocument build)
    {
        this.draft = SkillBuildBoardDraft.CreateCopy(build);
        this.editMode = true;
        this.RebuildContent();
    }

    private void ShowNewBuildMenu(Control owner)
    {
        var menu = this.CreateTransientContextMenu();
        menu.Items.Add(
            this.presentation.IsArchivedContext
                ? "Create from stored equipment"
                : "Create from current equipment",
            null,
            (_, _) => this.QueueBuildCommand(
                this.presentation.IsArchivedContext
                    ? "create a build from stored equipment"
                    : "create a build from current equipment",
                this.BeginCreateFromCurrent));
        var selected = this.GetSelectedBuild();
        if (selected != null)
        {
            menu.Items.Add(
                "Copy selected build",
                null,
                (_, _) => this.QueueBuildCommand(
                    "copy the selected build",
                    () => this.BeginCopy(selected)));
        }
        menu.Items.Add(
            "Start empty",
            null,
            (_, _) => this.QueueBuildCommand(
                "create an empty build",
                this.BeginCreateEmpty));
        this.ShowTransientContextMenu(menu, owner);
    }

    private SkillBuildEquipmentRequirementDraft? FindDraftRequirement(
        string requirementId) =>
        this.draft?.Equipment.FirstOrDefault(value => string.Equals(
            value.RequirementId,
            requirementId,
            StringComparison.Ordinal));

    private ContextMenuStrip CreateTransientContextMenu(
        bool showImages = false)
    {
        var menu = new ContextMenuStrip
        {
            Tag = showImages,
            ShowImageMargin = showImages,
            ShowItemToolTips = false,
        };
        MainWindowTheme.StyleContextMenu(menu);
        menu.ShowImageMargin = showImages;
        this.transientMenus.Add(menu);
        menu.Closed += this.TransientContextMenu_OnClosed;
        return menu;
    }

    private void ShowTransientContextMenu(
        ContextMenuStrip menu,
        Control owner)
    {
        try
        {
            MainWindowTheme.StyleContextMenu(menu);
            menu.ShowImageMargin = menu.Tag is true;
            menu.Show(owner, new Point(0, owner.Height));
        }
        catch
        {
            this.DisposeTransientMenu(menu);
            throw;
        }
    }

    private void TransientContextMenu_OnClosed(
        object? sender,
        ToolStripDropDownClosedEventArgs e)
    {
        if (sender is ContextMenuStrip menu)
        {
            this.QueueTransientMenuDisposal(menu);
        }
    }

    private void QueueTransientMenuDisposal(ContextMenuStrip menu)
    {
        if (menu.IsDisposed)
        {
            this.transientMenus.Remove(menu);
            return;
        }

        if (!this.IsHandleCreated || this.IsDisposed || this.Disposing)
        {
            return;
        }

        try
        {
            this.BeginInvoke(new Action(() => this.DisposeTransientMenu(menu)));
        }
        catch (InvalidOperationException)
        {
            // The Board is closing. Dispose() owns any remaining menus.
        }
    }

    private void DisposeTransientMenu(ContextMenuStrip menu)
    {
        menu.Closed -= this.TransientContextMenu_OnClosed;
        try
        {
            if (!menu.IsDisposed)
            {
                menu.Dispose();
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"[Builds] Could not dispose a Build menu: {exception}"));
        }
        finally
        {
            this.transientMenus.Remove(menu);
        }
    }

    private void DisposeTransientMenus()
    {
        foreach (var menu in this.transientMenus.ToArray())
        {
            this.DisposeTransientMenu(menu);
        }

        this.transientMenus.Clear();
    }

    private void SaveDraft()
    {
        var build = this.draft!.ToDocument();
        if (string.IsNullOrWhiteSpace(build.Title))
        {
            MessageBox.Show(
                this,
                "Give this build a name before saving it.",
                "Build name required",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        if (build.Equipment.Count == 0)
        {
            MessageBox.Show(
                this,
                "Choose at least one piece of equipment before saving this build.",
                "Equipment required",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var analysis = this.workspace.Board.Analyze(
            build,
            this.presentation.Baseline,
            equipped: null);
        if (!analysis.HasSupportedEquipmentSlots)
        {
            MessageBox.Show(
                this,
                analysis.Issues.FirstOrDefault() ??
                "This build needs more equipment positions than the profession can have.",
                "Hull limit",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        if (analysis.Issues.Count > 0)
        {
            MessageBox.Show(
                this,
                analysis.Issues[0],
                "Build needs attention",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        this.suppressWorkspaceRefresh = true;
        try
        {
            this.workspace.SaveBuild(this.presentation.CharacterId, build);
        }
        finally
        {
            this.suppressWorkspaceRefresh = false;
        }
        this.selectedBuildId = build.BuildId;
        this.draft = null;
        this.editMode = false;
        this.ReloadLibrary();
        this.RebuildContent();
    }

    private SkillBuildDocument? GetSelectedBuild() =>
        this.selectedBuildId == null
            ? null
            : this.library.Builds.FirstOrDefault(value => string.Equals(
                value.BuildId,
                this.selectedBuildId,
                StringComparison.OrdinalIgnoreCase));

    private void Workspace_OnChanged(object? sender, EventArgs e)
    {
        if (this.suppressWorkspaceRefresh || this.forgeBusy)
        {
            return;
        }

        if (this.IsDisposed || this.Disposing)
        {
            return;
        }

        if (this.InvokeRequired)
        {
            try
            {
                this.BeginInvoke(() => this.Workspace_OnChanged(sender, e));
            }
            catch (InvalidOperationException)
            {
                // The Board is closing or its handle has already gone away.
            }
            return;
        }

        this.RunBuildCommand(
            "refresh the Build library",
            () =>
            {
                this.ReloadLibrary();
                if (!this.editMode)
                {
                    this.RebuildContent();
                }
            },
            hideOnFailure: false);
    }

    private void SkillBuildBoardForm_OnFormClosing(
        object? sender,
        FormClosingEventArgs e)
    {
        try
        {
            this.PersistPlacement();
        }
        catch (Exception exception)
        {
            Debug.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"[Builds] Could not save the Build Board placement: {exception}"));
        }
    }

    private void PersistPlacement()
    {
        if (!this.placementRestored ||
            this.WindowState == FormWindowState.Minimized)
        {
            return;
        }

        var bounds = this.WindowState == FormWindowState.Normal
            ? this.Bounds
            : this.RestoreBounds;
        var defaultLocation = new Point(
            this.placementOwnerBounds.Left +
            Math.Max(24, (this.placementOwnerBounds.Width - bounds.Width) / 2),
            this.placementOwnerBounds.Top +
            Math.Max(24, (this.placementOwnerBounds.Height - bounds.Height) / 2));

        this.saveWindowPlacement(
            PlacementAddonId,
            PlacementWidgetId,
            new AddonWindowPlacement
            {
                AddonId = PlacementAddonId,
                WidgetId = PlacementWidgetId,
                OffsetX = bounds.Left - defaultLocation.X,
                OffsetY = bounds.Top - defaultLocation.Y,
                Width = bounds.Width,
                Height = bounds.Height,
                IsVisible = true,
            });
    }

    private void QueueBuildCommand(
        string operation,
        Action command)
    {
        if (!this.IsHandleCreated || this.IsDisposed || this.Disposing)
        {
            return;
        }

        try
        {
            this.BeginInvoke(
                () => this.RunBuildCommand(operation, command));
        }
        catch (InvalidOperationException exception)
        {
            Debug.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"[Builds] Could not queue '{operation}' because the Board is closing: {exception}"));
        }
    }

    private void RunBuildCommand(
        string operation,
        Action command,
        bool hideOnFailure = true)
    {
        try
        {
            command();
        }
        catch (Exception exception)
        {
            this.HandleBuildFailure(
                operation,
                exception,
                hideOnFailure);
        }
    }

    private void HandleBuildFailure(
        string operation,
        Exception exception,
        bool hideOnFailure = true)
    {
        Debug.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"[Builds] Could not {operation}: {exception}"));

        if (this.IsDisposed || this.Disposing)
        {
            return;
        }

        if (hideOnFailure)
        {
            this.Hide();
        }

        try
        {
            MessageBox.Show(
                this.Owner ?? this,
                string.Create(
                    CultureInfo.CurrentCulture,
                    $"Builds could not {operation}. The game client is still running.\r\n\r\n{exception.Message}"),
                "Builds",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        catch (Exception messageException)
        {
            Debug.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"[Builds] Could not show the error dialog: {messageException}"));
        }
    }

    private static Rectangle ClampToWorkingArea(Rectangle bounds)
    {
        var workingArea = Screen.FromRectangle(bounds).WorkingArea;
        var width = Math.Min(bounds.Width, workingArea.Width);
        var height = Math.Min(bounds.Height, workingArea.Height);
        var x = Math.Clamp(
            bounds.X,
            workingArea.Left,
            Math.Max(workingArea.Left, workingArea.Right - width));
        var y = Math.Clamp(
            bounds.Y,
            workingArea.Top,
            Math.Max(workingArea.Top, workingArea.Bottom - height));
        return new Rectangle(x, y, width, height);
    }

    private static void ConfigureBuildPicker(ComboBox picker)
    {
        picker.DrawMode = DrawMode.OwnerDrawFixed;
        picker.ItemHeight = 24;
        picker.TabStop = false;
        picker.DrawItem += (_, e) =>
        {
            var comboEdit = (e.State & DrawItemState.ComboBoxEdit) != 0;
            var selected = (e.State & DrawItemState.Selected) != 0;
            using var background = new SolidBrush(
                selected && !comboEdit
                    ? MainWindowTheme.ButtonHover
                    : MainWindowTheme.ElevatedPanel);
            e.Graphics.FillRectangle(background, e.Bounds);

            var text = e.Index >= 0
                ? picker.GetItemText(picker.Items[e.Index])
                : picker.Text;
            TextRenderer.DrawText(
                e.Graphics,
                text,
                picker.Font,
                Rectangle.Inflate(e.Bounds, -6, 0),
                MainWindowTheme.Text,
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPadding);
        };
    }

    private static Label CreateFieldLabel(string text) =>
        new()
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Text = text,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = MainWindowTheme.CreateBodyFont(8.5f),
            ForeColor = MainWindowTheme.MutedText,
            BackColor = Color.Transparent,
        };

    private Image? GetItemIcon(
        int itemTemplateId,
        Size size)
    {
        var key = new ItemIconKey(
            itemTemplateId,
            Math.Max(1, size.Width),
            Math.Max(1, size.Height));
        if (this.itemIcons.TryGetValue(key, out var cached))
        {
            return cached;
        }

        Image? icon = null;
        try
        {
            icon = this.resolveItemIcon(
                itemTemplateId,
                new Size(key.Width, key.Height));
        }
        catch (Exception exception)
        {
            Debug.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"[Builds] Could not load item icon {itemTemplateId}: {exception}"));
        }

        this.itemIcons[key] = icon;
        return icon;
    }

    private void RegisterItemToolTip(
        int itemTemplateId,
        string itemName,
        Image? icon,
        params Control?[] controls)
    {
        ClientRuntimeItemTemplateObservation? template = null;
        if (ClientItemTemplateCatalog.TryGetRuntimeObservation(
                itemTemplateId,
                out var observed))
        {
            template = observed;
        }

        var content = PilotArchiveItemToolTipBuilder.BuildCatalogItem(
            itemTemplateId,
            itemName,
            template,
            icon);
        foreach (var control in controls.Where(value => value != null))
        {
            this.itemToolTip.SetToolTip(
                control!,
                content,
                SystemInformation.MouseHoverTime);
        }
    }

    private void RegisterDarkScrollbarTheme(Control control)
    {
        if (!this.darkThemeControls.Add(control))
        {
            return;
        }

        control.HandleCreated +=
            this.DarkScrollbarControl_OnHandleCreated;
        control.ControlAdded +=
            this.DarkScrollbarControl_OnControlAdded;
        control.Disposed +=
            this.DarkScrollbarControl_OnDisposed;

        foreach (Control child in control.Controls)
        {
            this.RegisterDarkScrollbarTheme(child);
        }

        this.ApplyDarkScrollbarTheme(control);
    }

    private void DarkScrollbarControl_OnHandleCreated(
        object? sender,
        EventArgs e)
    {
        if (sender is Control control)
        {
            this.ApplyDarkScrollbarTheme(control);
        }
    }

    private void DarkScrollbarControl_OnControlAdded(
        object? sender,
        ControlEventArgs e)
    {
        this.RegisterDarkScrollbarTheme(e.Control);
    }

    private void DarkScrollbarControl_OnDisposed(
        object? sender,
        EventArgs e)
    {
        if (sender is not Control control)
        {
            return;
        }

        control.HandleCreated -=
            this.DarkScrollbarControl_OnHandleCreated;
        control.ControlAdded -=
            this.DarkScrollbarControl_OnControlAdded;
        control.Disposed -=
            this.DarkScrollbarControl_OnDisposed;
        this.darkThemeControls.Remove(control);
    }

    private void ApplyDarkScrollbarTheme(Control control)
    {
        if (control.IsHandleCreated &&
            IsScrollbarThemeTarget(control))
        {
            _ = NativeMethods.TryApplyDarkControlTheme(control.Handle);
            control.Invalidate();
        }

        foreach (Control child in control.Controls)
        {
            this.ApplyDarkScrollbarTheme(child);
        }
    }

    private static bool IsScrollbarThemeTarget(Control control) =>
        control is ListView ||
        control is ScrollBar ||
        control is TextBoxBase { Multiline: true } ||
        control is ScrollableControl { AutoScroll: true };

    private static string FormatEquipmentKindPlural(
        SkillBuildEquipmentKind kind) =>
        kind switch
        {
            SkillBuildEquipmentKind.Weapon => "weapons",
            SkillBuildEquipmentKind.Device => "devices",
            SkillBuildEquipmentKind.Shield => "shields",
            SkillBuildEquipmentKind.Reactor => "reactors",
            SkillBuildEquipmentKind.Engine => "engines",
            _ => "equipment positions",
        };

    private static SkillBuildEquipmentAlternative ToAlternative(
        SkillBuildEquipmentChoice choice) =>
        new()
        {
            ItemTemplateId = choice.ItemTemplateId,
            ItemName = choice.Name,
        };

    private static void ResizeFlowChildren(FlowLayoutPanel panel)
    {
        var width = Math.Max(
            80,
            panel.ClientSize.Width -
            (panel.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0));
        foreach (Control child in panel.Controls)
        {
            child.Width = width;
        }
    }

    private static Panel? FindScrollableChild(Control? root)
    {
        if (root == null)
        {
            return null;
        }
        if (root is Panel { AutoScroll: true } panel)
        {
            return panel;
        }

        foreach (Control child in root.Controls)
        {
            var result = FindScrollableChild(child);
            if (result != null)
            {
                return result;
            }
        }

        return null;
    }

    private static void RestoreScroll(
        Panel? panel,
        Point previous)
    {
        if (panel == null || previous == Point.Empty)
        {
            return;
        }

        panel.PerformLayout();
        panel.AutoScrollPosition = new Point(
            Math.Abs(previous.X),
            Math.Abs(previous.Y));
    }

    private sealed record BoundLevelCard(
        Control Card,
        Label Value,
        bool FormatPostCap);

    private sealed record LevelStripBinding(
        BoundLevelCard Overall,
        BoundLevelCard Hull,
        BoundLevelCard Combat,
        BoundLevelCard Explore,
        BoundLevelCard Trade,
        BoundLevelCard SkillPoints);

    private sealed class SkillRowBinding(
        Control row,
        Label name,
        SkillPipsControlBase pips,
        bool editable)
    {
        public void Update(
            SkillBuildSkillAnalysis skill,
            bool currentEditable)
        {
            if (editable != currentEditable)
            {
                return;
            }

            name.ForeColor = MainWindowTheme.Text;
            switch (pips)
            {
                case EditableSkillRankPipsControl editablePips:
                    editablePips.UpdateState(
                        skill.EquipmentMinimumRank,
                        skill.TargetRank,
                        skill.EquipmentMinimumRank);
                    break;
                case RankComparisonPipsControl comparisonPips:
                    comparisonPips.UpdateState(
                        skill.CurrentRank.GetValueOrDefault(),
                        skill.TargetRank);
                    break;
            }

            row.Invalidate();
        }
    }

    private sealed record EquipmentPickerSelection(
        SkillBuildEquipmentChoice Choice,
        SkillBuildEquipmentPickerAction Action);

    private readonly record struct ItemIconKey(
        int ItemTemplateId,
        int Width,
        int Height);

    private sealed record BuildChoice(
        SkillBuildDocument Build,
        string Text);

}
