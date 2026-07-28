// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using Net7ClientManager.Core;

internal sealed class ShoppingListNameDialog : ThemedForm
{
    private readonly TableLayoutPanel root = new();
    private readonly TextBox nameTextBox = new();
    private readonly Button acceptButton = new();

    public ShoppingListNameDialog(
        string title,
        string prompt,
        string initialName = "")
    {
        this.Text = title;
        this.Icon = ResourceLoader.Net7ClientManagerIcon;
        this.StartPosition = FormStartPosition.CenterParent;
        this.ShowInTaskbar = false;
        this.MinimumSize = new Size(600, 290);
        this.BackColor = MainWindowTheme.Background;
        this.ForeColor = MainWindowTheme.Text;
        this.Font = MainWindowTheme.CreateBodyFont();
        this.ConfigureWindowChrome(
            allowResize: false,
            showMinimizeButton: false,
            showMaximizeButton: false);

        this.root.Dock = DockStyle.Fill;
        this.root.ColumnCount = 1;
        this.root.RowCount = 3;
        this.root.Padding = new Padding(24, 20, 24, 20);
        this.root.BackColor = MainWindowTheme.Background;
        this.root.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 100f));
        this.root.RowStyles.Add(
            new RowStyle(SizeType.Percent, 100f));
        this.root.RowStyles.Add(
            new RowStyle(SizeType.Absolute, 44f));
        this.root.RowStyles.Add(
            new RowStyle(SizeType.Absolute, 58f));

        var label = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            Text = prompt,
            ForeColor = MainWindowTheme.Text,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(4, 0, 4, 0),
        };

        this.nameTextBox.Dock = DockStyle.Fill;
        this.nameTextBox.Text = initialName;
        this.nameTextBox.Margin = new Padding(0, 4, 0, 4);
        MainWindowTheme.StyleTextBox(this.nameTextBox);

        var cancelButton = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Width = 104,
            Height = 36,
            Margin = Padding.Empty,
        };
        MainWindowTheme.StyleButton(cancelButton);

        this.acceptButton.Text = "Save";
        this.acceptButton.DialogResult = DialogResult.OK;
        this.acceptButton.Width = 104;
        this.acceptButton.Height = 36;
        this.acceptButton.Margin = new Padding(8, 0, 0, 0);
        MainWindowTheme.StyleButton(
            this.acceptButton,
            primary: true);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 14, 0, 0),
            BackColor = MainWindowTheme.Background,
        };
        buttons.Controls.Add(this.acceptButton);
        buttons.Controls.Add(cancelButton);

        this.root.Controls.Add(label, 0, 0);
        this.root.Controls.Add(this.nameTextBox, 0, 1);
        this.root.Controls.Add(buttons, 0, 2);
        this.Controls.Add(this.root);

        this.AcceptButton = this.acceptButton;
        this.CancelButton = cancelButton;
        this.nameTextBox.TextChanged += this.NameTextBox_OnTextChanged;
        this.UpdateAcceptState();
        this.ResizeToContent();
    }

    public string ListName => this.nameTextBox.Text.Trim();

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        this.ResizeToContent();
        this.nameTextBox.Focus();
        this.nameTextBox.SelectAll();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.nameTextBox.TextChanged -=
                this.NameTextBox_OnTextChanged;
        }

        base.Dispose(disposing);
    }

    private void NameTextBox_OnTextChanged(
        object? sender,
        EventArgs e)
    {
        this.UpdateAcceptState();
    }

    private void UpdateAcceptState()
    {
        this.acceptButton.Enabled =
            this.ListName.Length is > 0 and <= 120;
    }

    private void ResizeToContent()
    {
        var scale = this.DeviceDpi / 96f;
        this.ClientSize = new Size(
            Math.Max(
                580,
                (int)Math.Ceiling(580 * scale)),
            Math.Max(
                260,
                (int)Math.Ceiling(260 * scale)));
    }
}
