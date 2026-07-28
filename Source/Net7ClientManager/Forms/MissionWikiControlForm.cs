// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

internal enum MissionWikiControlState
{
    Expanded,
    Collapsed,
}

internal sealed class MissionWikiControlForm : Form
{
    private const int WmMouseActivate = 0x0021;
    private const int MaNoActivate = 3;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;

    private readonly Button toggleButton = new();

    private MissionWikiControlState? state;
    private bool jobGuidance;

    public MissionWikiControlForm()
    {
        this.FormBorderStyle = FormBorderStyle.None;
        this.ShowInTaskbar = false;
        this.StartPosition = FormStartPosition.Manual;
        this.AutoScaleMode = AutoScaleMode.None;
        this.Padding = new Padding(1);
        this.BackColor = MainWindowTheme.AccentBorder;

        this.toggleButton.Dock = DockStyle.Fill;
        this.toggleButton.Margin = Padding.Empty;
        this.toggleButton.Padding = Padding.Empty;
        this.toggleButton.TabStop = false;
        this.toggleButton.TextAlign = ContentAlignment.MiddleCenter;
        MainWindowTheme.StyleButton(this.toggleButton, primary: true);
        this.toggleButton.FlatAppearance.BorderSize = 0;
        this.toggleButton.Click += this.ToggleButton_OnClick;

        this.Controls.Add(this.toggleButton);

        this.SetState(MissionWikiControlState.Expanded);
    }

    public event EventHandler? ToggleRequested;

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

    public void SetState(
        MissionWikiControlState nextState,
        bool jobGuidance = false)
    {
        if (this.state == nextState &&
            this.jobGuidance == jobGuidance)
        {
            return;
        }

        this.state = nextState;
        this.jobGuidance = jobGuidance;

        this.toggleButton.Text = (nextState, jobGuidance) switch
        {
            (MissionWikiControlState.Expanded, true) => "Hide Guide",
            (MissionWikiControlState.Collapsed, true) => "Show Guide",
            (MissionWikiControlState.Expanded, false) => "Hide Wiki",
            (MissionWikiControlState.Collapsed, false) => "Show Wiki",
            _ => throw new ArgumentOutOfRangeException(
                nameof(nextState),
                nextState,
                message: null),
        };
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
            this.toggleButton.Click -= this.ToggleButton_OnClick;
            this.toggleButton.Dispose();
        }

        base.Dispose(disposing);
    }

    private void ToggleButton_OnClick(object? sender, EventArgs e)
    {
        this.ToggleRequested?.Invoke(this, EventArgs.Empty);
    }
}
