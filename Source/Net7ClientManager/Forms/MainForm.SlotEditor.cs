// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using Net7ClientManager.Models;
using Net7ClientManager.Services;

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

        this.accountNameTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
        };

        this.passwordStatusLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(red: 90, green: 96, blue: 106),
        };

        this.setPasswordButton = new Button
        {
            Text = "Set...",
            Width = 65,
            Height = 28,
            Anchor = AnchorStyles.Left | AnchorStyles.Top,
        };

        this.clearPasswordButton = new Button
        {
            Text = "Clear",
            Width = 65,
            Height = 28,
            Anchor = AnchorStyles.Left | AnchorStyles.Top,
        };

        this.setPasswordButton.Click += this.SetPasswordButton_OnClick;
        this.clearPasswordButton.Click += this.ClearPasswordButton_OnClick;

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

        this.saveSlotButton = new Button
        {
            Text = "Save Slot",
            Width = 90,
            Height = 28,
            Anchor = AnchorStyles.Right | AnchorStyles.Top,
        };

        this.resolutionComboBox.SelectedIndexChanged += this.ResolutionComboBox_OnSelectedIndexChanged;
        this.leftNumeric.ValueChanged += this.LeftNumeric_OnValueChanged;
        this.removeSlotButton.Click += this.RemoveSlotButton_OnClick;
        this.saveSlotButton.Click += this.SaveSlotButton_OnClick;

        this.AddEditorLabel(table, "Name", column: 0, row: 0);
        table.Controls.Add(this.slotNameTextBox, column: 1, row: 0);

        this.AddEditorLabel(table, "Resolution", column: 2, row: 0);
        table.Controls.Add(this.resolutionComboBox, column: 3, row: 0);

        this.AddEditorLabel(table, "Account", column: 0, row: 1);
        table.Controls.Add(this.accountNameTextBox, column: 1, row: 1);

        this.AddEditorLabel(table, "X", column: 2, row: 1);
        table.Controls.Add(this.leftNumeric, column: 3, row: 1);

        this.AddEditorLabel(table, "Password", column: 0, row: 2);

        var passwordPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };

        passwordPanel.Controls.Add(this.passwordStatusLabel);
        passwordPanel.Controls.Add(this.setPasswordButton);
        passwordPanel.Controls.Add(this.clearPasswordButton);

        this.passwordStatusLabel.Width = 80;

        table.Controls.Add(passwordPanel, column: 1, row: 2);

        this.AddEditorLabel(table, "Y", column: 2, row: 2);
        table.Controls.Add(this.topNumeric, column: 3, row: 2);

        this.AddEditorLabel(table, "Auto login", column: 0, row: 3);
        table.Controls.Add(this.autoLoginCheckBox, column: 1, row: 3);

        table.Controls.Add(this.removeSlotButton, column: 2, row: 3);
        table.Controls.Add(this.saveSlotButton, column: 3, row: 3);

        table.Controls.Add(this.inputRiskWarningLabel, column: 0, row: 4);
        table.SetColumnSpan(this.inputRiskWarningLabel, value: 4);

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
            this.accountNameTextBox.Enabled = hasSlot;
            this.passwordStatusLabel.Enabled = hasSlot;
            this.setPasswordButton.Enabled = hasSlot;
            this.clearPasswordButton.Enabled = hasSlot && !string.IsNullOrWhiteSpace(slot?.ProtectedPassword);
            this.resolutionComboBox.Enabled = hasSlot;
            this.autoLoginCheckBox.Enabled = hasSlot;
            this.leftNumeric.Enabled = hasSlot;
            this.topNumeric.Enabled = hasSlot;
            this.removeSlotButton.Enabled = hasSlot;
            this.saveSlotButton.Enabled = hasSlot;

            if (slot == null)
            {
                this.slotNameTextBox.Text = string.Empty;
                this.accountNameTextBox.Text = string.Empty;
                this.passwordStatusLabel.Text = "Not set";
                this.clearPasswordButton.Enabled = false;
                this.autoLoginCheckBox.Checked = false;
                this.leftNumeric.Value = 0;
                this.topNumeric.Value = 0;
                this.UpdateInputRiskWarning(hasInputRisk: false);
                this.RefreshResolutionComboBox(slot: null);

                return;
            }

            this.slotNameTextBox.Text = slot.Name;
            this.accountNameTextBox.Text = slot.AccountName ?? string.Empty;
            var hasPassword = !string.IsNullOrWhiteSpace(slot.ProtectedPassword);
            this.passwordStatusLabel.Text = hasPassword ? "Set" : "Not set";
            this.clearPasswordButton.Enabled = hasPassword;
            this.autoLoginCheckBox.Checked = slot.AutoLogin;
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
        if (this.isUpdatingEditor)
        {
            return;
        }

        var slot = this.layoutDesignerControl.SelectedSlot;

        if (slot == null)
        {
            return;
        }

        if (this.resolutionComboBox.SelectedItem is not ResolutionPresetComboBoxItem selectedPreset)
        {
            return;
        }

        slot.Bounds.Width = selectedPreset.Width;
        slot.Bounds.Height = selectedPreset.Height;
        slot.ResolutionPresetName = selectedPreset.Name;

        this.clientManager.SaveSettings();

        var assignedClient = this.clientManager.Clients.FirstOrDefault(client => client.AssignedSlotId == slot.Id);
        assignedClient?.HostForm?.ApplySlot(slot);

        this.layoutDesignerControl.Invalidate();
    }

    private void SetPasswordButton_OnClick(object? sender, EventArgs e)
    {
        var slot = this.layoutDesignerControl.SelectedSlot;

        if (slot == null)
        {
            return;
        }

        using var dialog = new PasswordPromptForm();

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        slot.ProtectedPassword = PasswordProtector.Protect(dialog.Password);

        this.clientManager.SaveSettings();
        this.LoadSelectedSlotIntoEditor(slot);
    }

    private void ClearPasswordButton_OnClick(object? sender, EventArgs e)
    {
        var slot = this.layoutDesignerControl.SelectedSlot;

        if (slot == null)
        {
            return;
        }

        slot.ProtectedPassword = null;

        this.clientManager.SaveSettings();
        this.LoadSelectedSlotIntoEditor(slot);
    }

    private void LeftNumeric_OnValueChanged(object? sender, EventArgs e)
    {
        if (this.isUpdatingEditor)
        {
            return;
        }

        this.UpdateInputRiskWarning(decimal.ToInt32(this.leftNumeric.Value) < 0);
    }

    private void UpdateInputRiskWarning(bool hasInputRisk)
    {
        this.inputRiskWarningLabel.Visible = hasInputRisk;
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
