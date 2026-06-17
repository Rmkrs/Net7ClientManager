// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using Net7ClientManager.Models;

public sealed partial class MainForm
{
    private Control CreateEditorPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 245,
            Padding = new Padding(all: 12),
            BackColor = Color.FromArgb(red: 248, green: 250, blue: 253),
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
        };

        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, width: 55));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, width: 45));

        layout.Controls.Add(this.CreateSelectedSlotGroupBox(), column: 0, row: 0);
        layout.Controls.Add(this.CreateRunningClientsGroupBox(), column: 1, row: 0);

        panel.Controls.Add(layout);

        return panel;
    }

    private Control CreateSelectedSlotGroupBox()
    {
        var groupBox = new GroupBox
        {
            Text = "Selected slot",
            Dock = DockStyle.Fill,
            Padding = new Padding(left: 12, top: 20, right: 12, bottom: 12),
        };

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 5,
        };

        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, width: 80));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, width: 50));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, width: 110));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, width: 50));

        table.RowStyles.Add(new RowStyle(SizeType.Absolute, height: 32));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, height: 32));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, height: 32));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, height: 34));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, height: 34));

        this.slotNameTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
        };

        this.accountComboBox = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
        };

        this.characterComboBox = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
        };

        this.autoEnterGameCheckBox = new CheckBox
        {
            AutoSize = true,
            Dock = DockStyle.Left,
            Text = string.Empty,
        };

        this.autoLoginCheckBox = new CheckBox
        {
            AutoSize = true,
            Dock = DockStyle.Left,
            Text = string.Empty,
        };

        this.resolutionComboBox = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
        };

        this.leftNumeric = this.CreateBoundsNumeric();
        this.topNumeric = this.CreateBoundsNumeric();

        this.inputRiskWarningLabel = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(red: 150, green: 92, blue: 0),
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "Input may not work on screens with negative X coordinates.",
            Visible = false,
        };

        this.removeSlotButton = new Button
        {
            Text = "Remove Slot",
            Width = 100,
            Height = 28,
            Anchor = AnchorStyles.Right | AnchorStyles.Top,
        };

        var autoSaveLabel = new Label
        {
            Text = "Changes are saved automatically",
            AutoSize = false,
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(red: 90, green: 96, blue: 106),
            TextAlign = ContentAlignment.MiddleRight,
        };

        this.accountComboBox.SelectedIndexChanged += this.AccountComboBox_SelectedIndexChanged;
        this.characterComboBox.SelectedIndexChanged += this.CharacterComboBox_SelectedIndexChanged;

        this.slotNameTextBox.Leave += this.SlotEditorValueChanged;
        this.autoLoginCheckBox.CheckedChanged += this.SlotEditorValueChanged;
        this.autoEnterGameCheckBox.CheckedChanged += this.SlotEditorValueChanged;
        this.resolutionComboBox.SelectedIndexChanged += this.ResolutionComboBox_OnSelectedIndexChanged;
        this.leftNumeric.ValueChanged += this.LeftNumeric_OnValueChanged;
        this.topNumeric.ValueChanged += this.TopNumeric_OnValueChanged;

        this.AddEditorLabel(table, "Name", column: 0, row: 0);
        table.Controls.Add(this.slotNameTextBox, column: 1, row: 0);

        this.AddEditorLabel(table, "Resolution", column: 2, row: 0);
        table.Controls.Add(this.resolutionComboBox, column: 3, row: 0);

        this.AddEditorLabel(table, "Account", column: 0, row: 1);
        table.Controls.Add(this.accountComboBox, column: 1, row: 1);

        this.AddEditorLabel(table, "X", column: 2, row: 1);
        table.Controls.Add(this.leftNumeric, column: 3, row: 1);

        this.AddEditorLabel(table, "Character", column: 0, row: 2);
        table.Controls.Add(this.characterComboBox, column: 1, row: 2);

        this.AddEditorLabel(table, "Y", column: 2, row: 2);
        table.Controls.Add(this.topNumeric, column: 3, row: 2);

        this.AddEditorLabel(table, "Auto login", column: 0, row: 3);
        table.Controls.Add(this.autoLoginCheckBox, column: 1, row: 3);

        this.AddEditorLabel(table, "Auto enter", column: 2, row: 3);
        table.Controls.Add(this.autoEnterGameCheckBox, column: 3, row: 3);

        table.Controls.Add(this.removeSlotButton, column: 2, row: 4);
        table.Controls.Add(autoSaveLabel, column: 3, row: 4);

        table.Controls.Add(this.inputRiskWarningLabel, column: 0, row: 4);
        table.SetColumnSpan(this.inputRiskWarningLabel, value: 2);

        groupBox.Controls.Add(table);

        return groupBox;
    }

    private Control CreateRunningClientsGroupBox()
    {
        var groupBox = new GroupBox
        {
            Text = "Running clients",
            Dock = DockStyle.Fill,
            Padding = new Padding(left: 12, top: 20, right: 12, bottom: 12),
        };

        this.runningClientsFlowPanel = new DoubleBufferedFlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = Color.FromArgb(red: 248, green: 250, blue: 253),
            Padding = new Padding(all: 2),
        };

        groupBox.Controls.Add(this.runningClientsFlowPanel);

        return groupBox;
    }

    private void AddEditorLabel(TableLayoutPanel table, string text, int column, int row)
    {
        var label = new Label
        {
            Text = text,
            AutoSize = true,
            ForeColor = Color.FromArgb(red: 90, green: 96, blue: 106),
            TextAlign = ContentAlignment.MiddleLeft,
            Dock = DockStyle.Fill,
        };

        table.Controls.Add(label, column, row);
    }

    private NumericUpDown CreateBoundsNumeric()
    {
        return new NumericUpDown
        {
            Minimum = -10000,
            Maximum = 20000,
            Dock = DockStyle.Fill,
            ThousandsSeparator = false,
        };
    }

    private void LoadSelectedSlotIntoEditor(ClientSlot? slot)
    {
        this.isUpdatingEditor = true;

        try
        {
            var hasSlot = slot != null;

            this.slotNameTextBox.Enabled = hasSlot;
            this.accountComboBox.Enabled = hasSlot;
            this.characterComboBox.Enabled = hasSlot;
            this.autoEnterGameCheckBox.Enabled = hasSlot;
            this.resolutionComboBox.Enabled = hasSlot;
            this.autoLoginCheckBox.Enabled = hasSlot;
            this.leftNumeric.Enabled = hasSlot;
            this.topNumeric.Enabled = hasSlot;
            this.removeSlotButton.Enabled = hasSlot;

            if (slot == null)
            {
                this.slotNameTextBox.Text = string.Empty;
                this.autoEnterGameCheckBox.Checked = false;
                this.ReloadAccountAndCharacterCombos();
                this.autoLoginCheckBox.Checked = false;
                this.leftNumeric.Value = 0;
                this.topNumeric.Value = 0;
                this.UpdateInputRiskWarning(hasInputRisk: false);
                this.RefreshResolutionComboBox(slot: null);

                return;
            }

            this.slotNameTextBox.Text = slot.Name;
            this.autoLoginCheckBox.Checked = slot.AutoLogin;
            this.autoEnterGameCheckBox.Checked = slot.AutoEnterGame;
            this.ReloadAccountAndCharacterCombos();
            this.leftNumeric.Value = slot.Bounds.Left;
            this.topNumeric.Value = slot.Bounds.Top;

            this.UpdateInputRiskWarning(slot.Bounds.Left < 0);
            this.RefreshResolutionComboBox(slot);
        }
        finally
        {
            this.isUpdatingEditor = false;
        }
    }

    private void RefreshResolutionComboBox(ClientSlot? slot)
    {
        var selectedPresetName = slot?.ResolutionPresetName;

        if (string.IsNullOrWhiteSpace(selectedPresetName))
        {
            selectedPresetName = this.clientManager.DefaultSlotResolutionPreset.Name;
        }

        this.resolutionComboBox.BeginUpdate();
        this.resolutionComboBox.Items.Clear();

        foreach (var preset in this.clientManager.SlotResolutionPresets)
        {
            this.resolutionComboBox.Items.Add(new ResolutionPresetComboBoxItem(
                preset.Name,
                preset.Width,
                preset.Height));
        }

        for (var index = 0; index < this.resolutionComboBox.Items.Count; index++)
        {
            if (this.resolutionComboBox.Items[index] is ResolutionPresetComboBoxItem item
                && string.Equals(item.Name, selectedPresetName, StringComparison.Ordinal))
            {
                this.resolutionComboBox.SelectedIndex = index;
                break;
            }
        }

        this.resolutionComboBox.EndUpdate();

        if (this.resolutionComboBox is { SelectedIndex: < 0, Items.Count: > 0 })
        {
            this.resolutionComboBox.SelectedIndex = 0;
        }
    }

    private void ResolutionComboBox_OnSelectedIndexChanged(object? sender, EventArgs e)
    {
        this.SaveSelectedSlotFromEditor();
    }

    private void ReloadAccountAndCharacterCombos()
    {
        var selectedSlot = this.layoutDesignerControl.SelectedSlot;
        var selectedAccountId = selectedSlot?.AccountId;

        var usedAccountIds = this.clientManager.CurrentProfile.Slots
            .Where(slot => (selectedSlot == null || slot.Id != selectedSlot.Id) && slot.AccountId != null)
            .Select(slot => slot.AccountId!.Value)
            .ToHashSet();

        var availableAccounts = this.clientManager.Accounts
            .Where(account => account.Id == selectedAccountId || !usedAccountIds.Contains(account.Id))
            .OrderBy(account => account.SortOrder)
            .ThenBy(account => account.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(account => account.LoginName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        this.accountComboBox.BeginUpdate();

        try
        {
            this.accountComboBox.Items.Clear();
            this.accountComboBox.Items.Add(new AccountComboItem(AccountId: null, "(none)"));

            foreach (var account in availableAccounts)
            {
                this.accountComboBox.Items.Add(new AccountComboItem(account.Id, account.ToString()));
            }

            var selectedAccount = this.accountComboBox.Items
                .OfType<AccountComboItem>()
                .FirstOrDefault(item => item.AccountId == selectedAccountId);

            this.accountComboBox.SelectedItem = selectedAccount ?? this.accountComboBox.Items[0];
        }
        finally
        {
            this.accountComboBox.EndUpdate();
        }

        this.ReloadCharacterCombo();
    }

    private void ReloadCharacterCombo()
    {
        var selectedSlot = this.layoutDesignerControl.SelectedSlot;
        var accountId = (this.accountComboBox.SelectedItem as AccountComboItem)?.AccountId;
        var account = this.clientManager.FindAccount(accountId);

        this.characterComboBox.BeginUpdate();

        try
        {
            this.characterComboBox.Items.Clear();
            this.characterComboBox.Items.Add(new CharacterComboItem(CharacterId: null, "(none)"));

            if (account != null)
            {
                foreach (var character in account.Characters)
                {
                    this.characterComboBox.Items.Add(new CharacterComboItem(character.Id, character.ToString()));
                }
            }

            var selectedCharacter = this.characterComboBox.Items
                .OfType<CharacterComboItem>()
                .FirstOrDefault(item => item.CharacterId == selectedSlot?.CharacterId);

            this.characterComboBox.SelectedItem = selectedCharacter ?? this.characterComboBox.Items[0];
        }
        finally
        {
            this.characterComboBox.EndUpdate();
        }
    }

    private void LeftNumeric_OnValueChanged(object? sender, EventArgs e)
    {
        this.UpdateInputRiskWarning(decimal.ToInt32(this.leftNumeric.Value) < 0);
        this.SaveSelectedSlotFromEditor();
    }

    private void TopNumeric_OnValueChanged(object? sender, EventArgs e)
    {
        this.SaveSelectedSlotFromEditor();
    }

    private void AccountComboBox_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (this.isUpdatingEditor)
        {
            return;
        }

        var slot = this.layoutDesignerControl.SelectedSlot;

        slot?.CharacterId = null;

        this.isUpdatingEditor = true;

        try
        {
            this.ReloadCharacterCombo();
        }
        finally
        {
            this.isUpdatingEditor = false;
        }

        this.SaveSelectedSlotFromEditor();
    }

    private void CharacterComboBox_SelectedIndexChanged(object? sender, EventArgs e)
    {
        this.SaveSelectedSlotFromEditor();
    }

    private void UpdateInputRiskWarning(bool hasInputRisk)
    {
        this.inputRiskWarningLabel.Visible = hasInputRisk;
    }

    private void SlotEditorValueChanged(object? sender, EventArgs e)
    {
        this.SaveSelectedSlotFromEditor();
    }

    private void SaveSelectedSlotFromEditor()
    {
        if (this.isUpdatingEditor)
        {
            return;
        }

        var slot = this.layoutDesignerControl.SelectedSlot;

        if (slot == null)
        {
            return;
        }

        var selectedAccountId = (this.accountComboBox.SelectedItem as AccountComboItem)?.AccountId;
        var selectedCharacterId = (this.characterComboBox.SelectedItem as CharacterComboItem)?.CharacterId;

        if (selectedAccountId is { } accountId &&
            this.clientManager.CurrentProfile.Slots.Exists(otherSlot =>
                otherSlot.Id != slot.Id &&
                otherSlot.AccountId == accountId))
        {
            MessageBox.Show(
                this,
                "This account is already assigned to another slot in this profile. Each account can only be used once per profile.",
                "Account already assigned",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

            selectedAccountId = null;
            selectedCharacterId = null;
        }

        slot.Name = this.slotNameTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(slot.Name))
        {
            slot.Name = "Unnamed Slot";
            this.slotNameTextBox.Text = slot.Name;
        }

        slot.AccountId = selectedAccountId;
        slot.CharacterId = selectedAccountId == null
            ? null
            : selectedCharacterId;

        slot.AutoLogin = this.autoLoginCheckBox.Checked;
        slot.AutoEnterGame = this.autoEnterGameCheckBox.Checked;

        if (this.resolutionComboBox.SelectedItem is ResolutionPresetComboBoxItem selectedPreset)
        {
            slot.Bounds.Width = selectedPreset.Width;
            slot.Bounds.Height = selectedPreset.Height;
            slot.ResolutionPresetName = selectedPreset.Name;
        }

        slot.Bounds.Left = decimal.ToInt32(this.leftNumeric.Value);
        slot.Bounds.Top = decimal.ToInt32(this.topNumeric.Value);

        this.clientManager.SaveSettings();
        this.clientManager.ReconcileClientsToCurrentProfile();

        var assignedClient = this.clientManager.Clients.FirstOrDefault(client => client.AssignedSlotId == slot.Id);
        assignedClient?.HostForm?.ApplySlot(slot);

        this.UpdateInputRiskWarning(slot.Bounds.Left < 0);
        this.layoutDesignerControl.Invalidate();
        this.RefreshRunningClients();
    }

    private sealed class DoubleBufferedFlowLayoutPanel : FlowLayoutPanel
    {
        public DoubleBufferedFlowLayoutPanel()
        {
            this.DoubleBuffered = true;
            this.ResizeRedraw = true;
        }
    }
}
