// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

internal sealed class NewAddonWorkspaceDialog : AddonCenterDialogForm
{
    private readonly TextBox idTextBox = new();
    private readonly TextBox nameTextBox = new();
    private readonly TextBox authorTextBox = new();
    private readonly Label validationLabel = new();
    private readonly Button createButton = new();

    public NewAddonWorkspaceDialog()
        : base("New Addon Workspace", new Size(560, 348))
    {
        var heading = new Label
        {
            Text = "CREATE A LOCAL ADDON",
            Font = new Font("Segoe UI", 14.0f, FontStyle.Bold),
            ForeColor = AddonCenterTheme.Text,
            Location = new Point(24, 20),
            Size = new Size(500, 32),
        };

        var description = new Label
        {
            Text = "The id becomes the workspace folder and stable addon identity. Use lowercase letters, digits, dots, underscores or hyphens.",
            ForeColor = AddonCenterTheme.MutedText,
            Location = new Point(26, 58),
            Size = new Size(505, 46),
        };

        this.ConfigureTextBox(this.idTextBox, "com.example.my-addon", 122);
        this.ConfigureTextBox(this.nameTextBox, "My Addon", 184);
        this.ConfigureTextBox(this.authorTextBox, "Creator name (optional)", 246);

        this.AddLabel("ADDON ID", 104);
        this.AddLabel("DISPLAY NAME", 166);
        this.AddLabel("AUTHOR", 228);

        this.validationLabel.ForeColor = AddonCenterTheme.Warning;
        this.validationLabel.Location = new Point(26, 286);
        this.validationLabel.Size = new Size(300, 40);
        this.validationLabel.TextAlign = ContentAlignment.MiddleLeft;

        var cancelButton = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new Point(340, 294),
            Size = new Size(92, 36),
        };
        AddonCenterTheme.StyleButton(cancelButton);

        this.createButton.Text = "Create";
        this.createButton.DialogResult = DialogResult.OK;
        this.createButton.Location = new Point(440, 294);
        this.createButton.Size = new Size(92, 36);
        AddonCenterTheme.StyleButton(
            this.createButton,
            AddonCenterTheme.Success);

        this.idTextBox.TextChanged += this.Input_OnTextChanged;
        this.nameTextBox.TextChanged += this.Input_OnTextChanged;
        this.createButton.Click += this.CreateButton_OnClick;

        this.AcceptButton = this.createButton;
        this.CancelButton = cancelButton;
        this.ContentPanel.Controls.Add(heading);
        this.ContentPanel.Controls.Add(description);
        this.ContentPanel.Controls.Add(this.idTextBox);
        this.ContentPanel.Controls.Add(this.nameTextBox);
        this.ContentPanel.Controls.Add(this.authorTextBox);
        this.ContentPanel.Controls.Add(this.validationLabel);
        this.ContentPanel.Controls.Add(cancelButton);
        this.ContentPanel.Controls.Add(this.createButton);
        this.UpdateValidation();
    }

    public string AddonId => this.idTextBox.Text.Trim();

    public string AddonName => this.nameTextBox.Text.Trim();

    public string? Author => string.IsNullOrWhiteSpace(this.authorTextBox.Text)
        ? null
        : this.authorTextBox.Text.Trim();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.idTextBox.TextChanged -= this.Input_OnTextChanged;
            this.nameTextBox.TextChanged -= this.Input_OnTextChanged;
            this.createButton.Click -= this.CreateButton_OnClick;
        }

        base.Dispose(disposing);
    }

    private void ConfigureTextBox(
        TextBox textBox,
        string placeholder,
        int top)
    {
        textBox.PlaceholderText = placeholder;
        textBox.BackColor = AddonCenterTheme.Button;
        textBox.ForeColor = AddonCenterTheme.Text;
        textBox.BorderStyle = BorderStyle.FixedSingle;
        textBox.Location = new Point(152, top);
        textBox.Size = new Size(380, 28);
    }

    private void AddLabel(string text, int top)
    {
        this.ContentPanel.Controls.Add(new Label
        {
            Text = text,
            ForeColor = AddonCenterTheme.MutedText,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            Location = new Point(26, top + 2),
            Size = new Size(116, 24),
            TextAlign = ContentAlignment.MiddleLeft,
        });
    }

    private void Input_OnTextChanged(object? sender, EventArgs e)
    {
        this.UpdateValidation();
    }

    private void CreateButton_OnClick(object? sender, EventArgs e)
    {
        if (!this.UpdateValidation())
        {
            this.DialogResult = DialogResult.None;
        }
    }

    private bool UpdateValidation()
    {
        var id = this.AddonId;
        var name = this.AddonName;
        string? error = null;

        if (string.IsNullOrWhiteSpace(id))
        {
            error = "An addon id is required.";
        }
        else if (id.Length > 128 ||
                 !char.IsAsciiLetterOrDigit(id[0]) ||
                 !char.IsAsciiLetterOrDigit(id[^1]) ||
                 id.Any(character =>
                     !char.IsAsciiLetterOrDigit(character) &&
                     character is not '.' and not '_' and not '-'))
        {
            error = "Use lowercase ASCII letters, digits, dots, underscores or hyphens.";
        }
        else if (id.Any(char.IsUpper))
        {
            error = "Addon ids must be lowercase.";
        }
        else if (string.IsNullOrWhiteSpace(name))
        {
            error = "A display name is required.";
        }

        this.validationLabel.Text = error ?? "Creates addon.json, main.lua and a lib folder.";
        this.validationLabel.ForeColor = error == null
            ? AddonCenterTheme.MutedText
            : AddonCenterTheme.Warning;
        this.createButton.Enabled = error == null;
        return error == null;
    }
}
