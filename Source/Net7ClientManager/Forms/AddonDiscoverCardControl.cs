// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using Net7ClientManager.Addons.Contracts;
using Net7ClientManager.Addons.Registry;

internal sealed class AddonDiscoverCardControl : UserControl
{
    private readonly Label nameLabel = new();
    private readonly Label sourceLabel = new();
    private readonly Label publisherLabel = new();
    private readonly Label descriptionLabel = new();
    private readonly Label tagsLabel = new();
    private readonly Label versionLabel = new();
    private readonly Label stateLabel = new();
    private readonly TableLayoutPanel actionsLayout = new();
    private readonly Button primaryButton = new();
    private readonly Button developButton = new();
    private readonly Button detailsButton = new();

    private AddonRegistrySummary? addon;
    private AddonRuntimeStatus? installedStatus;
    private AddonInstallationInfo? installation;
    private string fingerprint = "";
    private bool operationsEnabled = true;

    public AddonDiscoverCardControl()
    {
        this.SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            value: true);

        this.Size = new Size(500, 202);
        this.MinimumSize = new Size(360, 202);
        this.Padding = new Padding(14);
        this.BackColor = AddonCenterTheme.Panel;
        this.ForeColor = AddonCenterTheme.Text;

        this.nameLabel.AutoEllipsis = true;
        this.nameLabel.Font = new Font("Segoe UI", 12.5f, FontStyle.Bold);
        this.nameLabel.ForeColor = AddonCenterTheme.Text;
        this.nameLabel.Location = new Point(14, 12);
        this.nameLabel.Size = new Size(300, 28);
        this.nameLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left |
                                AnchorStyles.Right;

        this.sourceLabel.AutoSize = false;
        this.sourceLabel.TextAlign = ContentAlignment.MiddleCenter;
        this.sourceLabel.Font = new Font("Segoe UI", 8.0f, FontStyle.Bold);
        this.sourceLabel.Location = new Point(330, 14);
        this.sourceLabel.Size = new Size(130, 24);
        this.sourceLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;

        this.publisherLabel.AutoEllipsis = true;
        this.publisherLabel.Font = new Font("Segoe UI", 8.75f);
        this.publisherLabel.ForeColor = AddonCenterTheme.MutedText;
        this.publisherLabel.Location = new Point(15, 42);
        this.publisherLabel.Size = new Size(445, 20);
        this.publisherLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left |
                                     AnchorStyles.Right;

        this.descriptionLabel.AutoEllipsis = true;
        this.descriptionLabel.Font = new Font("Segoe UI", 9.0f);
        this.descriptionLabel.ForeColor = AddonCenterTheme.Text;
        this.descriptionLabel.Location = new Point(15, 67);
        this.descriptionLabel.Size = new Size(445, 40);
        this.descriptionLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left |
                                       AnchorStyles.Right;

        this.tagsLabel.AutoEllipsis = true;
        this.tagsLabel.Font = new Font("Segoe UI", 8.25f);
        this.tagsLabel.ForeColor = AddonCenterTheme.MutedText;
        this.tagsLabel.Location = new Point(15, 110);
        this.tagsLabel.Size = new Size(445, 20);
        this.tagsLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left |
                                AnchorStyles.Right;

        this.versionLabel.AutoEllipsis = true;
        this.versionLabel.Font = new Font(
            "Segoe UI",
            8.75f,
            FontStyle.Bold);
        this.versionLabel.ForeColor = AddonCenterTheme.Accent;
        this.versionLabel.Location = new Point(15, 134);
        this.versionLabel.Size = new Size(250, 20);
        this.versionLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left;

        this.stateLabel.AutoEllipsis = true;
        this.stateLabel.Font = new Font(
            "Segoe UI",
            8.75f,
            FontStyle.Bold);
        this.stateLabel.Location = new Point(270, 134);
        this.stateLabel.Size = new Size(190, 20);
        this.stateLabel.TextAlign = ContentAlignment.TopRight;
        this.stateLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left |
                                 AnchorStyles.Right;

        AddonCenterTheme.StyleButton(this.primaryButton);
        this.primaryButton.AutoSize = true;
        this.primaryButton.Anchor = AnchorStyles.Left;
        this.primaryButton.MinimumSize = new Size(110, 30);
        this.primaryButton.Margin = Padding.Empty;
        this.primaryButton.Click += this.PrimaryButton_OnClick;

        AddonCenterTheme.StyleButton(
            this.developButton,
            AddonCenterTheme.Accent);
        this.developButton.Text = "Develop";
        this.developButton.AutoSize = true;
        this.developButton.Anchor = AnchorStyles.Left;
        this.developButton.MinimumSize = new Size(92, 30);
        this.developButton.Margin = new Padding(8, 0, 0, 0);
        this.developButton.Click += this.DevelopButton_OnClick;

