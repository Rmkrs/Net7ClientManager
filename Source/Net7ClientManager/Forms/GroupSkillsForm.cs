// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Net7ClientManager.Core;
using Net7ClientManager.Models;
using Net7ClientManager.Observations.Models;
using Net7ClientManager.Services;
using Net7ClientManager.Win32;

public sealed class GroupSkillsForm : ThemedForm
{
    private const int WmMouseActivate = 0x0021;
    private const int MaNoActivate = 3;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;

    private static readonly Color HullColor = Color.FromArgb(235, 68, 72);
    private static readonly Color ShieldColor = Color.FromArgb(86, 162, 255);
    private static readonly Color ReactorColor = Color.FromArgb(91, 204, 70);

    private const int TargetRowHeight = 76;
    private const int PilotRowHeight = 76;
    private const int NameWidth = 160;
    private const int VitalsWidth = 174;
    private const int ActionCellWidth = 46;
    private const int ActionIconSize = 30;
    private const int ActionToolTipIconSize = 32;
    private const int CloseButtonSize = 18;
    private const int CloseButtonMargin = 8;
    private const int LootButtonWidth = 86;
    private const int LootButtonHeight = 30;
    private const int MaximumActionChipsPerLine = 6;

    private const string ItemRecipientMarker =
        "\uE000item-recipient\uE001";

    private static readonly Regex ActivatedAnnotationRegex = new(
        @"\s*\(activated\)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex ActivationLeadRegex = new(
        @"^\s*(?:when activated|upon activation|on activation)\s*[:,\-]?\s*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex ActivationTailRegex = new(
        @"\s*(?:when activated|upon activation|on activation)\s*(?=[.!?]?$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex TargetPrepositionRegex = new(
        @"\b(?:to|on)\s+(?:the\s+)?target\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex TargetPossessiveRegex = new(
        @"\b(?:the\s+)?target's\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex TargetedFriendlyRegex = new(
        @"\b(?:the\s+|a\s+)?targeted\s+(?:friendly|group member)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex BareTargetRegex = new(
        @"\btarget\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex YourRegex = new(
        @"\byour\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex YouRegex = new(
        @"\byou\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex WhitespaceRegex = new(
        @"\s+",
        RegexOptions.CultureInvariant);

    private static readonly Regex PunctuationSpacingRegex = new(
        @"\s+([,.!?;:])",
        RegexOptions.CultureInvariant);

    private readonly ClientManager clientManager;
    private readonly int leaderProcessId;
    private readonly TableLayoutPanel layout = new();
    private readonly BufferedFlowLayoutPanel rowHost = new();
    private readonly OverlayCloseButton closeButton = new();
    private readonly ToolTip toolTip = new();
    private readonly ActionToolTip actionToolTip = new();
    private readonly System.Windows.Forms.Timer refreshTimer = new();
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly Dictionary<string, DynamicTargetControls> dynamicTargetControls = new(StringComparer.Ordinal);
    private readonly Dictionary<int, DynamicPilotControls> dynamicPilotControls = [];
    private readonly List<DynamicActionControl> dynamicActionControls = [];
    private readonly Dictionary<int, GroupSkillsPilotCard> currentCardsByProcessId = [];
    private readonly Dictionary<int, int> tooltipDelayByProcessId = [];

    private IReadOnlyList<GroupSkillsTargetRow> currentTargets = [];
    private GroupSkillsTargetRow? currentSelectedTarget;
    private NonFocusableButton? lootButton;
    private string? lastRowsSignature;
    private string selectedTargetKey = LeaderTargetKey;
    private bool executingAction;

    public GroupSkillsForm(
        ClientManager clientManager,
        int leaderProcessId)
    {
        this.clientManager = clientManager;
        this.leaderProcessId = leaderProcessId;

        this.Text = "Action HUD";
        this.StartPosition = FormStartPosition.Manual;
        this.MinimumSize = new Size(460, 80);
        this.Size = new Size(628, 208);
        this.BackColor = MainWindowTheme.Background;
        this.ForeColor = MainWindowTheme.Text;
        this.Font = MainWindowTheme.CreateBodyFont(8.0f);
        this.DoubleBuffered = true;
        this.ShowInTaskbar = false;
        this.ShowIcon = false;
        this.ConfigureWindowChrome(
            allowResize: false,
            showMinimizeButton: false,
            showMaximizeButton: false,
            showIcon: false);
        this.ConfigureCompactOverlayChrome();
        this.PositionNearCursor();

        this.layout.Dock = DockStyle.Fill;
        this.layout.RowCount = 1;
        this.layout.ColumnCount = 1;
        this.layout.Padding = new Padding(8);
        this.layout.BackColor = this.BackColor;
        this.layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        this.rowHost.Dock = DockStyle.Fill;
        this.rowHost.FlowDirection = FlowDirection.TopDown;
        this.rowHost.WrapContents = false;
        this.rowHost.AutoScroll = false;
        this.rowHost.BackColor = this.BackColor;
        this.rowHost.Margin = Padding.Empty;
        this.rowHost.Padding = Padding.Empty;

        this.layout.Controls.Add(this.rowHost, 0, 0);
        this.Controls.Add(this.layout);
        this.WireDragSurface(this.layout);
        this.WireDragSurface(this.rowHost);
        this.ConfigureCloseButton();

        this.toolTip.ShowAlways = true;
        this.toolTip.InitialDelay = 350;
        this.toolTip.ReshowDelay = 100;
        this.toolTip.AutoPopDelay = 20000;

        this.refreshTimer.Interval = 125;
        this.refreshTimer.Tick += this.RefreshTimer_OnTick;

        this.BuildRows(force: true);
    }

