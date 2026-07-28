// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using Net7ClientManager.Addons.Contracts;

internal sealed class PublishAddonDialog : AddonCenterDialogForm
{
    private readonly TextBox summaryTextBox = new();

    public PublishAddonDialog(
        AddonManifest manifest,
        string pilotName)
        : base("Publish Addon", new Size(640, 500))
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var heading = new Label
        {
            Text = "PUBLISH TO NET7 FORGE",
            Font = new Font("Segoe UI", 14.0f, FontStyle.Bold),
            ForeColor = AddonCenterTheme.Text,
            Location = new Point(24, 20),
            Size = new Size(580, 32),
        };

        var identity = new Label
        {
            Text = string.Concat(
                manifest.Name,
                "  ",
                manifest.Version,
                Environment.NewLine,
                manifest.Id),
            ForeColor = AddonCenterTheme.Text,
            Font = new Font("Segoe UI", 10.0f, FontStyle.Bold),
            Location = new Point(26, 62),
            Size = new Size(580, 52),
        };

        var publisher = new Label
        {
            Text = string.Concat(
                "Publishing from pilot ",
                pilotName,
                ". The pilot used for the first published version becomes the public publisher name and is kept for every later version."),
            ForeColor = AddonCenterTheme.MutedText,
            Location = new Point(26, 120),
            Size = new Size(580, 54),
        };

        var warningPanel = new Panel
        {
            BackColor = AddonCenterTheme.Panel,
            Location = new Point(24, 180),
            Size = new Size(592, 98),
            Padding = new Padding(14, 12, 14, 12),
        };
        warningPanel.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "Published releases are permanent and immutable. This version can never be replaced or withdrawn by its publisher. Later releases must use a higher version number.",
            ForeColor = AddonCenterTheme.Warning,
            Font = new Font("Segoe UI", 9.0f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
        });

        var summaryLabel = new Label
        {
            Text = "RELEASE NOTES (OPTIONAL)",
            ForeColor = AddonCenterTheme.MutedText,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            Location = new Point(26, 294),
            Size = new Size(220, 22),
        };

        this.summaryTextBox.Multiline = true;
        this.summaryTextBox.AcceptsReturn = true;
        this.summaryTextBox.ScrollBars = ScrollBars.Vertical;
        this.summaryTextBox.MaxLength = 2048;
        this.summaryTextBox.PlaceholderText =
            "What changed in this release?";
        this.summaryTextBox.BackColor = AddonCenterTheme.Button;
        this.summaryTextBox.ForeColor = AddonCenterTheme.Text;
        this.summaryTextBox.BorderStyle = BorderStyle.FixedSingle;
        this.summaryTextBox.Location = new Point(26, 320);
        this.summaryTextBox.Size = new Size(590, 100);

        var cancelButton = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new Point(414, 440),
            Size = new Size(94, 36),
        };
        AddonCenterTheme.StyleButton(cancelButton);

        var publishButton = new Button
        {
            Text = "Publish",
            DialogResult = DialogResult.OK,
            Location = new Point(518, 440),
            Size = new Size(98, 36),
        };
        AddonCenterTheme.StyleButton(
            publishButton,
            AddonCenterTheme.Success);

        this.AcceptButton = publishButton;
        this.CancelButton = cancelButton;
        this.ContentPanel.Controls.Add(heading);
        this.ContentPanel.Controls.Add(identity);
        this.ContentPanel.Controls.Add(publisher);
        this.ContentPanel.Controls.Add(warningPanel);
        this.ContentPanel.Controls.Add(summaryLabel);
        this.ContentPanel.Controls.Add(this.summaryTextBox);
        this.ContentPanel.Controls.Add(cancelButton);
        this.ContentPanel.Controls.Add(publishButton);
    }

    public string? Summary => string.IsNullOrWhiteSpace(
            this.summaryTextBox.Text)
        ? null
        : this.summaryTextBox.Text.Trim();
}
