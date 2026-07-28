namespace Net7ClientManager.Forms;

using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

internal sealed partial class ActionToolTip : IDisposable
{
    private const int MaximumDelayMilliseconds = 60_000;

    private readonly Dictionary<Control, ActionToolTipRegistration> contents = [];
    private readonly System.Windows.Forms.Timer showTimer = new();
    private readonly ActionToolTipWindow window;

    private Control? pendingControl;
    private Control? activeControl;
    private string externalKey = "";
    private string externalSignature = "";
    private Image? externalIcon;
    private string preparedExternalKey = "";
    private string preparedExternalSignature = "";
    private Image? preparedExternalIcon;

    public ActionToolTip(bool topMost = true)
    {
        this.window = new ActionToolTipWindow(topMost);
        this.showTimer.Tick += this.ShowTimer_OnTick;
    }

    public void PrepareExternal(
        string key,
        ActionToolTipContent content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(content);

        this.pendingControl = null;
        this.showTimer.Stop();
        this.activeControl = null;

        var contentSignature = content.Signature;
        var preparationChanged =
            !string.Equals(
                this.preparedExternalKey,
                key,
                StringComparison.Ordinal) ||
            !string.Equals(
                this.preparedExternalSignature,
                contentSignature,
                StringComparison.Ordinal) ||
            !ReferenceEquals(
                this.preparedExternalIcon,
                content.Icon);

        if (preparationChanged &&
            !this.window.Visible)
        {
            this.window.PrepareContent(content);
        }

        this.preparedExternalKey = key;
        this.preparedExternalSignature = contentSignature;
        this.preparedExternalIcon = content.Icon;
    }

    public void ShowExternal(
        Form owner,
        string key,
        ActionToolTipContent content,
        Point cursorPosition)
    {
        this.ShowExternal(
            owner,
            key,
            content,
            cursorPosition,
            ActionToolTipPlacement.Cursor,
            Point.Empty);
    }

    public void ShowExternalOverNative(
        Form owner,
        string key,
        ActionToolTipContent content,
        Point cursorPosition,
        Point placementOffset)
    {
        this.ShowExternal(
            owner,
            key,
            content,
            cursorPosition,
            ActionToolTipPlacement.NativeTooltipCover,
            placementOffset);
    }

    public void HideExternal()
    {
        if (this.externalKey.Length == 0 &&
            this.preparedExternalKey.Length == 0 &&
            !this.window.Visible)
        {
            return;
        }

        this.HideWindow();
    }

    public void HideExternalPreservePreparation()
    {
        if (this.externalKey.Length == 0 &&
            !this.window.Visible)
        {
            return;
        }

        this.HideWindow(clearExternalPreparation: false);
    }

    private void ShowExternal(
        Form owner,
        string key,
        ActionToolTipContent content,
        Point cursorPosition,
        ActionToolTipPlacement placement,
        Point placementOffset)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(content);

        this.pendingControl = null;
        this.showTimer.Stop();
        this.activeControl = null;

        var contentSignature = content.Signature;
        var samePresentation =
            this.window.Visible &&
            string.Equals(
                this.externalKey,
                key,
                StringComparison.Ordinal);
        var contentChanged =
            !string.Equals(
                this.externalSignature,
                contentSignature,
                StringComparison.Ordinal) ||
            !ReferenceEquals(
                this.externalIcon,
                content.Icon);

        if (samePresentation)
        {
            if (contentChanged)
            {
                this.window.UpdateContent(content);
            }

            this.window.MoveTo(
                cursorPosition,
                placement,
                placementOffset);
        }
        else if (string.Equals(
                     this.preparedExternalKey,
                     key,
                     StringComparison.Ordinal) &&
                 string.Equals(
                     this.preparedExternalSignature,
                     contentSignature,
                     StringComparison.Ordinal) &&
                 ReferenceEquals(
                     this.preparedExternalIcon,
                     content.Icon))
        {
            this.window.ShowPreparedContent(
                owner,
                cursorPosition,
                placement,
                placementOffset);
        }
        else
        {
            this.window.ShowContent(
                owner,
                content,
                cursorPosition,
                placement,
                placementOffset);
        }