    protected override bool ShowWithoutActivation =>
        true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= WsExToolWindow | WsExNoActivate;
            return parameters;
        }
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmMouseActivate)
        {
            message.Result = new IntPtr(MaNoActivate);
            return;
        }

        base.WndProc(ref message);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        this.refreshTimer.Start();
        this.BuildRows(force: false);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        this.refreshTimer.Stop();
        this.refreshTimer.Tick -= this.RefreshTimer_OnTick;
        this.lifetimeCancellation.Cancel();
        this.lifetimeCancellation.Dispose();
        this.toolTip.RemoveAll();
        this.actionToolTip.RemoveAll();
        this.actionToolTip.Dispose();
        base.OnFormClosed(e);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        this.PositionCloseButton();
    }

    private const string LeaderTargetKey = "leader-target";

    private void RefreshTimer_OnTick(object? sender, EventArgs e)
    {
        if (!this.clientManager.IsActionHudAvailable(this.leaderProcessId))
        {
            this.Close();
            return;
        }

        if (!this.executingAction)
        {
            this.clientManager.RequestGroupSkillsObservation(this.leaderProcessId);
            this.BuildRows(force: false);
        }
    }

    private void BuildRows(bool force)
    {
        var cards = this.clientManager.BuildGroupSkillsCards(this.leaderProcessId);
        var targets = GetSharedTargets(cards);
        var selectedTarget = this.ResolveSelectedTarget(targets);
        var rowsSignature = CreateRowsLayoutSignature(cards);

        this.currentTargets = targets;
        this.currentSelectedTarget = selectedTarget;
        this.currentCardsByProcessId.Clear();
        this.tooltipDelayByProcessId.Clear();

        foreach (var card in cards)
        {
            this.currentCardsByProcessId[card.ProcessId] = card;
            this.tooltipDelayByProcessId[card.ProcessId] =
                card.TooltipDelayMilliseconds;
        }

        if (!force &&
            string.Equals(
                this.lastRowsSignature,
                rowsSignature,
                StringComparison.Ordinal))
        {
            this.UpdateDynamicTargetControls(targets);
            this.UpdatePilotActionLayouts(cards);
            this.UpdateDynamicActionControls();
            this.UpdateLootButtonAvailability();
            return;
        }

        this.lastRowsSignature = rowsSignature;

        this.rowHost.SuspendLayout();
        this.layout.SuspendLayout();
        this.DisposeRebuiltRowControls();
        this.dynamicTargetControls.Clear();
        this.dynamicPilotControls.Clear();
        this.dynamicActionControls.Clear();

        try
        {
            if (cards.Count == 0)
            {
                this.rowHost.Controls.Add(
                    new Label
                    {
                        AutoSize = true,
                        ForeColor = MainWindowTheme.MutedText,
                        BackColor = this.BackColor,
                        Text = "No controlled in-game pilots were found.",
                        Padding = new Padding(4),
                    });
                return;
            }

            var leaderTarget = targets.FirstOrDefault(target => target.IsLeaderTarget);

            if (leaderTarget != null)
            {
                this.rowHost.Controls.Add(this.CreateTargetRow(leaderTarget));
            }

            foreach (var card in cards)
            {
                this.rowHost.Controls.Add(this.CreatePilotRow(card));
            }

            this.ResizeToContent(cards.Count + (leaderTarget != null ? 1 : 0));
            this.UpdateDynamicTargetControls(targets);
            this.UpdateDynamicActionControls();
            this.UpdateLootButtonAvailability();
        }
        finally
        {
            this.rowHost.ResumeLayout(performLayout: true);
            this.layout.ResumeLayout(performLayout: true);
        }
    }

    private void DisposeRebuiltRowControls()
    {
        this.toolTip.RemoveAll();
        this.actionToolTip.RemoveAll();
        this.lootButton = null;

        var controls = this.rowHost.Controls
            .Cast<Control>()
            .ToArray();

        this.rowHost.Controls.Clear();

        foreach (var control in controls)
        {
            control.Dispose();
        }
    }

    private void ConfigureCloseButton()
    {
        this.closeButton.Size = new Size(CloseButtonSize, CloseButtonSize);
        this.closeButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        this.closeButton.Margin = Padding.Empty;
        this.closeButton.TabStop = false;
        this.closeButton.Cursor = Cursors.Hand;
        this.closeButton.AccessibleName = "Close Action HUD";
        this.closeButton.AccessibleRole = AccessibleRole.PushButton;
        this.closeButton.Click += (_, _) => this.Close();
        this.Controls.Add(this.closeButton);
        this.closeButton.BringToFront();
        this.PositionCloseButton();
    }

    private void PositionCloseButton()
    {
        this.closeButton.Location = new Point(
            Math.Max(
                CloseButtonMargin,
                this.ClientSize.Width - CloseButtonSize - CloseButtonMargin),
            CloseButtonMargin);
        this.closeButton.BringToFront();
    }


    private void ResizeToContent(int rowCount)
    {
        var visibleRows = Math.Max(1, rowCount);
        var desiredHeight = this.layout.Padding.Vertical +
                            visibleRows * (PilotRowHeight + 2) +
                            this.Padding.Vertical;
        var workingArea = Screen.FromRectangle(this.Bounds).WorkingArea;
        var maximumHeight = Math.Max(160, workingArea.Height - 24);
        var height = Math.Clamp(desiredHeight, 80, maximumHeight);
        this.rowHost.AutoScroll = desiredHeight > maximumHeight;

        if (this.Height != height)
        {
            this.Height = height;
        }
    }

    private void WireDragSurface(Control control)
    {
        control.MouseDown += this.DragSurface_OnMouseDown;
    }

    private void DragSurface_OnMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        _ = NativeMethods.ReleaseCapture();
        NativeMethods.SendMoveWindowMessage(this.Handle);
    }

    private Control CreateTargetRow(
        GroupSkillsTargetRow target)
    {
        var selected = this.IsSelectedTarget(target);
        var row = this.CreateRowShell(TargetRowHeight);
        var targetButton = this.CreateTargetButton(
            target,
            this.GetTargetDisplayName(target));
        var vitals = this.CreateVitalBars(target, selected);

        row.Controls.Add(targetButton, 0, 0);
        row.Controls.Add(vitals, 1, 0);
        row.Controls.Add(this.CreateTargetActionHost(), 2, 0);
        this.RegisterDynamicTargetControls(target, targetButton, vitals);
        return row;
    }

    private Control CreateTargetActionHost()
    {
        var host = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = MainWindowTheme.Panel,
        };

        var button = new NonFocusableButton
        {
            Text = "Loot",
            Size = new Size(LootButtonWidth, LootButtonHeight),
            Location = new Point(9, (TargetRowHeight - LootButtonHeight) / 2),
            Anchor = AnchorStyles.Left,
            Visible = false,
            AccessibleName = "Open loot window",
        };

        MainWindowTheme.StyleButton(button, primary: true);
        button.Font = MainWindowTheme.CreateHeadingFont(8.5f);
        button.Click += (_, _) =>
            this.clientManager.OpenFleetLootFromActionHud(
                this.leaderProcessId,
                this);

        this.toolTip.SetToolTip(
            button,
            "Open the loot window for the current game target");
        host.Controls.Add(button);
        this.WireDragSurface(host);
        this.lootButton = button;
        return host;
    }

    private void UpdateLootButtonAvailability()
    {
        if (this.lootButton == null)
        {
            return;
        }

        this.lootButton.Visible =
            this.clientManager.CanOpenFleetLootFromActionHud(
                this.leaderProcessId);
    }

    private Control CreatePilotRow(
        GroupSkillsPilotCard card)
    {
        var ownTarget = GetOwnTarget(card);
        var row = this.CreateRowShell(PilotRowHeight);

        if (ownTarget != null)
        {
            var selected = this.IsSelectedTarget(ownTarget);
            var targetButton = this.CreateTargetButton(
                ownTarget,
                GetPilotDisplayName(card));
            var vitals = this.CreateVitalBars(ownTarget, selected);

            row.Controls.Add(targetButton, 0, 0);
            row.Controls.Add(vitals, 1, 0);
            this.RegisterDynamicTargetControls(ownTarget, targetButton, vitals);
        }
        else
        {
            row.Controls.Add(CreateNameChip(GetPilotDisplayName(card)), 0, 0);
            row.Controls.Add(this.CreateVitalBars(null, selected: false), 1, 0);
        }

        var actionHost = this.CreateActionChips(card);
        row.Controls.Add(actionHost, 2, 0);
        this.dynamicPilotControls[card.ProcessId] = new DynamicPilotControls(
            row,
            actionHost,
            CreateActionLayoutSignature(card));
        return row;
    }

    private TableLayoutPanel CreateRowShell(int height)
    {
        var row = new TableLayoutPanel
        {
            Width = Math.Max(400, this.ClientSize.Width - this.layout.Padding.Horizontal),
            Height = height,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 2),
            Padding = new Padding(1),
            BackColor = MainWindowTheme.Panel,
        };

        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, NameWidth));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, VitalsWidth));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        this.WireDragSurface(row);
        return row;
    }

    private TargetNameControl CreateTargetButton(
        GroupSkillsTargetRow target,
        string displayName)
    {
        var selected = string.Equals(
            this.selectedTargetKey,
            GetTargetKey(target),
            StringComparison.Ordinal);
        var control = new TargetNameControl
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 4, 0),
            Cursor = Cursors.Hand,
        };

        control.UpdateState(
            displayName,
            target.Detail,
            target.OverallLevel,
            target.CombatLevel,
            target.ExploreLevel,
            target.TradeLevel,
            selected,
            interactive: true);
        this.toolTip.SetToolTip(
            control,
            this.GetTargetSelectionTooltip(target, selected));
        control.Click += (_, _) => this.SelectTarget(target);
        return control;
    }

    private void SelectTarget(GroupSkillsTargetRow target)
    {
        this.selectedTargetKey = GetTargetKey(target);
        this.currentSelectedTarget = this.ResolveSelectedTarget(this.currentTargets);
        this.UpdateDynamicTargetControls(this.currentTargets);
        this.UpdateDynamicActionControls();
        this.ReactivateToolTips();
    }

    private string GetTargetSelectionTooltip(
        GroupSkillsTargetRow target,
        bool selected)
    {
        if (target.IsLeaderTarget && !target.HasTarget)
        {
            return selected
                ? "Current target selected (currently empty)"
                : "Select current target (currently empty)";
        }

        return selected
            ? string.Concat("Selected target: ", this.GetTargetDisplayName(target))
            : string.Concat("Set selected target to ", this.GetTargetDisplayName(target));
    }

    private static TargetNameControl CreateNameChip(string text)
    {
        var control = new TargetNameControl
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 4, 0),
            Cursor = Cursors.Default,
        };

        control.UpdateState(
            text,
            detail: "",
            overallLevel: null,
            combatLevel: null,
            exploreLevel: null,
            tradeLevel: null,
            selected: false,
            interactive: false);
        return control;
    }

    private VitalBarsControl CreateVitalBars(
        GroupSkillsTargetRow? target,
        bool selected)
    {
        var control = new VitalBarsControl
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 2, 4, 2),
        };

        control.UpdateState(
            target?.HasTarget == true,
            target?.ShieldCurrent,
            target?.ShieldPercent,
            target?.HullCurrent,
            target?.HullPercent,
            target?.ReactorCurrent,
            target?.ReactorPercent,
            selected);

        this.SetToolTipIfChanged(
            control,
            BuildVitalsTooltip(target));
        return control;
    }

    private void RegisterDynamicTargetControls(
        GroupSkillsTargetRow target,
        TargetNameControl targetButton,
        VitalBarsControl vitals)
    {
        this.dynamicTargetControls[GetTargetKey(target)] = new DynamicTargetControls(
            targetButton,
            vitals);
    }

    private void UpdateDynamicTargetControls(
        IReadOnlyList<GroupSkillsTargetRow> targets)
    {
        this.rowHost.SuspendLayout();

        try
        {
            foreach (var target in targets)
            {
                if (!this.dynamicTargetControls.TryGetValue(
                        GetTargetKey(target),
                        out var controls))
                {
                    continue;
                }

                var selected = this.IsSelectedTarget(target);
                controls.NameButton.UpdateState(
                    this.GetTargetDisplayName(target),
                    target.Detail,
                    target.OverallLevel,
                    target.CombatLevel,
                    target.ExploreLevel,
                    target.TradeLevel,
                    selected,
                    interactive: true);

                this.SetToolTipIfChanged(
                    controls.NameButton,
                    this.GetTargetSelectionTooltip(target, selected));

                controls.Vitals.UpdateState(
                    target.HasTarget,
                    target.ShieldCurrent,
                    target.ShieldPercent,
                    target.HullCurrent,
                    target.HullPercent,
                    target.ReactorCurrent,
                    target.ReactorPercent,
                    selected);
                this.SetToolTipIfChanged(
                    controls.Vitals,
                    BuildVitalsTooltip(target));
            }
        }
        finally
        {
            this.rowHost.ResumeLayout(performLayout: false);
        }
    }

    private static string BuildReactorTooltip(
        GroupSkillsTargetRow? target)
    {
        if (target == null ||
            !target.ReactorCurrent.HasValue ||
            !target.ReactorMaximum.HasValue ||
            !target.ReactorPercent.HasValue)
        {
            return "";
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"Reactor: {target.ReactorCurrent.Value}/{target.ReactorMaximum.Value} ({target.ReactorPercent.Value}%)");
    }

    private static string BuildVitalsTooltip(
        GroupSkillsTargetRow? target)
    {
        if (target?.HasTarget != true)
        {
            return "No target";
        }

        var shield = BuildShieldTooltip(target);
        var hull = BuildHullTooltip(target);
        var reactor = BuildReactorTooltip(target);

        return string.IsNullOrWhiteSpace(reactor)
            ? string.Concat(shield, Environment.NewLine, hull)
            : string.Concat(
                shield,
                Environment.NewLine,
                hull,
                Environment.NewLine,
                reactor);
    }

    private static string BuildShieldTooltip(
        GroupSkillsTargetRow target)
    {
        if (target.ShieldMaximum is <= 0 ||
            (!target.ShieldMaximum.HasValue &&
             !target.ShieldPercent.HasValue))
        {
            return "No shields";
        }

        if (target.ShieldCurrent.HasValue &&
            target.ShieldMaximum.HasValue)
        {
            var percentSuffix = target.ShieldPercent.HasValue
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $" ({Math.Clamp(target.ShieldPercent.Value, 0, 100)}%)")
                : "";
            return string.Create(
                CultureInfo.InvariantCulture,
                $"Shield: {target.ShieldCurrent.Value}/{target.ShieldMaximum.Value}{percentSuffix}");
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"Shield: {Math.Clamp(target.ShieldPercent ?? 0, 0, 100)}%");
    }

    private static string BuildHullTooltip(
        GroupSkillsTargetRow target)
    {
        if (target.HullCurrent.HasValue &&
            target.HullMaximum.HasValue)
        {
            var percentSuffix = target.HullPercent.HasValue
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $" ({Math.Clamp(target.HullPercent.Value, 0, 100)}%)")
                : "";
            return string.Create(
                CultureInfo.InvariantCulture,
                $"Hull Strength: {target.HullCurrent.Value}/{target.HullMaximum.Value}{percentSuffix}");
        }

        if (target.Kind == ClientTargetKind.Corpse)
        {
            return "Hull Strength: 0/0";
        }

        if (target.HullPercent.HasValue)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"Hull Strength: {Math.Clamp(target.HullPercent.Value, 0, 100)}%");
        }

        return "No hull strength";
    }

    private TableLayoutPanel CreateActionChips(
        GroupSkillsPilotCard card)
    {
        var host = CreateActionHost();
        this.PopulateActionChips(host, card);
        return host;
    }

    private static TableLayoutPanel CreateActionHost()
    {
        var host = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = MaximumActionChipsPerLine,
            RowCount = 2,
            BackColor = MainWindowTheme.Panel,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            GrowStyle = TableLayoutPanelGrowStyle.FixedSize,
        };

        for (var columnIndex = 0; columnIndex < MaximumActionChipsPerLine; columnIndex++)
        {
            host.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ActionCellWidth));
        }

        host.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        host.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        return host;
    }

    private void PopulateActionChips(
        TableLayoutPanel host,
        GroupSkillsPilotCard card)
    {
        var fireAllImage = this.clientManager.GetGameItemIcon(
            card.ProcessId,
            card.FireAllIconItemTemplateId,
            new Size(ActionIconSize, ActionIconSize));
        var fireAllToolTipImage = this.clientManager.GetGameItemIcon(
            card.ProcessId,
            card.FireAllIconItemTemplateId,
            new Size(ActionToolTipIconSize, ActionToolTipIconSize));
        var fireAllButton = this.CreateChip(
            fireAllImage == null
                ? "F"
                : "",
            fireAllImage,
            async () =>
            {
                var selectedTarget = this.currentSelectedTarget;

                if (selectedTarget != null)
                {
                    await this.InvokeFireAllAsync(card, selectedTarget).ConfigureAwait(true);
                }
            });

        this.RegisterDynamicActionControl(
            card.ProcessId,
            fireAllButton,
            "Fire All",
            action: null,
            isFireAll: true,
            fireAllToolTipImage);

        var chips = new List<IconActionButton>
        {
            fireAllButton,
        };

        foreach (var category in GetActionCategoryOrder())
        {
            var actions = card.Actions
                .Where(ShouldShowActionChip)
                .Where(action => action.Category == category)
                .OrderBy(action => action.Group)
                .ThenBy(action => action.Bar)
                .ThenBy(action => action.Button)
                .ThenBy(action => action.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var action in actions)
            {
                var image = this.clientManager.GetGroupSkillsActionIcon(
                    card.ProcessId,
                    action,
                    new Size(ActionIconSize, ActionIconSize));
                var toolTipImage = this.clientManager.GetGroupSkillsActionIcon(
                    card.ProcessId,
                    action,
                    new Size(ActionToolTipIconSize, ActionToolTipIconSize));
                var actionButton = this.CreateChip(
                    image == null
                        ? Abbreviate(action.Name)
                        : "",
                    image,
                    async () =>
                    {
                        var selectedTarget = this.currentSelectedTarget;

                        if (selectedTarget != null)
                        {
                            await this.InvokeShortcutAsync(card, action, selectedTarget).ConfigureAwait(true);
                        }
                    });

                this.RegisterDynamicActionControl(
                    card.ProcessId,
                    actionButton,
                    action.Name,
                    action,
                    isFireAll: false,
                    toolTipImage);
                chips.Add(actionButton);
            }
        }

        for (var chipIndex = 0; chipIndex < Math.Min(chips.Count, MaximumActionChipsPerLine * 2); chipIndex++)
        {
            var rowIndex = chipIndex / MaximumActionChipsPerLine;
            var chip = chips[chipIndex];
            chip.Margin = rowIndex == 0
                ? new Padding(0, 0, 2, 2)
                : new Padding(0, 2, 2, 0);

            host.Controls.Add(
                chip,
                chipIndex % MaximumActionChipsPerLine,
                rowIndex);
        }

    }

    private IconActionButton CreateChip(
        string label,
        Image? image,
        Func<Task> click)
    {
        var button = new IconActionButton
        {
            Dock = DockStyle.Fill,
            Text = label,
            Image = image,
            ImageAlign = ContentAlignment.MiddleCenter,
            TextImageRelation = TextImageRelation.Overlay,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            TabStop = false,
            FlatStyle = FlatStyle.Flat,
            UseVisualStyleBackColor = false,
            Cursor = Cursors.Hand,
            BackColor = MainWindowTheme.Panel,
            ForeColor = MainWindowTheme.Text,
        };

        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.BorderColor = MainWindowTheme.Panel;
        button.FlatAppearance.MouseOverBackColor = MainWindowTheme.Panel;
        button.FlatAppearance.MouseDownBackColor = MainWindowTheme.ElevatedPanel;
        button.Font = MainWindowTheme.CreateBodyFont(7.0f);
        button.Click += async (_, _) =>
        {
            await click().ConfigureAwait(true);
        };

        return button;
    }

    private void RegisterDynamicActionControl(
        int processId,
        IconActionButton button,
        string actionName,
        GameShortcutPaletteEntry? action,
        bool isFireAll,
        Image? toolTipIcon)
    {
        button.AccessibleName = actionName;
        this.dynamicActionControls.Add(
            new DynamicActionControl(
                processId,
                button,
                actionName,
                action,
                isFireAll,
                toolTipIcon));
    }

    private void UpdateDynamicActionControls()
    {
        foreach (var actionControl in this.dynamicActionControls)
        {
            var tooltipDelayMilliseconds =
                this.tooltipDelayByProcessId.TryGetValue(
                    actionControl.ProcessId,
                    out var resolvedTooltipDelay)
                    ? resolvedTooltipDelay
                    : ClientTooltipDelayObservation
                        .DefaultDelayMilliseconds;

            this.actionToolTip.SetToolTip(
                actionControl.Button,
                this.BuildActionToolTipContent(
                    actionControl.ProcessId,
                    actionControl.ActionName,
                    actionControl.Action,
                    actionControl.IsFireAll,
                    this.currentSelectedTarget,
                    actionControl.ToolTipIcon),
                tooltipDelayMilliseconds);
        }
    }

    private void SetToolTipIfChanged(
        Control control,
        string text)
    {
        if (!string.Equals(
                this.toolTip.GetToolTip(control),
                text,
                StringComparison.Ordinal))
        {
            this.toolTip.SetToolTip(control, text);
        }
    }

    private ActionToolTipContent BuildActionToolTipContent(
        int processId,
        string actionName,
        GameShortcutPaletteEntry? action,
        bool isFireAll,
        GroupSkillsTargetRow? selectedTarget,
        Image? icon)
    {
        if (isFireAll)
        {
            return this.BuildFireAllActionToolTipContent(
                processId,
                selectedTarget,
                icon);
        }

        if (action?.SkillDetails is { } details)
        {
            return this.BuildSkillActionToolTipContent(
                processId,
                actionName,
                details,
                selectedTarget,
                icon);
        }

        if (action?.ItemDetails is { } itemDetails)
        {
            return this.BuildItemActionToolTipContent(
                processId,
                itemDetails,
                selectedTarget,
                icon);
        }

        var typeName = action?.Kind switch
        {
            GameShortcutKind.Equipment => "Equipment",
            GameShortcutKind.Cargo => "Item",
            _ => "Action",
        };
        var paragraphs = new List<ActionToolTipParagraph>
        {
            new(
                [
                    new(string.Concat(typeName, ": ")),
                    new(actionName, ActionToolTipTextRole.Accent, Bold: true),
                ],
                ActionToolTipParagraphStyle.Header,
                SpaceAfter: 6),
        };


        return new ActionToolTipContent(paragraphs, icon);
    }

    private ActionToolTipContent BuildFireAllActionToolTipContent(
        int processId,
        GroupSkillsTargetRow? selectedTarget,
        Image? icon)
    {
        var paragraphs = new List<ActionToolTipParagraph>
        {
            new(
                [
                    new("Action: "),
                    new(
                        "Fire All",
                        ActionToolTipTextRole.Accent,
                        Bold: true),
                ],
                ActionToolTipParagraphStyle.Header,
                SpaceAfter: 7),
        };

        var targetStatus = BuildFireAllTargetStatusParagraph(
            selectedTarget);

        if (targetStatus != null)
        {
            paragraphs.Add(targetStatus);
        }

        if (!this.currentCardsByProcessId.TryGetValue(
                processId,
                out var card) ||
            card.FireAllWeapons.Count == 0)
        {
            paragraphs.Add(
                new ActionToolTipParagraph(
                    [
                        new(
                            "No equipped weapons.",
                            ActionToolTipTextRole.Muted),
                    ],
                    SpaceAfter: 2));
            return new ActionToolTipContent(paragraphs, icon);
        }

        var hasHostileTarget = IsFireAllHostileTarget(
            selectedTarget);
        var targetDistance =
            hasHostileTarget &&
            selectedTarget != null
                ? this.clientManager.ResolveGroupSkillsTargetDistance(
                    processId,
                    selectedTarget)
                : null;

        for (var index = 0;
             index < card.FireAllWeapons.Count;
             index++)
        {
            var weapon = card.FireAllWeapons[index];
            paragraphs.Add(
                BuildFireAllWeaponParagraph(
                    weapon,
                    index + 1,
                    index == 0));

            var operationalParagraph =
                BuildFireAllWeaponOperationalParagraph(
                    weapon,
                    targetDistance,
                    hasHostileTarget);

            if (operationalParagraph != null)
            {
                paragraphs.Add(operationalParagraph);
            }
        }

        return new ActionToolTipContent(paragraphs, icon);
    }

    private static ActionToolTipParagraph? BuildFireAllTargetStatusParagraph(
        GroupSkillsTargetRow? selectedTarget)
    {
        if (!IsSelectedTargetPresent(selectedTarget))
        {
            return CreateErrorStatus(
                "No target to ",
                "Fire All",
                ".");
        }

        if (selectedTarget == null)
        {
            return null;
        }

        if (selectedTarget.ProcessId.HasValue ||
            selectedTarget.Relation is
                ClientTargetRelation.Self or
                ClientTargetRelation.GroupMember or
                ClientTargetRelation.Friendly)
        {
            return CreateErrorStatus(
                "Cannot ",
                "Fire All",
                " friendly target.");
        }

        if (!IsFireAllHostileTarget(selectedTarget))
        {
            return CreateErrorStatus(
                "Cannot ",
                "Fire All",
                " non-hostile target.");
        }

        return null;
    }

    private static bool IsFireAllHostileTarget(
        GroupSkillsTargetRow? target)
    {
        return target != null &&
               IsSelectedTargetPresent(target) &&
               target.Relation == ClientTargetRelation.Enemy &&
               !IsContextOnlyTarget(target.Kind);
    }

    private static ActionToolTipParagraph BuildFireAllWeaponParagraph(
        GroupSkillsFireAllWeapon weapon,
        int ordinal,
        bool isFirst)
    {
        var runs = new List<ActionToolTipRun>
        {
            new(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{ordinal}. "),
                ActionToolTipTextRole.Muted),
        };

        AppendLevelledItemName(
            runs,
            weapon.TechLevel,
            weapon.Name);

        var ammoName = weapon.AmmoName;

        if (!string.IsNullOrWhiteSpace(ammoName))
        {
            runs.Add(new(" with "));
            AppendLevelledItemName(
                runs,
                weapon.AmmoTechLevel,
                ammoName);

            if (weapon.AmmoCount is >= 0)
            {
                runs.Add(new(" ×"));
                runs.Add(
                    new(
                        weapon.AmmoCount.Value.ToString(
                            "#,0",
                            CultureInfo.InvariantCulture),
                        ActionToolTipTextRole.Success,
                        Bold: true));
            }
        }
        else if (weapon.RequiresAmmo)
        {
            runs.Add(new(" · "));
            runs.Add(
                new(
                    "No ammo loaded",
                    ActionToolTipTextRole.Danger,
                    Bold: true));
        }

        return new ActionToolTipParagraph(
            runs,
            SpaceBefore: isFirst ? 3 : 7,
            SpaceAfter: 1);
    }

    private static void AppendLevelledItemName(
        ICollection<ActionToolTipRun> runs,
        uint? techLevel,
        string name)
    {
        if (techLevel is > 0)
        {
            runs.Add(
                new(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Level {techLevel.Value} ")));
        }

        runs.Add(
            new(
                name,
                ActionToolTipTextRole.Accent,
                Bold: true));
    }

    private static ActionToolTipParagraph?
        BuildFireAllWeaponOperationalParagraph(
            GroupSkillsFireAllWeapon weapon,
            float? targetDistance,
            bool showTargetDistance)
    {
        var runs = new List<ActionToolTipRun>();

        if (weapon.IsBusy)
        {
            runs.Add(
                new(
                    "Busy",
                    ActionToolTipTextRole.Danger,
                    Bold: true));

            if (weapon.NominalRemainingMilliseconds is > 0)
            {
                runs.Add(new(" · "));
                runs.Add(
                    new(
                        FormatRemainingSeconds(
                            weapon.NominalRemainingMilliseconds.Value),
                        ActionToolTipTextRole.Accent,
                        Bold: true));
            }
            else if (weapon.IsInPostDeadlineBusyTail)
            {
                runs.Add(
                    new(
                        " · waiting for game release",
                        ActionToolTipTextRole.Muted));
            }
        }
        else if (weapon.IsOperationallyReady)
        {
            runs.Add(
                new(
                    "Ready",
                    ActionToolTipTextRole.Success,
                    Bold: true));
        }
        else
        {
            runs.Add(
                new(
                    "Readiness unknown",
                    ActionToolTipTextRole.Muted));
        }

        if (weapon.Range is > 0)
        {
            if (runs.Count > 0)
            {
                runs.Add(new(" · "));
            }

            runs.Add(new("Range "));
            runs.Add(
                new(
                    FormatToolTipWholeNumber(
                        weapon.Range.Value),
                    ActionToolTipTextRole.Accent,
                    Bold: true));

            if (showTargetDistance)
            {
                var currentText = targetDistance.HasValue
                    ? FormatToolTipWholeNumber(
                        targetDistance.Value)
                    : "?";
                var currentRole = !targetDistance.HasValue
                    ? ActionToolTipTextRole.Muted
                    : targetDistance.Value <= weapon.Range.Value
                        ? ActionToolTipTextRole.Success
                        : ActionToolTipTextRole.Danger;

                runs.Add(new(" / "));
                runs.Add(
                    new(
                        currentText,
                        currentRole,
                        Bold: true));
            }

            runs.Add(new(" units"));
        }

        return runs.Count == 0
            ? null
            : new ActionToolTipParagraph(
                runs,
                SpaceAfter: 1);
    }

    private ActionToolTipContent BuildItemActionToolTipContent(
        int processId,
        ItemShortcutDetails details,
        GroupSkillsTargetRow? selectedTarget,
        Image? icon)
    {
        var paragraphs = new List<ActionToolTipParagraph>();
        var typeName = string.IsNullOrWhiteSpace(
                details.TypeDisplayName)
            ? details.Kind == GameShortcutKind.Equipment
                ? "Equipment"
                : "Item"
            : details.TypeDisplayName;
        var headerPrefix = details.TechLevel is > 0
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"Level {details.TechLevel.Value} {typeName}: ")
            : string.Concat(typeName, ": ");

        paragraphs.Add(
            new ActionToolTipParagraph(
                [
                    new(headerPrefix),
                    new(
                        details.Name,
                        ActionToolTipTextRole.Accent,
                        Bold: true),
                ],
                ActionToolTipParagraphStyle.Header,
                SpaceAfter: 7));

        var status = BuildItemStatusParagraph(details);

        if (status != null)
        {
            paragraphs.Add(status);
        }

        var targetContext = this.ResolveItemActionTargetContext(
            processId,
            details,
            selectedTarget);

        if (details.Range is > 0)
        {
            var targetPresent = targetContext.HasRecipient ||
                                IsSelectedTargetPresent(selectedTarget);
            float? targetDistance;

            if (targetContext.HasRecipient)
            {
                targetDistance = targetContext.IsSelf
                    ? 0
                    : targetContext.DistanceTarget != null
                        ? this.clientManager.ResolveGroupSkillsTargetDistance(
                            processId,
                            targetContext.DistanceTarget)
                        : null;
            }
            else
            {
                targetDistance =
                    IsSelectedTargetPresent(selectedTarget) &&
                    selectedTarget != null
                        ? this.clientManager.ResolveGroupSkillsTargetDistance(
                            processId,
                            selectedTarget)
                        : null;
            }

            paragraphs.Add(
                BuildRangeParagraph(
                    details.Range.Value,
                    targetDistance,
                    targetPresent));
        }

        AppendCompactItemActivatedEffects(
            paragraphs,
            details.ActivatedEffects,
            targetContext);

        if (details.Kind == GameShortcutKind.Cargo &&
            details.StackCount is > 1)
        {
            paragraphs.Add(
                new ActionToolTipParagraph(
                    [
                        new("Stack: "),
                        new(
                            details.StackCount.Value.ToString(
                                CultureInfo.InvariantCulture),
                            ActionToolTipTextRole.Accent,
                            Bold: true),
                    ],
                    SpaceBefore: 4));
        }

        return new ActionToolTipContent(paragraphs, icon);
    }

    private ItemActionTargetContext ResolveItemActionTargetContext(
        int processId,
        ItemShortcutDetails details,
        GroupSkillsTargetRow? selectedTarget)
    {
        if (details.ActivatedEffects.Count == 0)
        {
            return default;
        }

        var disposition =
            details.ActivatedTargetSemantics.TargetDisposition;
        var supportsFriendly = disposition.HasFlag(
            SkillTargetDisposition.Friendly);
        var supportsHostile = disposition.HasFlag(
            SkillTargetDisposition.Hostile);
        var supportsSelf = disposition.HasFlag(
            SkillTargetDisposition.Self);

        if (supportsFriendly && !supportsHostile)
        {
            if (TryResolveFriendlyItemRecipient(
                    processId,
                    selectedTarget,
                    out var friendlyRecipient))
            {
                return friendlyRecipient;
            }

            return this.ResolveItemSelfRecipient(processId);
        }

        if (supportsSelf && !supportsFriendly && !supportsHostile)
        {
            return this.ResolveItemSelfRecipient(processId);
        }

        if (supportsHostile && !supportsFriendly &&
            IsSelectedTargetPresent(selectedTarget) &&
            selectedTarget?.Relation == ClientTargetRelation.Enemy)
        {
            return new ItemActionTargetContext(
                HasRecipient: true,
                RecipientName: GetActionTargetName(selectedTarget),
                DistanceTarget: selectedTarget,
                IsSelf: false);
        }

        return default;
    }

    private bool TryResolveFriendlyItemRecipient(
        int processId,
        GroupSkillsTargetRow? selectedTarget,
        out ItemActionTargetContext context)
    {
        context = default;

        if (!IsSelectedTargetPresent(selectedTarget) ||
            selectedTarget == null)
        {
            return false;
        }

        var selectedSelf = selectedTarget.ProcessId == processId ||
                           selectedTarget.Relation ==
                           ClientTargetRelation.Self;
        var selectedFriendly = selectedSelf ||
                               selectedTarget.ProcessId.HasValue ||
                               selectedTarget.Relation is
                                   ClientTargetRelation.GroupMember or
                                   ClientTargetRelation.Friendly;

        if (!selectedFriendly)
        {
            return false;
        }

        var name = GetActionTargetName(selectedTarget);

        if (string.IsNullOrWhiteSpace(name) && selectedSelf)
        {
            name = this.ResolveActingPilotName(processId);
        }

        context = new ItemActionTargetContext(
            HasRecipient: !string.IsNullOrWhiteSpace(name),
            RecipientName: name,
            DistanceTarget: selectedSelf ? null : selectedTarget,
            IsSelf: selectedSelf);
        return context.HasRecipient;
    }

    private ItemActionTargetContext ResolveItemSelfRecipient(
        int processId)
    {
        var name = this.ResolveActingPilotName(processId);

        return new ItemActionTargetContext(
            HasRecipient: !string.IsNullOrWhiteSpace(name),
            name,
            DistanceTarget: null,
            IsSelf: true);
    }

    private string ResolveActingPilotName(int processId)
    {
        return this.currentTargets.FirstOrDefault(target =>
                   target.ProcessId == processId &&
                   !target.IsLeaderTarget)?.Name ??
               "acting pilot";
    }

    private static ActionToolTipParagraph? BuildItemStatusParagraph(
        ItemShortcutDetails details)
    {
        if (details.Kind != GameShortcutKind.Equipment)
        {
            return null;
        }

        if (details.IsNativeAction == false)
        {
            return new ActionToolTipParagraph(
                [
                    new(
                        "Passive equipment effect; no native action is available.",
                        ActionToolTipTextRole.Muted),
                ],
                SpaceAfter: 7);
        }

        if (details.IsNativeAction != true)
        {
            return new ActionToolTipParagraph(
                [
                    new(
                        "Action availability is unknown; the game remains authoritative.",
                        ActionToolTipTextRole.Muted),
                ],
                SpaceAfter: 7);
        }

        if (details.IsBusy)
        {
            var runs = new List<ActionToolTipRun>
            {
                new("Busy", ActionToolTipTextRole.Danger, Bold: true),
            };

            if (details.NominalRemainingMilliseconds is > 0)
            {
                runs.Add(new(": approximately "));
                runs.Add(
                    new(
                        FormatRemainingSeconds(
                            details.NominalRemainingMilliseconds.Value),
                        ActionToolTipTextRole.Accent,
                        Bold: true));
                runs.Add(new(" remaining."));
            }
            else if (details.IsInPostDeadlineBusyTail)
            {
                runs.Add(new(": waiting for the game to release the item."));
            }
            else
            {
                runs.Add(new("."));
            }

            return new ActionToolTipParagraph(
                runs,
                SpaceAfter: 7);
        }

        if (details.IsOperationallyReady)
        {
            return new ActionToolTipParagraph(
                [
                    new("Ready", ActionToolTipTextRole.Success, Bold: true),
                ],
                SpaceAfter: 7);
        }

        return new ActionToolTipParagraph(
            [
                new(
                    "Native action is available; readiness is currently unknown.",
                    ActionToolTipTextRole.Muted),
            ],
            SpaceAfter: 7);
    }

    private static void AppendCompactItemActivatedEffects(
        ICollection<ActionToolTipParagraph> paragraphs,
        IReadOnlyList<ItemShortcutEffectDetails> effects,
        ItemActionTargetContext targetContext)
    {
        for (var index = 0; index < effects.Count; index++)
        {
            var effect = effects[index];
            var sourceText = !string.IsNullOrWhiteSpace(effect.Description)
                ? effect.Description
                : effect.Name;
            var text = BuildCompactItemActivatedEffectText(
                sourceText,
                targetContext.HasRecipient);

            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            paragraphs.Add(
                new ActionToolTipParagraph(
                    BuildItemEffectRuns(
                        text,
                        targetContext.RecipientName),
                    SpaceBefore: index == 0 ? 7 : 2,
                    SpaceAfter: 1));
        }
    }

    private static string BuildCompactItemActivatedEffectText(
        string sourceText,
        bool personalizeTarget)
    {
        if (string.IsNullOrWhiteSpace(sourceText))
        {
            return "";
        }

        var text = ActivatedAnnotationRegex.Replace(
            sourceText,
            "");
        text = ActivationLeadRegex.Replace(text, "");
        text = ActivationTailRegex.Replace(text, "");
        text = UppercaseFirstLetter(
            NormalizeItemEffectText(text));

        if (!personalizeTarget)
        {
            return EnsureSentenceTerminator(text);
        }

        var personalized = false;
        text = ReplaceFirstMatch(
            TargetPossessiveRegex,
            text,
            string.Concat(ItemRecipientMarker, "'s"),
            ref personalized);
        text = ReplaceFirstMatch(
            TargetPrepositionRegex,
            text,
            string.Concat("on ", ItemRecipientMarker),
            ref personalized);
        text = ReplaceFirstMatch(
            TargetedFriendlyRegex,
            text,
            ItemRecipientMarker,
            ref personalized);

        if (!personalized &&
            TryFindTargetPhrase(
                text,
                out var phraseIndex,
                out var phraseLength))
        {
            text = string.Concat(
                text[..phraseIndex],
                ItemRecipientMarker,
                text[(phraseIndex + phraseLength)..]);
            personalized = true;
        }

        text = ReplaceFirstMatch(
            YourRegex,
            text,
            string.Concat(ItemRecipientMarker, "'s"),
            ref personalized);
        text = ReplaceFirstMatch(
            YouRegex,
            text,
            ItemRecipientMarker,
            ref personalized);
        text = ReplaceFirstMatch(
            BareTargetRegex,
            text,
            ItemRecipientMarker,
            ref personalized);

        if (!personalized)
        {
            text = InsertRecipientBeforeDuration(text);
        }
        else
        {
            text = MoveRecipientBeforeDuration(text);
        }

        return EnsureSentenceTerminator(
            NormalizeItemEffectText(text));
    }

    private static string ReplaceFirstMatch(
        Regex regex,
        string text,
        string replacement,
        ref bool replaced)
    {
        if (replaced || !regex.IsMatch(text))
        {
            return text;
        }

        replaced = true;
        return regex.Replace(
            text,
            replacement,
            1);
    }

    private static string InsertRecipientBeforeDuration(string text)
    {
        var durationIndex = text.LastIndexOf(
            " for ",
            StringComparison.OrdinalIgnoreCase);
        var recipientClause = string.Concat(
            " on ",
            ItemRecipientMarker);

        if (durationIndex >= 0)
        {
            return text.Insert(
                durationIndex,
                recipientClause);
        }

        var punctuationIndex = text.Length > 0 &&
                               text[^1] is '.' or '!' or '?'
            ? text.Length - 1
            : text.Length;

        return text.Insert(
            punctuationIndex,
            recipientClause);
    }

    private static string MoveRecipientBeforeDuration(string text)
    {
        var recipientClause = string.Concat(
            " on ",
            ItemRecipientMarker);
        var recipientIndex = text.IndexOf(
            recipientClause,
            StringComparison.Ordinal);

        if (recipientIndex < 0)
        {
            return text;
        }

        var durationIndex = text.LastIndexOf(
            " for ",
            recipientIndex,
            StringComparison.OrdinalIgnoreCase);

        if (durationIndex < 0)
        {
            return text;
        }

        var withoutRecipient = text.Remove(
            recipientIndex,
            recipientClause.Length);

        return withoutRecipient.Insert(
            durationIndex,
            recipientClause);
    }

    private static IReadOnlyList<ActionToolTipRun> BuildItemEffectRuns(
        string text,
        string? recipientName)
    {
        if (string.IsNullOrWhiteSpace(recipientName) ||
            !text.Contains(
                ItemRecipientMarker,
                StringComparison.Ordinal))
        {
            return [new ActionToolTipRun(text)];
        }

        List<ActionToolTipRun> runs = [];
        var cursor = 0;

        while (cursor < text.Length)
        {
            var markerIndex = text.IndexOf(
                ItemRecipientMarker,
                cursor,
                StringComparison.Ordinal);

            if (markerIndex < 0)
            {
                runs.Add(new ActionToolTipRun(text[cursor..]));
                break;
            }

            if (markerIndex > cursor)
            {
                runs.Add(
                    new ActionToolTipRun(
                        text[cursor..markerIndex]));
            }

            runs.Add(
                new ActionToolTipRun(
                    recipientName,
                    ActionToolTipTextRole.Accent,
                    Bold: true));
            cursor = markerIndex + ItemRecipientMarker.Length;
        }

        return runs;
    }

    private static string NormalizeItemEffectText(string text)
    {
        var normalized = WhitespaceRegex.Replace(
            text,
            " ");
        normalized = PunctuationSpacingRegex.Replace(
            normalized,
            "$1");
        return normalized.Trim();
    }

    private static string UppercaseFirstLetter(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            if (!char.IsLetter(text[index]))
            {
                continue;
            }

            var uppercase = char.ToUpperInvariant(text[index]);

            if (uppercase == text[index])
            {
                return text;
            }

            return string.Concat(
                text[..index],
                uppercase,
                text[(index + 1)..]);
        }

        return text;
    }

    private static string EnsureSentenceTerminator(string text)
    {
        var trimmed = text.Trim();

        if (trimmed.Length == 0 ||
            trimmed[^1] is '.' or '!' or '?')
        {
            return trimmed;
        }

        return string.Concat(trimmed, ".");
    }

    private static string FormatRemainingSeconds(
        long remainingMilliseconds)
    {
        var seconds = Math.Max(
            0,
            remainingMilliseconds) / 1000.0;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{seconds:0.0} seconds");
    }

    private ActionToolTipContent BuildSkillActionToolTipContent(
        int processId,
        string actionName,
        SkillShortcutDetails details,
        GroupSkillsTargetRow? selectedTarget,
        Image? icon)
    {
        var paragraphs = new List<ActionToolTipParagraph>();
        var familyName = string.IsNullOrWhiteSpace(details.SkillFamilyName)
            ? actionName
            : details.SkillFamilyName;

        paragraphs.Add(
            details.Rank > 0
                ? new ActionToolTipParagraph(
                    [
                        new("Rank "),
                        new(
                            details.Rank.ToString(CultureInfo.InvariantCulture),
                            ActionToolTipTextRole.Accent,
                            Bold: true),
                        new(" Skill: "),
                        new(
                            familyName,
                            ActionToolTipTextRole.Accent,
                            Bold: true),
                    ],
                    ActionToolTipParagraphStyle.Header,
                    SpaceAfter: 7)
                : new ActionToolTipParagraph(
                    [
                        new("Skill: "),
                        new(
                            familyName,
                            ActionToolTipTextRole.Accent,
                            Bold: true),
                    ],
                    ActionToolTipParagraphStyle.Header,
                    SpaceAfter: 7));

        var caster = this.currentTargets.FirstOrDefault(target =>
            target.ProcessId == processId &&
            !target.IsLeaderTarget);
        var reactorCurrent = caster?.ReactorCurrent;
        var reactorMaximum = caster?.ReactorMaximum;
        var requiredEnergy = ResolveRequiredEnergy(
            details,
            reactorMaximum,
            out var requiredEnergyText,
            out var percentageText);
        var usesSelectedTarget =
            UsesSelectedTarget(details.Semantics);
        var selectedTargetPresent =
            IsSelectedTargetPresent(selectedTarget);
        var targetPresent =
            usesSelectedTarget &&
            selectedTargetPresent;
        var targetName = targetPresent
            ? GetActionTargetName(selectedTarget)
            : null;
        var targetDistance =
            targetPresent &&
            selectedTarget != null
                ? this.clientManager.ResolveGroupSkillsTargetDistance(
                    processId,
                    selectedTarget)
                : null;
        var targetCompatibility = usesSelectedTarget
            ? ResolveTargetCompatibility(
                processId,
                details.Semantics,
                selectedTarget)
            : SkillTargetCompatibility.Unknown;
        var status = BuildSkillStatusParagraph(
            actionName,
            details,
            selectedTarget,
            targetPresent,
            targetName,
            targetDistance,
            targetCompatibility,
            requiredEnergy,
            reactorCurrent);

        if (status != null)
        {
            paragraphs.Add(status);
        }

        paragraphs.Add(
            BuildEnergyParagraph(
                requiredEnergyText,
                percentageText,
                requiredEnergy,
                reactorCurrent));

        if (details.Range is > 0)
        {
            paragraphs.Add(
                BuildRangeParagraph(
                    details.Range.Value,
                    targetDistance,
                    targetPresent));
        }

        if (!string.IsNullOrWhiteSpace(details.Description))
        {
            paragraphs.Add(
                BuildEffectParagraph(
                    details.Description,
                    targetName,
                    targetPresent &&
                    targetCompatibility == SkillTargetCompatibility.Compatible &&
                    details.Semantics.EffectScope.HasFlag(SkillEffectScope.Target)));
        }


        return new ActionToolTipContent(paragraphs, icon);
    }

    private static ActionToolTipParagraph? BuildSkillStatusParagraph(
        string actionName,
        SkillShortcutDetails details,
        GroupSkillsTargetRow? selectedTarget,
        bool targetPresent,
        string? targetName,
        float? targetDistance,
        SkillTargetCompatibility targetCompatibility,
        double? requiredEnergy,
        int? reactorCurrent)
    {
        var requiresTarget = RequiresSelectedTarget(details.Semantics);

        if (requiresTarget && !targetPresent)
        {
            return CreateErrorStatus(
                "No target to ",
                actionName,
                ".");
        }

        if (targetCompatibility == SkillTargetCompatibility.FriendlyForHostile)
        {
            return CreateErrorStatus(
                "Cannot ",
                actionName,
                " friendly target.");
        }

        if (targetCompatibility == SkillTargetCompatibility.HostileForFriendly)
        {
            return CreateErrorStatus(
                "Cannot ",
                actionName,
                " hostile target.");
        }

        if (targetCompatibility == SkillTargetCompatibility.InvalidTargetKind &&
            selectedTarget != null)
        {
            return CreateErrorStatus(
                "Cannot ",
                actionName,
                string.Concat(
                    " ",
                    DescribeTargetKind(selectedTarget.Kind),
                    "."));
        }

        if (details.Range is > 0 &&
            targetDistance.HasValue &&
            targetDistance.Value > details.Range.Value)
        {
            return new ActionToolTipParagraph(
                [
                    new(
                        string.IsNullOrWhiteSpace(targetName)
                            ? "Target"
                            : targetName,
                        ActionToolTipTextRole.Accent,
                        Bold: true),
                    new(" is out of range.", ActionToolTipTextRole.Danger, Bold: true),
                ],
                SpaceAfter: 7);
        }

        if (requiredEnergy.HasValue &&
            reactorCurrent.HasValue &&
            reactorCurrent.Value < requiredEnergy.Value)
        {
            return CreateErrorStatus(
                "Not enough energy to ",
                actionName,
                ".");
        }

        return null;
    }

    private static ActionToolTipParagraph CreateErrorStatus(
        string prefix,
        string actionName,
        string suffix)
    {
        return new ActionToolTipParagraph(
            [
                new(prefix, ActionToolTipTextRole.Danger, Bold: true),
                new(actionName, ActionToolTipTextRole.Accent, Bold: true),
                new(suffix, ActionToolTipTextRole.Danger, Bold: true),
            ],
            SpaceAfter: 7);
    }

    private static ActionToolTipParagraph BuildEnergyParagraph(
        string requiredEnergyText,
        string percentageText,
        double? requiredEnergy,
        int? reactorCurrent)
    {
        var currentText = reactorCurrent.HasValue
            ? FormatToolTipNumber(reactorCurrent.Value)
            : "?";
        var currentRole = !reactorCurrent.HasValue ||
                          !requiredEnergy.HasValue
            ? ActionToolTipTextRole.Muted
            : reactorCurrent.Value >= requiredEnergy.Value
                ? ActionToolTipTextRole.Success
                : ActionToolTipTextRole.Danger;
        var requiredRole =
            requiredEnergy.HasValue ||
            !string.Equals(
                requiredEnergyText,
                "?",
                StringComparison.Ordinal)
                ? ActionToolTipTextRole.Accent
                : ActionToolTipTextRole.Muted;

        return new ActionToolTipParagraph(
            [
                new("Costs: "),
                new(requiredEnergyText, requiredRole, Bold: true),
                new(percentageText, ActionToolTipTextRole.Accent),
                new(" / "),
                new(currentText, currentRole, Bold: true),
                new(" energy"),
            ],
            SpaceAfter: 2);
    }

    private static ActionToolTipParagraph BuildRangeParagraph(
        float requiredRange,
        float? currentDistance,
        bool showCurrentDistance)
    {
        var runs = new List<ActionToolTipRun>
        {
            new("Range: "),
            new(
                FormatToolTipWholeNumber(requiredRange),
                ActionToolTipTextRole.Accent,
                Bold: true),
        };

        if (showCurrentDistance)
        {
            var currentText = currentDistance.HasValue
                ? FormatToolTipWholeNumber(currentDistance.Value)
                : "?";
            var currentRole = !currentDistance.HasValue
                ? ActionToolTipTextRole.Muted
                : currentDistance.Value <= requiredRange
                    ? ActionToolTipTextRole.Success
                    : ActionToolTipTextRole.Danger;

            runs.Add(new(" / "));
            runs.Add(
                new(
                    currentText,
                    currentRole,
                    Bold: true));
        }

        runs.Add(new(" units"));

        return new ActionToolTipParagraph(
            runs,
            SpaceAfter: 2);
    }

    private static ActionToolTipParagraph BuildEffectParagraph(
        string description,
        string? targetName,
        bool personalizeTarget)
    {
        var runs = new List<ActionToolTipRun>
        {
            new("Effect: ", Bold: true),
        };

        AppendTargetAwareTextRuns(
            runs,
            description,
            targetName,
            personalizeTarget);

        return new ActionToolTipParagraph(
            runs,
            SpaceBefore: 7,
            SpaceAfter: 1);
    }

    private static void AppendTargetAwareTextRuns(
        ICollection<ActionToolTipRun> runs,
        string description,
        string? targetName,
        bool personalizeTarget)
    {
        var trimmedDescription = description.Trim();

        if (personalizeTarget &&
            !string.IsNullOrWhiteSpace(targetName) &&
            TryFindTargetPhrase(
                trimmedDescription,
                out var phraseIndex,
                out var phraseLength))
        {
            if (phraseIndex > 0)
            {
                runs.Add(new(trimmedDescription[..phraseIndex]));
            }

            runs.Add(
                new(
                    targetName,
                    ActionToolTipTextRole.Accent,
                    Bold: true));

            var suffixIndex = phraseIndex + phraseLength;

            if (suffixIndex < trimmedDescription.Length)
            {
                runs.Add(new(trimmedDescription[suffixIndex..]));
            }

            return;
        }

        runs.Add(new(trimmedDescription));
    }

    private static double? ResolveRequiredEnergy(
        SkillShortcutDetails details,
        int? reactorMaximum,
        out string requiredEnergyText,
        out string percentageText)
    {
        if (details.MaximumReactorCostPercent is >= 0)
        {
            var percentage = details.MaximumReactorCostPercent.Value;
            percentageText = string.Create(
                CultureInfo.InvariantCulture,
                $" ({percentage:0.##}%)");

            if (reactorMaximum is > 0)
            {
                var approximateCost = reactorMaximum.Value *
                                      percentage /
                                      100.0;
                requiredEnergyText = string.Concat(
                    "~",
                    FormatToolTipNumber(
                        Math.Round(
                            approximateCost,
                            MidpointRounding.AwayFromZero)));
                return approximateCost;
            }

            requiredEnergyText = "?";
            return null;
        }

        percentageText = "";

        if (details.ListedEnergyCost is >= 0)
        {
            requiredEnergyText = FormatToolTipNumber(
                details.ListedEnergyCost.Value);
            return details.ListedEnergyCost.Value;
        }

        if (details.ListedEnergyCost.HasValue)
        {
            requiredEnergyText = "Variable";
            return null;
        }

        requiredEnergyText = "?";
        return null;
    }

    private static SkillTargetCompatibility ResolveTargetCompatibility(
        int processId,
        SkillAbilitySemantics semantics,
        GroupSkillsTargetRow? selectedTarget)
    {
        if (!IsSelectedTargetPresent(selectedTarget) ||
            selectedTarget == null)
        {
            return SkillTargetCompatibility.Unknown;
        }

        var target = semantics.TargetDisposition;
        var selectedSelf = selectedTarget.ProcessId == processId ||
                           selectedTarget.Relation == ClientTargetRelation.Self;
        var selectedFriendly = selectedSelf ||
                               selectedTarget.ProcessId.HasValue ||
                               selectedTarget.Relation is
                                   ClientTargetRelation.GroupMember or
                                   ClientTargetRelation.Friendly;
        var selectedHostile =
            selectedTarget.Relation == ClientTargetRelation.Enemy;
        var hostileOnly =
            target.HasFlag(SkillTargetDisposition.Hostile) &&
            !target.HasFlag(SkillTargetDisposition.Friendly) &&
            !target.HasFlag(SkillTargetDisposition.Self);
        var friendlyOnly =
            target.HasFlag(SkillTargetDisposition.Friendly) &&
            !target.HasFlag(SkillTargetDisposition.Hostile);
        var canAssertDisposition = semantics.Confidence is
            SkillSemanticConfidence.Explicit or
            SkillSemanticConfidence.Curated;

        if (canAssertDisposition &&
            hostileOnly &&
            selectedFriendly)
        {
            return SkillTargetCompatibility.FriendlyForHostile;
        }

        if (canAssertDisposition &&
            friendlyOnly &&
            selectedHostile)
        {
            return SkillTargetCompatibility.HostileForFriendly;
        }

        if (canAssertDisposition &&
            semantics.EffectScope.HasFlag(SkillEffectScope.Target) &&
            IsContextOnlyTarget(selectedTarget.Kind))
        {
            return SkillTargetCompatibility.InvalidTargetKind;
        }

        return SkillTargetCompatibility.Compatible;
    }

    private static bool UsesSelectedTarget(
        SkillAbilitySemantics semantics)
    {
        return semantics.EffectScope.HasFlag(
                   SkillEffectScope.Target) ||
               semantics.TargetDisposition.HasFlag(
                   SkillTargetDisposition.Friendly) ||
               semantics.TargetDisposition.HasFlag(
                   SkillTargetDisposition.Hostile);
    }

    private static bool RequiresSelectedTarget(
        SkillAbilitySemantics semantics)
    {
        if (semantics.TargetDisposition == SkillTargetDisposition.Self)
        {
            return false;
        }

        if (semantics.TargetDisposition.HasFlag(SkillTargetDisposition.Self))
        {
            return false;
        }

        return semantics.EffectScope.HasFlag(SkillEffectScope.Target) ||
               semantics.TargetDisposition.HasFlag(SkillTargetDisposition.Friendly) ||
               semantics.TargetDisposition.HasFlag(SkillTargetDisposition.Hostile);
    }

    private static bool IsSelectedTargetPresent(
        GroupSkillsTargetRow? selectedTarget)
    {
        return selectedTarget != null &&
               (!selectedTarget.IsLeaderTarget || selectedTarget.HasTarget);
    }

    private static bool TryFindTargetPhrase(
        string description,
        out int index,
        out int length)
    {
        string[] phrases =
        [
            "the selected target enemy",
            "a selected target enemy",
            "the selected friendly target",
            "a selected friendly target",
            "a target NPC or NPC group",
            "the targeted group member",
            "a targeted group member",
            "a player or a friendly target",
            "the primary target",
            "a single target",
            "an organic target",
            "any player target",
            "the target enemy",
            "a target enemy",
            "target enemy",
            "an enemy target",
            "the enemy target",
            "a friendly target",
            "the friendly target",
            "the selected target",
            "a selected target",
            "another player's ship",
            "another player",
            "the target",
            "a target",
        ];

        index = int.MaxValue;
        length = 0;

        foreach (var phrase in phrases)
        {
            var candidateIndex = description.IndexOf(
                phrase,
                StringComparison.OrdinalIgnoreCase);

            if (candidateIndex < 0 ||
                candidateIndex >= index)
            {
                continue;
            }

            index = candidateIndex;
            length = phrase.Length;
        }

        if (index != int.MaxValue)
        {
            return true;
        }

        index = -1;
        return false;
    }

    private static string FormatToolTipNumber(double value)
    {
        return value.ToString("#,0.##", CultureInfo.InvariantCulture);
    }

    private static string FormatToolTipWholeNumber(double value)
    {
        return Math.Round(
                value,
                MidpointRounding.AwayFromZero)
            .ToString("#,0", CultureInfo.InvariantCulture);
    }

    private enum SkillTargetCompatibility
    {
        Unknown,
        Compatible,
        FriendlyForHostile,
        HostileForFriendly,
        InvalidTargetKind,
    }

    private static bool IsContextOnlyTarget(
        ClientTargetKind kind)
    {
        return kind is
            ClientTargetKind.Corpse or
            ClientTargetKind.Planet or
            ClientTargetKind.SectorGate or
            ClientTargetKind.Station or
            ClientTargetKind.NavigationPoint or
            ClientTargetKind.Asteroid;
    }

    private static string DescribeTargetKind(
        ClientTargetKind kind)
    {
        return kind switch
        {
            ClientTargetKind.Corpse => "corpse",
            ClientTargetKind.Planet => "planet",
            ClientTargetKind.SectorGate => "sector gate",
            ClientTargetKind.Station => "station",
            ClientTargetKind.NavigationPoint => "navigation point",
            ClientTargetKind.Asteroid => "asteroid",
            _ => "target",
        };
    }

    private static string? GetActionTargetName(
        GroupSkillsTargetRow? selectedTarget)
    {
        if (selectedTarget == null ||
            (selectedTarget.IsLeaderTarget && !selectedTarget.HasTarget))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(selectedTarget.Name))
        {
            return selectedTarget.Name;
        }

        return selectedTarget.IsLeaderTarget
            ? "current target"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"group member {selectedTarget.GroupSlot}");
    }

    private void ReactivateToolTips()
    {
        if (this.IsDisposed)
        {
            return;
        }

        this.toolTip.Active = false;
        this.toolTip.Active = true;
        this.actionToolTip.Reactivate();
    }

    private async Task InvokeFireAllAsync(
        GroupSkillsPilotCard card,
        GroupSkillsTargetRow selectedTarget)
    {
        var restorePoint = CaptureCursorPosition();
        this.executingAction = true;

        try
        {
            await this.clientManager.ExecuteGroupSkillsFireAllAsync(
                    this.leaderProcessId,
                    card.ProcessId,
                    selectedTarget,
                    CancellationToken.None)
                .ConfigureAwait(true);
        }
        finally
        {
            this.executingAction = false;
            this.BuildRows(force: false);
            this.clientManager.RestoreGroupSkillsOpenerFocus(this.leaderProcessId);
            RestoreCursorPosition(restorePoint);
            this.ReactivateToolTips();
        }
    }

    private async Task InvokeShortcutAsync(
        GroupSkillsPilotCard card,
        GameShortcutPaletteEntry action,
        GroupSkillsTargetRow selectedTarget)
    {
        var restorePoint = CaptureCursorPosition();
        this.executingAction = true;

        try
        {
            await this.clientManager.ExecuteGroupSkillsShortcutAsync(
                    this.leaderProcessId,
                    card.ProcessId,
                    action.ToInvocation(),
                    selectedTarget,
                    CancellationToken.None)
                .ConfigureAwait(true);
        }
        finally
        {
            this.executingAction = false;
            this.BuildRows(force: false);
            this.clientManager.RestoreGroupSkillsOpenerFocus(this.leaderProcessId);
            RestoreCursorPosition(restorePoint);
            this.ReactivateToolTips();
        }
    }

    private static Point? CaptureCursorPosition()
    {
        return NativeMethods.TryGetCursorScreenPosition(out var point)
            ? point
            : null;
    }

    private static void RestoreCursorPosition(Point? point)
    {
        if (point.HasValue)
        {
            _ = NativeMethods.MoveCursorToScreenPoint(point.Value);
        }
    }

    private bool IsSelectedTarget(
        GroupSkillsTargetRow target)
    {
        return string.Equals(
            this.selectedTargetKey,
            GetTargetKey(target),
            StringComparison.Ordinal);
    }

    private GroupSkillsTargetRow? ResolveSelectedTarget(
        IReadOnlyList<GroupSkillsTargetRow> targets)
    {
        var target = targets.FirstOrDefault(candidate =>
            string.Equals(
                GetTargetKey(candidate),
                this.selectedTargetKey,
                StringComparison.Ordinal));

        if (target != null)
        {
            return target;
        }

        target = targets.FirstOrDefault(candidate => candidate.IsLeaderTarget) ?? targets.FirstOrDefault();
        this.selectedTargetKey = target != null
            ? GetTargetKey(target)
            : LeaderTargetKey;
        return target;
    }

    private string GetTargetDisplayName(
        GroupSkillsTargetRow target)
    {
        if (!target.IsLeaderTarget)
        {
            return target.Name;
        }

        return target.HasTarget
            ? target.Name
            : "";
    }

    private static string GetTargetKey(
        GroupSkillsTargetRow target)
    {
        if (target.IsLeaderTarget)
        {
            return LeaderTargetKey;
        }

        return target.ProcessId.HasValue
            ? string.Create(CultureInfo.InvariantCulture, $"process:{target.ProcessId.Value}")
            : string.Create(CultureInfo.InvariantCulture, $"slot:{target.GroupSlot}:{target.Name}");
    }

    private static IReadOnlyList<GroupSkillsTargetRow> GetSharedTargets(
        IReadOnlyList<GroupSkillsPilotCard> cards)
    {
        return cards.Count == 0
            ? []
            : cards[0].Targets;
    }

    private static string GetPilotDisplayName(
        GroupSkillsPilotCard card)
    {
        return string.IsNullOrWhiteSpace(card.Name)
            ? string.Create(CultureInfo.InvariantCulture, $"Client {card.ProcessId}")
            : card.Name;
    }

    private static GroupSkillsTargetRow? GetOwnTarget(
        GroupSkillsPilotCard card)
    {
        return card.Targets.FirstOrDefault(target =>
            target.ProcessId == card.ProcessId &&
            !target.IsLeaderTarget);
    }

    private static bool ShouldShowActionChip(GameShortcutPaletteEntry action)
    {
        if (action.IsIndividualWeapon)
        {
            return false;
        }

        if (action.Kind == GameShortcutKind.Skill &&
            action.SkillDetails?.IsIntrinsicallyActivatable == false)
        {
            return false;
        }

        if (action.Kind == GameShortcutKind.Equipment &&
            action.ItemDetails?.IsNativeAction == false)
        {
            return false;
        }

        return true;
    }

    private static IEnumerable<GameShortcutActionCategory> GetActionCategoryOrder()
    {
        yield return GameShortcutActionCategory.Offensive;
        yield return GameShortcutActionCategory.Defensive;
        yield return GameShortcutActionCategory.Buff;
        yield return GameShortcutActionCategory.Utility;
        yield return GameShortcutActionCategory.Item;
    }

    private static string Abbreviate(string value)
    {
        var trimmed = value.Trim();

        if (trimmed.Length == 0)
        {
            return "?";
        }

        var known = trimmed.ToLowerInvariant() switch
        {
            "anger" => "Ang",
            "mass field" => "Mass",
            "regenerate equipment" => "Rep",
            "group sap" => "Sap",
            "contained voltoi essence" => "Vol",
            "normandy tria" => "Nor",
            "cloak" => "Clk",
            "carpenter gate" => "Gate",
            "teleport self" => "Tele",
            _ => null,
        };

        if (known != null)
        {
            return known;
        }

        var words = trimmed.Split(
            [' ', '-', '_', '/', '\\'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (words.Length == 1)
        {
            return trimmed.Length <= 4
                ? trimmed
                : trimmed[..4];
        }

        var builder = new StringBuilder();

        foreach (var word in words)
        {
            if (builder.Length >= 4)
            {
                break;
            }

            builder.Append(char.ToUpperInvariant(word[0]));
        }

        return builder.Length > 0
            ? builder.ToString()
            : trimmed[..Math.Min(4, trimmed.Length)];
    }

    private static string CreateRowsLayoutSignature(
        IReadOnlyList<GroupSkillsPilotCard> cards)
    {
        var builder = new StringBuilder();

        foreach (var card in cards)
        {
            builder.Append("|C:");
            builder.Append(card.ProcessId.ToString(CultureInfo.InvariantCulture));

            foreach (var target in card.Targets)
            {
                builder.Append("|T:");
                builder.Append(GetTargetKey(target));
                builder.Append(':');
                builder.Append(target.IsLeader ? '1' : '0');
                builder.Append(':');
                builder.Append(target.IsLeaderTarget ? '1' : '0');
            }
        }

        return builder.ToString();
    }

    private static string CreateActionLayoutSignature(
        GroupSkillsPilotCard card)
    {
        var builder = new StringBuilder();
        builder.Append(card.FireAllIconItemTemplateId?.ToString(CultureInfo.InvariantCulture) ?? "");

        foreach (var action in card.Actions)
        {
            builder.Append("|A:");
            builder.Append(action.Kind);
            builder.Append(':');
            builder.Append(action.Category);
            builder.Append(':');
            builder.Append(action.Bar.ToString(CultureInfo.InvariantCulture));
            builder.Append(':');
            builder.Append(action.Group.ToString(CultureInfo.InvariantCulture));
            builder.Append(':');
            builder.Append(action.Button.ToString(CultureInfo.InvariantCulture));
            builder.Append(':');
            builder.Append(action.SourceSlotIndex?.ToString(CultureInfo.InvariantCulture) ?? "");
            builder.Append(':');
            builder.Append(action.Name);
            builder.Append(':');
            builder.Append(action.IconResourceName);
            builder.Append(':');
            builder.Append(action.ItemTemplateId?.ToString(CultureInfo.InvariantCulture) ?? "");
            builder.Append(':');
            builder.Append(action.TintRed?.ToString("R", CultureInfo.InvariantCulture) ?? "");
            builder.Append(':');
            builder.Append(action.TintGreen?.ToString("R", CultureInfo.InvariantCulture) ?? "");
            builder.Append(':');
            builder.Append(action.TintBlue?.ToString("R", CultureInfo.InvariantCulture) ?? "");
            builder.Append(':');
            builder.Append(action.SkillDetails?.LayoutSignature ?? "");
            builder.Append(':');
            builder.Append(action.ItemDetails?.LayoutSignature ?? "");
        }

        return builder.ToString();
    }

    private void UpdatePilotActionLayouts(
        IReadOnlyList<GroupSkillsPilotCard> cards)
    {
        foreach (var card in cards)
        {
            if (!this.dynamicPilotControls.TryGetValue(
                    card.ProcessId,
                    out var controls))
            {
                continue;
            }

            var signature = CreateActionLayoutSignature(card);

            if (string.Equals(
                    controls.ActionSignature,
                    signature,
                    StringComparison.Ordinal))
            {
                this.RefreshDynamicActionControls(card);
                continue;
            }

            controls.ActionHost.SuspendLayout();

            try
            {
                this.UnregisterDynamicActionControls(card.ProcessId);

                var oldControls = controls.ActionHost.Controls
                    .Cast<Control>()
                    .ToArray();
                controls.ActionHost.Controls.Clear();

                foreach (var oldControl in oldControls)
                {
                    oldControl.Dispose();
                }

                this.PopulateActionChips(controls.ActionHost, card);
                controls.ActionSignature = signature;
            }
            finally
            {
                controls.ActionHost.ResumeLayout(performLayout: true);
                controls.ActionHost.Invalidate();
            }
        }
    }

    private void RefreshDynamicActionControls(
        GroupSkillsPilotCard card)
    {
        foreach (var actionControl in this.dynamicActionControls.Where(control =>
                     control.ProcessId == card.ProcessId &&
                     !control.IsFireAll &&
                     control.Action != null))
        {
            var previous = actionControl.Action!;
            var current = card.Actions.FirstOrDefault(action =>
                action.Kind == previous.Kind &&
                action.Bar == previous.Bar &&
                action.Group == previous.Group &&
                action.Button == previous.Button &&
                action.SourceSlotIndex == previous.SourceSlotIndex);

            if (current == null)
            {
                continue;
            }

            actionControl.UpdateAction(current);
        }
    }

    private void UnregisterDynamicActionControls(int processId)
    {
        for (var index = this.dynamicActionControls.Count - 1; index >= 0; index--)
        {
            var actionControl = this.dynamicActionControls[index];

            if (actionControl.ProcessId != processId)
            {
                continue;
            }

            this.actionToolTip.Remove(actionControl.Button);
            this.dynamicActionControls.RemoveAt(index);
        }
    }

    private readonly record struct ItemActionTargetContext(
        bool HasRecipient,
        string? RecipientName,
        GroupSkillsTargetRow? DistanceTarget,
        bool IsSelf);

    private sealed class DynamicTargetControls(
        TargetNameControl nameButton,
        VitalBarsControl vitals)
    {
        public TargetNameControl NameButton { get; } = nameButton;

        public VitalBarsControl Vitals { get; } = vitals;
    }

    private sealed class DynamicPilotControls(
        TableLayoutPanel row,
        TableLayoutPanel actionHost,
        string actionSignature)
    {
        public TableLayoutPanel Row { get; } = row;

        public TableLayoutPanel ActionHost { get; } = actionHost;

        public string ActionSignature { get; set; } = actionSignature;
    }

    private sealed class DynamicActionControl(
        int processId,
        IconActionButton button,
        string actionName,
        GameShortcutPaletteEntry? action,
        bool isFireAll,
        Image? toolTipIcon)
    {
        public int ProcessId { get; } = processId;

        public IconActionButton Button { get; } = button;

        public string ActionName { get; private set; } = actionName;

        public GameShortcutPaletteEntry? Action { get; private set; } = action;

        public bool IsFireAll { get; } = isFireAll;

        public Image? ToolTipIcon { get; } = toolTipIcon;

        public void UpdateAction(GameShortcutPaletteEntry latestAction)
        {
            this.Action = latestAction;
            this.ActionName = latestAction.Name;
            this.Button.AccessibleName = latestAction.Name;
        }
    }

    private sealed class TargetNameControl : Control
    {
        private static readonly Color SelectedBackground = Color.FromArgb(24, 64, 82);
        private static readonly Color CombatLevelColor = Color.FromArgb(67, 225, 196);
        private static readonly Color ExploreLevelColor = Color.FromArgb(92, 156, 255);
        private static readonly Color TradeLevelColor = Color.FromArgb(211, 116, 255);

        private string displayName = "";
        private string detail = "";
        private int? overallLevel;
        private int? combatLevel;
        private int? exploreLevel;
        private int? tradeLevel;
        private bool selected;
        private bool interactive;
        private bool hovered;
        private bool pressed;

        public TargetNameControl()
        {
            this.SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.StandardClick |
                ControlStyles.UserPaint,
                value: true);
            this.SetStyle(ControlStyles.Selectable, value: false);
            this.TabStop = false;
            this.BackColor = MainWindowTheme.Button;
        }

        public void UpdateState(
            string displayName,
            string detail,
            int? overallLevel,
            int? combatLevel,
            int? exploreLevel,
            int? tradeLevel,
            bool selected,
            bool interactive)
        {
            displayName ??= "";
            detail ??= "";

            if (string.Equals(this.displayName, displayName, StringComparison.Ordinal) &&
                string.Equals(this.detail, detail, StringComparison.Ordinal) &&
                this.overallLevel == overallLevel &&
                this.combatLevel == combatLevel &&
                this.exploreLevel == exploreLevel &&
                this.tradeLevel == tradeLevel &&
                this.selected == selected &&
                this.interactive == interactive)
            {
                return;
            }

            this.displayName = displayName;
            this.detail = detail;
            this.overallLevel = overallLevel;
            this.combatLevel = combatLevel;
            this.exploreLevel = exploreLevel;
            this.tradeLevel = tradeLevel;
            this.selected = selected;
            this.interactive = interactive;
            this.Cursor = interactive
                ? Cursors.Hand
                : Cursors.Default;
            this.AccessibleRole = interactive
                ? AccessibleRole.PushButton
                : AccessibleRole.StaticText;
            this.AccessibleName = displayName;
            this.Text = string.IsNullOrWhiteSpace(detail)
                ? displayName
                : string.Concat(displayName, Environment.NewLine, detail);
            this.Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);

            if (!this.interactive)
            {
                return;
            }

            this.hovered = true;
            this.Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);

            if (!this.hovered && !this.pressed)
            {
                return;
            }

            this.hovered = false;
            this.pressed = false;
            this.Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (!this.interactive || e.Button != MouseButtons.Left)
            {
                return;
            }

            this.pressed = true;
            this.Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);

            if (!this.interactive || e.Button != MouseButtons.Left)
            {
                return;
            }

            this.pressed = false;
            this.Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            var background = this.selected
                ? SelectedBackground
                : this.pressed
                    ? MainWindowTheme.ElevatedPanel
                    : this.hovered && this.interactive
                        ? MainWindowTheme.ButtonHover
                        : MainWindowTheme.Button;
            var border = this.selected
                ? MainWindowTheme.AccentBorder
                : MainWindowTheme.Border;
            var nameColor = this.selected
                ? MainWindowTheme.Accent
                : MainWindowTheme.Text;

            e.Graphics.Clear(background);

            using (var borderPen = new Pen(border))
            {
                e.Graphics.DrawRectangle(
                    borderPen,
                    0,
                    0,
                    Math.Max(0, this.ClientSize.Width - 1),
                    Math.Max(0, this.ClientSize.Height - 1));
            }

            var content = Rectangle.FromLTRB(
                9,
                7,
                Math.Max(9, this.ClientSize.Width - 8),
                Math.Max(7, this.ClientSize.Height - 6));
            var nameBounds = new Rectangle(
                content.X,
                content.Y + 2,
                content.Width,
                21);
            using var nameFont = MainWindowTheme.CreateHeadingFont(9.0f);
            TextRenderer.DrawText(
                e.Graphics,
                this.displayName,
                nameFont,
                nameBounds,
                nameColor,
                TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPadding |
                TextFormatFlags.SingleLine |
                TextFormatFlags.VerticalCenter);

            var detailBounds = new Rectangle(
                content.X,
                content.Y + 32,
                content.Width,
                20);

            if (this.overallLevel.HasValue &&
                this.combatLevel.HasValue &&
                this.exploreLevel.HasValue &&
                this.tradeLevel.HasValue)
            {
                this.DrawProgressionDetail(e.Graphics, detailBounds);
            }
            else if (!string.IsNullOrWhiteSpace(this.detail))
            {
                using var detailFont = MainWindowTheme.CreateBodyFont(8.5f);
                TextRenderer.DrawText(
                    e.Graphics,
                    this.detail,
                    detailFont,
                    detailBounds,
                    MainWindowTheme.MutedText,
                    TextFormatFlags.EndEllipsis |
                    TextFormatFlags.NoPadding |
                    TextFormatFlags.SingleLine |
                    TextFormatFlags.VerticalCenter);
            }
        }

        private void DrawProgressionDetail(
            Graphics graphics,
            Rectangle bounds)
        {
            using var font = MainWindowTheme.CreateBodyFont(8.5f);
            var x = bounds.X;
            x += DrawTextSegment(
                graphics,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Level {this.overallLevel!.Value} ["),
                font,
                new Point(x, bounds.Y),
                bounds.Height,
                MainWindowTheme.MutedText);
            x += DrawTextSegment(
                graphics,
                this.combatLevel!.Value.ToString(CultureInfo.InvariantCulture),
                font,
                new Point(x, bounds.Y),
                bounds.Height,
                CombatLevelColor);
            x += DrawTextSegment(
                graphics,
                "  ",
                font,
                new Point(x, bounds.Y),
                bounds.Height,
                MainWindowTheme.MutedText);
            x += DrawTextSegment(
                graphics,
                this.exploreLevel!.Value.ToString(CultureInfo.InvariantCulture),
                font,
                new Point(x, bounds.Y),
                bounds.Height,
                ExploreLevelColor);
            x += DrawTextSegment(
                graphics,
                "  ",
                font,
                new Point(x, bounds.Y),
                bounds.Height,
                MainWindowTheme.MutedText);
            x += DrawTextSegment(
                graphics,
                this.tradeLevel!.Value.ToString(CultureInfo.InvariantCulture),
                font,
                new Point(x, bounds.Y),
                bounds.Height,
                TradeLevelColor);
            _ = DrawTextSegment(
                graphics,
                "]",
                font,
                new Point(x, bounds.Y),
                bounds.Height,
                MainWindowTheme.MutedText);
        }

        private static int DrawTextSegment(
            Graphics graphics,
            string text,
            Font font,
            Point location,
            int height,
            Color color)
        {
            const TextFormatFlags flags =
                TextFormatFlags.NoPadding |
                TextFormatFlags.SingleLine |
                TextFormatFlags.VerticalCenter;
            var size = TextRenderer.MeasureText(
                graphics,
                text,
                font,
                new Size(1000, height),
                flags);
            TextRenderer.DrawText(
                graphics,
                text,
                font,
                new Rectangle(location.X, location.Y, size.Width, height),
                color,
                flags);
            return size.Width;
        }
    }

    private sealed class VitalBarsControl : Control
    {
        private static readonly Color TrackColor = Color.FromArgb(12, 27, 37);
        private static readonly Color TrackBorderColor = Color.FromArgb(70, 83, 92);
        private static readonly Color SelectedBackground = Color.FromArgb(24, 64, 82);

        private bool hasTarget;
        private int? shieldCurrent;
        private int? shieldPercent;
        private int? hullCurrent;
        private int? hullPercent;
        private int? reactorCurrent;
        private int? reactorPercent;
        private bool selected;

        public VitalBarsControl()
        {
            this.SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                value: true);
            this.SetStyle(ControlStyles.Selectable, value: false);
            this.BackColor = MainWindowTheme.ElevatedPanel;
            this.TabStop = false;
        }

        public void UpdateState(
            bool hasTarget,
            int? shieldCurrent,
            int? shieldPercent,
            int? hullCurrent,
            int? hullPercent,
            int? reactorCurrent,
            int? reactorPercent,
            bool selected)
        {
            shieldCurrent = NormalizeCurrent(shieldCurrent);
            shieldPercent = NormalizePercent(shieldPercent);
            hullCurrent = NormalizeCurrent(hullCurrent);
            hullPercent = NormalizePercent(hullPercent);
            reactorCurrent = NormalizeCurrent(reactorCurrent);
            reactorPercent = NormalizePercent(reactorPercent);

            if (this.hasTarget == hasTarget &&
                this.shieldCurrent == shieldCurrent &&
                this.shieldPercent == shieldPercent &&
                this.hullCurrent == hullCurrent &&
                this.hullPercent == hullPercent &&
                this.reactorCurrent == reactorCurrent &&
                this.reactorPercent == reactorPercent &&
                this.selected == selected)
            {
                return;
            }

            this.hasTarget = hasTarget;
            this.shieldCurrent = shieldCurrent;
            this.shieldPercent = shieldPercent;
            this.hullCurrent = hullCurrent;
            this.hullPercent = hullPercent;
            this.reactorCurrent = reactorCurrent;
            this.reactorPercent = reactorPercent;
            this.selected = selected;
            this.AccessibleName = BuildAccessibleName(
                shieldCurrent,
                shieldPercent,
                hullCurrent,
                hullPercent,
                reactorCurrent,
                reactorPercent);
            this.Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            var background = this.selected
                ? SelectedBackground
                : MainWindowTheme.ElevatedPanel;
            e.Graphics.Clear(background);

            var bars = new List<VitalBarState>(3)
            {
                new(
                    this.hasTarget ? this.shieldCurrent : null,
                    this.hasTarget ? this.shieldPercent : null,
                    ShieldColor),
                new(
                    this.hasTarget ? this.hullCurrent : null,
                    this.hasTarget ? this.hullPercent : null,
                    HullColor),
            };

            if (this.reactorCurrent.HasValue || this.reactorPercent.HasValue)
            {
                bars.Add(new VitalBarState(
                    this.reactorCurrent,
                    this.reactorPercent,
                    ReactorColor));
            }

            const int horizontalPadding = 5;
            const int verticalPadding = 5;
            const int gap = 3;
            const int barSlotCount = 3;
            var gridGap = gap * (barSlotCount - 1);
            var availableHeight = Math.Max(1,
                this.ClientSize.Height - verticalPadding * 2 - gridGap);
            var barHeight = Math.Max(10, availableHeight / barSlotCount);
            var visibleGap = gap * Math.Max(0, bars.Count - 1);
            var totalHeight = barHeight * bars.Count + visibleGap;
            var top = Math.Max(verticalPadding,
                (this.ClientSize.Height - totalHeight) / 2);
            var width = Math.Max(1, this.ClientSize.Width - horizontalPadding * 2);

            for (var index = 0; index < bars.Count; index++)
            {
                DrawBar(
                    e.Graphics,
                    new Rectangle(
                        horizontalPadding,
                        top + index * (barHeight + gap),
                        width,
                        barHeight),
                    bars[index]);
            }
        }

        private static int? NormalizeCurrent(int? value)
        {
            return value.HasValue
                ? Math.Max(0, value.Value)
                : null;
        }

        private static int? NormalizePercent(int? value)
        {
            return value.HasValue
                ? Math.Clamp(value.Value, 0, 100)
                : null;
        }

        private static string BuildAccessibleName(
            int? shieldCurrent,
            int? shieldPercent,
            int? hullCurrent,
            int? hullPercent,
            int? reactorCurrent,
            int? reactorPercent)
        {
            var values = new List<string>(3);
            AddAccessibleValue(values, "Shield", shieldCurrent, shieldPercent);
            AddAccessibleValue(values, "Hull", hullCurrent, hullPercent);
            AddAccessibleValue(values, "Reactor", reactorCurrent, reactorPercent);
            return values.Count == 0
                ? "Vitals unavailable"
                : string.Join(", ", values);
        }

        private static void AddAccessibleValue(
            ICollection<string> values,
            string label,
            int? current,
            int? percent)
        {
            if (!current.HasValue && !percent.HasValue)
            {
                return;
            }

            values.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"{label} {current?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}, {percent?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}%"));
        }

        private static void DrawBar(
            Graphics graphics,
            Rectangle bounds,
            VitalBarState state)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }

            using var trackBrush = new SolidBrush(TrackColor);
            using var borderPen = new Pen(TrackBorderColor);
            graphics.FillRectangle(trackBrush, bounds);
            graphics.DrawRectangle(
                borderPen,
                bounds.X,
                bounds.Y,
                Math.Max(0, bounds.Width - 1),
                Math.Max(0, bounds.Height - 1));

            if (state.Percent.HasValue)
            {
                var innerBounds = Rectangle.Inflate(bounds, -1, -1);
                var fillWidth = (int)Math.Round(
                    innerBounds.Width * state.Percent.Value / 100d);

                if (fillWidth > 0 && innerBounds.Height > 0)
                {
                    using var fillBrush = new SolidBrush(state.FillColor);
                    graphics.FillRectangle(
                        fillBrush,
                        innerBounds.X,
                        innerBounds.Y,
                        fillWidth,
                        innerBounds.Height);
                }
            }

            using var valueFont = MainWindowTheme.CreateHeadingFont(7.5f);
            var textBounds = Rectangle.Inflate(bounds, -4, 0);
            var leftBounds = new Rectangle(
                textBounds.X,
                textBounds.Y,
                Math.Max(1, textBounds.Width / 2),
                textBounds.Height);
            var rightBounds = new Rectangle(
                leftBounds.Right,
                textBounds.Y,
                Math.Max(1, textBounds.Right - leftBounds.Right),
                textBounds.Height);
            const TextFormatFlags commonFlags =
                TextFormatFlags.NoPadding |
                TextFormatFlags.SingleLine |
                TextFormatFlags.VerticalCenter;

            if (state.Current.HasValue)
            {
                TextRenderer.DrawText(
                    graphics,
                    state.Current.Value.ToString(CultureInfo.InvariantCulture),
                    valueFont,
                    leftBounds,
                    Color.White,
                    commonFlags | TextFormatFlags.Left);
            }

            if (state.Percent.HasValue)
            {
                TextRenderer.DrawText(
                    graphics,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{state.Percent.Value}%"),
                    valueFont,
                    rightBounds,
                    Color.White,
                    commonFlags | TextFormatFlags.Right);
            }
        }

        private readonly record struct VitalBarState(
            int? Current,
            int? Percent,
            Color FillColor);
    }

    private class NonFocusableButton : Button
    {
        public NonFocusableButton()
        {
            this.SetStyle(ControlStyles.Selectable, value: false);
            this.TabStop = false;
        }

        protected override bool ShowFocusCues => false;
    }

    private sealed class OverlayCloseButton : Control
    {
        private bool pointerInside;
        private bool pressed;

        public OverlayCloseButton()
        {
            this.SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                value: true);
            this.SetStyle(ControlStyles.Selectable, value: false);
            this.TabStop = false;
            this.BackColor = MainWindowTheme.Panel;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            this.pointerInside = true;
            this.Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            this.pointerInside = false;
            this.pressed = false;
            this.Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (e.Button == MouseButtons.Left)
            {
                this.pressed = true;
                this.Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);

            if (this.pressed)
            {
                this.pressed = false;
                this.Invalidate();
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // The button paints its entire surface in OnPaint so its normal
            // state blends exactly into the HUD's action panel.
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var background = this.pressed
                ? Color.FromArgb(155, 38, 42)
                : this.pointerInside
                    ? MainWindowTheme.Danger
                    : MainWindowTheme.Panel;
            var glyphColor = this.pointerInside || this.pressed
                ? Color.White
                : MainWindowTheme.MutedText;

            e.Graphics.Clear(background);
            e.Graphics.SmoothingMode =
                System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            var centerX = (this.ClientSize.Width - 1) / 2.0f;
            var centerY = (this.ClientSize.Height - 1) / 2.0f;
            const float halfGlyphSize = 3.5f;

            using var pen = new Pen(glyphColor, 1.5f)
            {
                StartCap = System.Drawing.Drawing2D.LineCap.Round,
                EndCap = System.Drawing.Drawing2D.LineCap.Round,
            };

            e.Graphics.DrawLine(
                pen,
                centerX - halfGlyphSize,
                centerY - halfGlyphSize,
                centerX + halfGlyphSize,
                centerY + halfGlyphSize);
            e.Graphics.DrawLine(
                pen,
                centerX + halfGlyphSize,
                centerY - halfGlyphSize,
                centerX - halfGlyphSize,
                centerY + halfGlyphSize);
        }
    }

    private sealed class IconActionButton : NonFocusableButton
    {
    }

    private sealed class BufferedFlowLayoutPanel : FlowLayoutPanel
    {
        public BufferedFlowLayoutPanel()
        {
            this.DoubleBuffered = true;
            this.ResizeRedraw = true;
        }
    }
}