        AddonCenterTheme.StyleButton(this.detailsButton);
        this.detailsButton.Text = "Details";
        this.detailsButton.AutoSize = true;
        this.detailsButton.Anchor = AnchorStyles.Right;
        this.detailsButton.MinimumSize = new Size(92, 30);
        this.detailsButton.Margin = Padding.Empty;
        this.detailsButton.Click += this.DetailsButton_OnClick;

        this.actionsLayout.ColumnCount = 4;
        this.actionsLayout.RowCount = 1;
        this.actionsLayout.Location = new Point(15, 162);
        this.actionsLayout.Size = new Size(470, 30);
        this.actionsLayout.Anchor = AnchorStyles.Left | AnchorStyles.Right |
                                    AnchorStyles.Bottom;
        this.actionsLayout.Margin = Padding.Empty;
        this.actionsLayout.Padding = Padding.Empty;
        this.actionsLayout.ColumnStyles.Add(
            new ColumnStyle(SizeType.AutoSize));
        this.actionsLayout.ColumnStyles.Add(
            new ColumnStyle(SizeType.AutoSize));
        this.actionsLayout.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 100));
        this.actionsLayout.ColumnStyles.Add(
            new ColumnStyle(SizeType.AutoSize));
        this.actionsLayout.Controls.Add(this.primaryButton, 0, 0);
        this.actionsLayout.Controls.Add(this.developButton, 1, 0);
        this.actionsLayout.Controls.Add(this.detailsButton, 3, 0);

        this.Controls.Add(this.nameLabel);
        this.Controls.Add(this.sourceLabel);
        this.Controls.Add(this.publisherLabel);
        this.Controls.Add(this.descriptionLabel);
        this.Controls.Add(this.tagsLabel);
        this.Controls.Add(this.versionLabel);
        this.Controls.Add(this.stateLabel);
        this.Controls.Add(this.actionsLayout);
    }

    public event EventHandler? PrimaryActionRequested;

    public event EventHandler? DevelopRequested;

    public event EventHandler? DetailsRequested;

    public AddonRegistrySummary? Addon => this.addon;

    public AddonRuntimeStatus? InstalledStatus => this.installedStatus;

    public AddonInstallationInfo? Installation => this.installation;

    internal void ShowPrimaryGuidance()
    {
        ControlGuidancePulse.Start(this.primaryButton);
    }


    public void UpdateAddon(
        AddonRegistrySummary newAddon,
        AddonRuntimeStatus? newInstalledStatus,
        AddonInstallationInfo? newInstallation,
        bool force = false)
    {
        ArgumentNullException.ThrowIfNull(newAddon);

        var newFingerprint = CreateFingerprint(
            newAddon,
            newInstalledStatus,
            newInstallation);

        if (!force &&
            string.Equals(
                this.fingerprint,
                newFingerprint,
                StringComparison.Ordinal))
        {
            return;
        }

        this.addon = newAddon;
        this.installedStatus = newInstalledStatus;
        this.installation = newInstallation;
        this.fingerprint = newFingerprint;

        var compatibleRelease =
            AddonRegistryCompatibility.GetLatestCompatibleRelease(newAddon);
        var isInstalled = newInstallation != null;
        var developmentActive = newInstalledStatus?.IsDevelopment == true;
        var hasUpdate = compatibleRelease != null &&
                        newInstallation != null &&
                        !developmentActive &&
                        AddonRegistryCompatibility.IsNewer(
                            compatibleRelease.Version,
                            newInstallation.Version);

        this.nameLabel.Text = newAddon.Name;
        this.sourceLabel.Text = newAddon.IsOfficial
            ? "OFFICIAL"
            : "COMMUNITY";
        this.sourceLabel.BackColor = newAddon.IsOfficial
            ? Color.FromArgb(29, 77, 93)
            : Color.FromArgb(57, 68, 91);
        this.sourceLabel.ForeColor = AddonCenterTheme.Text;

        var creator = !string.IsNullOrWhiteSpace(newAddon.Author)
            ? newAddon.Author
            : newAddon.PublisherName;
        this.publisherLabel.Text = string.Concat(
            "by ",
            creator,
            "  ·  published by ",
            newAddon.PublisherName);

        this.descriptionLabel.Text = string.IsNullOrWhiteSpace(
            newAddon.Description)
                ? "No description has been supplied for this addon yet."
                : newAddon.Description;

        var terms = newAddon.Categories
            .Concat(newAddon.Tags)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        this.tagsLabel.Text = string.Join("  ·  ", terms);

        this.versionLabel.Text = compatibleRelease == null
            ? string.Concat(
                "No release supports addon API ",
                AddonApiVersion.Current)
            : developmentActive
                ? isInstalled
                    ? string.Concat(
                        "Development ",
                        newInstalledStatus!.Version,
                        "  ·  installed ",
                        newInstallation!.Version)
                    : string.Concat(
                        "Development ",
                        newInstalledStatus!.Version,
                        "  ·  latest ",
                        compatibleRelease.Version)
                : isInstalled
                    ? string.Concat(
                        "Installed ",
                        newInstallation!.Version,
                        "  ·  latest ",
                        compatibleRelease.Version)
                    : string.Concat(
                        "Latest compatible ",
                        compatibleRelease.Version);

        this.stateLabel.Text = developmentActive
            ? "Development override"
            : hasUpdate
                ? "Update available"
                : isInstalled
                    ? newInstallation!.IsPinned
                        ? "Installed · pinned"
                        : "Installed"
                    : compatibleRelease == null
                        ? "Incompatible"
                        : "Available";

        this.stateLabel.ForeColor = hasUpdate
            ? AddonCenterTheme.Warning
            : compatibleRelease == null
                ? AddonCenterTheme.Danger
                : isInstalled
                    ? AddonCenterTheme.Success
                    : AddonCenterTheme.Accent;

        this.primaryButton.Text = developmentActive
            ? "Development active"
            : hasUpdate
                ? "Update"
                : isInstalled
                    ? "Installed"
                    : compatibleRelease == null
                        ? "Unavailable"
                        : "Install";
        this.developButton.Text = developmentActive
            ? "Edit"
            : "Develop";

        this.UpdateButtonState();
        this.Invalidate();
    }

    public void SetOperationsEnabled(bool enabled)
    {
        this.operationsEnabled = enabled;
        this.UpdateButtonState();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var compatible = this.addon == null
            ? null
            : AddonRegistryCompatibility.GetLatestCompatibleRelease(
                this.addon);
        var hasUpdate = compatible != null &&
                        this.installation != null &&
                        this.installedStatus?.IsDevelopment != true &&
                        AddonRegistryCompatibility.IsNewer(
                            compatible.Version,
                            this.installation.Version);

        using var borderPen = new Pen(
            hasUpdate
                ? AddonCenterTheme.Warning
                : AddonCenterTheme.SoftBorder);

        e.Graphics.DrawRectangle(
            borderPen,
            0,
            0,
            this.ClientSize.Width - 1,
            this.ClientSize.Height - 1);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.primaryButton.Click -= this.PrimaryButton_OnClick;
            this.developButton.Click -= this.DevelopButton_OnClick;
            this.detailsButton.Click -= this.DetailsButton_OnClick;
        }

        base.Dispose(disposing);
    }

    private void UpdateButtonState()
    {
        var compatible = this.addon == null
            ? null
            : AddonRegistryCompatibility.GetLatestCompatibleRelease(
                this.addon);
        var hasUpdate = compatible != null &&
                        this.installation != null &&
                        this.installedStatus?.IsDevelopment != true &&
                        AddonRegistryCompatibility.IsNewer(
                            compatible.Version,
                            this.installation.Version);

        this.primaryButton.Enabled =
            this.operationsEnabled &&
            this.installedStatus?.IsDevelopment != true &&
            compatible != null &&
            (this.installation == null || hasUpdate);
        this.developButton.Enabled =
            this.operationsEnabled &&
            this.addon != null &&
            (this.installedStatus?.IsDevelopment == true ||
             compatible != null);
        this.detailsButton.Enabled =
            this.operationsEnabled && this.addon != null;
    }

    private void PrimaryButton_OnClick(
        object? sender,
        EventArgs e)
    {
        this.PrimaryActionRequested?.Invoke(this, EventArgs.Empty);
    }

    private void DevelopButton_OnClick(
        object? sender,
        EventArgs e)
    {
        this.DevelopRequested?.Invoke(this, EventArgs.Empty);
    }

    private void DetailsButton_OnClick(
        object? sender,
        EventArgs e)
    {
        this.DetailsRequested?.Invoke(this, EventArgs.Empty);
    }

    private static string CreateFingerprint(
        AddonRegistrySummary addon,
        AddonRuntimeStatus? installedStatus,
        AddonInstallationInfo? installation)
    {
        return string.Join(
            "\u001f",
            addon.Id,
            addon.Name,
            addon.Description,
            addon.Author,
            addon.PublisherId,
            addon.PublisherName,
            addon.IsOfficial,
            string.Join(',', addon.Categories),
            string.Join(',', addon.Tags),
            string.Join(
                ',',
                addon.Releases.Select(release =>
                    string.Concat(
                        release.Version,
                        "@",
                        release.ApiVersion,
                        "@",
                        release.PackageSha256))),
            installedStatus?.Version,
            installedStatus?.IsDevelopment,
            installation?.Version,
            installation?.PackageSha256,
            installation?.IsPinned);
    }
}
