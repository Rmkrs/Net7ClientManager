// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using Net7ClientManager.Navigation;

internal sealed class JobTerminalRouteControlForm : Form
{
    private const int WmMouseActivate = 0x0021;
    private const int MaNoActivate = 3;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const string SetDestinationCaption = "Set Destination";

    private static readonly Color transparencyColor =
        Color.FromArgb(255, 0, 255);
    private static readonly Color labelColor =
        Color.White;
    private static readonly Color valueColor =
        Color.FromArgb(255, 255, 0);
    private static readonly Color buttonBackground =
        Color.FromArgb(50, 45, 108);
    private static readonly Color buttonHover =
        Color.FromArgb(118, 96, 194);
    private static readonly Color buttonPressed =
        Color.FromArgb(38, 35, 84);
    private static readonly Color buttonBorder =
        Color.FromArgb(143, 126, 231);
    private static readonly Color disabledButtonBackground =
        Color.FromArgb(31, 29, 66);
    private static readonly Color disabledButtonBorder =
        Color.FromArgb(85, 77, 139);
    private static readonly Color disabledButtonText =
        Color.FromArgb(151, 145, 111);

    private readonly Button setDestinationButton = new();
    private readonly ToolTip toolTip = new();

    private Font? informationFont;
    private Font? buttonFont;
    private JobTerminalRoutePresentation presentation =
        JobTerminalRoutePresentation.Hidden;
    private int informationRowHeight;
    private int informationTop;

    public JobTerminalRouteControlForm()
    {
        this.FormBorderStyle = FormBorderStyle.None;
        this.ShowInTaskbar = false;
        this.StartPosition = FormStartPosition.Manual;
        this.AutoScaleMode = AutoScaleMode.None;
        this.BackColor = transparencyColor;
        this.TransparencyKey = transparencyColor;
        this.DoubleBuffered = true;

        this.setDestinationButton.Text = string.Empty;
        this.setDestinationButton.AccessibleName =
            SetDestinationCaption;
        this.setDestinationButton.TabStop = false;
        this.setDestinationButton.FlatStyle = FlatStyle.Flat;
        this.setDestinationButton.UseVisualStyleBackColor = false;
        this.setDestinationButton.BackColor = buttonBackground;
        this.setDestinationButton.ForeColor = valueColor;
        this.setDestinationButton.FlatAppearance.BorderColor = buttonBorder;
        this.setDestinationButton.FlatAppearance.BorderSize = 1;
        this.setDestinationButton.FlatAppearance.MouseOverBackColor =
            buttonHover;
        this.setDestinationButton.FlatAppearance.MouseDownBackColor =
            buttonPressed;
        this.setDestinationButton.TextAlign =
            ContentAlignment.MiddleCenter;
        this.setDestinationButton.Padding = Padding.Empty;
        this.setDestinationButton.Cursor = Cursors.Hand;
        this.setDestinationButton.Click +=
            this.SetDestinationButton_OnClick;
        this.setDestinationButton.EnabledChanged +=
            this.SetDestinationButton_OnEnabledChanged;
        this.setDestinationButton.Paint +=
            this.SetDestinationButton_OnPaint;

        this.Controls.Add(this.setDestinationButton);
    }