        this.externalKey = key;
        this.externalSignature = contentSignature;
        this.externalIcon = content.Icon;
        this.preparedExternalKey = key;
        this.preparedExternalSignature = contentSignature;
        this.preparedExternalIcon = content.Icon;
    }

    public void SetToolTip(
        Control control,
        ActionToolTipContent? content,
        int delayMilliseconds)
    {
        if (content == null)
        {
            this.Remove(control);
            return;
        }

        delayMilliseconds = Math.Clamp(
            delayMilliseconds,
            0,
            MaximumDelayMilliseconds);

        var isNewControl = !this.contents.TryGetValue(
            control,
            out var existing);
        var contentUnchanged =
            !isNewControl &&
            existing != null &&
            string.Equals(
                existing.Content.Signature,
                content.Signature,
                StringComparison.Ordinal) &&
            ReferenceEquals(existing.Content.Icon, content.Icon);
        var delayUnchanged =
            !isNewControl &&
            existing?.DelayMilliseconds == delayMilliseconds;

        if (contentUnchanged && delayUnchanged)
        {
            return;
        }

        this.contents[control] = new ActionToolTipRegistration(
            content,
            delayMilliseconds);

        if (isNewControl)
        {
            control.MouseEnter += this.Control_OnMouseEnter;
            control.MouseLeave += this.Control_OnMouseLeave;
            control.MouseDown += this.Control_OnMouseDown;
            control.Disposed += this.Control_OnDisposed;
        }

        if (ReferenceEquals(this.activeControl, control) &&
            !contentUnchanged)
        {
            this.window.UpdateContent(content);
        }

        if (ReferenceEquals(this.pendingControl, control) &&
            !delayUnchanged)
        {
            this.ScheduleShow(
                control,
                delayMilliseconds);
        }
    }

    public void SetHoveredToolTip(
        Control control,
        ActionToolTipContent? content,
        int delayMilliseconds)
    {
        this.SetToolTip(
            control,
            content,
            delayMilliseconds);

        if (content == null ||
            control.IsDisposed ||
            !control.Visible ||
            !control.ClientRectangle.Contains(
                control.PointToClient(Cursor.Position)))
        {
            return;
        }

        if (ReferenceEquals(this.activeControl, control))
        {
            this.window.ShowContent(
                control,
                content,
                Cursor.Position);
            return;
        }

        this.ScheduleShow(
            control,
            Math.Clamp(
                delayMilliseconds,
                0,
                MaximumDelayMilliseconds));
    }

    public void Remove(Control control)
    {
        if (!this.contents.Remove(control))
        {
            return;
        }

        control.MouseEnter -= this.Control_OnMouseEnter;
        control.MouseLeave -= this.Control_OnMouseLeave;
        control.MouseDown -= this.Control_OnMouseDown;
        control.Disposed -= this.Control_OnDisposed;

        if (ReferenceEquals(this.pendingControl, control))
        {
            this.pendingControl = null;
            this.showTimer.Stop();
        }

        if (ReferenceEquals(this.activeControl, control))
        {
            this.HideWindow();
        }
    }

    public void RemoveAll()
    {
        foreach (var control in this.contents.Keys.ToArray())
        {
            control.MouseEnter -= this.Control_OnMouseEnter;
            control.MouseLeave -= this.Control_OnMouseLeave;
            control.MouseDown -= this.Control_OnMouseDown;
            control.Disposed -= this.Control_OnDisposed;
        }

        this.contents.Clear();
        this.pendingControl = null;
        this.showTimer.Stop();
        this.HideWindow();
    }

    public void Reactivate()
    {
        if (this.activeControl != null &&
            this.contents.TryGetValue(
                this.activeControl,
                out var registration))
        {
            this.window.UpdateContent(registration.Content);
        }
    }

    public void Dispose()
    {
        this.RemoveAll();
        this.showTimer.Tick -= this.ShowTimer_OnTick;
        this.showTimer.Dispose();
        this.window.Dispose();
    }

    private void Control_OnMouseEnter(
        object? sender,
        EventArgs e)
    {
        if (sender is not Control control ||
            control.IsDisposed ||
            !control.Visible ||
            !this.contents.TryGetValue(
                control,
                out var registration))
        {
            return;
        }

        this.ScheduleShow(
            control,
            registration.DelayMilliseconds);
    }

    private void Control_OnMouseLeave(
        object? sender,
        EventArgs e)
    {
        if (sender is not Control control)
        {
            return;
        }

        if (ReferenceEquals(this.pendingControl, control))
        {
            this.pendingControl = null;
            this.showTimer.Stop();
        }

        if (ReferenceEquals(this.activeControl, control))
        {
            this.HideWindow();
        }
    }

    private void Control_OnMouseDown(
        object? sender,
        MouseEventArgs e)
    {
        this.pendingControl = null;
        this.showTimer.Stop();
        this.HideWindow();
    }

    private void Control_OnDisposed(
        object? sender,
        EventArgs e)
    {
        if (sender is Control control)
        {
            this.Remove(control);
        }
    }

    private void ShowTimer_OnTick(
        object? sender,
        EventArgs e)
    {
        this.showTimer.Stop();
        this.ShowPendingControl();
    }

    private void ScheduleShow(
        Control control,
        int delayMilliseconds)
    {
        this.pendingControl = control;
        this.showTimer.Stop();

        if (delayMilliseconds <= 0)
        {
            this.ShowPendingControl();
            return;
        }

        this.showTimer.Interval = delayMilliseconds;
        this.showTimer.Start();
    }

    private void ShowPendingControl()
    {
        var control = this.pendingControl;
        this.pendingControl = null;

        if (control == null ||
            control.IsDisposed ||
            !control.Visible ||
            !control.ClientRectangle.Contains(
                control.PointToClient(Cursor.Position)) ||
            !this.contents.TryGetValue(
                control,
                out var registration))
        {
            return;
        }

        this.activeControl = control;
        this.externalKey = "";
        this.externalSignature = "";
        this.externalIcon = null;
        this.ClearExternalPreparation();
        this.window.ShowContent(
            control,
            registration.Content,
            Cursor.Position);
    }

    private void HideWindow(
        bool clearExternalPreparation = true)
    {
        if (this.window.Visible)
        {
            this.window.Hide();
        }

        this.activeControl = null;
        this.externalKey = "";
        this.externalSignature = "";
        this.externalIcon = null;

        if (clearExternalPreparation)
        {
            this.ClearExternalPreparation();
        }
    }

    private void ClearExternalPreparation()
    {
        this.preparedExternalKey = "";
        this.preparedExternalSignature = "";
        this.preparedExternalIcon = null;
    }

    private enum ActionToolTipPlacement
    {
        Cursor,
        NativeTooltipCover,
    }

    private sealed record ActionToolTipRegistration(
        ActionToolTipContent Content,
        int DelayMilliseconds);

    private sealed partial class ActionToolTipWindow : Form
    {
        private const int OuterPadding = 12;
        private const int IconSize = 32;
        private const int IconGap = 12;
        private const int ContentWidth = 456;
        private const int MinimumHeight = 64;

        private const int WmNcHitTest = 0x0084;
        private const int HtTransparent = -1;
        private const int WsExTransparent = 0x00000020;
        private const int WsExToolWindow = 0x00000080;
        private const int WsExLayered = 0x00080000;
        private const int WsExNoActivate = 0x08000000;
        private const uint LwaAlpha = 0x00000002;

        // These fonts are privately owned by the tooltip window.
        // MainWindowTheme returns process-wide cached Font instances, which
        // must never be disposed by an individual window.
        private readonly Font bodyFont =
            new("Segoe UI", 8.5f, FontStyle.Regular);
        private readonly Font bodyBoldFont =
            new("Segoe UI Semibold", 8.5f, FontStyle.Bold);
        private readonly Font headerFont =
            new("Segoe UI Semibold", 9.5f, FontStyle.Bold);

        private ActionToolTipContent? content;
        private ActionToolTipLayout? layout;

        public ActionToolTipWindow(bool topMost)
        {
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.BackColor = MainWindowTheme.ElevatedPanel;
            this.DoubleBuffered = true;
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowIcon = false;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.Manual;
            this.TopMost = topMost;

            this.SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint,
                true);
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

        protected override void OnHandleCreated(
            EventArgs e)
        {
            base.OnHandleCreated(e);

            _ = SetLayeredWindowAttributes(
                this.Handle,
                colorKey: 0,
                alpha: byte.MaxValue,
                flags: LwaAlpha);
        }

        protected override void WndProc(
            ref Message message)
        {
            if (message.Msg == WmNcHitTest)
            {
                message.Result = (IntPtr)HtTransparent;
                return;
            }

            base.WndProc(ref message);
        }

        public void PrepareContent(
            ActionToolTipContent content)
        {
            ArgumentNullException.ThrowIfNull(content);

            this.content = content;
            this.layout = this.CreateLayout(content);
            this.ClientSize = this.layout.Size;
        }

        public void ShowContent(
            Control anchor,
            ActionToolTipContent content,
            Point cursorPosition)
        {
            this.ShowContent(
                anchor.FindForm(),
                content,
                cursorPosition,
                ActionToolTipPlacement.Cursor,
                Point.Empty);
        }

        public void ShowContent(
            Form? owner,
            ActionToolTipContent content,
            Point cursorPosition)
        {
            this.ShowContent(
                owner,
                content,
                cursorPosition,
                ActionToolTipPlacement.Cursor,
                Point.Empty);
        }

        public void ShowContent(
            Form? owner,
            ActionToolTipContent content,
            Point cursorPosition,
            ActionToolTipPlacement placement,
            Point placementOffset)
        {
            this.PrepareContent(content);
            this.ShowPreparedContent(
                owner,
                cursorPosition,
                placement,
                placementOffset);
        }

        public void ShowPreparedContent(
            Form? owner,
            Point cursorPosition,
            ActionToolTipPlacement placement,
            Point placementOffset)
        {
            if (this.content == null ||
                this.layout == null)
            {
                return;
            }

            this.MoveTo(
                cursorPosition,
                placement,
                placementOffset);

            if (!this.Visible)
            {
                if (owner != null)
                {
                    this.Show(owner);
                }
                else
                {
                    this.Show();
                }
            }

            this.Invalidate();
        }

        public void MoveTo(
            Point cursorPosition,
            ActionToolTipPlacement placement,
            Point placementOffset)
        {
            this.Location = GetScreenLocation(
                cursorPosition,
                this.Size,
                placement,
                placementOffset);
        }

        public void UpdateContent(
            ActionToolTipContent content)
        {
            var oldContent = this.content;
            var oldLayout = this.layout;
            var newLayout = this.CreateLayout(content);
            var oldSize = this.ClientSize;

            this.content = content;
            this.layout = newLayout;

            if (oldSize != newLayout.Size)
            {
                this.ClientSize = newLayout.Size;
                this.Location = ClampToWorkingArea(
                    this.Location,
                    this.Size);
            }

            if (!this.Visible)
            {
                return;
            }

            var dirtyArea = GetDirtyArea(
                oldContent,
                oldLayout,
                content,
                newLayout,
                this.ClientRectangle);

            if (!dirtyArea.IsEmpty)
            {
                this.Invalidate(
                    Rectangle.Inflate(
                        dirtyArea,
                        2,
                        2));
            }
        }

        protected override void OnPaint(
            PaintEventArgs e)
        {
            base.OnPaint(e);

            if (this.content == null ||
                this.layout == null)
            {
                return;
            }

            var graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.InterpolationMode =
                InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            graphics.TextRenderingHint =
                TextRenderingHint.ClearTypeGridFit;

            using var backgroundBrush =
                new SolidBrush(MainWindowTheme.ElevatedPanel);
            graphics.FillRectangle(
                backgroundBrush,
                e.ClipRectangle);

            using var borderPen =
                new Pen(MainWindowTheme.AccentBorder);
            graphics.DrawRectangle(
                borderPen,
                this.ClientRectangle.Left,
                this.ClientRectangle.Top,
                Math.Max(0, this.ClientRectangle.Width - 1),
                Math.Max(0, this.ClientRectangle.Height - 1));

            var iconBounds = GetIconBounds(
                this.ClientRectangle);

            if (this.content.Icon != null &&
                iconBounds.IntersectsWith(e.ClipRectangle))
            {
                graphics.DrawImage(
                    this.content.Icon,
                    iconBounds);
            }

            foreach (var run in this.layout.Runs)
            {
                if (!run.Bounds.IntersectsWith(
                        e.ClipRectangle))
                {
                    continue;
                }

                using var brush =
                    new SolidBrush(GetColor(run.Role));
                graphics.DrawString(
                    run.Text,
                    run.Font,
                    brush,
                    run.Bounds.Location,
                    LayoutStringFormat);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.bodyFont.Dispose();
                this.bodyBoldFont.Dispose();
                this.headerFont.Dispose();
            }

            base.Dispose(disposing);
        }

        private ActionToolTipLayout CreateLayout(
            ActionToolTipContent content)
        {
            _ = this.Handle;

            using var graphics = this.CreateGraphics();
            var runs =
                new List<PositionedActionToolTipRun>();
            var paragraphBounds = new List<Rectangle>();
            var y = (float)OuterPadding;

            foreach (var paragraph in content.Paragraphs)
            {
                y += paragraph.SpaceBefore;

                var paragraphTop = y;
                var x = (float)OuterPadding;
                var lineHeight = this.GetFont(
                    paragraph.Style,
                    bold: false).GetHeight(graphics);
                var lineHasContent = false;

                foreach (var sourceRun in paragraph.Runs)
                {
                    var font = this.GetFont(
                        paragraph.Style,
                        sourceRun.Bold);

                    foreach (Match match in
                             TokenRegex().Matches(sourceRun.Text))
                    {
                        var token = match.Value;
                        var isWhiteSpace =
                            string.IsNullOrWhiteSpace(token);

                        if (isWhiteSpace && !lineHasContent)
                        {
                            continue;
                        }

                        var size = graphics.MeasureString(
                            token,
                            font,
                            int.MaxValue,
                            LayoutStringFormat);
                        var availableRight =
                            OuterPadding +
                            this.GetAvailableTextWidth(
                                y,
                                content.Icon != null);

                        if (!isWhiteSpace &&
                            lineHasContent &&
                            x + size.Width > availableRight)
                        {
                            y += lineHeight;
                            x = OuterPadding;
                            lineHeight =
                                font.GetHeight(graphics);
                            lineHasContent = false;
                        }

                        if (isWhiteSpace &&
                            x + size.Width > availableRight)
                        {
                            y += lineHeight;
                            x = OuterPadding;
                            lineHeight =
                                font.GetHeight(graphics);
                            lineHasContent = false;
                            continue;
                        }

                        var bounds = Rectangle.Ceiling(
                            new RectangleF(
                                x,
                                y,
                                size.Width,
                                Math.Max(
                                    lineHeight,
                                    font.GetHeight(graphics))));

                        runs.Add(
                            new PositionedActionToolTipRun(
                                token,
                                bounds,
                                font,
                                sourceRun.Role));

                        x += size.Width;
                        lineHeight = Math.Max(
                            lineHeight,
                            font.GetHeight(graphics));
                        lineHasContent |= !isWhiteSpace;
                    }
                }

                y += lineHeight + paragraph.SpaceAfter;

                paragraphBounds.Add(
                    Rectangle.FromLTRB(
                        OuterPadding,
                        (int)Math.Floor(paragraphTop),
                        OuterPadding + ContentWidth,
                        (int)Math.Ceiling(y)));
            }

            var width =
                ContentWidth + OuterPadding * 2;
            var height = Math.Max(
                MinimumHeight,
                (int)Math.Ceiling(y + OuterPadding));

            return new ActionToolTipLayout(
                new Size(width, height),
                runs,
                paragraphBounds);
        }

        private int GetAvailableTextWidth(
            float y,
            bool hasIcon)
        {
            var iconBottom =
                OuterPadding + IconSize + 4;

            return hasIcon && y < iconBottom
                ? ContentWidth - IconSize - IconGap
                : ContentWidth;
        }

        private Font GetFont(
            ActionToolTipParagraphStyle style,
            bool bold)
        {
            if (style ==
                ActionToolTipParagraphStyle.Header)
            {
                return this.headerFont;
            }

            return bold
                ? this.bodyBoldFont
                : this.bodyFont;
        }

        private static Rectangle GetDirtyArea(
            ActionToolTipContent? oldContent,
            ActionToolTipLayout? oldLayout,
            ActionToolTipContent newContent,
            ActionToolTipLayout newLayout,
            Rectangle fullBounds)
        {
            if (oldContent == null ||
                oldLayout == null ||
                oldLayout.Size != newLayout.Size ||
                oldContent.Paragraphs.Count !=
                newContent.Paragraphs.Count ||
                oldLayout.ParagraphBounds.Count !=
                newLayout.ParagraphBounds.Count)
            {
                return fullBounds;
            }

            var dirtyArea = Rectangle.Empty;

            for (var index = 0;
                 index < newContent.Paragraphs.Count;
                 index++)
            {
                if (string.Equals(
                        oldContent.GetParagraphSignature(index),
                        newContent.GetParagraphSignature(index),
                        StringComparison.Ordinal))
                {
                    continue;
                }

                dirtyArea = Union(
                    dirtyArea,
                    Rectangle.Union(
                        oldLayout.ParagraphBounds[index],
                        newLayout.ParagraphBounds[index]));
            }

            if (!ReferenceEquals(
                    oldContent.Icon,
                    newContent.Icon))
            {
                dirtyArea = Union(
                    dirtyArea,
                    GetIconBounds(fullBounds));
            }

            return dirtyArea;
        }

        private static Rectangle Union(
            Rectangle left,
            Rectangle right)
        {
            return left.IsEmpty
                ? right
                : Rectangle.Union(left, right);
        }

        private static Point GetScreenLocation(
            Point cursorPosition,
            Size size,
            ActionToolTipPlacement placement,
            Point placementOffset)
        {
            return placement ==
                ActionToolTipPlacement.NativeTooltipCover
                ? GetNativeTooltipCoverLocation(
                    cursorPosition,
                    size,
                    placementOffset)
                : GetCursorLocation(
                    cursorPosition,
                    size);
        }

        private static Point GetCursorLocation(
            Point cursorPosition,
            Size size)
        {
            var requested = new Point(
                cursorPosition.X + 18,
                cursorPosition.Y + 22);
            var workingArea =
                Screen.FromPoint(cursorPosition)
                    .WorkingArea;

            if (requested.X + size.Width >
                workingArea.Right)
            {
                requested.X =
                    cursorPosition.X -
                    size.Width -
                    18;
            }

            if (requested.Y + size.Height >
                workingArea.Bottom)
            {
                requested.Y =
                    cursorPosition.Y -
                    size.Height -
                    18;
            }

            return ClampToWorkingArea(
                requested,
                size);
        }

        private static Point GetNativeTooltipCoverLocation(
            Point cursorPosition,
            Size size,
            Point placementOffset)
        {
            const int horizontalLead = 64;
            const int upperGap = 2;
            const int lowerGap = 2;
            const int calibratedHorizontalOffset = 50;
            const int calibratedVerticalOffset = 10;

            var workingArea =
                Screen.FromPoint(cursorPosition)
                    .WorkingArea;
            var requested = new Point(
                cursorPosition.X -
                    horizontalLead +
                    calibratedHorizontalOffset +
                    placementOffset.X,
                cursorPosition.Y -
                    size.Height -
                    upperGap +
                    calibratedVerticalOffset +
                    placementOffset.Y);

            if (requested.Y < workingArea.Top)
            {
                requested.Y =
                    cursorPosition.Y +
                    lowerGap +
                    calibratedVerticalOffset +
                    placementOffset.Y;
            }

            return ClampToWorkingArea(
                requested,
                size);
        }

        private static Point ClampToWorkingArea(
            Point requested,
            Size size)
        {
            var workingArea =
                Screen.FromPoint(requested)
                    .WorkingArea;

            return new Point(
                Math.Clamp(
                    requested.X,
                    workingArea.Left,
                    Math.Max(
                        workingArea.Left,
                        workingArea.Right - size.Width)),
                Math.Clamp(
                    requested.Y,
                    workingArea.Top,
                    Math.Max(
                        workingArea.Top,
                        workingArea.Bottom - size.Height)));
        }

        private static Rectangle GetIconBounds(
            Rectangle clientBounds)
        {
            return new Rectangle(
                clientBounds.Right -
                OuterPadding -
                IconSize,
                clientBounds.Top + OuterPadding,
                IconSize,
                IconSize);
        }

        private static Color GetColor(
            ActionToolTipTextRole role)
        {
            return role switch
            {
                ActionToolTipTextRole.Accent =>
                    MainWindowTheme.Accent,
                ActionToolTipTextRole.Success =>
                    MainWindowTheme.Success,
                ActionToolTipTextRole.Danger =>
                    MainWindowTheme.Danger,
                ActionToolTipTextRole.Muted =>
                    MainWindowTheme.MutedText,
                _ => MainWindowTheme.Text,
            };
        }

        private static readonly StringFormat
            LayoutStringFormat = new(
                StringFormat.GenericTypographic)
            {
                FormatFlags =
                    StringFormatFlags.MeasureTrailingSpaces |
                    StringFormatFlags.NoClip,
            };

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool SetLayeredWindowAttributes(
            nint windowHandle,
            uint colorKey,
            byte alpha,
            uint flags);

        [GeneratedRegex(
            @"\s+|\S+",
            RegexOptions.CultureInvariant)]
        private static partial Regex TokenRegex();

        private sealed record ActionToolTipLayout(
            Size Size,
            IReadOnlyList<PositionedActionToolTipRun> Runs,
            IReadOnlyList<Rectangle> ParagraphBounds);

        private sealed record PositionedActionToolTipRun(
            string Text,
            Rectangle Bounds,
            Font Font,
            ActionToolTipTextRole Role);
    }
}

