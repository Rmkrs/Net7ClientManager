// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using System.Diagnostics;
using System.Globalization;
using Net7ClientManager.Observations.Models;
using Net7ClientManager.Observations.Observers;
using Net7ClientManager.SkillPlanning;
using Net7ClientManager.Win32;

internal sealed class SkillBuildEquipmentCompanionForm : Form
{
    private const int WmMouseActivate = 0x0021;
    private const int MaNoActivate = 3;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;

    private const int TitleHeight = 28;
    private const int LegendHeight = 22;
    private const int GroupHeadingHeight = 22;
    private const int EquipmentCardHeight = 46;
    private const int EquipmentCardBottomMargin = 2;
    private const int EmptyGroupHeight = 30;
    private const int EmptyStateHeight = 120;

    private readonly Func<int?, Size, Image?> resolveItemIcon;
    private readonly Panel content = new();
    private readonly Label title = new();
    private readonly Panel boardScroll = new();
    private readonly TableLayoutPanel board = new();
    private readonly ActionToolTip itemToolTip = new();
    private readonly Dictionary<string, EquipmentRowBinding> equipmentRows =
        new(StringComparer.Ordinal);
    private readonly Dictionary<ItemIconKey, Image?> itemIcons = [];
    private readonly HashSet<Control> darkThemeControls = [];

    private SkillBuildEquipmentCompanionPresentation presentation =
        SkillBuildEquipmentCompanionPresentation.Hidden;
    private string fingerprint = "";
    private string structureFingerprint = "";

