// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Net7ClientManager.ChatJournal;
using Net7ClientManager.Core;
using Net7ClientManager.Models;
using Net7ClientManager.Observations;
using Net7ClientManager.Services;
using Net7ClientManager.Win32;

internal sealed class ChatCompanionForm : ThemedForm
{
    private const int MaximumRenderedMessages = 5000;
    private const int ViewSidebarWidth = 220;

    private readonly ClientManager clientManager;
    private readonly ChatCompanionSettings settings;
    private readonly ChatCompanionPilotSettings pilotSettings;
    private readonly uint characterId;
    private readonly int processId;
    private readonly Guid? assignedSlotId;
    private readonly WindowPlacementBinding placementBinding;
    private readonly System.Windows.Forms.Timer refreshTimer = new()
    {
        Interval = 1000,
    };
    private readonly System.Windows.Forms.Timer searchTimer = new()
    {
        Interval = 220,
    };
    private readonly ToolTip toolTip = new();
    private readonly TableLayoutPanel rootLayout = new();
    private readonly TableLayoutPanel transcriptLayout = new();
    private readonly Control chatWorkspace;
    private readonly Control composerPanel;
    private readonly ListBox viewListBox = new();
    private readonly Button editViewButton = new();
    private readonly Button deleteViewButton = new();
    private readonly TextBox searchTextBox = new();
    private readonly Button clearSearchButton = new();
    private readonly DataGridView transcriptGrid =
        new DoubleBufferedDataGridView();
    private readonly ComboBox destinationComboBox = new();
    private readonly ComboBox recipientComboBox = new();
    private readonly TextBox messageTextBox = new();
    private readonly Button sendButton = new();
    private readonly Label composerStatusLabel = new();
    private readonly ContextMenuStrip transcriptContextMenu = new();
    private readonly ToolStripMenuItem copyCellMenuItem = new();
    private readonly ToolStripMenuItem copyRowMenuItem = new();
    private readonly List<PresentedChatEntry> presentedEntries = [];
    private readonly HashSet<string> knownEntryIds =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, ChatViewRuntime> viewStates =
        new(StringComparer.Ordinal);

    private string pilotName;
    private long renderedRevision = -1;
    private string renderedChannelNameFingerprint = "";
    private string renderedChatColorFingerprint = "";
    private IReadOnlyDictionary<string, Color> chatColors =
        new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
    private bool refreshPending;
    private bool companionUiCommandPending;
    private bool updatingViewList;
    private bool applyingHistory;
    private bool applyingComposerBinding;
    private bool initialTranscriptLoaded;
    private string? selectedViewKey;
    private string? composerOperationStatus;
    private Color composerOperationStatusColor;
    private int messageHistoryIndex = -1;
    private string messageHistoryDraft = "";
    private string? sortedColumnName;
    private ListSortDirection sortDirection =
        ListSortDirection.Ascending;
    private DataGridViewCell? contextMenuCell;
    private DateTime timestampDisplayDate = DateTime.Now.Date;
    private ChatCompanionOptionsForm? optionsForm;
    private bool preserveOpenPreferenceOnClose;

    public ChatCompanionForm(
        ClientManager clientManager,
        int initialProcessId,
        Guid? assignedSlotId,
        uint characterId,
        string pilotName)
    {
        this.clientManager = clientManager ??
            throw new ArgumentNullException(nameof(clientManager));
        this.settings = this.clientManager.ChatCompanionSettings;
        this.settings.EnsureDefaults();
        this.pilotSettings = this.settings.GetOrCreatePilot(characterId);
        this.processId = initialProcessId;
        this.assignedSlotId = assignedSlotId;
        this.characterId = characterId;
        this.pilotName = string.IsNullOrWhiteSpace(pilotName)
            ? string.Concat(
                "Pilot ",
                characterId.ToString(CultureInfo.InvariantCulture))
            : pilotName.Trim();
        this.RemoveCurrentPilotFromTellState();

        this.Text = string.Concat(
            "Chat Companion · ",
            this.pilotName);
        this.Icon = ResourceLoader.Net7ClientManagerIcon;
        this.StartPosition = FormStartPosition.CenterScreen;
        this.Size = new Size(1120, 700);
        this.MinimumSize = new Size(760, 440);
        this.BackColor = MainWindowTheme.Background;
        this.ForeColor = MainWindowTheme.Text;
        this.Font = MainWindowTheme.CreateBodyFont();
        this.DoubleBuffered = true;
        this.ConfigureWindowChrome(
            allowResize: true,
            showMinimizeButton: true,
            showMaximizeButton: true);

        this.rootLayout.Dock = DockStyle.Fill;
        this.rootLayout.Padding = new Padding(12, 12, 12, 12);
        this.rootLayout.ColumnCount = 1;
        this.rootLayout.RowCount = 1;
        this.rootLayout.RowStyles.Add(
            new RowStyle(SizeType.Percent, 100f));
        this.rootLayout.BackColor = MainWindowTheme.Background;
        this.composerPanel = this.CreateComposerPanel();
        this.chatWorkspace = this.CreateChatWorkspace();
        this.rootLayout.Controls.Add(this.chatWorkspace, 0, 0);
        this.Controls.Add(this.rootLayout);
        this.ApplyComposerPosition();

        this.placementBinding = this.clientManager.BindClientWindowPlacement(
            this,
            "chat-companion",
            initialProcessId);

        this.clientManager.ChatJournalChanged +=
            this.ClientManager_OnChatJournalChanged;
        this.refreshTimer.Tick += this.RefreshTimer_OnTick;
        this.searchTimer.Tick += this.SearchTimer_OnTick;
        this.refreshTimer.Start();

        this.RefreshRecipientChoices();
        this.RefreshTranscript(force: true);
        this.RefreshConnectionState();
    }

    public uint CharacterId => this.characterId;

    public int ProcessId => this.processId;

    public Guid? AssignedSlotId => this.assignedSlotId;

    public bool PreserveOpenPreferenceOnClose =>
        this.preserveOpenPreferenceOnClose;

    public void ClosePreservingOpenPreference()
    {
        this.preserveOpenPreferenceOnClose = true;
        this.Close();
    }

    public void UpdatePilotName(string nextPilotName)
    {
        if (string.IsNullOrWhiteSpace(nextPilotName))
        {
            return;
        }

        nextPilotName = nextPilotName.Trim();

        if (string.Equals(
                this.pilotName,
                nextPilotName,
                StringComparison.Ordinal))
        {
            return;
        }

        this.pilotName = nextPilotName;
        this.RemoveCurrentPilotFromTellState();
        this.Text = string.Concat(
            "Chat Companion · ",
            this.pilotName);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        this.RefreshTranscript(force: true);
        this.RefreshConnectionState();
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        this.MarkSelectedViewRead();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.refreshTimer.Stop();
            this.refreshTimer.Tick -= this.RefreshTimer_OnTick;
            this.refreshTimer.Dispose();
            this.searchTimer.Stop();
            this.searchTimer.Tick -= this.SearchTimer_OnTick;
            this.searchTimer.Dispose();
            this.clientManager.ChatJournalChanged -=
                this.ClientManager_OnChatJournalChanged;

            if (this.optionsForm is { IsDisposed: false } optionsForm)
            {
                optionsForm.Saved -=
                    this.ChatCompanionOptionsForm_OnSaved;
                optionsForm.FormClosed -=
                    this.ChatCompanionOptionsForm_OnFormClosed;
                optionsForm.Close();
                optionsForm.Dispose();
            }

            this.optionsForm = null;
            this.transcriptContextMenu.Dispose();
            this.toolTip.Dispose();
            this.placementBinding.Dispose();
        }

