// ReSharper disable StringLiteralTypo
// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using System.Globalization;
using Net7ClientManager.Core;
using Net7ClientManager.Models;

public sealed class CharacterEditorForm : ThemedForm
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
    private readonly bool guideCharacterName;

    public CharacterEditorForm(GameCharacter character)
        : this(character, guideCharacterName: false)
    {
    }

    internal CharacterEditorForm(
        GameCharacter character,
        bool guideCharacterName)
    {
        this.character = character;
        this.guideCharacterName = guideCharacterName;

        this.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"Edit character slot {character.CharacterSlotNumber}");
        this.Icon = ResourceLoader.Net7ClientManagerIcon;
        this.StartPosition = FormStartPosition.CenterParent;
        this.ShowInTaskbar = false;
        this.ClientSize = new Size(width: 520, height: 260);
        this.BackColor = MainWindowTheme.Background;
        this.ForeColor = MainWindowTheme.Text;
        this.Font = MainWindowTheme.CreateBodyFont();
        this.ConfigureWindowChrome(
            allowResize: false,
            showMinimizeButton: false,
            showMaximizeButton: false);
        this.ConfigureHelpTopic(HelpTopicIds.AutoLogin);
        this.ConfigureHelpTour(this.ShowHelpTour);

        this.BuildUi();

        this.nameTextBox.Text = character.Name;

        var selectedProfession = professionOptions.FirstOrDefault(option =>
            string.Equals(
                option.Race,
                character.Race,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                option.Profession,
                character.Profession,
                StringComparison.OrdinalIgnoreCase));

        this.professionComboBox.SelectedItem =
            selectedProfession ?? professionOptions[0];
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        if (this.guideCharacterName)
        {
            this.BeginInvoke(
                () => ControlGuidancePulse.Start(this.nameTextBox));
        }
    }

    private void ShowHelpTour()
    {
        GuidedTourOverlay.Show(
            this,
            [
                new GuidedTourStep(
                    () => this.nameTextBox,
                    "Enter the character name",
                    "Use the name exactly as it appears on the game's character selection screen."),
                new GuidedTourStep(
                    () => this.professionComboBox,
                    "Choose the character profession",
                    "The profession helps identify the character throughout Client Manager, including client slots and the Pilot Archive."),
            ]);
    }

    private void BuildUi()
    {
        MainWindowTheme.StyleTextBox(this.nameTextBox);
        MainWindowTheme.StyleComboBox(this.professionComboBox);

        this.nameTextBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;

        this.professionComboBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        this.professionComboBox.DropDownStyle = ComboBoxStyle.DropDownList;

        foreach (var option in professionOptions)
        {
            this.professionComboBox.Items.Add(option);
        }

        var saveButton = new Button
        {
            Text = "Save",
            DialogResult = DialogResult.OK,
            Width = 92,
            Height = 34,
            Margin = new Padding(left: 6, top: 0, right: 0, bottom: 0),
        };
        MainWindowTheme.StyleButton(saveButton, primary: true);
        saveButton.Click += this.SaveButton_OnClick;

        var cancelButton = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Width = 92,
            Height = 34,
            Margin = Padding.Empty,
        };
        MainWindowTheme.StyleButton(cancelButton);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            Padding = new Padding(all: 22),
            BackColor = MainWindowTheme.Background,
        };

        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, width: 126));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, width: 100));

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, height: 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, height: 42));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, height: 100));

        root.Controls.Add(this.CreateLabel("Name"), column: 0, row: 0);
        root.Controls.Add(this.nameTextBox, column: 1, row: 0);

        root.Controls.Add(this.CreateLabel("Profession"), column: 0, row: 1);
        root.Controls.Add(this.professionComboBox, column: 1, row: 1);

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(left: 0, top: 12, right: 0, bottom: 0),
            BackColor = MainWindowTheme.Background,
        };

        buttonPanel.Controls.Add(saveButton);
        buttonPanel.Controls.Add(cancelButton);

        root.Controls.Add(buttonPanel, column: 0, row: 2);
        root.SetColumnSpan(buttonPanel, value: 2);
        this.Controls.Add(root);

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
            ForeColor = MainWindowTheme.MutedText,
        };
    }

    private void SaveButton_OnClick(object? sender, EventArgs e)
    {
        var name = this.nameTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            ThemedMessageDialog.ShowWarning(
                this,
                "Character",
                "Character name is required.");

            this.DialogResult = DialogResult.None;
            return;
        }

        if (this.professionComboBox.SelectedItem is not
            CharacterProfessionOption option)
        {
            ThemedMessageDialog.ShowWarning(
                this,
                "Character",
                "Profession is required.");

            this.DialogResult = DialogResult.None;
            return;
        }

        this.character.Name = name;
        this.character.Race = option.Race;
        this.character.Profession = option.Profession;
    }

    private sealed record CharacterProfessionOption(
        string Race,
        string Profession)
    {
        public override string ToString()
        {
            return $"{this.Race} - {this.Profession}";
        }
    }
}