    public SkillBuildEquipmentCompanionForm(
        Func<int?, Size, Image?> resolveItemIcon)
    {
        this.resolveItemIcon = resolveItemIcon ??
            throw new ArgumentNullException(nameof(resolveItemIcon));

        this.FormBorderStyle = FormBorderStyle.None;
        this.ShowInTaskbar = false;
        this.StartPosition = FormStartPosition.Manual;
        this.AutoScaleMode = AutoScaleMode.None;
        this.BackColor = MainWindowTheme.AccentBorder;
        this.Padding = new Padding(1);
        this.DoubleBuffered = true;

        this.content.Dock = DockStyle.Fill;
        this.content.Margin = Padding.Empty;
        this.content.Padding = Padding.Empty;
        this.content.BackColor = MainWindowTheme.Background;

        this.title.Dock = DockStyle.Top;
        this.title.Height = TitleHeight;
        this.title.Margin = Padding.Empty;
        this.title.Padding = new Padding(9, 0, 8, 0);
        this.title.TextAlign = ContentAlignment.MiddleLeft;
        this.title.Font = MainWindowTheme.CreateHeadingFont(10.0f);
        this.title.ForeColor = MainWindowTheme.Accent;
        this.title.BackColor = MainWindowTheme.Panel;
        this.title.AutoEllipsis = true;

        var legend = CreateLegend();

        this.boardScroll.Dock = DockStyle.Fill;
        this.boardScroll.AutoScroll = true;
        this.boardScroll.Margin = Padding.Empty;
        this.boardScroll.Padding = Padding.Empty;
        this.boardScroll.BackColor = MainWindowTheme.Background;
        this.boardScroll.SizeChanged += this.BoardScroll_OnSizeChanged;

        this.board.Dock = DockStyle.Top;
        this.board.AutoSize = true;
        this.board.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        this.board.ColumnCount = 1;
        this.board.RowCount = 1;
        this.board.Margin = Padding.Empty;
        this.board.Padding = new Padding(0, 0, 0, 4);
        this.board.BackColor = MainWindowTheme.Background;
        this.board.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100.0f));
        this.board.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        this.boardScroll.Controls.Add(this.board);
        this.content.Controls.Add(this.boardScroll);
        this.content.Controls.Add(legend);
        this.content.Controls.Add(this.title);
        this.Controls.Add(this.content);
        this.RegisterDarkScrollbarTheme(this);
    }

    public event EventHandler? OpenBuildsRequested;

    public event EventHandler<SkillBuildEquipmentFindRequestedEventArgs>?
        FindItemRequested;

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= WsExToolWindow | WsExNoActivate;
            return parameters;
        }
    }

    public void SetPresentation(
        SkillBuildEquipmentCompanionPresentation nextPresentation)
    {
        ArgumentNullException.ThrowIfNull(nextPresentation);

        if (string.Equals(
                this.fingerprint,
                nextPresentation.Fingerprint,
                StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            var structureChanged = !string.Equals(
                this.structureFingerprint,
                nextPresentation.StructureFingerprint,
                StringComparison.Ordinal);

            this.presentation = nextPresentation;
            this.fingerprint = nextPresentation.Fingerprint;
            this.structureFingerprint = nextPresentation.StructureFingerprint;

            if (structureChanged)
            {
                this.RebuildContent();
            }
            else
            {
                this.UpdateContentInPlace();
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"[Builds] Could not refresh the Equipment companion: {exception}"));
            this.Hide();
        }
    }

    public int GetPreferredHeight(int maximumHeight)
    {
        maximumHeight = Math.Max(1, maximumHeight);

        var bodyHeight =
            this.presentation.HasLiveContext &&
            this.presentation.HasActiveBuild
                ? this.CalculateEquipmentBoardHeight()
                : EmptyStateHeight;
        var preferredHeight =
            this.Padding.Vertical +
            TitleHeight +
            LegendHeight +
            bodyHeight;

        return Math.Clamp(
            preferredHeight,
            Math.Min(150, maximumHeight),
            maximumHeight);
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.boardScroll.SizeChanged -= this.BoardScroll_OnSizeChanged;
            this.itemToolTip.Dispose();
            foreach (var control in this.darkThemeControls.ToArray())
            {
                control.HandleCreated -=
                    this.DarkScrollbarControl_OnHandleCreated;
                control.ControlAdded -=
                    this.DarkScrollbarControl_OnControlAdded;
                control.Disposed -=
                    this.DarkScrollbarControl_OnDisposed;
            }
            this.darkThemeControls.Clear();
            this.equipmentRows.Clear();
            this.itemIcons.Clear();
        }

        base.Dispose(disposing);
    }

    private static Control CreateLegend()
    {
        var legend = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = LegendHeight,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = new Padding(9, 0, 8, 0),
            BackColor = MainWindowTheme.Panel,
        };
        legend.Controls.Add(CreateLegendLabel("Equipped", MainWindowTheme.Success));
        legend.Controls.Add(CreateLegendLabel("  ·  ", MainWindowTheme.Text));
        legend.Controls.Add(CreateLegendLabel("Cargo", MainWindowTheme.Warning));
        legend.Controls.Add(CreateLegendLabel("  ·  ", MainWindowTheme.Text));
        legend.Controls.Add(CreateLegendLabel("Vault", MainWindowTheme.Accent));
        legend.Controls.Add(CreateLegendLabel("  ·  ", MainWindowTheme.Text));
        legend.Controls.Add(CreateLegendLabel("Missing", MainWindowTheme.Danger));
        return legend;
    }

    private static Label CreateLegendLabel(string text, Color color) =>
        new()
        {
            AutoSize = true,
            Height = LegendHeight,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            Text = text,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = MainWindowTheme.CreateHeadingFont(8.0f),
            ForeColor = color,
            BackColor = Color.Transparent,
        };

    private void RebuildContent()
    {
        this.itemToolTip.RemoveAll();
        this.equipmentRows.Clear();
        this.board.SuspendLayout();
        try
        {
            var previousControls = this.board.Controls
                .Cast<Control>()
                .ToArray();
            this.board.Controls.Clear();
            foreach (var control in previousControls)
            {
                control.Dispose();
            }

            this.title.Text = this.presentation.HasActiveBuild
                ? string.Concat("BUILD  ·  ", this.presentation.BuildTitle)
                : "BUILD";
            this.board.RowCount = 1;
            this.board.RowStyles.Clear();
            this.board.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            if (!this.presentation.HasLiveContext ||
                !this.presentation.HasActiveBuild)
            {
                var empty = this.CreateEmptyState();
                this.board.Controls.Add(empty, 0, 0);
                this.ResizeBoard();
                return;
            }

            SkillBuildEquipmentKind[] kinds =
            [
                SkillBuildEquipmentKind.Weapon,
                SkillBuildEquipmentKind.Shield,
                SkillBuildEquipmentKind.Reactor,
                SkillBuildEquipmentKind.Engine,
                SkillBuildEquipmentKind.Device,
            ];
            string[] headings =
            [
                "Weapons",
                "Shield",
                "Reactor",
                "Engine",
                "Devices",
            ];

            this.board.RowCount = kinds.Length;
            this.board.RowStyles.Clear();
            for (var index = 0; index < kinds.Length; index++)
            {
                this.board.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                this.board.Controls.Add(
                    this.CreateEquipmentGroup(headings[index], kinds[index]),
                    0,
                    index);
            }

            this.ResizeBoard();
        }
        finally
        {
            this.board.ResumeLayout(performLayout: true);
        }
    }

    private void UpdateContentInPlace()
    {
        this.title.Text = this.presentation.HasActiveBuild
            ? string.Concat("BUILD  ·  ", this.presentation.BuildTitle)
            : "BUILD";

        if (!this.presentation.HasLiveContext ||
            !this.presentation.HasActiveBuild ||
            this.equipmentRows.Count != this.presentation.Equipment.Count)
        {
            this.RebuildContent();
            return;
        }

        foreach (var equipment in this.presentation.Equipment)
        {
            if (!this.equipmentRows.TryGetValue(
                    equipment.RequirementId,
                    out var binding))
            {
                this.RebuildContent();
                return;
            }

            binding.Status.Text = FormatStatus(equipment);
            binding.Status.ForeColor = GetAvailabilityColor(
                equipment.Availability);
            this.RegisterEffectiveItemToolTip(equipment, binding.Status);
        }
    }

    private Control CreateEmptyState()
    {
        var panel = new Panel
        {
            Height = EmptyStateHeight,
            Width = 600,
            Margin = Padding.Empty,
            Padding = new Padding(14, 14, 14, 10),
            BackColor = MainWindowTheme.ElevatedPanel,
        };

        var button = new Button
        {
            Dock = DockStyle.Bottom,
            Height = 32,
            Width = 120,
            Text = "Open Builds",
            TabStop = false,
        };
        MainWindowTheme.StyleButton(button, primary: true);
        button.Click += this.OpenBuildsButton_OnClick;

        var message = new Label
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Text = this.presentation.StatusText,
            TextAlign = ContentAlignment.TopLeft,
            Font = MainWindowTheme.CreateBodyFont(9.0f),
            ForeColor = MainWindowTheme.MutedText,
            BackColor = Color.Transparent,
        };

        panel.Controls.Add(message);
        panel.Controls.Add(button);
        return panel;
    }

    private Control CreateEquipmentGroup(
        string headingText,
        SkillBuildEquipmentKind kind)
    {
        var group = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = MainWindowTheme.Background,
        };
        group.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100.0f));
        group.RowStyles.Add(new RowStyle(SizeType.Absolute, GroupHeadingHeight));

        var heading = new Label
        {
            Dock = DockStyle.Fill,
            Height = GroupHeadingHeight,
            Margin = Padding.Empty,
            Padding = new Padding(8, 0, 8, 0),
            Text = headingText,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = MainWindowTheme.CreateHeadingFont(9.0f),
            ForeColor = MainWindowTheme.Text,
            BackColor = MainWindowTheme.Panel,
        };
        group.Controls.Add(heading, 0, 0);

        var rows = this.presentation.Equipment
            .Where(value => value.Kind == kind)
            .OrderBy(value => value.Order)
            .ToArray();
        if (rows.Length == 0)
        {
            group.RowCount++;
            group.RowStyles.Add(new RowStyle(SizeType.Absolute, EmptyGroupHeight));
            group.Controls.Add(CreateEmptyGroupLabel(), 0, 1);
            return group;
        }

        for (var index = 0; index < rows.Length; index++)
        {
            group.RowCount++;
            group.RowStyles.Add(
                new RowStyle(
                    SizeType.Absolute,
                    EquipmentCardHeight + EquipmentCardBottomMargin));
            var card = this.CreateEquipmentCard(rows[index]);
            group.Controls.Add(card, 0, index + 1);
        }

        return group;
    }

    private static Control CreateEmptyGroupLabel() =>
        new Label
        {
            Dock = DockStyle.Fill,
            Height = EmptyGroupHeight,
            Margin = new Padding(0, 0, 0, EquipmentCardBottomMargin),
            Padding = new Padding(9, 0, 8, 0),
            Text = "Not in this build",
            TextAlign = ContentAlignment.MiddleLeft,
            Font = MainWindowTheme.CreateBodyFont(8.5f),
            ForeColor = MainWindowTheme.MutedText,
            BackColor = MainWindowTheme.ElevatedPanel,
        };

    private Control CreateEquipmentCard(
        SkillBuildEquipmentCompanionRow equipment)
    {
        var card = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Height = EquipmentCardHeight,
            ColumnCount = 3,
            RowCount = 2,
            Margin = new Padding(0, 0, 0, EquipmentCardBottomMargin),
            Padding = new Padding(5, 3, 4, 3),
            BackColor = MainWindowTheme.ElevatedPanel,
        };
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34.0f));
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100.0f));
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44.0f));
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 55.0f));
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 45.0f));

        var icon = new PictureBox
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 1, 5, 1),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent,
            Image = equipment.PreferredItemTemplateId > 0
                ? this.GetItemIcon(
                    equipment.PreferredItemTemplateId,
                    new Size(28, 28))
                : null,
        };
        card.Controls.Add(icon, 0, 0);
        card.SetRowSpan(icon, 2);

        var name = new Label
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Text = equipment.PreferredItemName,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = MainWindowTheme.CreateHeadingFont(8.8f),
            ForeColor = MainWindowTheme.Text,
            BackColor = Color.Transparent,
            AutoEllipsis = true,
        };
        card.Controls.Add(name, 1, 0);

        var status = new Label
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Text = FormatStatus(equipment),
            TextAlign = ContentAlignment.MiddleLeft,
            Font = MainWindowTheme.CreateBodyFont(7.9f),
            ForeColor = GetAvailabilityColor(equipment.Availability),
            BackColor = Color.Transparent,
            AutoEllipsis = true,
        };
        card.Controls.Add(status, 1, 1);

        var find = new Button
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(3, 5, 0, 5),
            Text = "Find",
            TabStop = false,
            Cursor = Cursors.Hand,
            Font = MainWindowTheme.CreateHeadingFont(7.6f),
        };
        MainWindowTheme.StyleButton(find);
        find.Click += (_, _) => this.RequestFindItem(equipment);
        card.Controls.Add(find, 2, 0);
        card.SetRowSpan(find, 2);

        this.RegisterItemToolTip(
            equipment.PreferredItemTemplateId,
            equipment.PreferredItemName,
            icon.Image,
            card,
            icon,
            name,
            find);
        this.RegisterEffectiveItemToolTip(equipment, status);

        this.equipmentRows[equipment.RequirementId] =
            new EquipmentRowBinding(status);
        return card;
    }

    private static string FormatStatus(
        SkillBuildEquipmentCompanionRow equipment)
    {
        var location = equipment.Availability switch
        {
            SkillBuildEquipmentAvailability.Equipped => "Equipped",
            SkillBuildEquipmentAvailability.Inventory => "In cargo",
            SkillBuildEquipmentAvailability.Vault => "In vault",
            _ => "Missing",
        };

        if (equipment.UsesAcceptedAlternative)
        {
            return string.Concat(location, " · ", equipment.EffectiveItemName);
        }

        var additionalAlternatives = Math.Max(
            0,
            equipment.Alternatives.Count - 1);
        return additionalAlternatives > 0
            ? string.Create(
                CultureInfo.CurrentCulture,
                $"{location} · +{additionalAlternatives} {(additionalAlternatives == 1 ? "alternative" : "alternatives")}")
            : location;
    }

    private static Color GetAvailabilityColor(
        SkillBuildEquipmentAvailability availability) =>
        availability switch
        {
            SkillBuildEquipmentAvailability.Equipped => MainWindowTheme.Success,
            SkillBuildEquipmentAvailability.Inventory => MainWindowTheme.Warning,
            SkillBuildEquipmentAvailability.Vault => MainWindowTheme.Accent,
            _ => MainWindowTheme.Danger,
        };

    private int CalculateEquipmentBoardHeight() =>
        CalculateGroupHeight(this.CountKind(SkillBuildEquipmentKind.Weapon)) +
        CalculateGroupHeight(this.CountKind(SkillBuildEquipmentKind.Shield)) +
        CalculateGroupHeight(this.CountKind(SkillBuildEquipmentKind.Reactor)) +
        CalculateGroupHeight(this.CountKind(SkillBuildEquipmentKind.Engine)) +
        CalculateGroupHeight(this.CountKind(SkillBuildEquipmentKind.Device)) +
        4;

    private int CountKind(SkillBuildEquipmentKind kind) =>
        this.presentation.Equipment.Count(value => value.Kind == kind);

    private static int CalculateGroupHeight(int count) =>
        GroupHeadingHeight +
        (count == 0
            ? EmptyGroupHeight
            : count * (EquipmentCardHeight + EquipmentCardBottomMargin));

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
                $"[Builds] Could not load Equipment companion item icon {itemTemplateId}: {exception}"));
        }

        this.itemIcons[key] = icon;
        return icon;
    }

    private void RegisterItemToolTip(
        int itemTemplateId,
        string itemName,
        Image? icon,
        params Control[] controls)
    {
        if (itemTemplateId <= 0)
        {
            return;
        }

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
        foreach (var control in controls)
        {
            this.itemToolTip.SetToolTip(
                control,
                content,
                SystemInformation.MouseHoverTime);
        }
    }

    private void RegisterEffectiveItemToolTip(
        SkillBuildEquipmentCompanionRow equipment,
        Control status)
    {
        this.itemToolTip.Remove(status);
        if (!equipment.UsesAcceptedAlternative)
        {
            return;
        }

        this.RegisterItemToolTip(
            equipment.EffectiveItemTemplateId,
            equipment.EffectiveItemName,
            this.GetItemIcon(
                equipment.EffectiveItemTemplateId,
                new Size(30, 30)),
            status);
    }

    private void RequestFindItem(
        SkillBuildEquipmentCompanionRow equipment)
    {
        try
        {
            this.FindItemRequested?.Invoke(
                this,
                new SkillBuildEquipmentFindRequestedEventArgs(
                    equipment.PreferredItemTemplateId,
                    equipment.PreferredItemName));
        }
        catch (Exception exception)
        {
            Debug.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"[Builds] Could not open Galaxy Finder from the Equipment companion: {exception}"));
        }
    }

    private void OpenBuildsButton_OnClick(object? sender, EventArgs e)
    {
        try
        {
            this.OpenBuildsRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            Debug.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"[Builds] Could not open Builds from the Equipment companion: {exception}"));
        }
    }

    private void BoardScroll_OnSizeChanged(object? sender, EventArgs e) =>
        this.ResizeBoard();

    private void ResizeBoard()
    {
        this.board.Width = Math.Max(
            120,
            this.boardScroll.ClientSize.Width -
            (this.boardScroll.VerticalScroll.Visible
                ? SystemInformation.VerticalScrollBarWidth
                : 0));

        foreach (Control child in this.board.Controls)
        {
            child.Width = this.board.Width;
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
        ControlEventArgs e) =>
        this.RegisterDarkScrollbarTheme(e.Control);

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
            control is ScrollableControl { AutoScroll: true })
        {
            _ = NativeMethods.TryApplyDarkControlTheme(control.Handle);
            control.Invalidate();
        }

        foreach (Control child in control.Controls)
        {
            this.ApplyDarkScrollbarTheme(child);
        }
    }

    private sealed record EquipmentRowBinding(Label Status);

    private readonly record struct ItemIconKey(
        int ItemTemplateId,
        int Width,
        int Height);
}

internal sealed class SkillBuildEquipmentFindRequestedEventArgs(
    int itemTemplateId,
    string itemName)
    : EventArgs
{
    public int ItemTemplateId { get; } = itemTemplateId;

    public string ItemName { get; } = itemName;
}
