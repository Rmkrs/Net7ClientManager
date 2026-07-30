// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

internal sealed record HostedClientMenuTourStep(
    string MenuItem,
    bool OpenMenu,
    string Title,
    string Body);

internal sealed class HostedClientMenuTourForm : Form
{
    private const int CalloutWidth = 450;
    private const int MinimumHeight = 208;
    private const int HorizontalPadding = 20;
    private const int VerticalPadding = 17;
    private const int ButtonHeight = 32;
    private const int ButtonGap = 8;
    private const int WindowGap = 14;
    private const int InsideMargin = 18;
    private const int HostedTitleBarHeight = 34;

    private readonly Form hostForm;
    private readonly IReadOnlyList<HostedClientMenuTourStep> steps;
    private readonly Action<HostedClientMenuTourStep> prepareStep;
    private Action? tourClosed;

    private readonly Label progressLabel = new();
    private readonly Label titleLabel = new();
    private readonly Label bodyLabel = new();
    private readonly Button closeButton = new();
    private readonly Button previousButton = new();
    private readonly Button nextButton = new();

    private int stepIndex;
    private bool closingTour;

    internal HostedClientMenuTourForm(
        Form owner,
        IReadOnlyList<HostedClientMenuTourStep> steps,
        Action<HostedClientMenuTourStep> prepareStep,
        Action tourClosed)
    {
        this.hostForm = owner ?? throw new ArgumentNullException(nameof(owner));
        this.steps = steps ?? throw new ArgumentNullException(nameof(steps));
        this.prepareStep = prepareStep ??
            throw new ArgumentNullException(nameof(prepareStep));
        this.tourClosed = tourClosed ??
            throw new ArgumentNullException(nameof(tourClosed));

        this.FormBorderStyle = FormBorderStyle.None;
        this.ShowInTaskbar = false;
        this.StartPosition = FormStartPosition.Manual;
        this.AutoScaleMode = AutoScaleMode.Dpi;
        this.BackColor = MainWindowTheme.Panel;
        this.ForeColor = MainWindowTheme.Text;
        this.Font = MainWindowTheme.CreateBodyFont();
        this.KeyPreview = true;
        this.AccessibleName = "Hosted Client Manager menu tour";

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
        this.closeButton.Click += (_, _) => this.CloseTour();
        MainWindowTheme.StyleButton(this.closeButton);

        this.previousButton.Text = "Previous";
        this.previousButton.UseMnemonic = false;
        this.previousButton.Click += (_, _) =>
            this.ShowStep(this.stepIndex - 1);
        MainWindowTheme.StyleButton(this.previousButton);

        this.nextButton.UseMnemonic = false;
        this.nextButton.Click += (_, _) => this.Advance();
        MainWindowTheme.StyleButton(this.nextButton, primary: true);

        this.Controls.Add(this.progressLabel);
        this.Controls.Add(this.titleLabel);
        this.Controls.Add(this.bodyLabel);
        this.Controls.Add(this.closeButton);
        this.Controls.Add(this.previousButton);
        this.Controls.Add(this.nextButton);

        this.hostForm.LocationChanged += this.Owner_OnGeometryChanged;
        this.hostForm.SizeChanged += this.Owner_OnGeometryChanged;
        this.hostForm.FormClosed += this.Owner_OnFormClosed;
    }

    internal void StartTour()
    {
        if (this.steps.Count == 0 || this.hostForm.IsDisposed)
        {
            this.CloseTour();
            return;
        }

        this.Show(this.hostForm);
        this.ShowStep(0);
        this.BringToFront();
        this.Activate();
    }

    protected override bool ProcessCmdKey(
        ref Message msg,
        Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Escape:
                this.CloseTour();
                return true;

            case Keys.Left when this.stepIndex > 0:
                this.ShowStep(this.stepIndex - 1);
                return true;

            case Keys.Right:
                this.Advance();
                return true;

            default:
                return base.ProcessCmdKey(ref msg, keyData);
        }
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

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        this.hostForm.LocationChanged -= this.Owner_OnGeometryChanged;
        this.hostForm.SizeChanged -= this.Owner_OnGeometryChanged;
        this.hostForm.FormClosed -= this.Owner_OnFormClosed;

