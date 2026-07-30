// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using System.Drawing.Drawing2D;

internal sealed class GuidedTourOverlay : Control
{
    private const int SpotlightPadding = 7;
    private const int CalloutGap = 14;

    private readonly Form owner;
    private readonly IReadOnlyList<GuidedTourStep> steps;
    private readonly GuidedTourCalloutForm callout;
    private Action? tourClosed;
    private Bitmap? snapshot;
    private Rectangle targetBounds;
    private Control? currentTarget;
    private int stepIndex;
    private bool disposed;

    private GuidedTourOverlay(
        Form owner,
        IReadOnlyList<GuidedTourStep> steps,
        Action? tourClosed)
    {
        this.owner = owner;
        this.steps = steps;
        this.tourClosed = tourClosed;
        this.SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.Selectable |
            ControlStyles.UserPaint,
            value: true);
        this.TabStop = true;
        this.Cursor = Cursors.Default;
        this.BackColor = Color.Black;
        this.AccessibleName = "Guided introduction spotlight";

        this.callout = new GuidedTourCalloutForm(
            previous: () => this.ShowStep(this.stepIndex - 1),
            next: this.Advance,
            close: this.CloseTour);

        this.owner.LocationChanged += this.Owner_OnGeometryChanged;
        this.owner.SizeChanged += this.Owner_OnGeometryChanged;
        this.owner.FormClosed += this.Owner_OnFormClosed;
    }

    public static void Show(
        Form owner,
        IReadOnlyList<GuidedTourStep> steps,
        int startIndex = 0,
        Action? tourClosed = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(steps);

        if (owner.IsDisposed || steps.Count == 0)
        {
            return;
        }

        foreach (var existing in owner.Controls
                     .OfType<GuidedTourOverlay>()
                     .ToArray())
        {
            existing.CloseTour(invokeCallback: false);
        }

        var overlay = new GuidedTourOverlay(owner, steps, tourClosed)
        {
            Bounds = owner.DisplayRectangle,
            Anchor = AnchorStyles.Top |
                     AnchorStyles.Bottom |
                     AnchorStyles.Left |
                     AnchorStyles.Right,
        };

        owner.Controls.Add(overlay);
        overlay.BringToFront();
        overlay.ShowStep(Math.Clamp(startIndex, 0, steps.Count - 1));
    }

    protected override bool IsInputKey(Keys keyData)
    {
        return keyData is Keys.Left or Keys.Right or Keys.Escape ||
               base.IsInputKey(keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        switch (e.KeyCode)
        {
            case Keys.Escape:
                this.CloseTour();
                e.Handled = true;
                break;

            case Keys.Left when this.stepIndex > 0:
                this.ShowStep(this.stepIndex - 1);
                e.Handled = true;
                break;

            case Keys.Right:
                this.Advance();
                e.Handled = true;
                break;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(MainWindowTheme.Background);

        if (this.snapshot != null)
        {
            e.Graphics.DrawImageUnscaled(this.snapshot, Point.Empty);
        }

        using (var veil = new SolidBrush(Color.FromArgb(202, 4, 8, 13)))
        {
            e.Graphics.FillRectangle(veil, this.ClientRectangle);
        }

        if (this.snapshot != null &&
            this.targetBounds.Width > 0 &&
            this.targetBounds.Height > 0)
        {
            var source = Rectangle.Intersect(
                this.targetBounds,
                new Rectangle(Point.Empty, this.snapshot.Size));

            if (source.Width > 0 && source.Height > 0)
            {
                e.Graphics.DrawImage(
                    this.snapshot,
                    source,
                    source,
                    GraphicsUnit.Pixel);
            }
        }

        if (this.targetBounds.Width > 0 &&
            this.targetBounds.Height > 0)
        {
            using var glow = new Pen(
                Color.FromArgb(110, MainWindowTheme.Warning),
                7.0f);
            using var border = new Pen(MainWindowTheme.Warning, 2.0f);
            e.Graphics.DrawRectangle(glow, this.targetBounds);
            e.Graphics.DrawRectangle(border, this.targetBounds);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !this.disposed)
        {
            this.disposed = true;
            this.owner.LocationChanged -= this.Owner_OnGeometryChanged;
            this.owner.SizeChanged -= this.Owner_OnGeometryChanged;
            this.owner.FormClosed -= this.Owner_OnFormClosed;
            this.snapshot?.Dispose();
            this.snapshot = null;
            this.callout.CloseSilently();
            this.callout.Dispose();
        }

        base.Dispose(disposing);
    }

    private void ShowStep(int index)
    {
        if (index < 0 || index >= this.steps.Count || this.IsDisposed)
        {
            return;
        }

        this.stepIndex = index;
        var step = this.steps[this.stepIndex];
        step.Prepare?.Invoke();
        this.owner.PerformLayout();
        this.owner.Update();
        this.CaptureOwner();
        this.ResolveTarget(step.ResolveTarget());
        this.callout.SetStep(
            step,
            this.stepIndex,
            this.steps.Count);
        this.PositionCallout();
        this.Invalidate();

        if (!this.callout.Visible)
        {
            this.callout.Show(this.owner);
        }

        this.callout.BringToFront();
        this.callout.Activate();
    }

    private void Advance()
    {
        if (this.stepIndex >= this.steps.Count - 1)
        {
            this.CloseTour();
            return;
        }

        this.ShowStep(this.stepIndex + 1);
    }

    private void CloseTour()
    {
        this.CloseTour(invokeCallback: true);
    }

    private void CloseTour(bool invokeCallback)
    {
        if (this.IsDisposed)
        {
            return;
        }

        var callback = invokeCallback
            ? this.tourClosed
            : null;
        this.tourClosed = null;

        if (!this.owner.IsDisposed && !this.owner.Disposing)
        {
            this.owner.Controls.Remove(this);
        }

        this.Dispose();

        if (!this.owner.IsDisposed && !this.owner.Disposing)
        {
            this.owner.Activate();
        }

        callback?.Invoke();
    }

    private void CaptureOwner()
    {
        this.snapshot?.Dispose();
        this.snapshot = null;

        var size = new Size(
            Math.Max(1, this.ClientSize.Width),
            Math.Max(1, this.ClientSize.Height));
        var bitmap = new Bitmap(size.Width, size.Height);

        try
        {
            this.Visible = false;
            this.owner.Invalidate(invalidateChildren: true);
            this.owner.Update();

            var ownerSize = new Size(
                Math.Max(1, this.owner.ClientSize.Width),
                Math.Max(1, this.owner.ClientSize.Height));
            using var ownerBitmap = new Bitmap(
                ownerSize.Width,
                ownerSize.Height);
            this.owner.DrawToBitmap(
                ownerBitmap,
                new Rectangle(Point.Empty, ownerSize));

            var displaySource = Rectangle.Intersect(
                this.owner.DisplayRectangle,
                new Rectangle(Point.Empty, ownerBitmap.Size));

            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(MainWindowTheme.Background);
            if (displaySource.Width > 0 && displaySource.Height > 0)
            {
                graphics.DrawImage(
                    ownerBitmap,
                    new Rectangle(
                        Point.Empty,
                        displaySource.Size),
                    displaySource,
                    GraphicsUnit.Pixel);
            }

            this.snapshot = bitmap;
        }
        catch
        {
            bitmap.Dispose();
        }
        finally
        {
            if (!this.IsDisposed)
            {
                this.Visible = true;
            }
        }
    }

    private void ResolveTarget(Control? target)
    {
        this.currentTarget = target;
        var client = this.ClientRectangle;
        var fallback = new Rectangle(
            client.Left + (client.Width / 4),
            client.Top + (client.Height / 3),
            Math.Max(120, client.Width / 2),
            Math.Max(72, client.Height / 4));

        if (target == null || target.IsDisposed || !target.Visible)
        {
            this.targetBounds = Rectangle.Intersect(fallback, client);
            return;
        }

        var screenBounds = target.RectangleToScreen(target.ClientRectangle);
        this.targetBounds = this.RectangleToClient(screenBounds);
        this.targetBounds.Inflate(SpotlightPadding, SpotlightPadding);
        this.targetBounds = Rectangle.Intersect(this.targetBounds, client);
    }

    private void PositionCallout()
    {
        if (this.callout.IsDisposed)
        {
            return;
        }

        var ownerBounds = this.owner.Bounds;
        var targetScreenBounds = this.currentTarget is
            { IsDisposed: false, Visible: true }
            ? this.currentTarget.RectangleToScreen(
                this.currentTarget.ClientRectangle)
            : this.RectangleToScreen(this.targetBounds);
        var workingArea = Screen.FromRectangle(ownerBounds).WorkingArea;
        var calloutSize = this.callout.Size;

        var outsideCandidates = new[]
        {
            new Rectangle(
                ownerBounds.Right + CalloutGap,
                ClampCoordinate(
                    targetScreenBounds.Top,
                    workingArea.Top,
                    workingArea.Bottom - calloutSize.Height),
                calloutSize.Width,
                calloutSize.Height),
            new Rectangle(
                ownerBounds.Left - calloutSize.Width - CalloutGap,
                ClampCoordinate(
                    targetScreenBounds.Top,
                    workingArea.Top,
                    workingArea.Bottom - calloutSize.Height),
                calloutSize.Width,
                calloutSize.Height),
            new Rectangle(
                ClampCoordinate(
                    targetScreenBounds.Left,
                    workingArea.Left,
                    workingArea.Right - calloutSize.Width),
                ownerBounds.Bottom + CalloutGap,
                calloutSize.Width,
                calloutSize.Height),
            new Rectangle(
                ClampCoordinate(
                    targetScreenBounds.Left,
                    workingArea.Left,
                    workingArea.Right - calloutSize.Width),
                ownerBounds.Top - calloutSize.Height - CalloutGap,
                calloutSize.Width,
                calloutSize.Height),
        };

        var outside = outsideCandidates.FirstOrDefault(candidate =>
            workingArea.Contains(candidate) &&
            !candidate.IntersectsWith(ownerBounds));

        if (outside.Width > 0 && outside.Height > 0)
        {
            this.callout.Bounds = outside;
            return;
        }

        var nearbyCandidates = new[]
        {
            new Rectangle(
                targetScreenBounds.Right + CalloutGap,
                targetScreenBounds.Top,
                calloutSize.Width,
                calloutSize.Height),
            new Rectangle(
                targetScreenBounds.Left - calloutSize.Width - CalloutGap,
                targetScreenBounds.Top,
                calloutSize.Width,
                calloutSize.Height),
            new Rectangle(
                targetScreenBounds.Left,
                targetScreenBounds.Bottom + CalloutGap,
                calloutSize.Width,
                calloutSize.Height),
            new Rectangle(
                targetScreenBounds.Left,
                targetScreenBounds.Top - calloutSize.Height - CalloutGap,
                calloutSize.Width,
                calloutSize.Height),
        };

        var best = nearbyCandidates
            .Select(candidate => ClampToWorkingArea(candidate, workingArea))
            .OrderBy(candidate => IntersectionArea(
                candidate,
                targetScreenBounds))
            .ThenBy(candidate => IntersectionArea(candidate, ownerBounds))
            .First();

        this.callout.Bounds = best;
    }

    private void Owner_OnGeometryChanged(object? sender, EventArgs e)
    {
        if (!this.IsDisposed)
        {
            this.PositionCallout();
        }
    }

    private void Owner_OnFormClosed(object? sender, FormClosedEventArgs e)
    {
        this.CloseTour(invokeCallback: false);
    }

    private static int ClampCoordinate(
        int value,
        int minimum,
        int maximum)
    {
        return maximum < minimum
            ? minimum
            : Math.Clamp(value, minimum, maximum);
    }

    private static Rectangle ClampToWorkingArea(
        Rectangle candidate,
        Rectangle workingArea)
    {
        return new Rectangle(
            ClampCoordinate(
                candidate.Left,
                workingArea.Left,
                workingArea.Right - candidate.Width),
            ClampCoordinate(
                candidate.Top,
                workingArea.Top,
                workingArea.Bottom - candidate.Height),
            candidate.Width,
            candidate.Height);
    }

    private static int IntersectionArea(
        Rectangle first,
        Rectangle second)
    {
        var intersection = Rectangle.Intersect(first, second);
        return intersection.Width * intersection.Height;
    }

    private sealed class GuidedTourCalloutForm : Form
    {
        private const int CalloutWidth = 430;
        private const int CalloutMinimumHeight = 196;
        private const int HorizontalPadding = 20;
        private const int VerticalPadding = 17;
        private const int ButtonHeight = 32;
        private const int ButtonGap = 8;

        private readonly Label progressLabel = new();
        private readonly Label titleLabel = new();
        private readonly Label bodyLabel = new();
        private readonly Button closeButton = new();
        private readonly Button previousButton = new();
        private readonly Button nextButton = new();
        private readonly Action previous;
        private readonly Action next;
        private readonly Action close;
        private bool closeSilently;

        internal GuidedTourCalloutForm(
            Action previous,
            Action next,
            Action close)
        {
            this.previous = previous;
            this.next = next;
            this.close = close;

            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.Manual;
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.BackColor = MainWindowTheme.Panel;
            this.ForeColor = MainWindowTheme.Text;
            this.Font = MainWindowTheme.CreateBodyFont();
            this.KeyPreview = true;
            this.AccessibleName = "Guided introduction step";

            this.progressLabel.AutoSize = false;
            this.progressLabel.ForeColor = MainWindowTheme.Accent;
            this.progressLabel.Font = MainWindowTheme.CreateBodyFont(8.5f);
            this.progressLabel.UseMnemonic = false;

            this.titleLabel.AutoSize = false;
            this.titleLabel.ForeColor = MainWindowTheme.Text;
            this.titleLabel.Font = MainWindowTheme.CreateHeadingFont(13.0f);
            this.titleLabel.UseMnemonic = false;

            this.bodyLabel.AutoSize = false;
            this.bodyLabel.ForeColor = MainWindowTheme.MutedText;
            this.bodyLabel.Font = MainWindowTheme.CreateBodyFont(9.5f);
            this.bodyLabel.UseMnemonic = false;

            this.closeButton.Text = "Close";
            this.closeButton.UseMnemonic = false;
            this.closeButton.Click += (_, _) => this.close();
            MainWindowTheme.StyleButton(this.closeButton);

            this.previousButton.Text = "Previous";
            this.previousButton.UseMnemonic = false;
            this.previousButton.Click += (_, _) => this.previous();
            MainWindowTheme.StyleButton(this.previousButton);

            this.nextButton.UseMnemonic = false;
            this.nextButton.Click += (_, _) => this.next();
            MainWindowTheme.StyleButton(this.nextButton, primary: true);

            this.Controls.Add(this.progressLabel);
            this.Controls.Add(this.titleLabel);
            this.Controls.Add(this.bodyLabel);
            this.Controls.Add(this.closeButton);
            this.Controls.Add(this.previousButton);
            this.Controls.Add(this.nextButton);
        }

        internal void SetStep(
            GuidedTourStep step,
            int stepIndex,
            int stepCount)
        {
            this.progressLabel.Text = string.Concat(
                "STEP ",
                stepIndex + 1,
                " OF ",
                stepCount);
            this.titleLabel.Text = step.Title;
            this.bodyLabel.Text = step.Body;
            this.previousButton.Enabled = stepIndex > 0;
            this.nextButton.Text = stepIndex == stepCount - 1
                ? "Done"
                : "Next";

            var contentWidth = CalloutWidth - (HorizontalPadding * 2);
            var titleHeight = TextRenderer.MeasureText(
                step.Title,
                this.titleLabel.Font,
                new Size(contentWidth, 0),
                TextFormatFlags.WordBreak |
                TextFormatFlags.NoPrefix |
                TextFormatFlags.NoPadding).Height;
            var bodyHeight = TextRenderer.MeasureText(
                step.Body,
                this.bodyLabel.Font,
                new Size(contentWidth, 0),
                TextFormatFlags.WordBreak |
                TextFormatFlags.NoPrefix |
                TextFormatFlags.NoPadding).Height;

            var height = Math.Max(
                CalloutMinimumHeight,
                VerticalPadding +
                20 +
                6 +
                titleHeight +
                10 +
                bodyHeight +
                22 +
                ButtonHeight +
                VerticalPadding);

            this.Size = new Size(CalloutWidth, height);
            this.LayoutControls(titleHeight, bodyHeight);
            this.Invalidate();
        }

        internal void CloseSilently()
        {
            if (this.IsDisposed)
            {
                return;
            }

            this.closeSilently = true;
            this.Close();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using var border = new Pen(MainWindowTheme.AccentBorder, 1.5f);
            e.Graphics.DrawRectangle(
                border,
                x: 0,
                y: 0,
                width: Math.Max(0, this.ClientSize.Width - 1),
                height: Math.Max(0, this.ClientSize.Height - 1));
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            switch (e.KeyCode)
            {
                case Keys.Escape:
                    this.close();
                    e.Handled = true;
                    break;

                case Keys.Left when this.previousButton.Enabled:
                    this.previous();
                    e.Handled = true;
                    break;

                case Keys.Right:
                    this.next();
                    e.Handled = true;
                    break;
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!this.closeSilently &&
                e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                this.BeginInvoke(this.close);
                return;
            }

            base.OnFormClosing(e);
        }

        private void LayoutControls(
            int titleHeight,
            int bodyHeight)
        {
            var contentWidth = this.ClientSize.Width -
                               (HorizontalPadding * 2);
            var y = VerticalPadding;

            this.progressLabel.Bounds = new Rectangle(
                HorizontalPadding,
                y,
                contentWidth,
                20);
            y += 26;

            this.titleLabel.Bounds = new Rectangle(
                HorizontalPadding,
                y,
                contentWidth,
                titleHeight);
            y += titleHeight + 10;

            this.bodyLabel.Bounds = new Rectangle(
                HorizontalPadding,
                y,
                contentWidth,
                bodyHeight);

            var buttonY = this.ClientSize.Height -
                          VerticalPadding -
                          ButtonHeight;
            this.closeButton.Bounds = new Rectangle(
                HorizontalPadding,
                buttonY,
                70,
                ButtonHeight);
            this.nextButton.Bounds = new Rectangle(
                this.ClientSize.Width - HorizontalPadding - 86,
                buttonY,
                86,
                ButtonHeight);
            this.previousButton.Bounds = new Rectangle(
                this.nextButton.Left - ButtonGap - 96,
                buttonY,
                96,
                ButtonHeight);
        }
    }
}
