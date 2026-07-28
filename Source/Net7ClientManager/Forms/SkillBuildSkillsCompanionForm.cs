// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Globalization;
using Net7ClientManager.SkillPlanning;
using Net7ClientManager.Win32;

internal sealed class SkillBuildSkillsCompanionForm : Form
{
    private const int WmMouseActivate = 0x0021;
    private const int MaNoActivate = 3;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;

    private const int TitleHeight = 30;
    private const int LegendHeight = 26;
    private const int SkillRowHeight = 32;
    private const int SkillRowBottomMargin = 1;
    private const int EmptyStateHeight = 120;

    private readonly Panel content = new();
    private readonly Label title = new();
    private readonly FlowLayoutPanel skillList = new();
    private readonly ActionToolTip skillToolTip = new();
    private readonly Dictionary<int, SkillRowBinding> skillRows = [];
    private readonly HashSet<Control> darkThemeControls = [];

    private SkillBuildSkillsCompanionPresentation presentation =
        SkillBuildSkillsCompanionPresentation.Hidden;
    private string fingerprint = "";
    private string structureFingerprint = "";

    public SkillBuildSkillsCompanionForm()
    {
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

        this.skillList.Dock = DockStyle.Fill;
        this.skillList.AutoScroll = true;
        this.skillList.FlowDirection = FlowDirection.TopDown;
        this.skillList.WrapContents = false;
        this.skillList.Margin = Padding.Empty;
        this.skillList.Padding = Padding.Empty;
        this.skillList.BackColor = MainWindowTheme.Background;
        this.skillList.SizeChanged += this.SkillList_OnSizeChanged;

        this.content.Controls.Add(this.skillList);
        this.content.Controls.Add(legend);
        this.content.Controls.Add(this.title);
        this.Controls.Add(this.content);
        this.RegisterDarkScrollbarTheme(this);
    }

