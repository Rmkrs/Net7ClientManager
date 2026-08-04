namespace Net7ClientManager.Forms;

using System.Drawing.Text;
using System.Globalization;
using Net7ClientManager.Models;

internal sealed class BuffDurationOverlayForm : Form
{
    private const int WmNcHitTest = 0x0084;
    private const int WmMouseActivate = 0x0021;
    private const int HtTransparent = -1;
    private const int MaNoActivate = 3;

    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExLayered = 0x00080000;
    private const int WsExNoActivate = 0x08000000;

    // Confirmed game-space buff click points. The visible icon artwork sits
    // a few pixels to the right of that point, as measured from the live
    // 1280x720 client. Slots grow right-to-left in rows of four.
    private const int BaseCanvasWidth = 1280;
    private const int BaseCanvasHeight = 720;
    private const int FirstSlotClickX = 1227;
    private const int FirstSlotCenterY = 36;
    private const int VisibleIconCenterOffsetX = 3;
    private const int ColumnSpacing = 49;
    // The visible buff frames advance by 35 base pixels. The recorded
    // click targets use a 36-pixel stride, but using that for rendered
    // content accumulates a visible downward drift on lower rows.
    private const int RowSpacing = 35;
    private const int BuffIconBaseWidth = 48;
    private const int BuffIconBaseHeight = 36;
    private const int DurationBaseWidth = 52;
    private const int DurationBaseHeight = 14;
    private const int DurationBaseOffsetY = 5;
    private const float DurationFontBaseSize = 8.0f;
    private const float PermanentDurationFontBaseSize = 12.0f;

    // Normal observation noise can move the reconstructed deadline by roughly
    // one second. Only a larger change represents a real refresh/reapplication
    // or a meaningful correction.
    private const long DeadlineCorrectionThresholdMilliseconds = 5000;

    private static readonly Color TransparencyColor =
        Color.FromArgb(255, 0, 255);

    private readonly BuffToolTipForm toolTip = new();
    private readonly System.Windows.Forms.Timer refreshTimer = new()
    {
        Interval = 100,
    };
    private readonly Dictionary<int, BuffCountdownState> countdowns = [];

    private GameBuffOverlayPresentation presentation =
        GameBuffOverlayPresentation.Empty;
    private int? hoveredSlot;
    private bool toolTipVisible;
    private string lastRenderSignature = "";

    public BuffDurationOverlayForm()
    {
        this.FormBorderStyle = FormBorderStyle.None;
        this.ShowInTaskbar = false;
        this.StartPosition = FormStartPosition.Manual;
        this.BackColor = TransparencyColor;
        this.TransparencyKey = TransparencyColor;
        this.AutoScaleMode = AutoScaleMode.None;
        this.DoubleBuffered = true;

        this.refreshTimer.Tick += this.RefreshTimer_OnTick;
        this.refreshTimer.Start();
        this.VisibleChanged += this.BuffDurationOverlayForm_OnVisibleChanged;
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

    public void SetPresentation(
        GameBuffOverlayPresentation nextPresentation)
    {
        ArgumentNullException.ThrowIfNull(nextPresentation);

        this.UpdateCountdowns(nextPresentation);
        this.presentation = nextPresentation;

        if (this.hoveredSlot.HasValue &&
            nextPresentation.Buffs.All(buff =>
                buff.Slot != this.hoveredSlot.Value))
        {
            this.ResetHover();
        }

        this.lastRenderSignature = "";
        this.Invalidate();
    }

    public void HideToolTip()
    {
        this.ResetHover();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.VisibleChanged -=
                this.BuffDurationOverlayForm_OnVisibleChanged;
            this.refreshTimer.Stop();
            this.refreshTimer.Tick -= this.RefreshTimer_OnTick;
            this.refreshTimer.Dispose();
            this.toolTip.Dispose();
        }

        base.Dispose(disposing);
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

        if (this.presentation.Buffs.Count == 0 ||
            this.ClientSize.Width <= 0 ||
            this.ClientSize.Height <= 0)
        {
            return;
        }

        var now = ResolveSharedDisplayTime(DateTimeOffset.UtcNow);
        var scale = Math.Max(
            0.5f,
            Math.Min(
                this.ClientSize.Width / (float)BaseCanvasWidth,
                this.ClientSize.Height / (float)BaseCanvasHeight));
        using var durationFont = new Font(
            "Segoe UI Semibold",
            Math.Clamp(
                DurationFontBaseSize * scale,
                6.0f,
                12.0f),
            FontStyle.Bold,
            GraphicsUnit.Point);
        using var permanentDurationFont = new Font(
            "Segoe UI Symbol",
            Math.Clamp(
                PermanentDurationFontBaseSize * scale,
                9.0f,
                18.0f),
            FontStyle.Bold,
            GraphicsUnit.Point);
        using var shadowBrush = new SolidBrush(Color.Black);
        using var textBrush = new SolidBrush(Color.White);
        using var textFormat = new StringFormat(
            StringFormat.GenericTypographic)
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags =
                StringFormatFlags.NoWrap |
                StringFormatFlags.NoClip,
        };