        base.Dispose(disposing);
    }

    private Control CreateChatWorkspace()
    {
        var workspace = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = MainWindowTheme.Background,
        };
        workspace.ColumnStyles.Add(
            new ColumnStyle(SizeType.Absolute, ViewSidebarWidth));
        workspace.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 100f));
        workspace.Controls.Add(this.CreateViewSidebar(), 0, 0);
        workspace.Controls.Add(this.CreateTranscriptPanel(), 1, 0);
        return workspace;
    }

    private Control CreateViewSidebar()
    {
        var sidebar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 10, 0),
            Padding = Padding.Empty,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = MainWindowTheme.Header,
        };
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var heading = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = new Padding(10, 4, 5, 4),
            BackColor = MainWindowTheme.Header,
        };
        heading.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 100f));
        heading.ColumnStyles.Add(
            new ColumnStyle(SizeType.Absolute, 70f));
        var title = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Views",
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = MainWindowTheme.Accent,
            Font = MainWindowTheme.CreateHeadingFont(10f),
        };
        var optionsButton = new Button
        {
            Dock = DockStyle.Fill,
            Text = "Options",
            Margin = Padding.Empty,
        };
        MainWindowTheme.StyleButton(optionsButton);
        optionsButton.Click += this.OptionsButton_OnClick;
        heading.Controls.Add(title, 0, 0);
        heading.Controls.Add(optionsButton, 1, 0);

        var actions = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = new Padding(5, 2, 5, 4),
            BackColor = MainWindowTheme.Header,
        };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34f));

        var addViewButton = new Button
        {
            Dock = DockStyle.Fill,
            Text = "New",
            Margin = new Padding(0, 0, 3, 0),
        };
        MainWindowTheme.StyleButton(addViewButton, primary: true);
        addViewButton.Click += this.AddViewButton_OnClick;

        this.editViewButton.Dock = DockStyle.Fill;
        this.editViewButton.Text = "Edit";
        this.editViewButton.Margin = new Padding(0, 0, 3, 0);
        MainWindowTheme.StyleButton(this.editViewButton);
        this.editViewButton.Click += this.EditViewButton_OnClick;

        this.deleteViewButton.Dock = DockStyle.Fill;
        this.deleteViewButton.Text = "Delete";
        this.deleteViewButton.Margin = Padding.Empty;
        MainWindowTheme.StyleButton(this.deleteViewButton, danger: true);
        this.deleteViewButton.Click += this.DeleteViewButton_OnClick;

        actions.Controls.Add(addViewButton, 0, 0);
        actions.Controls.Add(this.editViewButton, 1, 0);
        actions.Controls.Add(this.deleteViewButton, 2, 0);

        this.viewListBox.Dock = DockStyle.Fill;
        this.viewListBox.Margin = new Padding(5, 0, 5, 5);
        this.viewListBox.BorderStyle = BorderStyle.FixedSingle;
        this.viewListBox.BackColor = MainWindowTheme.Panel;
        this.viewListBox.ForeColor = MainWindowTheme.Text;
        this.viewListBox.Font = MainWindowTheme.CreateBodyFont();
        this.viewListBox.DrawMode = DrawMode.OwnerDrawFixed;
        this.viewListBox.ItemHeight = 34;
        this.viewListBox.IntegralHeight = false;
        this.viewListBox.HorizontalScrollbar = false;
        this.viewListBox.DrawItem += this.ViewListBox_OnDrawItem;
        this.viewListBox.SelectedIndexChanged +=
            this.ViewListBox_OnSelectedIndexChanged;
        this.viewListBox.DoubleClick += (_, _) =>
            this.QueueCompanionUiCommand(
                "open the chat view editor",
                this.EditSelectedView);
        this.viewListBox.KeyDown += this.ViewListBox_OnKeyDown;

        this.toolTip.SetToolTip(
            addViewButton,
            "Create a view and choose which messages it contains.");
        this.toolTip.SetToolTip(
            this.editViewButton,
            "Rename the selected view or change its channel filters.");
        this.toolTip.SetToolTip(
            this.deleteViewButton,
            "Delete the selected configured view.");
        this.toolTip.SetToolTip(
            optionsButton,
            "Choose chat ordering, Tell views, colours, and the message box location.");

        sidebar.Controls.Add(heading, 0, 0);
        sidebar.Controls.Add(actions, 0, 1);
        sidebar.Controls.Add(this.viewListBox, 0, 2);
        return sidebar;
    }

    private Control CreateTranscriptPanel()
    {
        this.transcriptLayout.Dock = DockStyle.Fill;
        this.transcriptLayout.ColumnCount = 1;
        this.transcriptLayout.RowCount = 3;
        this.transcriptLayout.Margin = Padding.Empty;
        this.transcriptLayout.Padding = Padding.Empty;
        this.transcriptLayout.BackColor = MainWindowTheme.Background;

        var searchPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = new Padding(0, 0, 0, 7),
            BackColor = MainWindowTheme.Background,
        };
        searchPanel.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 100f));
        searchPanel.ColumnStyles.Add(
            new ColumnStyle(SizeType.Absolute, 74f));

        this.searchTextBox.Dock = DockStyle.Fill;
        this.searchTextBox.PlaceholderText =
            "Search timestamp, channel, player, or message...";
        MainWindowTheme.StyleTextBox(this.searchTextBox);
        this.searchTextBox.TextChanged += (_, _) =>
        {
            this.searchTimer.Stop();
            this.searchTimer.Start();
            this.clearSearchButton.Enabled =
                this.searchTextBox.TextLength > 0;
        };

        this.clearSearchButton.Dock = DockStyle.Fill;
        this.clearSearchButton.Text = "Clear";
        this.clearSearchButton.Margin = new Padding(6, 0, 0, 0);
        MainWindowTheme.StyleButton(this.clearSearchButton);
        this.clearSearchButton.Enabled = false;
        this.clearSearchButton.Click += (_, _) =>
            this.searchTextBox.Clear();

        searchPanel.Controls.Add(this.searchTextBox, 0, 0);
        searchPanel.Controls.Add(this.clearSearchButton, 1, 0);

        this.ConfigureTranscriptGrid();
        this.transcriptLayout.Controls.Add(searchPanel, 0, 0);
        this.transcriptLayout.Controls.Add(this.transcriptGrid, 0, 1);
        this.transcriptLayout.Controls.Add(this.composerPanel, 0, 2);
        return this.transcriptLayout;
    }

    private Control CreateComposerPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = MainWindowTheme.ElevatedPanel,
            Padding = new Padding(8),
            ColumnCount = 4,
            RowCount = 2,
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132f));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 184f));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));

        this.destinationComboBox.Dock = DockStyle.Fill;
        this.destinationComboBox.DropDownStyle =
            ComboBoxStyle.DropDownList;
        this.destinationComboBox.FormattingEnabled = true;
        this.destinationComboBox.Format += (_, args) =>
        {
            if (args.ListItem is ChatSendDestination destination)
            {
                args.Value = ChatSendDestinationCatalog.GetDisplayName(
                    destination);
            }
        };
        MainWindowTheme.StyleComboBox(this.destinationComboBox);
        this.RefreshDestinationChoices();
        this.destinationComboBox.SelectedIndexChanged += (_, _) =>
        {
            this.ClearComposerOperationStatus();
            this.RefreshComposerState();
        };

        this.recipientComboBox.Dock = DockStyle.Fill;
        this.recipientComboBox.DropDownStyle = ComboBoxStyle.DropDown;
        this.recipientComboBox.AutoCompleteMode =
            AutoCompleteMode.SuggestAppend;
        this.recipientComboBox.AutoCompleteSource =
            AutoCompleteSource.ListItems;
        this.recipientComboBox.MaxDropDownItems = 12;
        MainWindowTheme.StyleComboBox(this.recipientComboBox);
        this.recipientComboBox.Enabled = false;
        this.recipientComboBox.TextChanged += (_, _) =>
        {
            this.ClearComposerOperationStatus();
            this.RefreshComposerState();
        };

        this.messageTextBox.Dock = DockStyle.Fill;
        this.messageTextBox.Multiline = false;
        this.messageTextBox.AcceptsReturn = false;
        this.messageTextBox.ScrollBars = ScrollBars.None;
        this.messageTextBox.PlaceholderText = "Type a message...";
        MainWindowTheme.StyleTextBox(this.messageTextBox);
        this.messageTextBox.TextChanged += (_, _) =>
        {
            if (!this.applyingHistory)
            {
                this.messageHistoryIndex = -1;
                this.messageHistoryDraft = this.messageTextBox.Text;
            }

            this.ClearComposerOperationStatus();
            this.RefreshComposerState();
        };
        this.messageTextBox.KeyDown += this.MessageTextBox_OnKeyDown;

        this.sendButton.Dock = DockStyle.Fill;
        this.sendButton.Text = "Send";
        MainWindowTheme.StyleButton(this.sendButton, primary: true);
        this.sendButton.Click += this.SendButton_OnClick;

        this.composerStatusLabel.Dock = DockStyle.Fill;
        this.composerStatusLabel.TextAlign =
            ContentAlignment.MiddleLeft;
        this.composerStatusLabel.ForeColor =
            MainWindowTheme.MutedText;
        this.composerStatusLabel.AutoEllipsis = true;
        panel.SetColumnSpan(this.composerStatusLabel, 4);

        panel.Controls.Add(this.destinationComboBox, 0, 0);
        panel.Controls.Add(this.recipientComboBox, 1, 0);
        panel.Controls.Add(this.messageTextBox, 2, 0);
        panel.Controls.Add(this.sendButton, 3, 0);
        panel.Controls.Add(this.composerStatusLabel, 0, 1);

        this.toolTip.SetToolTip(
            this.recipientComboBox,
            "Type a pilot name or select a recent Tell recipient.");
        this.toolTip.SetToolTip(
            this.messageTextBox,
            "Enter sends. Up and Down recall previously sent messages.");

        this.RefreshComposerState();
        return panel;
    }

    private void ApplyComposerPosition()
    {
        if (this.transcriptLayout.IsDisposed ||
            this.transcriptGrid.IsDisposed ||
            this.composerPanel.IsDisposed)
        {
            return;
        }

        this.transcriptLayout.SuspendLayout();

        try
        {
            this.transcriptLayout.RowStyles.Clear();
            this.transcriptLayout.RowStyles.Add(
                new RowStyle(SizeType.Absolute, 40f));

            if (this.settings.ComposerAtTop)
            {
                this.transcriptLayout.RowStyles.Add(
                    new RowStyle(SizeType.Absolute, 76f));
                this.transcriptLayout.RowStyles.Add(
                    new RowStyle(SizeType.Percent, 100f));
                this.transcriptLayout.SetRow(this.composerPanel, 1);
                this.transcriptLayout.SetRow(this.transcriptGrid, 2);
            }
            else
            {
                this.transcriptLayout.RowStyles.Add(
                    new RowStyle(SizeType.Percent, 100f));
                this.transcriptLayout.RowStyles.Add(
                    new RowStyle(SizeType.Absolute, 76f));
                this.transcriptLayout.SetRow(this.transcriptGrid, 1);
                this.transcriptLayout.SetRow(this.composerPanel, 2);
            }
        }
        finally
        {
            this.transcriptLayout.ResumeLayout(performLayout: true);
        }
    }

    private void RefreshComposerState()
    {
        var tellPilot = this.GetSelectedTellPilot();

        if (!this.applyingComposerBinding &&
            !string.IsNullOrWhiteSpace(tellPilot))
        {
            this.applyingComposerBinding = true;

            try
            {
                if (this.destinationComboBox.Items.Contains(
                        ChatSendDestination.Tell) &&
                    !Equals(
                        this.destinationComboBox.SelectedItem,
                        ChatSendDestination.Tell))
                {
                    this.destinationComboBox.SelectedItem =
                        ChatSendDestination.Tell;
                }

                if (!string.Equals(
                        this.recipientComboBox.Text,
                        tellPilot,
                        StringComparison.Ordinal))
                {
                    this.recipientComboBox.Text = tellPilot;
                }
            }
            finally
            {
                this.applyingComposerBinding = false;
            }
        }

        var destination = !string.IsNullOrWhiteSpace(tellPilot)
            ? ChatSendDestination.Tell
            : this.destinationComboBox.SelectedItem is
                ChatSendDestination selected
                ? selected
                : ChatSendDestination.Local;
        var recipient = !string.IsNullOrWhiteSpace(tellPilot)
            ? tellPilot
            : this.recipientComboBox.Text.Trim();
        var isTell = destination == ChatSendDestination.Tell;
        var isTellConversation = !string.IsNullOrWhiteSpace(tellPilot);
        this.destinationComboBox.Enabled = !isTellConversation;
        this.recipientComboBox.Enabled =
            isTell && !isTellConversation;

        if (isTellConversation)
        {
            var bindingText = string.Concat(
                "Messages in this conversation are always sent to ",
                tellPilot,
                ".");
            this.toolTip.SetToolTip(
                this.destinationComboBox,
                bindingText);
            this.toolTip.SetToolTip(
                this.recipientComboBox,
                bindingText);
        }
        else
        {
            this.toolTip.SetToolTip(this.destinationComboBox, "");
            this.toolTip.SetToolTip(
                this.recipientComboBox,
                "Type a pilot name or select a recent Tell recipient.");
        }

        var destinationAvailable =
            this.clientManager.CanUseChatDestination(
                this.characterId,
                destination,
                out var unavailableReason);

        this.sendButton.Enabled =
            destinationAvailable &&
            !string.IsNullOrWhiteSpace(this.messageTextBox.Text) &&
            (!isTell || !string.IsNullOrWhiteSpace(recipient));

        if (!string.IsNullOrWhiteSpace(this.composerOperationStatus))
        {
            this.SetComposerStatus(
                this.composerOperationStatus,
                this.composerOperationStatusColor);
        }
        else if (!destinationAvailable)
        {
            this.SetComposerStatus(
                unavailableReason,
                MainWindowTheme.Warning);
        }
        else
        {
            this.SetComposerStatus("", MainWindowTheme.MutedText);
        }
    }

    private void RefreshDestinationChoices()
    {
        var previous = this.destinationComboBox.SelectedItem is
            ChatSendDestination selected
            ? selected
            : ChatSendDestination.Local;
        var available = this.clientManager
            .GetAvailableChatDestinations(this.characterId)
            .ToArray();

        if (available.Length == this.destinationComboBox.Items.Count &&
            available.Select((destination, index) =>
                    Equals(this.destinationComboBox.Items[index], destination))
                .All(matches => matches))
        {
            return;
        }

        this.destinationComboBox.BeginUpdate();

        try
        {
            this.destinationComboBox.Items.Clear();
            this.destinationComboBox.Items.AddRange(
                available.Cast<object>().ToArray());
            this.destinationComboBox.SelectedItem =
                available.Contains(previous)
                    ? previous
                    : available.FirstOrDefault();
        }
        finally
        {
            this.destinationComboBox.EndUpdate();
        }
    }

    private void RemoveCurrentPilotFromTellState()
    {
        if (string.IsNullOrWhiteSpace(this.pilotName))
        {
            return;
        }

        var changed =
            this.pilotSettings.RecentTellRecipients.RemoveAll(candidate =>
                string.Equals(
                    candidate,
                    this.pilotName,
                    StringComparison.OrdinalIgnoreCase)) > 0;
        changed |=
            this.pilotSettings.TellConversationPilots.RemoveAll(candidate =>
                string.Equals(
                    candidate,
                    this.pilotName,
                    StringComparison.OrdinalIgnoreCase)) > 0;

        if (changed)
        {
            this.clientManager.SaveSettings();
        }
    }

    private void RefreshRecipientChoices()
    {
        var currentText = this.recipientComboBox.Text;
        this.recipientComboBox.BeginUpdate();

        try
        {
            this.recipientComboBox.Items.Clear();
            this.recipientComboBox.Items.AddRange(
                this.pilotSettings.RecentTellRecipients
                    .Where(recipient => !string.Equals(
                        recipient,
                        this.pilotName,
                        StringComparison.OrdinalIgnoreCase))
                    .Cast<object>()
                    .ToArray());
        }
        finally
        {
            this.recipientComboBox.EndUpdate();
        }

        this.recipientComboBox.Text = currentText;
    }

    private void ClearComposerOperationStatus()
    {
        this.composerOperationStatus = null;
        this.composerOperationStatusColor = default;
    }

    private void SetComposerOperationStatus(
        string text,
        Color color)
    {
        this.composerOperationStatus = text;
        this.composerOperationStatusColor = color;
        this.SetComposerStatus(text, color);
    }

    private void SetComposerStatus(
        string text,
        Color color)
    {
        this.composerStatusLabel.ForeColor = color;
        this.composerStatusLabel.Text = text;
    }

    private async void SendButton_OnClick(
        object? sender,
        EventArgs e)
    {
        var tellPilot = this.GetSelectedTellPilot();
        ChatSendDestination destination;
        string recipient;

        if (!string.IsNullOrWhiteSpace(tellPilot))
        {
            destination = ChatSendDestination.Tell;
            recipient = tellPilot;
        }
        else if (this.destinationComboBox.SelectedItem is
                 ChatSendDestination selectedDestination)
        {
            destination = selectedDestination;
            recipient = this.recipientComboBox.Text.Trim();
        }
        else
        {
            return;
        }

        var message = this.messageTextBox.Text;
        var sentSuccessfully = false;
        this.sendButton.Enabled = false;
        this.SetComposerOperationStatus(
            "Sending...",
            MainWindowTheme.Accent);

        try
        {
            var result = await this.clientManager.SendChatMessageAsync(
                    this.characterId,
                    destination,
                    recipient,
                    message)
                .ConfigureAwait(true);

            if (result.Succeeded)
            {
                sentSuccessfully = true;
                this.RememberSentMessage(message);

                if (destination == ChatSendDestination.Tell)
                {
                    this.RememberTellRecipient(recipient);
                }

                this.messageTextBox.Clear();
                this.messageHistoryIndex = -1;
                this.messageHistoryDraft = "";
            }

            this.SetComposerOperationStatus(
                result.Succeeded ? "Sent." : result.Status,
                result.Succeeded
                    ? MainWindowTheme.Success
                    : MainWindowTheme.Warning);
        }
        catch (Exception exception)
        {
            this.SetComposerOperationStatus(
                string.Concat(
                    "Could not send the message: ",
                    exception.Message),
                MainWindowTheme.Warning);
        }
        finally
        {
            this.RefreshComposerState();

            if (sentSuccessfully)
            {
                this.RestoreComposerFocusAfterSend();
            }
        }
    }

    private void RestoreComposerFocusAfterSend()
    {
        if (this.IsDisposed ||
            this.Disposing ||
            !this.IsHandleCreated)
        {
            return;
        }

        if (this.WindowState == FormWindowState.Minimized)
        {
            this.WindowState = FormWindowState.Normal;
        }

        if (!this.Visible)
        {
            this.Show();
        }

        this.BringToFront();
        this.Activate();
        NativeMethods.FocusWindow(this.Handle);
        this.messageTextBox.Focus();
        this.messageTextBox.SelectionStart =
            this.messageTextBox.TextLength;
        this.messageTextBox.SelectionLength = 0;
    }

    private void RememberTellRecipient(string recipient)
    {
        if (string.IsNullOrWhiteSpace(recipient))
        {
            return;
        }

        recipient = ChatChannelPresentation.NormalizeTellPilotName(
            recipient);

        if (recipient.Length == 0)
        {
            return;
        }

        if (string.Equals(
                recipient,
                this.pilotName,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        this.pilotSettings.RecentTellRecipients.RemoveAll(candidate =>
            string.Equals(
                candidate,
                recipient,
                StringComparison.OrdinalIgnoreCase));
        this.pilotSettings.RecentTellRecipients.Insert(0, recipient);

        if (this.pilotSettings.RecentTellRecipients.Count > 30)
        {
            this.pilotSettings.RecentTellRecipients.RemoveRange(
                30,
                this.pilotSettings.RecentTellRecipients.Count - 30);
        }

        this.clientManager.SaveSettings();
        this.RefreshRecipientChoices();
        this.recipientComboBox.Text = recipient;
    }

    private void RememberSentMessage(string message)
    {
        message = message.Trim();

        if (message.Length == 0)
        {
            return;
        }

        this.settings.SentMessageHistory.RemoveAll(candidate =>
            string.Equals(candidate, message, StringComparison.Ordinal));
        this.settings.SentMessageHistory.Insert(0, message);

        if (this.settings.SentMessageHistory.Count > 100)
        {
            this.settings.SentMessageHistory.RemoveRange(
                100,
                this.settings.SentMessageHistory.Count - 100);
        }

        this.clientManager.SaveSettings();
    }

    private void MessageTextBox_OnKeyDown(
        object? sender,
        KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter &&
            !e.Shift &&
            this.sendButton.Enabled)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            this.SendButton_OnClick(this.sendButton, EventArgs.Empty);
            return;
        }

        if (e.Control || e.Alt || e.Shift)
        {
            return;
        }

        if (e.KeyCode == Keys.Up)
        {
            if (this.TryNavigateMessageHistory(older: true))
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
            }

            return;
        }

        if (e.KeyCode == Keys.Down &&
            this.TryNavigateMessageHistory(older: false))
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
    }

    private bool TryNavigateMessageHistory(bool older)
    {
        if (this.settings.SentMessageHistory.Count == 0)
        {
            return false;
        }

        if (older)
        {
            if (this.messageHistoryIndex < 0)
            {
                this.messageHistoryDraft = this.messageTextBox.Text;
            }

            if (this.messageHistoryIndex >=
                this.settings.SentMessageHistory.Count - 1)
            {
                return false;
            }

            this.messageHistoryIndex++;
            this.ApplyHistoryText(
                this.settings.SentMessageHistory[
                    this.messageHistoryIndex]);
            return true;
        }

        if (this.messageHistoryIndex < 0)
        {
            return false;
        }

        this.messageHistoryIndex--;
        this.ApplyHistoryText(
            this.messageHistoryIndex < 0
                ? this.messageHistoryDraft
                : this.settings.SentMessageHistory[
                    this.messageHistoryIndex]);
        return true;
    }

    private void ApplyHistoryText(string text)
    {
        this.applyingHistory = true;

        try
        {
            this.messageTextBox.Text = text;
            this.messageTextBox.SelectionStart =
                this.messageTextBox.TextLength;
        }
        finally
        {
            this.applyingHistory = false;
        }
    }

    private void ConfigureTranscriptGrid()
    {
        this.transcriptGrid.Dock = DockStyle.Fill;
        this.transcriptGrid.BackgroundColor = MainWindowTheme.Panel;
        this.transcriptGrid.BorderStyle = BorderStyle.None;
        this.transcriptGrid.GridColor = MainWindowTheme.Border;
        this.transcriptGrid.RowHeadersVisible = false;
        this.transcriptGrid.AllowUserToAddRows = false;
        this.transcriptGrid.AllowUserToDeleteRows = false;
        this.transcriptGrid.AllowUserToOrderColumns = true;
        this.transcriptGrid.AllowUserToResizeRows = false;
        this.transcriptGrid.ReadOnly = true;
        this.transcriptGrid.MultiSelect = true;
        this.transcriptGrid.SelectionMode =
            DataGridViewSelectionMode.FullRowSelect;
        this.transcriptGrid.AutoGenerateColumns = false;
        this.transcriptGrid.AutoSizeRowsMode =
            DataGridViewAutoSizeRowsMode.None;
        this.transcriptGrid.RowTemplate.Height = 25;
        this.transcriptGrid.ClipboardCopyMode =
            DataGridViewClipboardCopyMode.EnableWithoutHeaderText;
        this.transcriptGrid.EnableHeadersVisualStyles = false;
        this.transcriptGrid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = MainWindowTheme.Panel,
            ForeColor = MainWindowTheme.Text,
            SelectionBackColor = MainWindowTheme.ButtonHover,
            SelectionForeColor = MainWindowTheme.Text,
            Font = MainWindowTheme.CreateBodyFont(9.0f),
            Padding = new Padding(4, 2, 4, 2),
        };
        this.transcriptGrid.AlternatingRowsDefaultCellStyle =
            new DataGridViewCellStyle
            {
                BackColor = MainWindowTheme.ElevatedPanel,
                ForeColor = MainWindowTheme.Text,
                SelectionBackColor = MainWindowTheme.ButtonHover,
                SelectionForeColor = MainWindowTheme.Text,
            };
        this.transcriptGrid.ColumnHeadersDefaultCellStyle =
            new DataGridViewCellStyle
            {
                BackColor = MainWindowTheme.Header,
                ForeColor = MainWindowTheme.Accent,
                SelectionBackColor = MainWindowTheme.Header,
                SelectionForeColor = MainWindowTheme.Accent,
                Font = MainWindowTheme.CreateBodyFont(8.5f),
                Alignment = DataGridViewContentAlignment.MiddleLeft,
                Padding = new Padding(4, 2, 4, 2),
            };
        this.transcriptGrid.ColumnHeadersHeight = 28;
        this.transcriptGrid.ColumnHeadersHeightSizeMode =
            DataGridViewColumnHeadersHeightSizeMode.DisableResizing;

        this.transcriptGrid.Columns.Add(
            new DataGridViewTextBoxColumn
            {
                Name = "New",
                HeaderText = "",
                Width = 28,
                MinimumWidth = 28,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                Resizable = DataGridViewTriState.False,
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    Alignment = DataGridViewContentAlignment.MiddleCenter,
                    ForeColor = MainWindowTheme.Accent,
                    SelectionForeColor = MainWindowTheme.Accent,
                    Padding = Padding.Empty,
                },
            });
        this.transcriptGrid.Columns.Add(
            new DataGridViewTextBoxColumn
            {
                Name = "Timestamp",
                HeaderText = "Timestamp",
                Width = 156,
                SortMode = DataGridViewColumnSortMode.Automatic,
            });
        this.transcriptGrid.Columns.Add(
            new DataGridViewTextBoxColumn
            {
                Name = "Channel",
                HeaderText = "Channel",
                Width = 138,
                SortMode = DataGridViewColumnSortMode.Automatic,
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    ForeColor = MainWindowTheme.Accent,
                },
            });
        this.transcriptGrid.Columns.Add(
            new DataGridViewTextBoxColumn
            {
                Name = "Player",
                HeaderText = "Player",
                Width = 150,
                SortMode = DataGridViewColumnSortMode.Automatic,
            });
        this.transcriptGrid.Columns.Add(
            new DataGridViewTextBoxColumn
            {
                Name = "Message",
                HeaderText = "Message",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                MinimumWidth = 220,
                SortMode = DataGridViewColumnSortMode.Automatic,
            });

        this.transcriptGrid.SortCompare +=
            this.TranscriptGrid_OnSortCompare;
        this.transcriptGrid.Sorted += this.TranscriptGrid_OnSorted;
        this.transcriptGrid.CellDoubleClick +=
            this.TranscriptGrid_OnCellDoubleClick;
        this.transcriptGrid.CellMouseDown +=
            this.TranscriptGrid_OnCellMouseDown;
        this.transcriptGrid.CellMouseClick +=
            this.TranscriptGrid_OnCellMouseClick;
        this.transcriptGrid.Scroll += (_, _) =>
            this.MarkSelectedViewRead();

        this.copyCellMenuItem.Text = "Copy cell";
        this.copyCellMenuItem.Click += (_, _) =>
            this.CopyContextMenuCell();
        this.copyRowMenuItem.Text = "Copy row";
        this.copyRowMenuItem.Click += (_, _) =>
            this.CopyContextMenuRow();
        this.transcriptContextMenu.Items.Add(this.copyCellMenuItem);
        this.transcriptContextMenu.Items.Add(this.copyRowMenuItem);
        MainWindowTheme.StyleContextMenu(this.transcriptContextMenu);
    }

    private void TranscriptGrid_OnSortCompare(
        object? sender,
        DataGridViewSortCompareEventArgs e)
    {
        if (!string.Equals(
                this.transcriptGrid.Columns[e.Column.Index].Name,
                "Timestamp",
                StringComparison.Ordinal))
        {
            return;
        }

        var firstValue = this.transcriptGrid.Rows[e.RowIndex1]
            .Cells["Timestamp"].Tag is DateTimeOffset first
            ? first
            : DateTimeOffset.MinValue;
        var secondValue = this.transcriptGrid.Rows[e.RowIndex2]
            .Cells["Timestamp"].Tag is DateTimeOffset second
            ? second
            : DateTimeOffset.MinValue;
        e.SortResult = firstValue.CompareTo(secondValue);
        e.Handled = true;
    }

    private void TranscriptGrid_OnSorted(
        object? sender,
        EventArgs e)
    {
        if (this.transcriptGrid.SortedColumn == null ||
            this.transcriptGrid.SortOrder == SortOrder.None)
        {
            this.sortedColumnName = null;
            return;
        }

        this.sortedColumnName =
            this.transcriptGrid.SortedColumn.Name;
        this.sortDirection = this.transcriptGrid.SortOrder ==
            SortOrder.Descending
            ? ListSortDirection.Descending
            : ListSortDirection.Ascending;
    }

    private void TranscriptGrid_OnCellDoubleClick(
        object? sender,
        DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0)
        {
            return;
        }

        var value = Convert.ToString(
            this.transcriptGrid.Rows[e.RowIndex]
                .Cells[e.ColumnIndex].Value,
            CultureInfo.CurrentCulture)?.Trim();

        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var columnName =
            this.transcriptGrid.Columns[e.ColumnIndex].Name;

        if (string.Equals(
                columnName,
                "Channel",
                StringComparison.Ordinal))
        {
            this.PrimeComposerForChannel(value);
        }
        else if (string.Equals(
                     columnName,
                     "Player",
                     StringComparison.Ordinal))
        {
            var rowTag = this.transcriptGrid.Rows[e.RowIndex].Tag as
                ChatTranscriptRowTag;
            var tellPilot = rowTag?.ConversationPlayerName;
            this.PrimeComposerForTell(
                string.IsNullOrWhiteSpace(tellPilot)
                    ? value
                    : tellPilot);
        }
    }

    private void PrimeComposerForChannel(string channelName)
    {
        var definition = ChatSendDestinationCatalog.Ordered
            .FirstOrDefault(candidate =>
                ChatSendDestinationCatalog.ChannelNamesEqual(
                    candidate.DisplayName,
                    channelName));

        if (definition == null)
        {
            this.SetComposerOperationStatus(
                string.Concat(
                    channelName,
                    " is not a direct send destination."),
                MainWindowTheme.Warning);
            return;
        }

        if (!this.destinationComboBox.Items.Contains(
                definition.Destination))
        {
            this.SetComposerOperationStatus(
                string.Concat(
                    channelName,
                    " is not currently enabled in the game's monitored channels."),
                MainWindowTheme.Warning);
            return;
        }

        this.destinationComboBox.SelectedItem = definition.Destination;
        this.messageTextBox.Focus();
    }

    private void PrimeComposerForTell(string playerName)
    {
        playerName = ChatChannelPresentation.NormalizeTellPilotName(
            playerName);

        if (playerName.Length == 0)
        {
            return;
        }

        if (!this.destinationComboBox.Items.Contains(
                ChatSendDestination.Tell))
        {
            this.SetComposerOperationStatus(
                "Tell is not currently available.",
                MainWindowTheme.Warning);
            return;
        }

        this.destinationComboBox.SelectedItem =
            ChatSendDestination.Tell;
        this.recipientComboBox.Text = playerName;
        this.messageTextBox.Focus();
    }

    private void TranscriptGrid_OnCellMouseClick(
        object? sender,
        DataGridViewCellMouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left ||
            e.RowIndex < 0 ||
            this.transcriptGrid.Rows[e.RowIndex].Tag is not
                ChatTranscriptRowTag tag)
        {
            return;
        }

        var selected = this.GetSelectedView();

        if (selected == null)
        {
            return;
        }

        var clickedEntryIndex = this.presentedEntries.FindIndex(entry =>
            string.Equals(
                entry.Entry.EntryId,
                tag.EntryId,
                StringComparison.Ordinal));
        IReadOnlyList<string> entryIdsToDismiss = clickedEntryIndex < 0
            ? [tag.EntryId]
            : this.presentedEntries
                .Take(clickedEntryIndex + 1)
                .Where(selected.Matches)
                .Select(entry => entry.Entry.EntryId)
                .ToArray();

        if (!selected.DismissNewMarkers(entryIdsToDismiss))
        {
            return;
        }

        this.ApplyUnreadMarkersToCurrentGrid();
        this.viewListBox.Invalidate();
    }

    private void TranscriptGrid_OnCellMouseDown(
        object? sender,
        DataGridViewCellMouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right ||
            e.RowIndex < 0 ||
            e.ColumnIndex < 0 ||
            string.Equals(
                this.transcriptGrid.Columns[e.ColumnIndex].Name,
                "New",
                StringComparison.Ordinal))
        {
            return;
        }

        this.transcriptGrid.ClearSelection();
        this.transcriptGrid.CurrentCell =
            this.transcriptGrid.Rows[e.RowIndex]
                .Cells[e.ColumnIndex];
        this.transcriptGrid.Rows[e.RowIndex].Selected = true;
        this.contextMenuCell = this.transcriptGrid.CurrentCell;
        var header = this.transcriptGrid.Columns[e.ColumnIndex]
            .HeaderText;
        this.copyCellMenuItem.Text = string.Concat("Copy ", header);
        this.transcriptContextMenu.Show(
            this.transcriptGrid,
            this.transcriptGrid.PointToClient(Cursor.Position));
    }

    private void CopyContextMenuCell()
    {
        var text = Convert.ToString(
            this.contextMenuCell?.Value,
            CultureInfo.CurrentCulture);
        this.CopyToClipboard(text);
    }

    private void CopyContextMenuRow()
    {
        var row = this.contextMenuCell?.OwningRow;

        if (row == null)
        {
            return;
        }

        var text = string.Join(
            "\t",
            row.Cells.Cast<DataGridViewCell>()
                .Where(cell => !string.Equals(
                    cell.OwningColumn.Name,
                    "New",
                    StringComparison.Ordinal))
                .Select(cell => Convert.ToString(
                    cell.Value,
                    CultureInfo.CurrentCulture) ?? ""));
        this.CopyToClipboard(text);
    }

    private void CopyToClipboard(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        try
        {
            Clipboard.SetText(text);
        }
        catch (ExternalException exception)
        {
            this.SetComposerOperationStatus(
                string.Concat(
                    "Could not copy to the clipboard: ",
                    exception.Message),
                MainWindowTheme.Warning);
        }
    }

    private void ClientManager_OnChatJournalChanged(
        object? sender,
        ChatJournalChangedEventArgs e)
    {
        if (e.CharacterId != this.characterId ||
            this.IsDisposed ||
            this.Disposing ||
            !this.IsHandleCreated)
        {
            return;
        }

        if (this.InvokeRequired)
        {
            if (this.refreshPending)
            {
                return;
            }

            this.refreshPending = true;

            try
            {
                this.BeginInvoke(() =>
                {
                    this.refreshPending = false;
                    this.RefreshTranscript(force: false);
                });
            }
            catch (InvalidOperationException)
            {
                this.refreshPending = false;
            }

            return;
        }

        this.RefreshTranscript(force: false);
    }

    private void RefreshTimer_OnTick(
        object? sender,
        EventArgs e)
    {
        this.RefreshConnectionState();

        var currentDate = DateTime.Now.Date;

        if (currentDate != this.timestampDisplayDate)
        {
            this.timestampDisplayDate = currentDate;
            this.RefreshDisplayedTimestamps();
        }
    }

    private void SearchTimer_OnTick(
        object? sender,
        EventArgs e)
    {
        this.searchTimer.Stop();
        this.RefreshTranscriptGrid();
    }

    private void RefreshTranscript(bool force)
    {
        var snapshot = this.clientManager.GetChatJournalSnapshot(
            this.characterId,
            MaximumRenderedMessages);

        if (!force && snapshot.Revision == this.renderedRevision)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(snapshot.PilotName))
        {
            this.UpdatePilotName(snapshot.PilotName);
        }

        var observedChannelNames =
            ChatChannelPresentation.BuildObservedChannelNames(
                snapshot.Entries);
        var channelNameFingerprint = string.Join(
            "|",
            observedChannelNames
                .OrderBy(pair => pair.Key)
                .Select(pair => string.Concat(
                    pair.Key.ToString(CultureInfo.InvariantCulture),
                    "=",
                    pair.Value)));
        var nextPresentedEntries = snapshot.Entries
            .Select(entry => new PresentedChatEntry(
                entry,
                ChatChannelPresentation.Present(
                    entry,
                    observedChannelNames,
                    this.pilotName)))
            .ToList();
        var previousEntryCount = this.presentedEntries.Count;
        var previousSelectedViewKey = this.selectedViewKey;
        var previousEntriesArePrefix =
            previousEntryCount <= nextPresentedEntries.Count &&
            this.presentedEntries.Select(entry => entry.Entry.EntryId)
                .SequenceEqual(
                    nextPresentedEntries
                        .Take(previousEntryCount)
                        .Select(entry => entry.Entry.EntryId),
                    StringComparer.Ordinal);
        var canAppend =
            !force &&
            this.initialTranscriptLoaded &&
            previousEntriesArePrefix &&
            string.Equals(
                this.renderedChannelNameFingerprint,
                channelNameFingerprint,
                StringComparison.Ordinal) &&
            this.searchTextBox.TextLength == 0 &&
            string.IsNullOrWhiteSpace(this.sortedColumnName);
        var appendedEntries = canAppend
            ? nextPresentedEntries.Skip(previousEntryCount).ToArray()
            : [];
        var newEntries = this.initialTranscriptLoaded
            ? nextPresentedEntries
                .Where(entry =>
                    !this.knownEntryIds.Contains(
                        entry.Entry.EntryId))
                .ToList()
            : [];

        this.presentedEntries.Clear();
        this.presentedEntries.AddRange(nextPresentedEntries);
        this.knownEntryIds.Clear();
        this.knownEntryIds.UnionWith(
            nextPresentedEntries.Select(entry =>
                entry.Entry.EntryId));
        this.renderedRevision = snapshot.Revision;
        this.renderedChannelNameFingerprint = channelNameFingerprint;

        this.RememberTellConversationPilots(nextPresentedEntries);
        this.RefreshViewStates(newEntries);
        this.initialTranscriptLoaded = true;

        if (canAppend &&
            string.Equals(
                previousSelectedViewKey,
                this.selectedViewKey,
                StringComparison.Ordinal))
        {
            this.AppendTranscriptRows(appendedEntries);
        }
        else
        {
            this.RefreshTranscriptGrid();
        }
    }

    private void RememberTellConversationPilots(
        IEnumerable<PresentedChatEntry> entries)
    {
        var changed = false;

        foreach (var playerName in entries
                     .Where(entry => string.Equals(
                         entry.Presentation.ChannelName,
                         "Tell",
                         StringComparison.OrdinalIgnoreCase))
                     .Select(entry =>
                         entry.Presentation.ConversationPlayerName)
                     .OfType<string>()
                     .Where(name => !string.IsNullOrWhiteSpace(name))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.Equals(
                    playerName,
                    this.pilotName,
                    StringComparison.OrdinalIgnoreCase) ||
                this.pilotSettings.TellConversationPilots.Contains(
                    playerName,
                    StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            this.pilotSettings.TellConversationPilots.Add(playerName);
            changed = true;
        }

        if (changed)
        {
            this.settings.EnsureDefaults();
            this.clientManager.SaveSettings();
        }
    }

    private void RefreshViewStates(
        IReadOnlyList<PresentedChatEntry> newEntries)
    {
        var previousStates = this.viewStates.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal);
        this.viewStates.Clear();

        for (var index = 0;
             index < this.settings.Views.Count;
             index++)
        {
            var view = this.settings.Views[index];
            var key = GetConfiguredViewKey(view.Id);
            var state = new ChatViewRuntime(
                key,
                view.Name,
                view,
                tellPilot: null,
                configuredOrder: index);
            this.CopyRuntimeState(previousStates, state);
            this.viewStates.Add(key, state);
        }

        if (this.settings.CreateTellConversationViews)
        {
            foreach (var playerName in this.pilotSettings.TellConversationPilots
                         .Where(name => !string.Equals(
                             name,
                             this.pilotName,
                             StringComparison.OrdinalIgnoreCase))
                         .OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
            {
                var key = GetTellViewKey(playerName);
                var state = new ChatViewRuntime(
                    key,
                    string.Concat("Tell · ", playerName),
                    settings: null,
                    tellPilot: playerName,
                    configuredOrder: int.MaxValue);
                this.CopyRuntimeState(previousStates, state);
                this.viewStates.TryAdd(key, state);
            }
        }

        var openViewKey = this.IsSelectedViewOpen()
            ? this.selectedViewKey
            : null;

        foreach (var state in this.viewStates.Values)
        {
            state.LastMessageAt = this.presentedEntries
                .Where(state.Matches)
                .Select(entry => entry.Entry.ObservedAt)
                .DefaultIfEmpty(DateTimeOffset.MinValue)
                .Max();

            if (string.Equals(
                    state.Key,
                    openViewKey,
                    StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var entry in newEntries)
            {
                state.AddUnread(entry);
            }
        }

        this.RebuildViewList();
    }

    private void CopyRuntimeState(
        IReadOnlyDictionary<string, ChatViewRuntime> previousStates,
        ChatViewRuntime nextState)
    {
        if (!previousStates.TryGetValue(
                nextState.Key,
                out var previous))
        {
            return;
        }

        nextState.UnreadEntryIds.UnionWith(previous.UnreadEntryIds);
        nextState.HighlightedEntryIds.UnionWith(
            previous.HighlightedEntryIds);
        nextState.UnreadEntryIds.IntersectWith(this.knownEntryIds);
        nextState.HighlightedEntryIds.IntersectWith(
            this.knownEntryIds);
        nextState.LastMessageAt = previous.LastMessageAt;
    }

    private void RebuildViewList()
    {
        var pinnedKey = this.settings.Views.Count > 0
            ? GetConfiguredViewKey(this.settings.Views[0].Id)
            : null;
        IEnumerable<ChatViewRuntime> ordered = this.viewStates.Values;

        if (this.settings.OrderViewsByRecentMessage)
        {
            ordered = this.settings.KeepFirstViewAtTop
                ? ordered
                    .OrderByDescending(state => string.Equals(
                        state.Key,
                        pinnedKey,
                        StringComparison.Ordinal))
                    .ThenByDescending(state => state.LastMessageAt)
                    .ThenBy(
                        state => state.Title,
                        StringComparer.OrdinalIgnoreCase)
                : ordered
                    .OrderByDescending(state => state.LastMessageAt)
                    .ThenBy(
                        state => state.Title,
                        StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            ordered = ordered
                .OrderBy(state => state.ConfiguredOrder)
                .ThenBy(state => state.TellPilot == null ? 0 : 1)
                .ThenBy(state => state.Title, StringComparer.OrdinalIgnoreCase);
        }

        var orderedStates = ordered.ToArray();
        var requestedKey = this.selectedViewKey;

        if (string.IsNullOrWhiteSpace(requestedKey) ||
            !this.viewStates.ContainsKey(requestedKey))
        {
            requestedKey = orderedStates.FirstOrDefault()?.Key;
        }

        this.updatingViewList = true;
        this.viewListBox.BeginUpdate();

        try
        {
            this.viewListBox.Items.Clear();
            this.viewListBox.Items.AddRange(
                orderedStates.Cast<object>().ToArray());
            var selectedIndex = Array.FindIndex(
                orderedStates,
                state => string.Equals(
                    state.Key,
                    requestedKey,
                    StringComparison.Ordinal));
            this.viewListBox.SelectedIndex = selectedIndex >= 0
                ? selectedIndex
                : orderedStates.Length > 0
                    ? 0
                    : -1;
            this.selectedViewKey = this.viewListBox.SelectedItem is
                ChatViewRuntime selected
                ? selected.Key
                : null;
        }
        finally
        {
            this.viewListBox.EndUpdate();
            this.updatingViewList = false;
        }

        this.MarkSelectedViewRead();
        this.RefreshViewActionState();
        this.RefreshComposerState();
        this.viewListBox.Invalidate();
    }

    private void AppendTranscriptRows(
        IReadOnlyList<PresentedChatEntry> appendedEntries)
    {
        var selectedView = this.GetSelectedView();

        if (selectedView == null || appendedEntries.Count == 0)
        {
            this.MarkSelectedViewRead();
            return;
        }

        var matchingEntries = appendedEntries
            .Where(selectedView.Matches)
            .ToArray();

        if (matchingEntries.Length == 0)
        {
            this.MarkSelectedViewRead();
            return;
        }

        var wasAtBottom = this.IsScrolledToBottom();
        this.transcriptGrid.SuspendLayout();

        try
        {
            foreach (var entry in matchingEntries)
            {
                this.AddTranscriptRow(entry);
            }

            if (wasAtBottom)
            {
                this.ScrollToBottom();
            }
        }
        finally
        {
            this.transcriptGrid.ResumeLayout();
        }

        this.MarkSelectedViewRead();
    }

    private void RefreshTranscriptGrid()
    {
        var selectedView = this.GetSelectedView();

        if (selectedView == null)
        {
            this.transcriptGrid.Rows.Clear();
            return;
        }

        var query = this.searchTextBox.Text.Trim();
        var matchingEntries = this.presentedEntries
            .Where(selectedView.Matches)
            .Where(entry => MatchesSearch(entry, query))
            .ToArray();
        var wasAtBottom = this.IsScrolledToBottom();
        var firstVisibleEntryId = this.GetFirstVisibleEntryId();
        var selectedEntryIds = this.transcriptGrid.SelectedRows
            .Cast<DataGridViewRow>()
            .Select(row => (row.Tag as ChatTranscriptRowTag)?.EntryId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);

        this.transcriptGrid.SuspendLayout();

        try
        {
            this.transcriptGrid.Rows.Clear();

            foreach (var entry in matchingEntries)
            {
                this.AddTranscriptRow(entry);
            }

            this.RestoreGridSort();

            if (wasAtBottom)
            {
                this.ScrollToBottom();
            }
            else if (!string.IsNullOrWhiteSpace(firstVisibleEntryId))
            {
                this.RestoreFirstVisibleEntry(firstVisibleEntryId);
            }

            if (selectedEntryIds.Count > 0)
            {
                foreach (DataGridViewRow row in this.transcriptGrid.Rows)
                {
                    row.Selected = row.Tag is ChatTranscriptRowTag tag &&
                                   selectedEntryIds.Contains(tag.EntryId);
                }
            }
        }
        finally
        {
            this.transcriptGrid.ResumeLayout();
        }

        this.MarkSelectedViewRead();
    }

    private static bool MatchesSearch(
        PresentedChatEntry entry,
        string query)
    {
        if (query.Length == 0)
        {
            return true;
        }

        return entry.Presentation.ChannelName.Contains(
                   query,
                   StringComparison.CurrentCultureIgnoreCase) ||
               entry.Presentation.PlayerName.Contains(
                   query,
                   StringComparison.CurrentCultureIgnoreCase) ||
               (entry.Presentation.ConversationPlayerName?.Contains(
                    query,
                    StringComparison.CurrentCultureIgnoreCase) ?? false) ||
               entry.Presentation.DisplayText.Contains(
                   query,
                   StringComparison.CurrentCultureIgnoreCase) ||
               (entry.Entry.IsSnapshot
                   ? "Earlier"
                   : FormatObservedAt(entry.Entry.ObservedAt)).Contains(
                       query,
                       StringComparison.CurrentCultureIgnoreCase);
    }

    private void AddTranscriptRow(PresentedChatEntry presentedEntry)
    {
        var entry = presentedEntry.Entry;
        var presentation = presentedEntry.Presentation;
        var selectedView = this.GetSelectedView();
        var isNew = selectedView?.ShouldHighlight(entry.EntryId) == true;
        var timestampText = entry.IsSnapshot
            ? "Earlier"
            : FormatObservedAt(entry.ObservedAt);
        var rowIndex = this.transcriptGrid.Rows.Add(
            isNew ? "●" : "",
            timestampText,
            presentation.ChannelName,
            presentation.PlayerName,
            presentation.DisplayText);
        var row = this.transcriptGrid.Rows[rowIndex];
        row.Tag = new ChatTranscriptRowTag(
            entry.EntryId,
            entry.ObservedAt,
            entry.IsSnapshot,
            presentation.ConversationPlayerName);
        row.Cells["New"].ToolTipText = isNew
            ? "New since you last viewed this chat view."
            : "";
        row.Cells["Timestamp"].Tag = entry.IsSnapshot
            ? null
            : entry.ObservedAt;
        row.Cells["Channel"].Tag = presentation.ColorOptionName;
        row.Cells["Channel"].ToolTipText = presentation.ChannelToolTip;
        row.Cells["Player"].ToolTipText = presentation.PlayerToolTip;
        this.ApplyTranscriptColor(
            row,
            presentation.ColorOptionName);

        if (entry.IsSnapshot)
        {
            row.DefaultCellStyle.ForeColor = MainWindowTheme.MutedText;
            row.Cells["Timestamp"].ToolTipText =
                "Recovered from the game's current chat buffer. " +
                "The original message time is unavailable.";
        }
        else
        {
            row.Cells["Timestamp"].ToolTipText = entry.ObservedAt
                .ToLocalTime()
                .ToString("F", CultureInfo.CurrentCulture);
        }
    }

    private void RestoreGridSort()
    {
        if (string.IsNullOrWhiteSpace(this.sortedColumnName) ||
            !this.transcriptGrid.Columns.Contains(
                this.sortedColumnName))
        {
            return;
        }

        var column = this.transcriptGrid.Columns[
            this.sortedColumnName];

        if (column == null)
        {
            return;
        }

        try
        {
            this.transcriptGrid.Sort(column, this.sortDirection);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void RefreshConnectionState()
    {
        this.RefreshDestinationChoices();
        this.RefreshComposerState();
        var client = this.clientManager.Clients.FirstOrDefault(candidate =>
            candidate.LiveCharacterIdentity.CharacterObjectId ==
            this.characterId);

        if (client != null)
        {
            this.RefreshChatColors(client);
        }
    }

    private void RefreshChatColors(ClientInstance client)
    {
        if (client.LifecycleState != ClientLifecycleState.InGame ||
            client.LoadingOrTransitionFlag != 0)
        {
            return;
        }

        var observed = this.clientManager.ReadChatColorOptions(
            client.ProcessId);

        if (!observed.IsAvailable)
        {
            return;
        }

        var fingerprint = string.Join(
            "|",
            observed.Colors
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => string.Concat(
                    pair.Key,
                    "=",
                    BitConverter.SingleToInt32Bits(pair.Value.Red)
                        .ToString("X8", CultureInfo.InvariantCulture),
                    BitConverter.SingleToInt32Bits(pair.Value.Green)
                        .ToString("X8", CultureInfo.InvariantCulture),
                    BitConverter.SingleToInt32Bits(pair.Value.Blue)
                        .ToString("X8", CultureInfo.InvariantCulture))));

        if (string.Equals(
                this.renderedChatColorFingerprint,
                fingerprint,
                StringComparison.Ordinal))
        {
            return;
        }

        this.chatColors = observed.Colors.ToDictionary(
            pair => pair.Key,
            pair => ToDrawingColor(pair.Value),
            StringComparer.OrdinalIgnoreCase);
        this.renderedChatColorFingerprint = fingerprint;
        this.ApplyTranscriptColorsToAllRows();
    }

    private void ApplyTranscriptColorsToAllRows()
    {
        foreach (DataGridViewRow row in this.transcriptGrid.Rows)
        {
            this.ApplyTranscriptColor(
                row,
                row.Cells["Channel"].Tag as string);
        }
    }

    private void ApplyTranscriptColor(
        DataGridViewRow row,
        string? colorOptionName)
    {
        string[] coloredColumns = ["Channel", "Player", "Message"];

        foreach (var columnName in coloredColumns)
        {
            row.Cells[columnName].Style.ForeColor = Color.Empty;
            row.Cells[columnName].Style.SelectionForeColor = Color.Empty;
        }

        if (!this.settings.UseGameChatColors ||
            string.IsNullOrWhiteSpace(colorOptionName) ||
            !this.chatColors.TryGetValue(
                colorOptionName,
                out var color))
        {
            return;
        }

        foreach (var columnName in coloredColumns)
        {
            row.Cells[columnName].Style.ForeColor = color;
            row.Cells[columnName].Style.SelectionForeColor = color;
        }
    }

    private static Color ToDrawingColor(ClientObservedColor color)
    {
        return Color.FromArgb(
            ToColorByte(color.Red),
            ToColorByte(color.Green),
            ToColorByte(color.Blue));
    }

    private static int ToColorByte(float component)
    {
        return (int)MathF.Round(
            Math.Clamp(component, 0f, 1f) * 255f);
    }

    private void ViewListBox_OnDrawItem(
        object? sender,
        DrawItemEventArgs e)
    {
        if (e.Index < 0 ||
            e.Index >= this.viewListBox.Items.Count ||
            this.viewListBox.Items[e.Index] is not ChatViewRuntime state)
        {
            return;
        }

        var selected = (e.State & DrawItemState.Selected) != 0;
        var background = selected
            ? MainWindowTheme.ButtonHover
            : MainWindowTheme.Panel;
        using var backgroundBrush = new SolidBrush(background);
        e.Graphics.FillRectangle(backgroundBrush, e.Bounds);

        var badgeText = !state.ShowUnreadBadge
            ? ""
            : state.UnreadCount > 99
                ? "99+"
                : state.UnreadCount > 0
                    ? state.UnreadCount.ToString(
                        CultureInfo.InvariantCulture)
                    : "";
        var badgeWidth = badgeText.Length == 0
            ? 0
            : Math.Max(
                26,
                TextRenderer.MeasureText(
                    badgeText,
                    MainWindowTheme.CreateBodyFont(8.5f)).Width + 12);
        var textBounds = new Rectangle(
            e.Bounds.Left + 10,
            e.Bounds.Top,
            Math.Max(0, e.Bounds.Width - 18 - badgeWidth),
            e.Bounds.Height);
        TextRenderer.DrawText(
            e.Graphics,
            state.Title,
            MainWindowTheme.CreateBodyFont(9f),
            textBounds,
            selected ? MainWindowTheme.Text : MainWindowTheme.MutedText,
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPrefix |
            TextFormatFlags.SingleLine);

        if (badgeText.Length > 0)
        {
            var badgeBounds = new Rectangle(
                e.Bounds.Right - badgeWidth - 8,
                e.Bounds.Top + 6,
                badgeWidth,
                e.Bounds.Height - 12);
            using var badgeBrush = new SolidBrush(
                selected
                    ? MainWindowTheme.AccentBorder
                    : MainWindowTheme.ElevatedPanel);
            e.Graphics.FillRectangle(badgeBrush, badgeBounds);
            TextRenderer.DrawText(
                e.Graphics,
                badgeText,
                MainWindowTheme.CreateBodyFont(8.5f),
                badgeBounds,
                MainWindowTheme.Text,
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPrefix |
                TextFormatFlags.SingleLine);
        }

        if ((e.State & DrawItemState.Focus) != 0)
        {
            e.DrawFocusRectangle();
        }
    }

    private void ViewListBox_OnSelectedIndexChanged(
        object? sender,
        EventArgs e)
    {
        if (this.updatingViewList)
        {
            return;
        }

        var previous = this.GetSelectedView();
        var next = this.viewListBox.SelectedItem as ChatViewRuntime;

        if (previous != null &&
            !string.Equals(
                previous.Key,
                next?.Key,
                StringComparison.Ordinal))
        {
            previous.ClearHighlights();
        }

        this.selectedViewKey = next?.Key;
        this.RefreshViewActionState();
        this.RefreshComposerState();
        this.RefreshTranscriptGrid();
    }

    private void ViewListBox_OnKeyDown(
        object? sender,
        KeyEventArgs e)
    {
        if (e.KeyCode == Keys.F2)
        {
            e.Handled = true;
            this.QueueCompanionUiCommand(
                "open the chat view editor",
                this.EditSelectedView);
        }
        else if (e.KeyCode == Keys.Delete)
        {
            e.Handled = true;
            this.QueueCompanionUiCommand(
                "delete the selected chat view",
                this.DeleteSelectedView);
        }
    }

    private void MarkSelectedViewRead()
    {
        if (!this.IsSelectedViewBeingRead())
        {
            return;
        }

        var selected = this.GetSelectedView();

        if (selected == null)
        {
            return;
        }

        var visibleEntryIds = this.transcriptGrid.Rows
            .Cast<DataGridViewRow>()
            .Where(row => row.Displayed)
            .Select(row => (row.Tag as ChatTranscriptRowTag)?.EntryId)
            .Where(entryId => !string.IsNullOrWhiteSpace(entryId))
            .Cast<string>()
            .ToArray();

        if (!selected.RevealMessages(visibleEntryIds))
        {
            return;
        }

        this.viewListBox.Invalidate();
        this.ApplyUnreadMarkersToCurrentGrid();
    }

    private void ApplyUnreadMarkersToCurrentGrid()
    {
        var selected = this.GetSelectedView();

        foreach (DataGridViewRow row in this.transcriptGrid.Rows)
        {
            if (row.Tag is not ChatTranscriptRowTag tag)
            {
                continue;
            }

            var isHighlighted = selected?.ShouldHighlight(tag.EntryId) == true;
            row.Cells["New"].Value = isHighlighted ? "●" : "";
            row.Cells["New"].ToolTipText = isHighlighted
                ? "New since you last viewed this chat view."
                : "";
        }
    }

    private bool IsSelectedViewOpen() =>
        this.Visible &&
        this.WindowState != FormWindowState.Minimized;

    private bool IsSelectedViewBeingRead()
    {
        return this.IsSelectedViewOpen() &&
               this.ContainsFocus;
    }

    private string? GetSelectedTellPilot()
    {
        var tellPilot = this.GetSelectedView()?.TellPilot;
        return string.IsNullOrWhiteSpace(tellPilot)
            ? null
            : tellPilot;
    }

    private ChatViewRuntime? GetSelectedView()
    {
        return !string.IsNullOrWhiteSpace(this.selectedViewKey) &&
               this.viewStates.TryGetValue(
                   this.selectedViewKey,
                   out var state)
            ? state
            : null;
    }

    private void RefreshViewActionState()
    {
        var selected = this.GetSelectedView();
        var canEdit = selected?.Settings != null;
        this.editViewButton.Enabled = canEdit;
        this.deleteViewButton.Enabled =
            canEdit && this.settings.Views.Count > 1;
    }

    private void AddViewButton_OnClick(
        object? sender,
        EventArgs e)
    {
        this.QueueCompanionUiCommand(
            "open the new chat view editor",
            this.AddChatView);
    }

    private void AddChatView()
    {
        var draft = new ChatCompanionViewSettings
        {
            Name = "New view",
            IncludeAllMessages = true,
        };
        using var dialog = new ChatCompanionViewEditorForm(
            draft,
            isNew: true);

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        this.settings.Views.Add(dialog.Result);
        this.selectedViewKey = GetConfiguredViewKey(dialog.Result.Id);
        this.SaveChatSettings();
        this.RefreshViewStates([]);
        this.RefreshTranscriptGrid();
    }

    private void EditViewButton_OnClick(
        object? sender,
        EventArgs e)
    {
        this.QueueCompanionUiCommand(
            "open the chat view editor",
            this.EditSelectedView);
    }

    private void EditSelectedView()
    {
        var selected = this.GetSelectedView();

        if (selected?.Settings == null)
        {
            return;
        }

        using var dialog = new ChatCompanionViewEditorForm(
            selected.Settings,
            isNew: false);

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var index = this.settings.Views.FindIndex(view =>
            view.Id == selected.Settings.Id);

        if (index < 0)
        {
            return;
        }

        this.settings.Views[index] = dialog.Result;
        this.selectedViewKey = GetConfiguredViewKey(dialog.Result.Id);
        this.SaveChatSettings();
        this.RefreshViewStates([]);
        this.RefreshTranscriptGrid();
    }

    private void DeleteViewButton_OnClick(
        object? sender,
        EventArgs e)
    {
        this.QueueCompanionUiCommand(
            "delete the selected chat view",
            this.DeleteSelectedView);
    }

    private void DeleteSelectedView()
    {
        var selected = this.GetSelectedView();

        if (selected?.Settings == null ||
            this.settings.Views.Count <= 1)
        {
            return;
        }

        if (!ThemedMessageDialog.Confirm(
                this,
                "Delete chat view",
                string.Concat(
                    "Delete the chat view ‘",
                    selected.Title,
                    "’?\n\nChat messages are not deleted."),
                "Delete"))
        {
            return;
        }

        var removedIndex = this.settings.Views.FindIndex(view =>
            view.Id == selected.Settings.Id);
        this.settings.Views.RemoveAll(view =>
            view.Id == selected.Settings.Id);
        var replacementIndex = Math.Clamp(
            removedIndex,
            0,
            this.settings.Views.Count - 1);
        this.selectedViewKey = GetConfiguredViewKey(
            this.settings.Views[replacementIndex].Id);
        this.SaveChatSettings();
        this.RefreshViewStates([]);
        this.RefreshTranscriptGrid();
    }

    private void OptionsButton_OnClick(
        object? sender,
        EventArgs e)
    {
        this.QueueCompanionUiCommand(
            "open Chat Companion options",
            this.OpenChatCompanionOptions);
    }

    private void OpenChatCompanionOptions()
    {
        if (this.optionsForm is { IsDisposed: false } existing)
        {
            if (existing.WindowState == FormWindowState.Minimized)
            {
                existing.WindowState = FormWindowState.Normal;
            }

            existing.BringToFront();
            existing.Activate();
            return;
        }

        var dialog = new ChatCompanionOptionsForm(this.settings);
        this.optionsForm = dialog;
        dialog.Saved += this.ChatCompanionOptionsForm_OnSaved;
        dialog.FormClosed +=
            this.ChatCompanionOptionsForm_OnFormClosed;
        dialog.Show(this);
        dialog.Activate();
    }

    private void ChatCompanionOptionsForm_OnSaved(
        object? sender,
        EventArgs e)
    {
        if (sender is not ChatCompanionOptionsForm dialog ||
            !ReferenceEquals(this.optionsForm, dialog))
        {
            return;
        }

        this.RunCompanionUiCommand(
            "save Chat Companion options",
            () =>
            {
                this.settings.OrderViewsByRecentMessage =
                    dialog.OrderViewsByRecentMessage;
                this.settings.KeepFirstViewAtTop =
                    dialog.KeepFirstViewAtTop;
                this.settings.CreateTellConversationViews =
                    dialog.CreateTellConversationViews;
                this.settings.UseGameChatColors =
                    dialog.UseGameChatColors;
                this.settings.ComposerAtTop = dialog.ComposerAtTop;
                this.SaveChatSettings();
                this.ApplyComposerPosition();
                this.RefreshViewStates([]);
                this.ApplyTranscriptColorsToAllRows();
                this.RefreshTranscriptGrid();
            });
    }

    private void ChatCompanionOptionsForm_OnFormClosed(
        object? sender,
        FormClosedEventArgs e)
    {
        if (sender is not ChatCompanionOptionsForm dialog)
        {
            return;
        }

        dialog.Saved -= this.ChatCompanionOptionsForm_OnSaved;
        dialog.FormClosed -=
            this.ChatCompanionOptionsForm_OnFormClosed;

        if (ReferenceEquals(this.optionsForm, dialog))
        {
            this.optionsForm = null;
        }

        dialog.Dispose();
    }

    private void QueueCompanionUiCommand(
        string operation,
        Action command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentNullException.ThrowIfNull(command);

        if (this.companionUiCommandPending ||
            this.IsDisposed ||
            this.Disposing ||
            !this.IsHandleCreated)
        {
            return;
        }

        this.companionUiCommandPending = true;

        try
        {
            this.BeginInvoke((Action)(() =>
            {
                this.companionUiCommandPending = false;

                if (this.IsDisposed || this.Disposing)
                {
                    return;
                }

                this.RunCompanionUiCommand(operation, command);
            }));
        }
        catch (InvalidOperationException exception)
        {
            this.companionUiCommandPending = false;
            Debug.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"[Chat Companion] Could not queue '{operation}' because the window is closing: {exception}"));
        }
    }

    private void RunCompanionUiCommand(
        string operation,
        Action command)
    {
        try
        {
            command();
        }
        catch (Exception exception)
        {
            Debug.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"[Chat Companion] Could not {operation}: {exception}"));

            if (this.IsDisposed || this.Disposing)
            {
                return;
            }

            this.SetComposerOperationStatus(
                string.Concat(
                    "Could not ",
                    operation,
                    ". The game client is still running."),
                MainWindowTheme.Warning);
        }
    }

    private void SaveChatSettings()
    {
        this.settings.EnsureDefaults();
        this.clientManager.SaveSettings();
    }

    private static string GetConfiguredViewKey(Guid id) =>
        string.Concat("view:", id.ToString("N", CultureInfo.InvariantCulture));

    private static string GetTellViewKey(string playerName) =>
        string.Concat(
            "tell:",
            ChatChannelPresentation.NormalizeTellPilotName(playerName)
                .ToUpperInvariant());

    private bool IsScrolledToBottom()
    {
        if (this.transcriptGrid.Rows.Count == 0)
        {
            return true;
        }

        try
        {
            var first = this.transcriptGrid.FirstDisplayedScrollingRowIndex;

            if (first < 0)
            {
                return true;
            }

            return first +
                   this.transcriptGrid.DisplayedRowCount(
                       includePartialRow: true) >=
                   this.transcriptGrid.Rows.Count;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private string? GetFirstVisibleEntryId()
    {
        try
        {
            var index = this.transcriptGrid.FirstDisplayedScrollingRowIndex;
            return index >= 0 && index < this.transcriptGrid.Rows.Count &&
                   this.transcriptGrid.Rows[index].Tag is
                       ChatTranscriptRowTag tag
                ? tag.EntryId
                : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private void RestoreFirstVisibleEntry(string entryId)
    {
        foreach (DataGridViewRow row in this.transcriptGrid.Rows)
        {
            if (row.Tag is not ChatTranscriptRowTag tag ||
                !string.Equals(
                    tag.EntryId,
                    entryId,
                    StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                this.transcriptGrid.FirstDisplayedScrollingRowIndex =
                    row.Index;
            }
            catch (InvalidOperationException)
            {
            }

            return;
        }
    }

    private void ScrollToBottom()
    {
        if (this.transcriptGrid.Rows.Count == 0)
        {
            return;
        }

        try
        {
            var displayedRows = Math.Max(
                1,
                this.transcriptGrid.DisplayedRowCount(
                    includePartialRow: false));
            this.transcriptGrid.FirstDisplayedScrollingRowIndex = Math.Max(
                0,
                this.transcriptGrid.Rows.Count - displayedRows);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void RefreshDisplayedTimestamps()
    {
        if (this.searchTextBox.TextLength > 0)
        {
            this.RefreshTranscriptGrid();
            return;
        }

        foreach (DataGridViewRow row in this.transcriptGrid.Rows)
        {
            if (row.Tag is not ChatTranscriptRowTag tag ||
                tag.IsSnapshot)
            {
                continue;
            }

            row.Cells["Timestamp"].Value =
                FormatObservedAt(tag.ObservedAt);
        }
    }

    private static string FormatObservedAt(DateTimeOffset observedAt)
    {
        var local = observedAt.ToLocalTime();
        var now = DateTimeOffset.Now;

        return local.Date == now.Date
            ? local.ToString("HH:mm:ss", CultureInfo.CurrentCulture)
            : local.ToString(
                "dd MMM yyyy HH:mm:ss",
                CultureInfo.CurrentCulture);
    }

    private sealed class DoubleBufferedDataGridView : DataGridView
    {
        public DoubleBufferedDataGridView()
        {
            this.DoubleBuffered = true;
        }
    }

    private sealed record PresentedChatEntry(
        ChatJournalEntry Entry,
        ChatMessagePresentation Presentation);

    private sealed record ChatTranscriptRowTag(
        string EntryId,
        DateTimeOffset ObservedAt,
        bool IsSnapshot,
        string? ConversationPlayerName);

    private sealed class ChatViewRuntime(
        string key,
        string title,
        ChatCompanionViewSettings? settings,
        string? tellPilot,
        int configuredOrder)
    {
        public string Key { get; } = key;

        public string Title { get; } = title;

        public ChatCompanionViewSettings? Settings { get; } = settings;

        public string? TellPilot { get; } = tellPilot;

        public int ConfiguredOrder { get; } = configuredOrder;

        public HashSet<string> UnreadEntryIds { get; } =
            new(StringComparer.Ordinal);

        public HashSet<string> HighlightedEntryIds { get; } =
            new(StringComparer.Ordinal);

        public int UnreadCount => this.UnreadEntryIds.Count;

        public bool ShowUnreadBadge =>
            this.Settings?.ShowUnreadBadge ?? true;

        public DateTimeOffset LastMessageAt { get; set; }

        public void AddUnread(PresentedChatEntry entry)
        {
            if (this.Matches(entry))
            {
                this.UnreadEntryIds.Add(entry.Entry.EntryId);
            }
        }

        public bool RevealMessages(IEnumerable<string> entryIds)
        {
            var changed = false;

            foreach (var entryId in entryIds)
            {
                if (!this.UnreadEntryIds.Remove(entryId))
                {
                    continue;
                }

                this.HighlightedEntryIds.Add(entryId);
                changed = true;
            }

            return changed;
        }

        public void ClearHighlights() =>
            this.HighlightedEntryIds.Clear();

        public bool DismissNewMarkers(IEnumerable<string> entryIds)
        {
            var changed = false;

            foreach (var entryId in entryIds)
            {
                changed |= this.UnreadEntryIds.Remove(entryId);
                changed |= this.HighlightedEntryIds.Remove(entryId);
            }

            return changed;
        }

        public bool ShouldHighlight(string entryId) =>
            this.UnreadEntryIds.Contains(entryId) ||
            this.HighlightedEntryIds.Contains(entryId);

        public bool Matches(PresentedChatEntry entry)
        {
            if (!string.IsNullOrWhiteSpace(this.TellPilot))
            {
                return string.Equals(
                           entry.Presentation.ChannelName,
                           "Tell",
                           StringComparison.OrdinalIgnoreCase) &&
                       string.Equals(
                           entry.Presentation.ConversationPlayerName,
                           this.TellPilot,
                           StringComparison.OrdinalIgnoreCase);
            }

            if (this.Settings == null)
            {
                return false;
            }

            if (this.Settings.IncludeAllMessages)
            {
                return true;
            }

            var filterChannelName =
                ChatChannelPresentation.GetFilterChannelName(
                    entry.Presentation.ChannelName);
            return this.Settings.IncludedChannels.Contains(
                filterChannelName,
                StringComparer.OrdinalIgnoreCase);
        }

        public override string ToString() => this.Title;
    }

}
