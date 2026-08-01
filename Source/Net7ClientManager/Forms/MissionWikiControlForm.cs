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
    private readonly Button popOutButton = new();

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

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        this.ConfigureButton(this.toggleButton);
        this.ConfigureButton(this.popOutButton);
        this.toggleButton.Click += this.ToggleButton_OnClick;
        this.popOutButton.Text = "Pop out";
        this.popOutButton.Click += this.PopOutButton_OnClick;

        layout.Controls.Add(this.toggleButton, 0, 0);
        layout.Controls.Add(this.popOutButton, 1, 0);
        this.Controls.Add(layout);

        this.SetState(MissionWikiControlState.Expanded);
    }

    public event EventHandler? ToggleRequested;

    public event EventHandler? PopOutRequested;

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
            this.popOutButton.Click -= this.PopOutButton_OnClick;
            this.toggleButton.Dispose();
            this.popOutButton.Dispose();
        }

        base.Dispose(disposing);
    }

    private void ConfigureButton(Button button)
    {
        button.Dock = DockStyle.Fill;
        button.Margin = Padding.Empty;
        button.Padding = Padding.Empty;
        button.TabStop = false;
        button.TextAlign = ContentAlignment.MiddleCenter;
        MainWindowTheme.StyleButton(button, primary: true);
        button.FlatAppearance.BorderSize = 0;
    }

    private void ToggleButton_OnClick(object? sender, EventArgs e)
    {
        this.ToggleRequested?.Invoke(this, EventArgs.Empty);
    }

    private void PopOutButton_OnClick(object? sender, EventArgs e)
    {
        this.PopOutRequested?.Invoke(this, EventArgs.Empty);
    }
}
