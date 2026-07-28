// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;


internal sealed class AddonUninstallDialog : AddonCenterDialogForm
{
    private readonly RadioButton keepSettingsRadio = new();
    private readonly RadioButton removeSettingsRadio = new();

    public AddonUninstallDialog(string addonName)
        : base("Uninstall Addon", new Size(520, 250))
    {

        var titleLabel = new Label
        {
            Text = string.Concat("UNINSTALL ", addonName.ToUpperInvariant()),
            Font = new Font("Segoe UI", 14.0f, FontStyle.Bold),
            ForeColor = AddonCenterTheme.Text,
            Location = new Point(24, 20),
            Size = new Size(470, 32),
            AutoEllipsis = true,
        };

        var explanationLabel = new Label
        {
            Text = "The addon will be uninstalled and disabled for every client slot. Choose whether its saved data and window layout should remain for a later reinstall.",
            ForeColor = AddonCenterTheme.MutedText,
            Location = new Point(26, 60),
            Size = new Size(464, 48),
        };

        this.keepSettingsRadio.Text =
            "Keep addon data and window layout for later";
        this.keepSettingsRadio.Checked = true;
        this.keepSettingsRadio.ForeColor = AddonCenterTheme.Text;
        this.keepSettingsRadio.Location = new Point(28, 116);
        this.keepSettingsRadio.Size = new Size(440, 24);

        this.removeSettingsRadio.Text =
            "Remove addon data and window layout too";
        this.removeSettingsRadio.ForeColor = AddonCenterTheme.Warning;
        this.removeSettingsRadio.Location = new Point(28, 146);
        this.removeSettingsRadio.Size = new Size(440, 24);

        var cancelButton = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new Point(286, 195),
            Size = new Size(98, 34),
        };
        AddonCenterTheme.StyleButton(cancelButton);

        var uninstallButton = new Button
        {
            Text = "Uninstall",
            DialogResult = DialogResult.OK,
            Location = new Point(392, 195),
            Size = new Size(98, 34),
        };
        AddonCenterTheme.StyleButton(
            uninstallButton,
            AddonCenterTheme.Danger);

        this.AcceptButton = uninstallButton;
        this.CancelButton = cancelButton;
        this.ContentPanel.Controls.Add(titleLabel);
        this.ContentPanel.Controls.Add(explanationLabel);
        this.ContentPanel.Controls.Add(this.keepSettingsRadio);
        this.ContentPanel.Controls.Add(this.removeSettingsRadio);
        this.ContentPanel.Controls.Add(cancelButton);
        this.ContentPanel.Controls.Add(uninstallButton);
    }

    public bool RemoveSettings => this.removeSettingsRadio.Checked;
}
