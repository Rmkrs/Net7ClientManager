// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using Net7ClientManager.Core;
using Net7ClientManager.Models;
using Net7ClientManager.Services;

internal sealed class ChatCompanionViewEditorForm : ThemedForm
{
    private readonly TextBox nameTextBox = new();
    private readonly CheckedListBox channelsList = new();
    private readonly CheckBox showUnreadBadgeCheckBox = new();
    private readonly Button saveButton = new();
    private bool suppressChannelCheckRefresh;

    public ChatCompanionViewEditorForm(
        ChatCompanionViewSettings source,
        bool isNew)
    {
        ArgumentNullException.ThrowIfNull(source);

        this.Text = isNew ? "New chat view" : "Edit chat view";
        this.Icon = ResourceLoader.Net7ClientManagerIcon;
        this.StartPosition = FormStartPosition.CenterParent;
        this.ShowInTaskbar = false;
        this.ClientSize = new Size(470, 700);
        this.MinimumSize = new Size(400, 550);
        this.BackColor = MainWindowTheme.Background;
        this.ForeColor = MainWindowTheme.Text;
        this.Font = MainWindowTheme.CreateBodyFont();
        this.ConfigureWindowChrome(
            allowResize: true,
            showMinimizeButton: false,
            showMaximizeButton: false);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18, 32, 18, 24),
            ColumnCount = 1,
            RowCount = 6,
            BackColor = MainWindowTheme.Background,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62f));

        var nameLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = "View name",
            TextAlign = ContentAlignment.BottomLeft,
            ForeColor = MainWindowTheme.MutedText,
        };

        this.nameTextBox.Dock = DockStyle.Fill;
        this.nameTextBox.MaxLength = 48;
        this.nameTextBox.Text = source.Name;
        MainWindowTheme.StyleTextBox(this.nameTextBox);
        this.nameTextBox.TextChanged += (_, _) =>
            this.RefreshSaveButton();

        var channelHeader = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = new Padding(0, 7, 0, 5),
            BackColor = MainWindowTheme.Background,
        };
        channelHeader.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 100f));
        channelHeader.ColumnStyles.Add(
            new ColumnStyle(SizeType.Absolute, 90f));
        channelHeader.ColumnStyles.Add(
            new ColumnStyle(SizeType.Absolute, 90f));

        var channelLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Messages shown in this view",
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = MainWindowTheme.MutedText,
        };
        var selectAllButton = new Button
        {
            Dock = DockStyle.Fill,
            Text = "Select all",
            Margin = new Padding(4, 0, 0, 0),
        };
        MainWindowTheme.StyleButton(selectAllButton);
        selectAllButton.Click += (_, _) =>
            this.SetAllChannelsChecked(true);

        var clearButton = new Button
        {
            Dock = DockStyle.Fill,
            Text = "Clear",
            Margin = new Padding(4, 0, 0, 0),
        };
        MainWindowTheme.StyleButton(clearButton);
        clearButton.Click += (_, _) =>
            this.SetAllChannelsChecked(false);

        channelHeader.Controls.Add(channelLabel, 0, 0);
        channelHeader.Controls.Add(selectAllButton, 1, 0);
        channelHeader.Controls.Add(clearButton, 2, 0);

        this.channelsList.Dock = DockStyle.Fill;
        this.channelsList.CheckOnClick = true;
        this.channelsList.IntegralHeight = false;
        this.channelsList.BorderStyle = BorderStyle.FixedSingle;
        this.channelsList.BackColor = MainWindowTheme.ElevatedPanel;
        this.channelsList.ForeColor = MainWindowTheme.Text;
        this.channelsList.Font = MainWindowTheme.CreateBodyFont();

        foreach (var channel in ChatChannelPresentation.FilterChannelNames)
        {
            var isChecked = source.IncludeAllMessages ||
                source.IncludedChannels.Contains(
                    channel,
                    StringComparer.OrdinalIgnoreCase);
            this.channelsList.Items.Add(channel, isChecked);
        }

        this.channelsList.ItemCheck += this.ChannelsList_OnItemCheck;

        this.showUnreadBadgeCheckBox.AutoSize = true;
        this.showUnreadBadgeCheckBox.Dock = DockStyle.Fill;
        this.showUnreadBadgeCheckBox.Text =
            "Show an unread-message badge for this view";
        this.showUnreadBadgeCheckBox.Checked = source.ShowUnreadBadge;
        this.showUnreadBadgeCheckBox.ForeColor = MainWindowTheme.Text;
        this.showUnreadBadgeCheckBox.BackColor = MainWindowTheme.Background;
        this.showUnreadBadgeCheckBox.Font = MainWindowTheme.CreateBodyFont();

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 8, 0, 12),
            BackColor = MainWindowTheme.Background,
        };

        this.saveButton.Text = "Save";
        this.saveButton.Size = new Size(96, 34);
        this.saveButton.Margin = Padding.Empty;
        MainWindowTheme.StyleButton(this.saveButton, primary: true);
        this.saveButton.Click += this.SaveButton_OnClick;

        var cancelButton = new Button
        {
            Text = "Cancel",
            Size = new Size(96, 34),
            Margin = new Padding(8, 0, 0, 0),
            DialogResult = DialogResult.Cancel,
        };
        MainWindowTheme.StyleButton(cancelButton);

        actions.Controls.Add(this.saveButton);
        actions.Controls.Add(cancelButton);

        root.Controls.Add(nameLabel, 0, 0);
        root.Controls.Add(this.nameTextBox, 0, 1);
        root.Controls.Add(channelHeader, 0, 2);
        root.Controls.Add(this.channelsList, 0, 3);
        root.Controls.Add(this.showUnreadBadgeCheckBox, 0, 4);
        root.Controls.Add(actions, 0, 5);
        this.Controls.Add(root);

        this.AcceptButton = this.saveButton;
        this.CancelButton = cancelButton;
        this.Result = source;
        this.RefreshSaveButton();
    }

    public ChatCompanionViewSettings Result { get; private set; }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        this.nameTextBox.Focus();
        this.nameTextBox.SelectAll();
    }

    private void SetAllChannelsChecked(bool isChecked)
    {
        this.suppressChannelCheckRefresh = true;

        try
        {
            for (var index = 0;
                 index < this.channelsList.Items.Count;
                 index++)
            {
                this.channelsList.SetItemChecked(index, isChecked);
            }
        }
        finally
        {
            this.suppressChannelCheckRefresh = false;
        }

        this.RefreshSaveButton();
    }

    private void ChannelsList_OnItemCheck(
        object? sender,
        ItemCheckEventArgs e)
    {
        if (this.suppressChannelCheckRefresh)
        {
            return;
        }

        this.RefreshSaveButton(e.Index, e.NewValue);
    }

    private void RefreshSaveButton(
        int pendingIndex = -1,
        CheckState pendingState = CheckState.Indeterminate)
    {
        var checkedCount = this.channelsList.CheckedItems.Count;

        if (pendingIndex >= 0 &&
            pendingIndex < this.channelsList.Items.Count)
        {
            var currentlyChecked =
                this.channelsList.GetItemChecked(pendingIndex);
            var willBeChecked = pendingState == CheckState.Checked;

            if (currentlyChecked != willBeChecked)
            {
                checkedCount += willBeChecked ? 1 : -1;
            }
        }

        this.saveButton.Enabled =
            !string.IsNullOrWhiteSpace(this.nameTextBox.Text) &&
            checkedCount > 0;
    }

    private void SaveButton_OnClick(
        object? sender,
        EventArgs e)
    {
        if (!this.saveButton.Enabled)
        {
            return;
        }

        var channels = this.channelsList.CheckedItems
            .Cast<string>()
            .ToList();
        var allSelected = channels.Count ==
            ChatChannelPresentation.FilterChannelNames.Count;

        this.Result = new ChatCompanionViewSettings
        {
            Id = this.Result.Id,
            Name = this.nameTextBox.Text.Trim(),
            IncludeAllMessages = allSelected,
            ShowUnreadBadge = this.showUnreadBadgeCheckBox.Checked,
            IncludedChannels = allSelected ? [] : channels,
        };
        this.DialogResult = DialogResult.OK;
        this.Close();
    }
}