    public event Action<NavigationDestination>? SetDestinationRequested;

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
        JobTerminalRoutePresentation nextPresentation)
    {
        ArgumentNullException.ThrowIfNull(nextPresentation);

        this.presentation = nextPresentation;
        this.setDestinationButton.Enabled =
            nextPresentation.CanSetDestination;

        var tooltipText = string.IsNullOrWhiteSpace(
                nextPresentation.StatusText)
            ? nextPresentation.DestinationName
            : nextPresentation.StatusText;

        this.toolTip.SetToolTip(this, tooltipText);
        this.toolTip.SetToolTip(
            this.setDestinationButton,
            tooltipText);

        this.Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);

        this.informationRowHeight = Math.Clamp(
            (int)Math.Round(this.ClientSize.Height * 0.19),
            18,
            27);

        var buttonHeight = Math.Clamp(
            (int)Math.Round(this.ClientSize.Height * 0.18),
            20,
            26);
        var bottomPadding = Math.Clamp(
            (int)Math.Round(this.ClientSize.Height * 0.04),
            4,
            8);
        var blockHeight =
            (this.informationRowHeight * 3) + buttonHeight;

        this.informationTop = Math.Max(
            0,
            this.ClientSize.Height - bottomPadding - blockHeight);

        var hopsRowTop =
            this.informationTop + (this.informationRowHeight * 3);
        var buttonVerticalOffset = Math.Clamp(
            (int)Math.Round(this.ClientSize.Height * 0.02),
            2,
            3);
        var buttonTop = hopsRowTop + buttonVerticalOffset;
        var buttonWidth = Math.Clamp(
            (int)Math.Round(this.ClientSize.Width * 0.60),
            112,
            Math.Max(112, this.ClientSize.Width - 72));

        this.setDestinationButton.Bounds = new Rectangle(
            Math.Max(0, this.ClientSize.Width - buttonWidth),
            buttonTop,
            buttonWidth,
            buttonHeight);

        var informationFontSize = Math.Clamp(
            this.informationRowHeight * 0.84f,
            15.5f,
            20.0f);
        if (this.informationFont == null ||
            Math.Abs(this.informationFont.Size - informationFontSize) > 0.05f)
        {
            this.informationFont?.Dispose();
            this.informationFont = new Font(
                "Segoe UI Semibold",
                informationFontSize,
                FontStyle.Bold,
                GraphicsUnit.Pixel);
        }

        var buttonFontSize = Math.Clamp(
            buttonHeight * 0.62f,
            13.0f,
            16.5f);
        if (this.buttonFont == null ||
            Math.Abs(this.buttonFont.Size - buttonFontSize) > 0.05f)
        {
            this.buttonFont?.Dispose();
            this.buttonFont = new Font(
                "Segoe UI Semibold",
                buttonFontSize,
                FontStyle.Bold,
                GraphicsUnit.Pixel);
            this.setDestinationButton.Font = this.buttonFont;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        if (this.informationFont == null ||
            this.informationRowHeight <= 0)
        {
            return;
        }

        var labelColumnWidth = (int)Math.Ceiling(
            e.Graphics.MeasureString(
                "Location:",
                this.informationFont,
                int.MaxValue,
                StringFormat.GenericTypographic).Width) + 7;

        var destination = this.presentation.Destination;
        var systemName = FirstNonEmpty(
            destination?.SystemName,
            "—");
        var sectorName = FirstNonEmpty(
            destination?.SectorName,
            "—");
        var locationName = ResolveLocationName(destination);

        this.DrawInformationRow(
            e.Graphics,
            "System:",
            systemName,
            this.GetInformationRowBounds(0),
            labelColumnWidth);
        this.DrawInformationRow(
            e.Graphics,
            "Sector:",
            sectorName,
            this.GetInformationRowBounds(1),
            labelColumnWidth);
        this.DrawInformationRow(
            e.Graphics,
            "Location:",
            locationName,
            this.GetInformationRowBounds(2),
            labelColumnWidth);

        var hopsTop =
            this.informationTop + (this.informationRowHeight * 3);
        var hopsBounds = new Rectangle(
            0,
            hopsTop,
            Math.Max(1, this.setDestinationButton.Left - 8),
            this.informationRowHeight);
        this.DrawInformationRow(
            e.Graphics,
            "Hops:",
            this.presentation.HopCount?.ToString() ?? "—",
            hopsBounds,
            labelColumnWidth);
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
            this.setDestinationButton.Click -=
                this.SetDestinationButton_OnClick;
            this.setDestinationButton.EnabledChanged -=
                this.SetDestinationButton_OnEnabledChanged;
            this.setDestinationButton.Paint -=
                this.SetDestinationButton_OnPaint;
            this.setDestinationButton.Dispose();
            this.informationFont?.Dispose();
            this.informationFont = null;
            this.buttonFont?.Dispose();
            this.buttonFont = null;
            this.toolTip.Dispose();
        }

        base.Dispose(disposing);
    }

    private Rectangle GetInformationRowBounds(int rowIndex)
    {
        var top = this.informationTop +
                  (this.informationRowHeight * rowIndex);
        return new Rectangle(
            0,
            top,
            Math.Max(1, this.ClientSize.Width),
            this.informationRowHeight);
    }

    private static string ResolveLocationName(
        NavigationDestination? destination)
    {
        if (destination is
            {
                Kind: NavigationDestinationKind.Target,
                TargetName: { Length: > 0 },
            })
        {
            return destination.TargetName.Trim();
        }

        return FirstNonEmpty(
            destination?.SectorName,
            destination?.SystemName,
            "—");
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return "—";
    }

    private void DrawInformationRow(
        Graphics graphics,
        string label,
        string value,
        Rectangle bounds,
        int labelColumnWidth)
    {
        if (this.informationFont == null ||
            bounds.Width <= 0 ||
            bounds.Height <= 0)
        {
            return;
        }

        var previousTextRenderingHint = graphics.TextRenderingHint;
        graphics.TextRenderingHint =
            System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;

        try
        {
            using var labelBrush = new SolidBrush(labelColor);
            using var valueBrush = new SolidBrush(valueColor);
            using var format = new StringFormat(
                StringFormat.GenericTypographic)
            {
                Alignment = StringAlignment.Near,
                LineAlignment = StringAlignment.Center,
                FormatFlags = StringFormatFlags.NoWrap,
                Trimming = StringTrimming.EllipsisCharacter,
            };

            var valueLeft = Math.Min(
                bounds.Right,
                bounds.Left + labelColumnWidth);

            graphics.DrawString(
                label,
                this.informationFont,
                labelBrush,
                new RectangleF(
                    bounds.Left,
                    bounds.Top,
                    Math.Max(1, labelColumnWidth - 2),
                    bounds.Height),
                format);
            graphics.DrawString(
                value,
                this.informationFont,
                valueBrush,
                new RectangleF(
                    valueLeft,
                    bounds.Top,
                    Math.Max(1, bounds.Right - valueLeft),
                    bounds.Height),
                format);
        }
        finally
        {
            graphics.TextRenderingHint = previousTextRenderingHint;
        }
    }

    private void SetDestinationButton_OnClick(
        object? sender,
        EventArgs e)
    {
        if (this.presentation.Destination == null)
        {
            return;
        }

        this.SetDestinationRequested?.Invoke(
            this.presentation.Destination);
    }

    private void SetDestinationButton_OnEnabledChanged(
        object? sender,
        EventArgs e)
    {
        this.setDestinationButton.Cursor =
            this.setDestinationButton.Enabled
                ? Cursors.Hand
                : Cursors.Default;
        this.setDestinationButton.Invalidate();
    }

    private void SetDestinationButton_OnPaint(
        object? sender,
        PaintEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        var bounds = button.ClientRectangle;
        if (!button.Enabled)
        {
            using var backgroundBrush = new SolidBrush(
                disabledButtonBackground);
            using var borderPen = new Pen(disabledButtonBorder);

            e.Graphics.FillRectangle(backgroundBrush, bounds);
            e.Graphics.DrawRectangle(
                borderPen,
                bounds.Left,
                bounds.Top,
                Math.Max(0, bounds.Width - 1),
                Math.Max(0, bounds.Height - 1));
        }

        var textBounds = bounds;
        textBounds.Inflate(0, 1);
        textBounds.Offset(0, -1);

        TextRenderer.DrawText(
            e.Graphics,
            SetDestinationCaption,
            button.Font,
            textBounds,
            button.Enabled ? valueColor : disabledButtonText,
            TextFormatFlags.NoPadding |
            TextFormatFlags.SingleLine |
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter);
    }
}