internal sealed record ActionToolTipContent(
    IReadOnlyList<ActionToolTipParagraph> Paragraphs,
    Image? Icon)
{
    public string Signature =>
        CreateSignature(this.Paragraphs, this.Icon);

    public string GetParagraphSignature(int index)
    {
        return CreateParagraphSignature(
            this.Paragraphs[index]);
    }

    private static string CreateSignature(
        IReadOnlyList<ActionToolTipParagraph> paragraphs,
        Image? icon)
    {
        var builder = new StringBuilder();
        builder.Append(
            icon == null
                ? "no-icon"
                : "icon");

        foreach (var paragraph in paragraphs)
        {
            builder.Append('|');
            builder.Append(
                CreateParagraphSignature(paragraph));
        }

        return builder.ToString();
    }

    private static string CreateParagraphSignature(
        ActionToolTipParagraph paragraph)
    {
        var builder = new StringBuilder();
        builder.Append(paragraph.Style);
        builder.Append(':');
        builder.Append(paragraph.SpaceBefore);
        builder.Append(':');
        builder.Append(paragraph.SpaceAfter);

        foreach (var run in paragraph.Runs)
        {
            builder.Append('|');
            builder.Append(run.Role);
            builder.Append(':');
            builder.Append(run.Bold ? '1' : '0');
            builder.Append(':');
            builder.Append(run.Text);
        }

        return builder.ToString();
    }
}

internal sealed record ActionToolTipParagraph(
    IReadOnlyList<ActionToolTipRun> Runs,
    ActionToolTipParagraphStyle Style =
        ActionToolTipParagraphStyle.Body,
    int SpaceBefore = 0,
    int SpaceAfter = 0);

internal sealed record ActionToolTipRun(
    string Text,
    ActionToolTipTextRole Role =
        ActionToolTipTextRole.Normal,
    bool Bold = false);

internal enum ActionToolTipParagraphStyle
{
    Body,
    Header,
}

internal enum ActionToolTipTextRole
{
    Normal,
    Accent,
    Success,
    Danger,
    Muted,
}