        // TransparencyKey and ClearType do not mix: the antialiased edge
        // pixels blend with the magenta key and become visible purple
        // fringes. Render a crisp one-bit glyph with a restrained drop shadow
        // instead of surrounding every digit with a heavy outline.
        e.Graphics.TextRenderingHint =
            TextRenderingHint.SingleBitPerPixelGridFit;

        foreach (var buff in this.presentation.Buffs)
        {
            var bounds = this.ResolveDurationBounds(buff.Slot);

            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                continue;
            }

            var text = this.FormatDuration(buff, now);
            var font = buff.IsPermanent == true
                ? permanentDurationFont
                : durationFont;
            var layout = new RectangleF(
                bounds.X,
                bounds.Y,
                bounds.Width,
                bounds.Height);
            var shadowOffset = Math.Max(
                1.0f,
                (float)Math.Round(scale));

            var shadowLayout = layout;
            shadowLayout.Offset(shadowOffset, shadowOffset);
            e.Graphics.DrawString(
                text,
                font,
                shadowBrush,
                shadowLayout,
                textFormat);

            e.Graphics.DrawString(
                text,
                font,
                textBrush,
                layout,
                textFormat);
        }
    }

    private void RefreshTimer_OnTick(object? sender, EventArgs e)
    {
        if (this.IsDisposed ||
            this.Disposing ||
            !this.Visible ||
            this.presentation.Buffs.Count == 0)
        {
            this.ResetHover();
            return;
        }

        var now = ResolveSharedDisplayTime(DateTimeOffset.UtcNow);
        var renderSignature = this.BuildRenderSignature(now);

        if (!string.Equals(
                renderSignature,
                this.lastRenderSignature,
                StringComparison.Ordinal))
        {
            this.lastRenderSignature = renderSignature;
            this.Invalidate();
        }

        var cursorClientPoint = this.PointToClient(Cursor.Position);
        var hovered = this.presentation.Buffs
            .LastOrDefault(buff =>
                this.ResolveBuffIconBounds(buff.Slot)
                    .Contains(cursorClientPoint));
        var nextSlot = hovered?.Slot;

        if (nextSlot != this.hoveredSlot)
        {
            this.hoveredSlot = nextSlot;
            this.toolTipVisible = false;
            this.toolTip.HideContent();
        }

        if (hovered == null)
        {
            return;
        }

        this.toolTipVisible = true;
        var owner = this.Owner ?? this;
        this.toolTip.ShowContent(
            owner,
            string.Create(
                CultureInfo.InvariantCulture,
                $"buff:{hovered.Slot}:{hovered.Identity}"),
            hovered.Name,
            hovered.Description,
            Cursor.Position,
            this.RectangleToScreen(this.ClientRectangle));
    }

    private void BuffDurationOverlayForm_OnVisibleChanged(
        object? sender,
        EventArgs e)
    {
        if (!this.Visible)
        {
            this.ResetHover();
        }
    }

    private void ResetHover()
    {
        this.hoveredSlot = null;
        this.toolTipVisible = false;
        this.toolTip.HideContent();
    }

    private void UpdateCountdowns(
        GameBuffOverlayPresentation nextPresentation)
    {
        var now = DateTimeOffset.UtcNow;
        var observedAt = nextPresentation.ObservedAt == default
            ? now
            : nextPresentation.ObservedAt;
        var activeSlots = nextPresentation.Buffs
            .Select(buff => buff.Slot)
            .ToHashSet();

        foreach (var staleSlot in this.countdowns.Keys
                     .Where(slot => !activeSlots.Contains(slot))
                     .ToArray())
        {
            this.countdowns.Remove(staleSlot);
        }

        foreach (var buff in nextPresentation.Buffs)
        {
            if (buff.IsPermanent == true ||
                !buff.RemainingMilliseconds.HasValue)
            {
                this.countdowns.Remove(buff.Slot);
                continue;
            }

            var candidateExpiresAt = observedAt.AddMilliseconds(
                Math.Max(0L, buff.RemainingMilliseconds.Value));

            if (!this.countdowns.TryGetValue(
                    buff.Slot,
                    out var current) ||
                !string.Equals(
                    current.Identity,
                    buff.Identity,
                    StringComparison.Ordinal))
            {
                this.countdowns[buff.Slot] = new BuffCountdownState(
                    buff.Identity,
                    buff.RemovalAtClientTime,
                    candidateExpiresAt);
                continue;
            }

            var authoritativeDeadlineChanged =
                buff.RemovalAtClientTime.HasValue &&
                buff.RemovalAtClientTime != current.RemovalAtClientTime;
            var gainedAuthoritativeDeadline =
                buff.RemovalAtClientTime.HasValue &&
                !current.RemovalAtClientTime.HasValue;
            var fallbackCorrection =
                !buff.RemovalAtClientTime.HasValue &&
                !current.RemovalAtClientTime.HasValue
                    ? (candidateExpiresAt - current.ExpiresAt)
                        .TotalMilliseconds
                    : 0;

            if (authoritativeDeadlineChanged ||
                gainedAuthoritativeDeadline ||
                fallbackCorrection <=
                -DeadlineCorrectionThresholdMilliseconds)
            {
                this.countdowns[buff.Slot] = current with
                {
                    RemovalAtClientTime = buff.RemovalAtClientTime,
                    ExpiresAt = candidateExpiresAt,
                };
            }
        }
    }

    private Rectangle ResolveDurationBounds(int slot)
    {
        var row = slot / 4;
        var column = slot % 4;
        var scaleX = this.ClientSize.Width /
            (float)BaseCanvasWidth;
        var scaleY = this.ClientSize.Height /
            (float)BaseCanvasHeight;
        var centerX = ScaleCoordinate(
            FirstSlotClickX +
            VisibleIconCenterOffsetX -
            (column * ColumnSpacing),
            scaleX);
        var top = ScaleCoordinate(
            FirstSlotCenterY +
            (row * RowSpacing) +
            DurationBaseOffsetY,
            scaleY);
        var width = Math.Max(
            24,
            ScaleCoordinate(DurationBaseWidth, scaleX));
        var height = Math.Max(
            10,
            ScaleCoordinate(DurationBaseHeight, scaleY));

        return new Rectangle(
            centerX - (width / 2),
            top,
            width,
            height);
    }

    private Rectangle ResolveBuffIconBounds(int slot)
    {
        var row = slot / 4;
        var column = slot % 4;
        var scaleX = this.ClientSize.Width /
            (float)BaseCanvasWidth;
        var scaleY = this.ClientSize.Height /
            (float)BaseCanvasHeight;
        var centerX = ScaleCoordinate(
            FirstSlotClickX +
            VisibleIconCenterOffsetX -
            (column * ColumnSpacing),
            scaleX);
        var centerY = ScaleCoordinate(
            FirstSlotCenterY +
            (row * RowSpacing),
            scaleY);
        var width = Math.Max(
            24,
            ScaleCoordinate(BuffIconBaseWidth, scaleX));
        var height = Math.Max(
            18,
            ScaleCoordinate(BuffIconBaseHeight, scaleY));

        return new Rectangle(
            centerX - (width / 2),
            centerY - (height / 2),
            width,
            height);
    }

    private string BuildRenderSignature(DateTimeOffset now)
    {
        return string.Join(
            '|',
            this.presentation.Buffs.Select(buff =>
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{buff.Slot}:{buff.Identity}:{this.FormatDuration(buff, now)}")));
    }

    private string FormatDuration(
        GameBuffOverlayEntry buff,
        DateTimeOffset now)
    {
        if (buff.IsPermanent == true)
        {
            return "∞";
        }

        var remaining = this.ResolveRemainingMilliseconds(buff, now);

        if (!remaining.HasValue)
        {
            return "?";
        }

        var seconds = Math.Max(
            0L,
            (long)Math.Ceiling(remaining.Value / 1000.0));

        if (seconds >= 3600)
        {
            var hours = seconds / 3600;
            var minutes = (seconds % 3600) / 60;
            return hours >= 100
                ? "99h+"
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"{hours}:{minutes:00}");
        }

        if (seconds >= 60)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{seconds / 60}:{seconds % 60:00}");
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{seconds}s");
    }

    private long? ResolveRemainingMilliseconds(
        GameBuffOverlayEntry buff,
        DateTimeOffset now)
    {
        if (this.countdowns.TryGetValue(
                buff.Slot,
                out var countdown) &&
            string.Equals(
                countdown.Identity,
                buff.Identity,
                StringComparison.Ordinal))
        {
            return Math.Max(
                0L,
                (long)(countdown.ExpiresAt - now)
                .TotalMilliseconds);
        }

        return buff.RemainingMilliseconds;
    }

    private static int ScaleCoordinate(int value, float scale)
    {
        return (int)Math.Round(
            value * scale,
            MidpointRounding.AwayFromZero);
    }

    private static DateTimeOffset ResolveSharedDisplayTime(
        DateTimeOffset now)
    {
        // Every buff must cross its displayed second on the same clock edge.
        // Using one wall-clock second boundary prevents a field of active
        // buffs from producing a rolling wave of independent text updates.
        return DateTimeOffset.FromUnixTimeSeconds(
            now.ToUnixTimeSeconds());
    }

    private sealed record BuffCountdownState(
        string Identity,
        ulong? RemovalAtClientTime,
        DateTimeOffset ExpiresAt);
}
