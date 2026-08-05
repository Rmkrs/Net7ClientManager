// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using Net7ClientManager.Core;
using Net7ClientManager.Models;

internal sealed class ChatCompanionOptionsForm : ThemedForm
{
    private readonly CheckBox orderByRecentCheckBox = new();
    private readonly CheckBox keepFirstViewCheckBox = new();
    private readonly CheckBox tellViewsCheckBox = new();
    private readonly CheckBox gameColorsCheckBox = new();
    private readonly ComboBox messageBoxLocationComboBox = new();
    private bool centeredOnOwner;

    public ChatCompanionOptionsForm(ChatCompanionSettings source)
    {
        ArgumentNullException.ThrowIfNull(source);

        this.Text = "Chat Companion options";
        this.Icon = ResourceLoader.Net7ClientManagerIcon;
        this.StartPosition = FormStartPosition.CenterParent;
        this.ShowInTaskbar = false;
        this.ClientSize = new Size(600, 570);
        this.MinimumSize = new Size(540, 540);
        this.BackColor = MainWindowTheme.Background;
        this.ForeColor = MainWindowTheme.Text;
        this.Font = MainWindowTheme.CreateBodyFont();
        this.ConfigureWindowChrome(
            allowResize: false,
            showMinimizeButton: false,
            showMaximizeButton: false);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20, 28, 20, 24),
            ColumnCount = 1,
            RowCount = 6,
            BackColor = MainWindowTheme.Background,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 88f));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        ConfigureCheckBox(
            this.orderByRecentCheckBox,
            "Show recently active chats first",
            "A view moves up when a new message arrives.",
            source.OrderViewsByRecentMessage);
        ConfigureCheckBox(
            this.keepFirstViewCheckBox,
            "Keep the first view at the top",
            "Useful when your first view is an always-visible overview.",
            source.KeepFirstViewAtTop);
        ConfigureCheckBox(
            this.tellViewsCheckBox,
            "Create a chat view for each Tell conversation",
            "Messages to and from the same pilot stay together.",
            source.CreateTellConversationViews);
        ConfigureCheckBox(
            this.gameColorsCheckBox,
            "Match the game's message colours",
            "Chat Companion follows the colours chosen in the game.",
            source.UseGameChatColors);

        this.keepFirstViewCheckBox.Enabled =
            this.orderByRecentCheckBox.Checked;
        this.orderByRecentCheckBox.CheckedChanged += (_, _) =>
            this.keepFirstViewCheckBox.Enabled =
                this.orderByRecentCheckBox.Checked;

        this.messageBoxLocationComboBox.DropDownStyle =
            ComboBoxStyle.DropDownList;
        this.messageBoxLocationComboBox.Items.AddRange(["Bottom", "Top"]);
        this.messageBoxLocationComboBox.SelectedItem = source.ComposerAtTop
            ? "Top"
            : "Bottom";
        MainWindowTheme.StyleComboBox(this.messageBoxLocationComboBox);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 58,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 8, 0, 10),
            BackColor = MainWindowTheme.Background,
        };
        var saveButton = new Button
        {
            Text = "Save",
            Size = new Size(104, 36),
            Margin = Padding.Empty,
        };
        MainWindowTheme.StyleButton(saveButton, primary: true);
        saveButton.Click += (_, _) =>
        {
            this.OrderViewsByRecentMessage =
                this.orderByRecentCheckBox.Checked;
            this.KeepFirstViewAtTop =
                this.keepFirstViewCheckBox.Checked;
            this.CreateTellConversationViews =
                this.tellViewsCheckBox.Checked;
            this.UseGameChatColors =
                this.gameColorsCheckBox.Checked;
            this.ComposerAtTop = string.Equals(
                Convert.ToString(
                    this.messageBoxLocationComboBox.SelectedItem),
                "Top",
                StringComparison.Ordinal);
            this.Saved?.Invoke(this, EventArgs.Empty);
            this.Close();
        };

        var cancelButton = new Button
        {
            Text = "Cancel",
            Size = new Size(104, 36),
            Margin = new Padding(8, 0, 0, 0),
        };
        MainWindowTheme.StyleButton(cancelButton);
        cancelButton.Click += (_, _) => this.Close();
        actions.Controls.Add(saveButton);
        actions.Controls.Add(cancelButton);

        root.Controls.Add(
            CreateOptionPanel(this.orderByRecentCheckBox),
            0,
            0);
        root.Controls.Add(
            CreateOptionPanel(this.keepFirstViewCheckBox),
            0,
            1);
        root.Controls.Add(
            CreateOptionPanel(this.tellViewsCheckBox),
            0,
            2);
        root.Controls.Add(
            CreateOptionPanel(this.gameColorsCheckBox),
            0,
            3);
        root.Controls.Add(
            CreateMessageBoxLocationPanel(
                this.messageBoxLocationComboBox),
            0,
            4);
        root.Controls.Add(actions, 0, 5);
        this.Controls.Add(root);

        this.AcceptButton = saveButton;
        this.CancelButton = cancelButton;
    }

    public event EventHandler? Saved;

    public bool OrderViewsByRecentMessage { get; private set; }

    public bool KeepFirstViewAtTop { get; private set; }

    public bool CreateTellConversationViews { get; private set; }

    public bool UseGameChatColors { get; private set; }

    public bool ComposerAtTop { get; private set; }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        if (this.centeredOnOwner)
        {
            return;
        }

        this.centeredOnOwner = true;
        this.CenterToParent();
    }

    private static void ConfigureCheckBox(
        CheckBox checkBox,
        string text,
        string accessibleDescription,
        bool isChecked)
    {
        checkBox.AutoSize = true;
        checkBox.Text = text;
        checkBox.Checked = isChecked;
        checkBox.ForeColor = MainWindowTheme.Text;
        checkBox.BackColor = MainWindowTheme.ElevatedPanel;
        checkBox.Font = MainWindowTheme.CreateBodyFont();
        checkBox.AccessibleDescription = accessibleDescription;
    }

    private static Control CreateOptionPanel(CheckBox checkBox)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 10),
            Padding = new Padding(12, 10, 12, 10),
            BackColor = MainWindowTheme.ElevatedPanel,
        };
        checkBox.Dock = DockStyle.Top;
        panel.Controls.Add(checkBox);

        if (!string.IsNullOrWhiteSpace(checkBox.AccessibleDescription))
        {
            var description = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 25,
                Text = checkBox.AccessibleDescription,
                ForeColor = MainWindowTheme.MutedText,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
            };
            panel.Controls.Add(description);
        }

        return panel;
    }

    private static Control CreateMessageBoxLocationPanel(
        ComboBox locationComboBox)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 10),
            Padding = new Padding(12, 10, 12, 10),
            BackColor = MainWindowTheme.ElevatedPanel,
            ColumnCount = 2,
            RowCount = 2,
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var label = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Message box location",
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = MainWindowTheme.Text,
        };
        locationComboBox.Dock = DockStyle.Fill;
        locationComboBox.Margin = Padding.Empty;

        var description = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Choose whether the send box appears below or above the messages.",
            ForeColor = MainWindowTheme.MutedText,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
        };
        panel.SetColumnSpan(description, 2);

        panel.Controls.Add(label, 0, 0);
        panel.Controls.Add(locationComboBox, 1, 0);
        panel.Controls.Add(description, 0, 1);
        return panel;
    }
}