    public event EventHandler? OpenBuildsRequested;

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
        SkillBuildSkillsCompanionPresentation nextPresentation)
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
                $"[Builds] Could not refresh the Skills companion: {exception}"));
            this.Hide();
        }
    }

    public int GetPreferredHeight(int maximumHeight)
    {
        maximumHeight = Math.Max(1, maximumHeight);

        var bodyHeight =
            this.presentation.HasLiveContext &&
            this.presentation.HasActiveBuild
                ? this.presentation.Skills.Count *
                  (SkillRowHeight + SkillRowBottomMargin)
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
            this.skillList.SizeChanged -= this.SkillList_OnSizeChanged;
            this.skillToolTip.Dispose();
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
            this.skillRows.Clear();
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
        legend.Controls.Add(CreateLegendLabel("Skills  ·  ", MainWindowTheme.Text));
        legend.Controls.Add(CreateLegendLabel("gold trained", MainWindowTheme.Warning));
        legend.Controls.Add(CreateLegendLabel(", ", MainWindowTheme.Text));
        legend.Controls.Add(CreateLegendLabel("blue missing", MainWindowTheme.Accent));
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
            Font = MainWindowTheme.CreateHeadingFont(9.0f),
            ForeColor = color,
            BackColor = Color.Transparent,
        };

    private void RebuildContent()
    {
        this.skillToolTip.RemoveAll();
        this.skillRows.Clear();
        this.skillList.SuspendLayout();
        try
        {
            var previousControls =
                this.skillList.Controls.Cast<Control>().ToArray();
            this.skillList.Controls.Clear();
            foreach (var control in previousControls)
            {
                control.Dispose();
            }

            this.title.Text = this.presentation.HasActiveBuild
                ? string.Concat("BUILD  ·  ", this.presentation.BuildTitle)
                : "BUILD";

            if (!this.presentation.HasLiveContext ||
                !this.presentation.HasActiveBuild)
            {
                this.skillList.Controls.Add(this.CreateEmptyState());
                this.ResizeSkillRows();
                return;
            }

            foreach (var skill in this.presentation.Skills)
            {
                var row = this.CreateSkillRow(skill);
                this.skillRows[skill.SkillId] = row;
                this.skillList.Controls.Add(row.Row);
            }

            this.ResizeSkillRows();
        }
        finally
        {
            this.skillList.ResumeLayout(performLayout: true);
        }
    }

    private void UpdateContentInPlace()
    {
        this.title.Text = this.presentation.HasActiveBuild
            ? string.Concat("BUILD  ·  ", this.presentation.BuildTitle)
            : "BUILD";

        if (!this.presentation.HasLiveContext ||
            !this.presentation.HasActiveBuild ||
            this.skillRows.Count != this.presentation.Skills.Count)
        {
            this.RebuildContent();
            return;
        }

        foreach (var skill in this.presentation.Skills)
        {
            if (!this.skillRows.TryGetValue(skill.SkillId, out var row))
            {
                this.RebuildContent();
                return;
            }

            row.Pips.UpdateState(skill.CurrentRank, skill.TargetRank);
        }
    }

    private Control CreateEmptyState()
    {
        var panel = new Panel
        {
            Height = EmptyStateHeight,
            Width = 280,
            Margin = Padding.Empty,
            Padding = new Padding(12, 12, 12, 8),
            BackColor = MainWindowTheme.ElevatedPanel,
        };

        var button = new Button
        {
            Dock = DockStyle.Bottom,
            Height = 32,
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

    private SkillRowBinding CreateSkillRow(
        SkillBuildSkillsCompanionRow skill)
    {
        var row = new TableLayoutPanel
        {
            Height = SkillRowHeight,
            Width = 280,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, SkillRowBottomMargin),
            Padding = new Padding(10, 2, 8, 2),
            BackColor = MainWindowTheme.ElevatedPanel,
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100.0f));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 198.0f));
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100.0f));

        var name = new Label
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Text = skill.SkillName,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = MainWindowTheme.CreateHeadingFont(10.0f),
            ForeColor = MainWindowTheme.Text,
            BackColor = Color.Transparent,
            AutoEllipsis = true,
        };
        var pips = new CompanionPipsControl(
            skill.CurrentRank,
            skill.TargetRank,
            skill.MaximumRank)
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(3, 0, 0, 0),
        };

        row.Controls.Add(name, 0, 0);
        row.Controls.Add(pips, 1, 0);
        this.RegisterSkillToolTip(skill, row, name, pips);
        return new SkillRowBinding(row, pips);
    }

    private void RegisterSkillToolTip(
        SkillBuildSkillsCompanionRow skill,
        params Control[] controls)
    {
        if (string.IsNullOrWhiteSpace(skill.Description))
        {
            return;
        }

        var content = new ActionToolTipContent(
            [
                new ActionToolTipParagraph(
                    [
                        new(
                            skill.SkillName,
                            ActionToolTipTextRole.Accent,
                            Bold: true),
                    ],
                    ActionToolTipParagraphStyle.Header,
                    SpaceAfter: 7),
                new ActionToolTipParagraph(
                    [new(skill.Description.Trim())]),
            ],
            Icon: null);

        foreach (var control in controls)
        {
            this.skillToolTip.SetToolTip(
                control,
                content,
                SystemInformation.MouseHoverTime);
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
                $"[Builds] Could not open Builds from the Skills companion: {exception}"));
        }
    }

    private void SkillList_OnSizeChanged(object? sender, EventArgs e) =>
        this.ResizeSkillRows();

    private void ResizeSkillRows()
    {
        var width = Math.Max(
            120,
            this.skillList.ClientSize.Width -
            (this.skillList.VerticalScroll.Visible
                ? SystemInformation.VerticalScrollBarWidth
                : 0));

        foreach (Control child in this.skillList.Controls)
        {
            child.Width = width;
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

    private sealed record SkillRowBinding(
        Control Row,
        CompanionPipsControl Pips);

    private sealed class CompanionPipsControl : Control
    {
        private const int PreferredDiameter = 17;
        private const int PreferredGap = 5;
        private const int MinimumDiameter = 13;
        private const int MinimumGap = 2;

        private int currentRank;
        private int targetRank;
        private readonly int maximumRank;

        public CompanionPipsControl(
            int currentRank,
            int targetRank,
            int maximumRank)
        {
            this.currentRank = Math.Max(0, currentRank);
            this.targetRank = Math.Max(0, targetRank);
            this.maximumRank = Math.Max(0, maximumRank);
            this.SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor |
                ControlStyles.UserPaint,
                value: true);
            this.TabStop = false;
            this.BackColor = Color.Transparent;
            this.Cursor = Cursors.Default;
            this.UpdateAccessibleName();
        }

        public void UpdateState(int nextCurrentRank, int nextTargetRank)
        {
            nextCurrentRank = Math.Max(0, nextCurrentRank);
            nextTargetRank = Math.Max(0, nextTargetRank);
            if (this.currentRank == nextCurrentRank &&
                this.targetRank == nextTargetRank)
            {
                return;
            }

            this.currentRank = nextCurrentRank;
            this.targetRank = nextTargetRank;
            this.UpdateAccessibleName();
            this.Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            var previousSmoothingMode = e.Graphics.SmoothingMode;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var (diameter, gap) = this.CalculateLayout();
            var y = Math.Max(0, (this.ClientSize.Height - diameter) / 2);

            for (var index = 0; index < this.maximumRank; index++)
            {
                var trained = index < this.currentRank;
                var missing = !trained && index < this.targetRank;
                var bounds = new Rectangle(
                    index * (diameter + gap),
                    y,
                    diameter,
                    diameter);
                PaintPip(
                    e.Graphics,
                    bounds,
                    trained
                        ? Color.FromArgb(194, 151, 45)
                        : missing
                            ? Color.FromArgb(28, 126, 157)
                            : Color.FromArgb(5, 9, 14),
                    trained
                        ? Color.FromArgb(238, 206, 99)
                        : missing
                            ? MainWindowTheme.Accent
                            : Color.FromArgb(82, 101, 117),
                    trained);
            }

            e.Graphics.SmoothingMode = previousSmoothingMode;
        }

        private (int Diameter, int Gap) CalculateLayout()
        {
            var diameter = PreferredDiameter;
            var gap = PreferredGap;
            var gapCount = Math.Max(0, this.maximumRank - 1);
            var availableWidth = Math.Max(0, this.ClientSize.Width - 1);

            if ((this.maximumRank * diameter) +
                (gapCount * gap) <= availableWidth)
            {
                return (diameter, gap);
            }

            if (gapCount > 0)
            {
                gap = Math.Max(
                    MinimumGap,
                    (availableWidth -
                     (this.maximumRank * diameter)) /
                    gapCount);
            }

            if ((this.maximumRank * diameter) +
                (gapCount * gap) <= availableWidth)
            {
                return (diameter, gap);
            }

            diameter = Math.Max(
                MinimumDiameter,
                (availableWidth -
                 (gapCount * MinimumGap)) /
                Math.Max(1, this.maximumRank));
            gap = gapCount > 0
                ? Math.Max(
                    0,
                    (availableWidth -
                     (this.maximumRank * diameter)) /
                    gapCount)
                : 0;
            return (diameter, gap);
        }

        private static void PaintPip(
            Graphics graphics,
            Rectangle bounds,
            Color fillColor,
            Color borderColor,
            bool highlight)
        {
            using var shadowBrush = new SolidBrush(
                Color.FromArgb(150, 0, 0, 0));
            graphics.FillEllipse(
                shadowBrush,
                bounds.X + 1,
                bounds.Y + 2,
                bounds.Width,
                bounds.Height);

            using var fillBrush = new SolidBrush(fillColor);
            using var borderPen = new Pen(borderColor);
            graphics.FillEllipse(fillBrush, bounds);
            graphics.DrawEllipse(borderPen, bounds);

            if (!highlight)
            {
                return;
            }

            using var highlightBrush = new SolidBrush(
                Color.FromArgb(210, 255, 235, 151));
            graphics.FillEllipse(
                highlightBrush,
                bounds.X + 3,
                bounds.Y + 2,
                Math.Max(4, bounds.Width / 3),
                Math.Max(3, bounds.Height / 4));
        }

        private void UpdateAccessibleName()
        {
            this.AccessibleName = string.Create(
                CultureInfo.InvariantCulture,
                $"Current rank {this.currentRank}, target rank {this.targetRank} of {this.maximumRank}");
        }
    }
}
