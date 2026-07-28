// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using Net7ClientManager.Core;
using Net7ClientManager.Models;
using Net7ClientManager.Services;

internal sealed class SlotEditorForm : ThemedForm
{
    private readonly ClientManager clientManager;
    private readonly ClientSlot slot;

    private readonly TextBox nameTextBox;
    private readonly ComboBox accountComboBox;
    private readonly ComboBox characterComboBox;
    private readonly ComboBox hostResolutionComboBox;
    private readonly ThemedCheckBox matchGameResolutionCheckBox;
    private readonly ComboBox gameResolutionComboBox;
    private readonly NumericUpDown leftNumeric;
    private readonly NumericUpDown topNumeric;
    private readonly CheckBox autoLoginCheckBox;
    private readonly CheckBox autoEnterGameCheckBox;
    private readonly Label inputRiskWarningLabel;

    private ResolutionPresetItem? independentGameResolution;
    private bool isRefreshing;

    public SlotEditorForm(
        ClientManager clientManager,
        ClientSlot slot,
        bool isNew = false)
    {
        this.clientManager = clientManager;
        this.slot = slot;

        this.Text = isNew
            ? "Add client slot"
            : string.Concat("Edit slot · ", slot.Name);
        this.Icon = ResourceLoader.Net7ClientManagerIcon;
        this.StartPosition = FormStartPosition.CenterParent;
        this.ShowInTaskbar = false;
        this.ClientSize = new Size(width: 720, height: 620);
        this.BackColor = MainWindowTheme.Background;
        this.ForeColor = MainWindowTheme.Text;
        this.Font = MainWindowTheme.CreateBodyFont();
        this.ConfigureWindowChrome(
            allowResize: false,
            showMinimizeButton: false,
            showMaximizeButton: false);

        this.nameTextBox = new TextBox();
        MainWindowTheme.StyleTextBox(this.nameTextBox);

        this.accountComboBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        MainWindowTheme.StyleComboBox(this.accountComboBox);

        this.characterComboBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        MainWindowTheme.StyleComboBox(this.characterComboBox);

        this.hostResolutionComboBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        MainWindowTheme.StyleComboBox(this.hostResolutionComboBox);

        this.matchGameResolutionCheckBox = new ThemedCheckBox
        {
            Text = "Match game resolution to host size",
            AutoSize = true,
            ForeColor = MainWindowTheme.Text,
        };

        this.gameResolutionComboBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        MainWindowTheme.StyleComboBox(this.gameResolutionComboBox);

        this.leftNumeric = CreateBoundsNumeric();
        this.topNumeric = CreateBoundsNumeric();

        this.autoLoginCheckBox = new ThemedCheckBox
        {
            Text = "Automatically log in",
            AutoSize = true,
            ForeColor = MainWindowTheme.Text,
        };

        this.autoEnterGameCheckBox = new ThemedCheckBox
        {
            Text = "Automatically enter the configured character",
            AutoSize = true,
            ForeColor = MainWindowTheme.Text,
        };

        this.inputRiskWarningLabel = new Label
        {
            AutoSize = false,
            Height = 38,
            Dock = DockStyle.Fill,
            ForeColor = MainWindowTheme.Warning,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "This slot is on a screen with a negative X coordinate. Windows may reject synthetic input there.",
        };

        var saveButton = new Button
        {
            Text = isNew
                ? "Add slot"
                : "Save slot",
            Width = 104,
            Height = 34,
        };
        MainWindowTheme.StyleButton(saveButton, primary: true);
        saveButton.Click += this.SaveButton_OnClick;

        var cancelButton = new Button
        {
            Text = "Cancel",
            Width = 90,
            Height = 34,
            DialogResult = DialogResult.Cancel,
        };
        MainWindowTheme.StyleButton(cancelButton);

        var accountsButton = new Button
        {
            Text = "Manage accounts",
            Width = 126,
            Height = 30,
        };
        MainWindowTheme.StyleButton(accountsButton);
        accountsButton.Click += this.AccountsButton_OnClick;

        this.AcceptButton = saveButton;
        this.CancelButton = cancelButton;

        this.accountComboBox.SelectedIndexChanged +=
            this.AccountComboBox_OnSelectedIndexChanged;
        this.hostResolutionComboBox.SelectedIndexChanged +=
            this.HostResolutionComboBox_OnSelectedIndexChanged;
        this.matchGameResolutionCheckBox.CheckedChanged +=
            this.MatchGameResolutionCheckBox_OnCheckedChanged;
        this.gameResolutionComboBox.SelectedIndexChanged +=
            this.GameResolutionComboBox_OnSelectedIndexChanged;
        this.leftNumeric.ValueChanged +=
            this.LeftNumeric_OnValueChanged;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(all: 20),
            ColumnCount = 1,
            RowCount = 4,
            BackColor = MainWindowTheme.Background,
        };

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, height: 58));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, height: 380));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, height: 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, height: 54));

        root.Controls.Add(CreateHeader(), column: 0, row: 0);
        root.Controls.Add(this.CreateEditorPanel(accountsButton), column: 0, row: 1);
        root.Controls.Add(this.inputRiskWarningLabel, column: 0, row: 2);
        root.Controls.Add(CreateButtonBar(saveButton, cancelButton), column: 0, row: 3);

        this.Controls.Add(root);

        this.LoadSlot();
    }

    private Control CreateHeader()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
        };

        var titleLabel = new Label
        {
            Text = "Client slot settings",
            AutoSize = true,
            Font = MainWindowTheme.CreateHeadingFont(size: 15.0f),
            ForeColor = MainWindowTheme.Text,
            Location = new Point(x: 0, y: 0),
        };

        var descriptionLabel = new Label
        {
            Text = "Choose the host-window size and the resolution rendered by Earth & Beyond.",
            AutoSize = true,
            ForeColor = MainWindowTheme.MutedText,
            Location = new Point(x: 1, y: 30),
        };

        panel.Controls.Add(titleLabel);
        panel.Controls.Add(descriptionLabel);

        return panel;
    }

    private Control CreateEditorPanel(Button accountsButton)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(all: 18),
            BackColor = MainWindowTheme.Panel,
        };

        panel.Paint += static (sender, e) =>
        {
            if (sender is not Panel editorPanel)
            {
                return;
            }

            using var pen = new Pen(MainWindowTheme.Border);
            e.Graphics.DrawRectangle(
                pen,
                x: 0,
                y: 0,
                width: Math.Max(0, editorPanel.ClientSize.Width - 1),
                height: Math.Max(0, editorPanel.ClientSize.Height - 1));
        };

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 7,
            BackColor = MainWindowTheme.Panel,
        };

        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, width: 108));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, width: 56));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, width: 108));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, width: 44));

        for (var index = 0; index < 6; index++)
        {
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, height: 40));
        }

        table.RowStyles.Add(new RowStyle(SizeType.Absolute, height: 52));

        ConfigureFieldControl(this.nameTextBox);
        ConfigureFieldControl(this.accountComboBox);
        ConfigureFieldControl(this.characterComboBox, rightMargin: 0);
        ConfigureCompactFieldControl(
            this.hostResolutionComboBox,
            width: 190);
        ConfigureCompactFieldControl(
            this.gameResolutionComboBox,
            width: 190);
        ConfigureFieldControl(this.leftNumeric);
        ConfigureFieldControl(this.topNumeric, rightMargin: 0);

        accountsButton.Anchor = AnchorStyles.Left;
        accountsButton.Margin = new Padding(left: 0, top: 0, right: 0, bottom: 0);

        AddLabel(table, "Name", column: 0, row: 0);
        table.Controls.Add(this.nameTextBox, column: 1, row: 0);
        table.SetColumnSpan(this.nameTextBox, value: 3);

        AddLabel(table, "Account", column: 0, row: 1);
        table.Controls.Add(this.accountComboBox, column: 1, row: 1);
        table.Controls.Add(accountsButton, column: 2, row: 1);
        table.SetColumnSpan(accountsButton, value: 2);

        AddLabel(table, "Character", column: 0, row: 2);
        table.Controls.Add(this.characterComboBox, column: 1, row: 2);
        table.SetColumnSpan(this.characterComboBox, value: 3);

        AddLabel(table, "Host size", column: 0, row: 3);
        table.Controls.Add(this.hostResolutionComboBox, column: 1, row: 3);
        AddLabel(table, "Game resolution", column: 2, row: 3);
        table.Controls.Add(this.gameResolutionComboBox, column: 3, row: 3);

        this.matchGameResolutionCheckBox.Anchor = AnchorStyles.Left;
        this.matchGameResolutionCheckBox.Margin = Padding.Empty;
        table.Controls.Add(
            this.matchGameResolutionCheckBox,
            column: 1,
            row: 4);
        table.SetColumnSpan(this.matchGameResolutionCheckBox, value: 3);

        AddLabel(table, "Position X", column: 0, row: 5);
        table.Controls.Add(this.leftNumeric, column: 1, row: 5);
        AddLabel(table, "Position Y", column: 2, row: 5);
        table.Controls.Add(this.topNumeric, column: 3, row: 5);

        this.autoLoginCheckBox.Anchor = AnchorStyles.Left;
        this.autoEnterGameCheckBox.Anchor = AnchorStyles.Left;
        this.autoLoginCheckBox.Margin = new Padding(left: 0, top: 8, right: 12, bottom: 0);
        this.autoEnterGameCheckBox.Margin = new Padding(left: 0, top: 8, right: 0, bottom: 0);

        table.Controls.Add(this.autoLoginCheckBox, column: 0, row: 6);
        table.SetColumnSpan(this.autoLoginCheckBox, value: 2);
        table.Controls.Add(this.autoEnterGameCheckBox, column: 2, row: 6);
        table.SetColumnSpan(this.autoEnterGameCheckBox, value: 2);

        panel.Controls.Add(table);

        return panel;
    }

    private static void ConfigureFieldControl(
        Control control,
        int rightMargin = 12)
    {
        control.Dock = DockStyle.None;
        control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        control.Margin = new Padding(
            left: 0,
            top: 0,
            right: rightMargin,
            bottom: 0);
    }

    private static void ConfigureCompactFieldControl(
        Control control,
        int width)
    {
        control.Dock = DockStyle.None;
        control.Anchor = AnchorStyles.Left;
        control.Width = width;
        control.Margin = Padding.Empty;
    }

    private static Control CreateButtonBar(
        Button saveButton,
        Button cancelButton)
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(left: 0, top: 10, right: 0, bottom: 0),
        };

        panel.Controls.Add(saveButton);
        panel.Controls.Add(cancelButton);

        return panel;
    }

    private static NumericUpDown CreateBoundsNumeric()
    {
        var numeric = new NumericUpDown
        {
            Minimum = -10000,
            Maximum = 20000,
            ThousandsSeparator = false,
            Dock = DockStyle.Fill,
        };

        MainWindowTheme.StyleNumericUpDown(numeric);

        return numeric;
    }

    private static void AddLabel(
        TableLayoutPanel table,
        string text,
        int column,
        int row)
    {
        table.Controls.Add(
            new Label
            {
                Text = text,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = MainWindowTheme.MutedText,
            },
            column,
            row);
    }

    private void LoadSlot()
    {
        this.isRefreshing = true;

        try
        {
            this.nameTextBox.Text = this.slot.Name;
            this.autoLoginCheckBox.Checked = this.slot.AutoLogin;
            this.autoEnterGameCheckBox.Checked = this.slot.AutoEnterGame;
            this.leftNumeric.Value = this.slot.Bounds.Left;
            this.topNumeric.Value = this.slot.Bounds.Top;
            this.matchGameResolutionCheckBox.Checked =
                this.slot.MatchGameResolutionToHost;

            this.ReloadResolutionCombos();
            this.ReloadAccountCombo();
            this.UpdateGameResolutionState();
            this.UpdateInputRiskWarning();
        }
        finally
        {
            this.isRefreshing = false;
        }
    }

    private void ReloadResolutionCombos()
    {
        var selectedPresetName = this.slot.ResolutionPresetName;

        if (string.IsNullOrWhiteSpace(selectedPresetName))
        {
            selectedPresetName =
                this.clientManager.DefaultSlotResolutionPreset.Name;
        }

        PopulateResolutionCombo(this.hostResolutionComboBox);
        PopulateResolutionCombo(this.gameResolutionComboBox);

        var selectedHostItem = this.hostResolutionComboBox.Items
            .OfType<ResolutionPresetItem>()
            .FirstOrDefault(item => string.Equals(
                item.Name,
                selectedPresetName,
                StringComparison.Ordinal))
            ?? FindResolutionItem(
                this.hostResolutionComboBox,
                this.slot.Bounds.Width,
                this.slot.Bounds.Height)
            ?? this.hostResolutionComboBox.Items
                .OfType<ResolutionPresetItem>()
                .First();

        this.hostResolutionComboBox.SelectedItem = selectedHostItem;

        this.independentGameResolution = FindResolutionItem(
            this.gameResolutionComboBox,
            this.slot.GameResolutionWidth,
            this.slot.GameResolutionHeight)
            ?? FindResolutionItem(
                this.gameResolutionComboBox,
                selectedHostItem.Width,
                selectedHostItem.Height)
            ?? this.gameResolutionComboBox.Items
                .OfType<ResolutionPresetItem>()
                .First();

        this.gameResolutionComboBox.SelectedItem =
            this.slot.MatchGameResolutionToHost
                ? FindResolutionItem(
                    this.gameResolutionComboBox,
                    selectedHostItem.Width,
                    selectedHostItem.Height)
                : this.independentGameResolution;
    }

    private void PopulateResolutionCombo(ComboBox comboBox)
    {
        comboBox.BeginUpdate();

        try
        {
            comboBox.Items.Clear();

            foreach (var preset in this.clientManager.SlotResolutionPresets)
            {
                comboBox.Items.Add(
                    new ResolutionPresetItem(
                        preset.Name,
                        preset.Width,
                        preset.Height));
            }
        }
        finally
        {
            comboBox.EndUpdate();
        }
    }

    private static ResolutionPresetItem? FindResolutionItem(
        ComboBox comboBox,
        int width,
        int height)
    {
        return comboBox.Items
            .OfType<ResolutionPresetItem>()
            .FirstOrDefault(item =>
                item.Width == width &&
                item.Height == height);
    }

    private void ReloadAccountCombo()
    {
        var selectedAccountId = this.slot.AccountId;
        var usedAccountIds = this.clientManager.CurrentProfile.Slots
            .Where(candidate =>
                candidate.Id != this.slot.Id &&
                candidate.AccountId != null)
            .Select(candidate => candidate.AccountId!.Value)
            .ToHashSet();

        var accounts = this.clientManager.ConfiguredAccounts
            .Where(account =>
                account.Id == selectedAccountId ||
                !usedAccountIds.Contains(account.Id))
            .OrderBy(account => account.SortOrder)
            .ThenBy(
                account => account.DisplayName,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(
                account => account.LoginName,
                StringComparer.OrdinalIgnoreCase)
            .ToList();

        this.accountComboBox.BeginUpdate();

        try
        {
            this.accountComboBox.Items.Clear();
            this.accountComboBox.Items.Add(
                new AccountItem(AccountId: null, "Not configured"));

            foreach (var account in accounts)
            {
                this.accountComboBox.Items.Add(
                    new AccountItem(
                        account.Id,
                        UiObfuscationMode.AccountName(
                            account.ToString())));
            }

            var selectedItem = this.accountComboBox.Items
                .OfType<AccountItem>()
                .FirstOrDefault(item => item.AccountId == selectedAccountId);

            this.accountComboBox.SelectedItem =
                selectedItem ?? this.accountComboBox.Items[0];
        }
        finally
        {
            this.accountComboBox.EndUpdate();
        }

        this.ReloadCharacterCombo(this.slot.CharacterId);
    }

    private void ReloadCharacterCombo(Guid? preferredCharacterId)
    {
        var accountId =
            (this.accountComboBox.SelectedItem as AccountItem)?.AccountId;
        var account = this.clientManager.FindConfiguredAccount(accountId);

        this.characterComboBox.BeginUpdate();

        try
        {
            this.characterComboBox.Items.Clear();
            this.characterComboBox.Items.Add(
                new CharacterItem(CharacterId: null, "Not configured"));

            if (account != null)
            {
                foreach (var character in account.Characters
                             .OrderBy(character => character.CharacterSlotNumber))
                {
                    this.characterComboBox.Items.Add(
                        new CharacterItem(character.Id, character.ToString()));
                }
            }

            var selectedItem = this.characterComboBox.Items
                .OfType<CharacterItem>()
                .FirstOrDefault(item =>
                    item.CharacterId == preferredCharacterId);

            this.characterComboBox.SelectedItem =
                selectedItem ?? this.characterComboBox.Items[0];
        }
        finally
        {
            this.characterComboBox.EndUpdate();
        }
    }

    private void HostResolutionComboBox_OnSelectedIndexChanged(
        object? sender,
        EventArgs e)
    {
        if (this.isRefreshing ||
            !this.matchGameResolutionCheckBox.Checked ||
            this.hostResolutionComboBox.SelectedItem is not
                ResolutionPresetItem hostResolution)
        {
            return;
        }

        this.SelectGameResolution(
            hostResolution.Width,
            hostResolution.Height);
    }

    private void MatchGameResolutionCheckBox_OnCheckedChanged(
        object? sender,
        EventArgs e)
    {
        if (this.isRefreshing)
        {
            return;
        }

        if (this.matchGameResolutionCheckBox.Checked)
        {
            if (this.gameResolutionComboBox.SelectedItem is
                ResolutionPresetItem currentGameResolution)
            {
                this.independentGameResolution = currentGameResolution;
            }

            if (this.hostResolutionComboBox.SelectedItem is
                ResolutionPresetItem hostResolution)
            {
                this.SelectGameResolution(
                    hostResolution.Width,
                    hostResolution.Height);
            }
        }
        else if (this.independentGameResolution != null)
        {
            this.SelectGameResolution(
                this.independentGameResolution.Width,
                this.independentGameResolution.Height);
        }

        this.UpdateGameResolutionState();
    }

    private void GameResolutionComboBox_OnSelectedIndexChanged(
        object? sender,
        EventArgs e)
    {
        if (this.isRefreshing ||
            this.matchGameResolutionCheckBox.Checked ||
            this.gameResolutionComboBox.SelectedItem is not
                ResolutionPresetItem gameResolution)
        {
            return;
        }

        this.independentGameResolution = gameResolution;
    }

    private void SelectGameResolution(int width, int height)
    {
        var item = FindResolutionItem(
            this.gameResolutionComboBox,
            width,
            height);

        if (item != null)
        {
            this.gameResolutionComboBox.SelectedItem = item;
        }
    }

    private void UpdateGameResolutionState()
    {
        this.gameResolutionComboBox.Enabled =
            !this.matchGameResolutionCheckBox.Checked;
    }

    private void AccountComboBox_OnSelectedIndexChanged(
        object? sender,
        EventArgs e)
    {
        if (this.isRefreshing)
        {
            return;
        }

        this.ReloadCharacterCombo(preferredCharacterId: null);
    }

    private void LeftNumeric_OnValueChanged(
        object? sender,
        EventArgs e)
    {
        this.UpdateInputRiskWarning();
    }

    private void UpdateInputRiskWarning()
    {
        this.inputRiskWarningLabel.Visible =
            this.leftNumeric.Value < 0;
    }

    private void AccountsButton_OnClick(
        object? sender,
        EventArgs e)
    {
        var selectedAccountId =
            (this.accountComboBox.SelectedItem as AccountItem)?.AccountId;
        var selectedCharacterId =
            (this.characterComboBox.SelectedItem as CharacterItem)?.CharacterId;

        using var form = new AccountsForm(
            this.clientManager.ConfiguredAccounts,
            this.clientManager.SaveConfiguredAccounts);
        using var windowPlacement =
            this.clientManager.BindGlobalWindowPlacement(
                form,
                Net7ClientManager.Services.WindowPlacementIds.Accounts,
                this);

        form.ShowDialog(this);

        this.isRefreshing = true;

        try
        {
            this.ReloadAccountCombo();

            var accountItem = this.accountComboBox.Items
                .OfType<AccountItem>()
                .FirstOrDefault(item => item.AccountId == selectedAccountId);

            if (accountItem != null)
            {
                this.accountComboBox.SelectedItem = accountItem;
                this.ReloadCharacterCombo(selectedCharacterId);
            }
        }
        finally
        {
            this.isRefreshing = false;
        }
    }

    private void SaveButton_OnClick(
        object? sender,
        EventArgs e)
    {
        var name = this.nameTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            ThemedMessageDialog.ShowWarning(
                this,
                "Slot name required",
                "Give the slot a name before saving it.");

            this.nameTextBox.Focus();
            return;
        }

        var selectedAccountId =
            (this.accountComboBox.SelectedItem as AccountItem)?.AccountId;
        var selectedCharacterId =
            (this.characterComboBox.SelectedItem as CharacterItem)?.CharacterId;

        if (selectedAccountId is { } accountId &&
            this.clientManager.CurrentProfile.Slots.Exists(candidate =>
                candidate.Id != this.slot.Id &&
                candidate.AccountId == accountId))
        {
            ThemedMessageDialog.ShowWarning(
                this,
                "Account already assigned",
                "That account is already assigned to another slot in this profile.");
            return;
        }

        this.slot.Name = name;
        this.slot.AccountId = selectedAccountId;
        this.slot.CharacterId = selectedAccountId == null
            ? null
            : selectedCharacterId;
        this.slot.AutoLogin = this.autoLoginCheckBox.Checked;
        this.slot.AutoEnterGame = this.autoEnterGameCheckBox.Checked;
        this.slot.Bounds.Left = decimal.ToInt32(this.leftNumeric.Value);
        this.slot.Bounds.Top = decimal.ToInt32(this.topNumeric.Value);

        if (this.hostResolutionComboBox.SelectedItem is
            ResolutionPresetItem hostResolution)
        {
            this.slot.ResolutionPresetName = hostResolution.Name;
            this.slot.Bounds.Width = hostResolution.Width;
            this.slot.Bounds.Height = hostResolution.Height;
        }

        this.slot.MatchGameResolutionToHost =
            this.matchGameResolutionCheckBox.Checked;

        var independentResolution =
            this.matchGameResolutionCheckBox.Checked
                ? this.independentGameResolution
                : this.gameResolutionComboBox.SelectedItem as
                    ResolutionPresetItem;

        independentResolution ??=
            this.hostResolutionComboBox.SelectedItem as
                ResolutionPresetItem;

        if (independentResolution != null)
        {
            this.slot.GameResolutionWidth = independentResolution.Width;
            this.slot.GameResolutionHeight = independentResolution.Height;
        }

        this.DialogResult = DialogResult.OK;
        this.Close();
    }

    private sealed record ResolutionPresetItem(
        string Name,
        int Width,
        int Height)
    {
        public override string ToString()
        {
            return this.Name;
        }
    }

    private sealed record AccountItem(
        Guid? AccountId,
        string DisplayText)
    {
        public override string ToString()
        {
            return this.DisplayText;
        }
    }

    private sealed record CharacterItem(
        Guid? CharacterId,
        string DisplayText)
    {
        public override string ToString()
        {
            return this.DisplayText;
        }
    }
}