        var callback = this.tourClosed;
        this.tourClosed = null;
        callback?.Invoke();

        base.OnFormClosed(e);
    }

    private void ShowStep(int index)
    {
        if (index < 0 || index >= this.steps.Count || this.IsDisposed)
        {
            return;
        }

        this.stepIndex = index;
        var step = this.steps[index];

        this.progressLabel.Text = string.Concat(
            "STEP ",
            index + 1,
            " OF ",
            this.steps.Count);
        this.titleLabel.Text = step.Title;
        this.bodyLabel.Text = step.Body;
        this.previousButton.Enabled = index > 0;
        this.nextButton.Text = index == this.steps.Count - 1
            ? "Done"
            : "Next";

        this.ResizeForContent(step);
        this.PositionNearOwner();
        this.prepareStep(step);
        this.BringToFront();
        this.Activate();
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
        if (this.closingTour || this.IsDisposed)
        {
            return;
        }

        this.closingTour = true;
        this.Close();
    }

    private void ResizeForContent(HostedClientMenuTourStep step)
    {
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
            MinimumHeight,
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
    }

    private void LayoutControls(int titleHeight, int bodyHeight)
    {
        var contentWidth = this.ClientSize.Width -
            (HorizontalPadding * 2);
        var top = VerticalPadding;

        this.progressLabel.SetBounds(
            HorizontalPadding,
            top,
            contentWidth,
            20);
        top += 26;

        this.titleLabel.SetBounds(
            HorizontalPadding,
            top,
            contentWidth,
            titleHeight);
        top += titleHeight + 10;

        this.bodyLabel.SetBounds(
            HorizontalPadding,
            top,
            contentWidth,
            bodyHeight);

        var buttonTop = this.ClientSize.Height -
            VerticalPadding -
            ButtonHeight;
        this.closeButton.SetBounds(
            HorizontalPadding,
            buttonTop,
            82,
            ButtonHeight);
        this.nextButton.SetBounds(
            this.ClientSize.Width - HorizontalPadding - 92,
            buttonTop,
            92,
            ButtonHeight);
        this.previousButton.SetBounds(
            this.nextButton.Left - ButtonGap - 104,
            buttonTop,
            104,
            ButtonHeight);
    }

    private void PositionNearOwner()
    {
        if (this.hostForm.IsDisposed || this.IsDisposed)
        {
            return;
        }

        var ownerBounds = this.hostForm.Bounds;
        var workingArea = Screen.FromRectangle(ownerBounds).WorkingArea;

        var rightCandidate = new Rectangle(
            ownerBounds.Right + WindowGap,
            Math.Clamp(
                ownerBounds.Top + HostedTitleBarHeight + InsideMargin,
                workingArea.Top,
                Math.Max(workingArea.Top, workingArea.Bottom - this.Height)),
            this.Width,
            this.Height);
        if (workingArea.Contains(rightCandidate))
        {
            this.Bounds = rightCandidate;
            return;
        }

        var leftCandidate = new Rectangle(
            ownerBounds.Left - this.Width - WindowGap,
            rightCandidate.Top,
            this.Width,
            this.Height);
        if (workingArea.Contains(leftCandidate))
        {
            this.Bounds = leftCandidate;
            return;
        }

        var inside = new Rectangle(
            ownerBounds.Right - this.Width - InsideMargin,
            ownerBounds.Bottom - this.Height - InsideMargin,
            this.Width,
            this.Height);

        this.Location = new Point(
            Math.Clamp(
                inside.Left,
                workingArea.Left,
                Math.Max(workingArea.Left, workingArea.Right - this.Width)),
            Math.Clamp(
                inside.Top,
                workingArea.Top,
                Math.Max(workingArea.Top, workingArea.Bottom - this.Height)));
    }

    private void Owner_OnGeometryChanged(object? sender, EventArgs e)
    {
        this.PositionNearOwner();
    }

    private void Owner_OnFormClosed(object? sender, FormClosedEventArgs e)
    {
        this.CloseTour();
    }
}
