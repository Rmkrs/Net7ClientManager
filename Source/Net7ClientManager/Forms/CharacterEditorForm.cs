// ReSharper disable StringLiteralTypo
// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using System.Globalization;
using Net7ClientManager.Models;

public sealed class CharacterEditorForm : Form
{
    private static readonly CharacterProfessionOption[] professionOptions =
    [
        new("Jenquai", "Defender"),
        new("Jenquai", "Explorer"),
        new("Jenquai", "Seeker"),

        new("Progen", "Sentinel"),
        new("Progen", "Warrior"),
        new("Progen", "Privateer"),

        new("Terran", "Enforcer"),
        new("Terran", "Trader"),
        new("Terran", "Scout"),
    ];

    private readonly TextBox nameTextBox = new();
    private readonly ComboBox professionComboBox = new();

    private readonly GameCharacter character;

    public CharacterEditorForm(GameCharacter character)
    {
        this.character = character;

        this.Text = string.Create(CultureInfo.InvariantCulture, $"Edit Character Slot {character.CharacterSlotNumber}");
        this.StartPosition = FormStartPosition.CenterParent;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.ClientSize = new Size(460, 180);

        this.BuildUi();

        this.nameTextBox.Text = character.Name;

        var selectedProfession = professionOptions.FirstOrDefault(option =>
            string.Equals(option.Race, character.Race, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(option.Profession, character.Profession, StringComparison.OrdinalIgnoreCase));

        this.professionComboBox.SelectedItem = selectedProfession ?? professionOptions[0];
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            Padding = new Padding(14),
        };

        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        this.Controls.Add(root);

        this.nameTextBox.Dock = DockStyle.Fill;

        this.professionComboBox.Dock = DockStyle.Fill;
        this.professionComboBox.DropDownStyle = ComboBoxStyle.DropDownList;

        foreach (var option in professionOptions)
        {
            this.professionComboBox.Items.Add(option);
        }

        root.Controls.Add(this.CreateLabel("Name"), 0, 0);
        root.Controls.Add(this.nameTextBox, 1, 0);

        root.Controls.Add(this.CreateLabel("Profession"), 0, 1);
        root.Controls.Add(this.professionComboBox, 1, 1);

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
        };

        var saveButton = new Button
        {
            Text = "Save",
            DialogResult = DialogResult.OK,
            Width = 90,
        };

        var cancelButton = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Width = 90,
        };

        saveButton.Click += this.SaveButton_OnClick;

        buttonPanel.Controls.Add(saveButton);
        buttonPanel.Controls.Add(cancelButton);

        root.Controls.Add(buttonPanel, 0, 2);
        root.SetColumnSpan(buttonPanel, 2);

        this.AcceptButton = saveButton;
        this.CancelButton = cancelButton;
    }

    private Label CreateLabel(string text)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
        };
    }

    private void SaveButton_OnClick(object? sender, EventArgs e)
    {
        var name = this.nameTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show(
                this,
                "Character name is required.",
                "Character",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

            this.DialogResult = DialogResult.None;
            return;
        }

        if (this.professionComboBox.SelectedItem is not CharacterProfessionOption option)
        {
            MessageBox.Show(
                this,
                "Profession is required.",
                "Character",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

            this.DialogResult = DialogResult.None;
            return;
        }

        this.character.Name = name;
        this.character.Race = option.Race;
        this.character.Profession = option.Profession;
    }

    private sealed record CharacterProfessionOption(string Race, string Profession)
    {
        public override string ToString()
        {
            return $"{this.Race} - {this.Profession}";
        }
    }
}
