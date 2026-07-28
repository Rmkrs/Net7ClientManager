// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

public sealed partial class MainForm
{
    private Control CreateTopPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 270,
            Padding = new Padding(left: 18, top: 12, right: 18, bottom: 14),
            BackColor = MainWindowTheme.Header,
        };

        panel.Paint += static (sender, e) =>
        {
            if (sender is not Panel header)
            {
                return;
            }

            using var pen = new Pen(MainWindowTheme.AccentBorder);
            e.Graphics.DrawLine(
                pen,
                x1: 0,
                y1: header.ClientSize.Height - 1,
                x2: header.ClientSize.Width,
                y2: header.ClientSize.Height - 1);
        };

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = MainWindowTheme.Header,
        };

        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, width: 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, height: 48));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, height: 100));

        root.Controls.Add(this.CreateTopTitlePanel(), column: 0, row: 0);
        root.Controls.Add(this.CreateTopCardsPanel(), column: 0, row: 1);

        this.accountsButton.Click += this.AccountsButton_OnClick;
        this.pilotArchiveButton.Click += this.PilotArchiveButton_OnClick;
        this.gameSettingsButton.Click += this.GameSettingsButton_OnClick;
        this.profileComboBox.SelectedIndexChanged +=
            this.ProfileComboBox_OnSelectedIndexChanged;
        this.addProfileButton.Click += this.AddProfileButton_OnClick;
        this.renameProfileButton.Click += this.RenameProfileButton_OnClick;
        this.duplicateProfileButton.Click +=
            this.DuplicateProfileButton_OnClick;
        this.deleteProfileButton.Click += this.DeleteProfileButton_OnClick;
        this.quickLaunchHostResolutionComboBox.SelectedIndexChanged +=
            this.QuickLaunchHostResolutionComboBox_OnSelectedIndexChanged;
        this.quickLaunchMatchGameResolutionCheckBox.CheckedChanged +=
            this.QuickLaunchMatchGameResolutionCheckBox_OnCheckedChanged;
        this.quickLaunchGameResolutionComboBox.SelectedIndexChanged +=
            this.QuickLaunchGameResolutionComboBox_OnSelectedIndexChanged;
        this.startClientButton.Click += this.StartClientButton_OnClick;

        this.ReloadQuickLaunchControls();

        panel.Controls.Add(root);

        return panel;
    }

    private Control CreateTopTitlePanel()
    {
        var titlePanel = new Panel
        {
            Dock = DockStyle.Fill,
        };

        var titleLabel = new Label
        {
            AutoSize = true,
            ForeColor = MainWindowTheme.Text,
            Font = MainWindowTheme.CreateHeadingFont(size: 17.0f),
            Text = "Net7 Client Manager",
            Location = new Point(x: 0, y: 0),
        };

        var subtitleLabel = new Label
        {
            AutoSize = true,
            ForeColor = MainWindowTheme.MutedText,
            Text = "Profiles configure the fleet. Live observations describe what is actually running.",
            Location = new Point(x: 1, y: 28),
        };

        titlePanel.Controls.Add(titleLabel);
        titlePanel.Controls.Add(subtitleLabel);

        return titlePanel;
    }

    private Control CreateTopCardsPanel()
    {
        var cards = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = MainWindowTheme.Header,
        };

        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, width: 33.333f));
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, width: 33.334f));
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, width: 33.333f));
        cards.RowStyles.Add(new RowStyle(SizeType.Percent, height: 100));

        cards.Controls.Add(this.CreateProfileCard(), column: 0, row: 0);
        cards.Controls.Add(this.CreateToolsCard(), column: 1, row: 0);
        cards.Controls.Add(this.CreateQuickLaunchCard(), column: 2, row: 0);

        return cards;
    }

    private Control CreateProfileCard()
    {
        var section = CreateTopCardSection(
            new Padding(left: 0, top: 0, right: 8, bottom: 0));
        var layout = CreateTopCardLayout();

        layout.Controls.Add(
            CreateTopCardHeader(
                title: "Profile",
                description: "Configure the managed fleet."),
            column: 0,
            row: 0);

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 3,
            Margin = Padding.Empty,
            BackColor = Color.Transparent,
        };

        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, width: 58));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, width: 250));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, width: 100));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, height: 34));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, height: 10));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, height: 40));

        var profileLabel = new Label
        {
            Text = "Profile",
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = MainWindowTheme.MutedText,
        };

        this.profileComboBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 240,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(left: 0, top: 3, right: 0, bottom: 3),
        };
        MainWindowTheme.StyleComboBox(this.profileComboBox);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = Padding.Empty,
            Margin = Padding.Empty,
            BackColor = Color.Transparent,
        };

        this.addProfileButton = CreateProfileActionButton(
            text: "New",
            width: 62,
            primary: true);
        this.renameProfileButton = CreateProfileActionButton(
            text: "Rename",
            width: 68);
        this.duplicateProfileButton = CreateProfileActionButton(
            text: "Duplicate",
            width: 76);
        this.deleteProfileButton = CreateProfileActionButton(
            text: "Delete",
            width: 62,
            danger: true,
            rightMargin: 0);

        actions.Controls.Add(this.addProfileButton);
        actions.Controls.Add(this.renameProfileButton);
        actions.Controls.Add(this.duplicateProfileButton);
        actions.Controls.Add(this.deleteProfileButton);

        body.Controls.Add(profileLabel, column: 0, row: 0);
        body.Controls.Add(this.profileComboBox, column: 1, row: 0);
        body.Controls.Add(actions, column: 1, row: 2);
        body.SetColumnSpan(actions, value: 2);

        layout.Controls.Add(CreateTopInsetPanel(body), column: 0, row: 1);
        section.Controls.Add(layout);

        return section;
    }

    private Control CreateToolsCard()
    {
        var section = CreateTopCardSection(
            new Padding(left: 8, top: 0, right: 8, bottom: 0));
        var layout = CreateTopCardLayout();

        layout.Controls.Add(
            CreateTopCardHeader(
                title: "Accounts & tools",
                description: "Managed access and shared utilities."),
            column: 0,
            row: 0);

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 3,
            Margin = Padding.Empty,
            BackColor = Color.Transparent,
        };

        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, width: 50));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, width: 17));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, width: 50));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, height: 24));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, height: 38));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, height: 38));

        var separator = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = MainWindowTheme.Border,
            Margin = new Padding(left: 8, top: 4, right: 8, bottom: 4),
        };

        this.accountsButton = CreateTopCardButton("Manage", width: 132);
        this.gameSettingsButton = CreateTopCardButton("Game Settings", width: 150);
        this.pilotArchiveButton = CreateTopCardButton("Pilot Archive", width: 150);

        body.Controls.Add(CreateTopSubsectionLabel("Accounts"), column: 0, row: 0);
        body.Controls.Add(CreateTopSubsectionLabel("Tools"), column: 2, row: 0);
        body.Controls.Add(separator, column: 1, row: 0);
        body.SetRowSpan(separator, value: 3);
        body.Controls.Add(this.accountsButton, column: 0, row: 1);
        body.Controls.Add(this.gameSettingsButton, column: 2, row: 1);
        body.Controls.Add(this.pilotArchiveButton, column: 2, row: 2);

        layout.Controls.Add(CreateTopInsetPanel(body), column: 0, row: 1);
        section.Controls.Add(layout);

        return section;
    }

    private Control CreateQuickLaunchCard()
    {
        var section = CreateTopCardSection(
            new Padding(left: 8, top: 0, right: 0, bottom: 0));
        var layout = CreateTopCardLayout();

        layout.Controls.Add(
            CreateTopCardHeader(
                title: "Quick launch",
                description: "Start an unassigned client."),
            column: 0,
            row: 0);

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 3,
            Margin = Padding.Empty,
            BackColor = Color.Transparent,
        };

        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, width: 112));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, width: 138));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, width: 100));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, height: 34));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, height: 34));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, height: 38));

        this.quickLaunchHostResolutionComboBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 128,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(left: 0, top: 3, right: 0, bottom: 3),
        };
        MainWindowTheme.StyleComboBox(this.quickLaunchHostResolutionComboBox);

        this.quickLaunchMatchGameResolutionCheckBox = new ThemedCheckBox
        {
            Text = string.Empty,
            AutoSize = false,
            Size = new Size(width: 22, height: 24),
            Anchor = AnchorStyles.Left,
            Margin = Padding.Empty,
            AccessibleName = "Match game resolution to host size",
        };

        this.quickLaunchGameResolutionComboBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 128,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(left: 0, top: 3, right: 0, bottom: 3),
        };
        MainWindowTheme.StyleComboBox(this.quickLaunchGameResolutionComboBox);

        this.startClientButton = new Button
        {
            Text = "Start client",
            Width = 120,
            Height = 30,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(left: 12, top: 3, right: 0, bottom: 3),
        };
        MainWindowTheme.StyleButton(this.startClientButton, primary: true);

        body.Controls.Add(CreateQuickLaunchLabel("Host size"), column: 0, row: 0);
        body.Controls.Add(this.quickLaunchHostResolutionComboBox, column: 1, row: 0);
        body.Controls.Add(CreateQuickLaunchLabel("Match host size"), column: 0, row: 1);
        body.Controls.Add(this.quickLaunchMatchGameResolutionCheckBox, column: 1, row: 1);
        body.Controls.Add(CreateQuickLaunchLabel("Game resolution"), column: 0, row: 2);
        body.Controls.Add(this.quickLaunchGameResolutionComboBox, column: 1, row: 2);
        body.Controls.Add(this.startClientButton, column: 2, row: 2);

        layout.Controls.Add(CreateTopInsetPanel(body), column: 0, row: 1);
        section.Controls.Add(layout);

        return section;
    }

    private static DashboardSectionPanel CreateTopCardSection(Padding margin)
    {
        return new DashboardSectionPanel
        {
            Dock = DockStyle.Fill,
            Margin = margin,
            Padding = new Padding(all: 1),
        };
    }

    private static TableLayoutPanel CreateTopCardLayout()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(left: 14, top: 10, right: 14, bottom: 10),
            ColumnCount = 1,
            RowCount = 2,
            BackColor = MainWindowTheme.Panel,
        };

        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, width: 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height: 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, height: 100));

        return layout;
    }

    private static Control CreateTopCardHeader(
        string title,
        string description)
    {
        var header = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
        };

        var titleLabel = new Label
        {
            Text = title,
            AutoSize = true,
            Font = MainWindowTheme.CreateHeadingFont(size: 12.0f),
            ForeColor = MainWindowTheme.Text,
            Location = new Point(x: 0, y: 0),
        };

        var descriptionLabel = new Label
        {
            Text = description,
            AutoSize = true,
            ForeColor = MainWindowTheme.MutedText,
            Location = new Point(x: 1, y: 22),
        };

        header.Controls.Add(titleLabel);
        header.Controls.Add(descriptionLabel);

        return header;
    }

    private static DashboardCardPanel CreateTopInsetPanel(Control content)
    {
        var panel = new DashboardCardPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(left: 16, top: 10, right: 12, bottom: 10),
            AccentColor = MainWindowTheme.AccentBorder,
            Cursor = Cursors.Default,
        };

        panel.Controls.Add(content);
        return panel;
    }

    private static Button CreateProfileActionButton(
        string text,
        int width,
        bool primary = false,
        bool danger = false,
        int rightMargin = 6)
    {
        var button = new Button
        {
            Text = text,
            Width = width,
            Height = 30,
            Margin = new Padding(left: 0, top: 2, right: rightMargin, bottom: 0),
        };
        MainWindowTheme.StyleButton(button, primary, danger);
        return button;
    }

    private static Button CreateTopCardButton(string text, int width = 180)
    {
        var button = new Button
        {
            Text = text,
            Width = width,
            Height = 30,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(left: 0, top: 2, right: 0, bottom: 2),
        };
        MainWindowTheme.StyleButton(button);

        return button;
    }

    private static Label CreateTopSubsectionLabel(string text)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = MainWindowTheme.MutedText,
        };
    }

    private static Label CreateQuickLaunchLabel(string text)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = MainWindowTheme.MutedText,
        };
    }
}
