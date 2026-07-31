// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using System.Drawing.Drawing2D;

internal enum BuildCompanionToggleStyle
{
    SkillsTab,
    EquipmentStack,
}

internal sealed class BuildCompanionToggleForm : Form
{
    private const int WmMouseActivate = 0x0021;
    private const int MaNoActivate = 3;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;

    private static readonly Color transparencyColor =
        Color.FromArgb(255, 0, 255);

    private readonly BuildToggleControl toggle;

    public BuildCompanionToggleForm(
        string text,
        BuildCompanionToggleStyle style = BuildCompanionToggleStyle.SkillsTab)
    {
        this.toggle = new BuildToggleControl(style);
        this.FormBorderStyle = FormBorderStyle.None;
        this.ShowInTaskbar = false;
        this.StartPosition = FormStartPosition.Manual;
        this.AutoScaleMode = AutoScaleMode.None;
        this.BackColor = transparencyColor;
        this.TransparencyKey = transparencyColor;
        this.Padding = Padding.Empty;

        this.toggle.Dock = DockStyle.Fill;
        this.toggle.Text = text;
        this.toggle.ToggleRequested += this.Toggle_OnToggleRequested;
        this.Controls.Add(this.toggle);
    }

    public event EventHandler? ToggleRequested;

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

    public void SetActive(bool active) =>
        this.toggle.SetActive(active);

    public void SetScale(float scale) =>
        this.toggle.SetScale(scale);

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
            this.toggle.ToggleRequested -= this.Toggle_OnToggleRequested;
            this.toggle.Dispose();
        }

        base.Dispose(disposing);
    }

    private void Toggle_OnToggleRequested(object? sender, EventArgs e) =>
        this.ToggleRequested?.Invoke(this, EventArgs.Empty);

    private sealed class BuildToggleControl : Control
    {
        private readonly BuildCompanionToggleStyle style;
        private readonly float baseFontSize;
        private float appliedScale = 1f;
        private bool active;
        private bool hovered;
        private bool pressed;

        public BuildToggleControl(BuildCompanionToggleStyle style)
        {
            this.style = style;
            this.SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.Selectable |
                ControlStyles.UserPaint,
                value: true);
            this.TabStop = false;
            this.BackColor = transparencyColor;
            this.Cursor = Cursors.Hand;
            this.baseFontSize =
                style == BuildCompanionToggleStyle.EquipmentStack
                    ? 13f
                    : 14f;
            this.Font = this.CreateScaledFont(scale: 1f);
            this.ForeColor = Color.FromArgb(255, 255, 0);
            this.AccessibleRole = AccessibleRole.PushButton;
            this.AccessibleName = "Toggle build companion";
        }

        public event EventHandler? ToggleRequested;

        public void SetScale(float scale)
        {
            var nextScale = Math.Clamp(scale, 0.5f, 2f);

            if (Math.Abs(this.appliedScale - nextScale) < 0.01f)
            {
                return;
            }

            var previousFont = this.Font;
            this.appliedScale = nextScale;
            this.Font = this.CreateScaledFont(nextScale);
            previousFont.Dispose();
            this.Invalidate();
        }

        public void SetActive(bool nextActive)
        {
            if (this.active == nextActive)
            {
                return;
            }

            this.active = nextActive;
            this.AccessibleDescription = nextActive
                ? "The build companion is visible."
                : "The build companion is hidden.";
            this.Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            var font = disposing
                ? this.Font
                : null;

            base.Dispose(disposing);
            font?.Dispose();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            this.hovered = true;
            this.Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            this.hovered = false;
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
            var invoke = this.pressed &&
                         e.Button == MouseButtons.Left &&
                         this.ClientRectangle.Contains(e.Location);
            this.pressed = false;
            this.Invalidate();
            if (invoke)
            {
                this.ToggleRequested?.Invoke(this, EventArgs.Empty);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            var bounds = new Rectangle(
                0,
                0,
                Math.Max(1, this.ClientSize.Width - 1),
                Math.Max(1, this.ClientSize.Height - 1));
            e.Graphics.SmoothingMode = SmoothingMode.None;

            var top = this.pressed
                ? Color.FromArgb(49, 44, 94)
                : this.hovered
                    ? Color.FromArgb(103, 94, 165)
                    : this.active
                        ? Color.FromArgb(78, 72, 142)
                        : Color.FromArgb(79, 73, 137);
            var bottom = this.pressed
                ? Color.FromArgb(30, 29, 66)
                : this.hovered
                    ? Color.FromArgb(55, 51, 111)
                    : this.active
                        ? Color.FromArgb(40, 39, 88)
                        : Color.FromArgb(40, 39, 82);
            var textColor =
                this.style == BuildCompanionToggleStyle.EquipmentStack
                    ? Color.FromArgb(255, 255, 0)
                    : this.active
                        ? Color.FromArgb(83, 255, 68)
                        : Color.FromArgb(255, 242, 18);
            var borderColor = this.hovered || this.active
                ? Color.FromArgb(126, 123, 193)
                : Color.FromArgb(82, 88, 145);

            using var path = CreateButtonPath(bounds);
            using var background = new LinearGradientBrush(
                bounds,
                top,
                bottom,
                LinearGradientMode.Vertical);
            using var border = new Pen(borderColor);
            e.Graphics.FillPath(background, path);
            e.Graphics.DrawPath(border, path);

            TextRenderer.DrawText(
                e.Graphics,
                this.Text,
                this.Font,
                bounds,
                textColor,
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPrefix |
                TextFormatFlags.NoPadding |
                TextFormatFlags.SingleLine);
        }

        private Font CreateScaledFont(float scale)
        {
            return new Font(
                "Segoe UI Semibold",
                Math.Clamp(
                    this.baseFontSize * scale,
                    7f,
                    24f),
                FontStyle.Bold,
                GraphicsUnit.Point);
        }

        private static GraphicsPath CreateButtonPath(Rectangle bounds)
        {
            var cut = Math.Min(5, Math.Max(2, bounds.Height / 5));
            var path = new GraphicsPath();
            path.AddPolygon(
            [
                new Point(bounds.Left + cut, bounds.Top),
                new Point(bounds.Right - cut, bounds.Top),
                new Point(bounds.Right, bounds.Top + cut),
                new Point(bounds.Right, bounds.Bottom),
                new Point(bounds.Left, bounds.Bottom),
                new Point(bounds.Left, bounds.Top + cut),
            ]);
            path.CloseFigure();
            return path;
        }
    }
}
