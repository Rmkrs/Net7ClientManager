namespace Net7ClientManager.Forms;

using System.Drawing.Text;

internal sealed class DismantleCooldownOverlayForm : Form
{
    private const int WmNcHitTest = 0x0084;
    private const int WmMouseActivate = 0x0021;
    private const int HtTransparent = -1;
    private const int MaNoActivate = 3;

    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExLayered = 0x00080000;
    private const int WsExNoActivate = 0x08000000;

    private const int BaseCanvasWidth = 1280;
    private const int BaseCanvasHeight = 720;
    private const int BaseCenterX = 640;
    private const int BaseCenterY = 500;
    private const int BaseWidth = 320;
    private const int BaseHeight = 34;
    private const float BaseFontSize = 11.0f;

    private static readonly Color TransparencyColor =
        Color.FromArgb(255, 0, 255);

    private string text = "";
    private bool ready;

    public DismantleCooldownOverlayForm()
    {
        this.FormBorderStyle = FormBorderStyle.None;
        this.ShowInTaskbar = false;
        this.StartPosition = FormStartPosition.Manual;
        this.BackColor = TransparencyColor;
        this.TransparencyKey = TransparencyColor;
        this.AutoScaleMode = AutoScaleMode.None;
        this.DoubleBuffered = true;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |=
                WsExTransparent |
                WsExToolWindow |
                WsExLayered |
                WsExNoActivate;
            return parameters;
        }
    }

    public void SetStatus(string? nextText, bool isReady)
    {
        nextText ??= "";
        if (string.Equals(this.text, nextText, StringComparison.Ordinal) &&
            this.ready == isReady)
        {
            return;
        }

        this.text = nextText;
        this.ready = isReady;
        this.Invalidate();
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmNcHitTest)
        {
            message.Result = new IntPtr(HtTransparent);
            return;
        }

        if (message.Msg == WmMouseActivate)
        {
            message.Result = new IntPtr(MaNoActivate);
            return;
        }

        base.WndProc(ref message);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        if (string.IsNullOrEmpty(this.text) ||
            this.ClientSize.Width <= 0 ||
            this.ClientSize.Height <= 0)
        {
            return;
        }

        var scale = Math.Max(
            0.5f,
            Math.Min(
                this.ClientSize.Width / (float)BaseCanvasWidth,
                this.ClientSize.Height / (float)BaseCanvasHeight));
        var centerX = (int)Math.Round(BaseCenterX * scale);
        var centerY = (int)Math.Round(BaseCenterY * scale);
        var width = Math.Max(180, (int)Math.Round(BaseWidth * scale));
        var height = Math.Max(24, (int)Math.Round(BaseHeight * scale));
        var bounds = new RectangleF(
            centerX - width / 2.0f,
            centerY - height / 2.0f,
            width,
            height);

        using var font = new Font(
            "Segoe UI Semibold",
            Math.Clamp(BaseFontSize * scale, 8.0f, 16.0f),
            FontStyle.Bold,
            GraphicsUnit.Point);
        using var shadowBrush = new SolidBrush(Color.Black);
        using var textBrush = new SolidBrush(
            this.ready ? Color.PaleGreen : Color.White);
        using var textFormat = new StringFormat(
            StringFormat.GenericTypographic)
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags =
                StringFormatFlags.NoWrap |
                StringFormatFlags.NoClip,
        };

        // TransparencyKey and ClearType produce colored fringe pixels.
        e.Graphics.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;

        var shadowBounds = bounds;
        var shadowOffset = Math.Max(1.0f, (float)Math.Round(scale));
        shadowBounds.Offset(shadowOffset, shadowOffset);
        e.Graphics.DrawString(
            this.text,
            font,
            shadowBrush,
            shadowBounds,
            textFormat);
        e.Graphics.DrawString(
            this.text,
            font,
            textBrush,
            bounds,
            textFormat);
    }
}
