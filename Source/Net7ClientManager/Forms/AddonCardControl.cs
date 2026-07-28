// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using Net7ClientManager.Addons.Contracts;

internal sealed class AddonCardControl : UserControl
{
    private readonly Label nameLabel = new();
    private readonly Label sourceLabel = new();
    private readonly Label authorLabel = new();
    private readonly Label descriptionLabel = new();
    private readonly Label versionLabel = new();
    private readonly Label stateLabel = new();
    private readonly Label detailLabel = new();
    private readonly TableLayoutPanel actionsLayout = new();
    private readonly Button toggleButton = new();
    private readonly Button updateButton = new();
    private readonly Button reloadButton = new();
    private readonly Button developButton = new();
    private readonly Button detailsButton = new();
    private readonly Button uninstallButton = new();
    private readonly Button folderButton = new();

    private AddonRuntimeStatus? status;
    private string statusFingerprint = "";
    private bool operationsEnabled = true;
    private bool canPersistSelection;

    public AddonCardControl()
    {
        this.SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            value: true);

        this.Size = new Size(500, 194);
        this.MinimumSize = new Size(360, 194);
        this.Margin = new Padding(0, 0, 12, 12);
        this.Padding = new Padding(14);
        this.BackColor = AddonCenterTheme.Panel;
        this.ForeColor = AddonCenterTheme.Text;

        this.nameLabel.AutoEllipsis = true;
        this.nameLabel.Font = new Font("Segoe UI", 12.5f, FontStyle.Bold);
        this.nameLabel.ForeColor = AddonCenterTheme.Text;
        this.nameLabel.Location = new Point(14, 12);
        this.nameLabel.Size = new Size(300, 28);
        this.nameLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        this.sourceLabel.AutoSize = false;
        this.sourceLabel.TextAlign = ContentAlignment.MiddleCenter;
        this.sourceLabel.Font = new Font("Segoe UI", 8.0f, FontStyle.Bold);
        this.sourceLabel.Location = new Point(330, 14);
        this.sourceLabel.Size = new Size(130, 24);
        this.sourceLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;

        this.authorLabel.AutoEllipsis = true;
        this.authorLabel.Font = new Font("Segoe UI", 8.75f);
        this.authorLabel.ForeColor = AddonCenterTheme.MutedText;
        this.authorLabel.Location = new Point(15, 42);
        this.authorLabel.Size = new Size(445, 20);
        this.authorLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        this.descriptionLabel.AutoEllipsis = true;
        this.descriptionLabel.Font = new Font("Segoe UI", 9.0f);
        this.descriptionLabel.ForeColor = AddonCenterTheme.Text;
        this.descriptionLabel.Location = new Point(15, 67);
        this.descriptionLabel.Size = new Size(445, 40);
        this.descriptionLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        this.versionLabel.AutoEllipsis = true;
        this.versionLabel.Font = new Font("Segoe UI", 8.75f, FontStyle.Bold);
        this.versionLabel.ForeColor = AddonCenterTheme.Accent;
        this.versionLabel.Location = new Point(15, 111);
        this.versionLabel.Size = new Size(220, 20);
        this.versionLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left;

        this.stateLabel.AutoEllipsis = true;
        this.stateLabel.Font = new Font("Segoe UI", 8.75f, FontStyle.Bold);
        this.stateLabel.Location = new Point(240, 111);
        this.stateLabel.Size = new Size(220, 20);
        this.stateLabel.TextAlign = ContentAlignment.TopRight;
        this.stateLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        this.detailLabel.AutoEllipsis = true;
        this.detailLabel.Font = new Font("Segoe UI", 8.25f);
        this.detailLabel.ForeColor = AddonCenterTheme.MutedText;
        this.detailLabel.Location = new Point(15, 132);
        this.detailLabel.Size = new Size(445, 18);
        this.detailLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        AddonCenterTheme.StyleButton(this.toggleButton);
        this.toggleButton.AutoSize = true;
        this.toggleButton.Anchor = AnchorStyles.Left;
        this.toggleButton.MinimumSize = new Size(108, 30);
        this.toggleButton.Margin = new Padding(0, 0, 6, 0);
        this.toggleButton.Click += this.ToggleButton_OnClick;

        AddonCenterTheme.StyleButton(
            this.updateButton,
            AddonCenterTheme.Warning);
        this.updateButton.Text = "Update";
        this.updateButton.AutoSize = true;
        this.updateButton.Anchor = AnchorStyles.Left;
        this.updateButton.MinimumSize = new Size(82, 30);
        this.updateButton.Margin = new Padding(0, 0, 6, 0);
        this.updateButton.Click += this.UpdateButton_OnClick;

        AddonCenterTheme.StyleButton(
            this.reloadButton,
            AddonCenterTheme.Accent);
        this.reloadButton.Text = "Reload";
        this.reloadButton.AutoSize = true;
        this.reloadButton.Anchor = AnchorStyles.Left;
        this.reloadButton.MinimumSize = new Size(82, 30);
        this.reloadButton.Margin = new Padding(0, 0, 6, 0);
        this.reloadButton.Click += this.ReloadButton_OnClick;

        AddonCenterTheme.StyleButton(
            this.developButton,
            AddonCenterTheme.Accent);
        this.developButton.Text = "Develop";
        this.developButton.AutoSize = true;
        this.developButton.Anchor = AnchorStyles.Left;
        this.developButton.MinimumSize = new Size(82, 30);
        this.developButton.Margin = new Padding(0, 0, 6, 0);
        this.developButton.Click += this.DevelopButton_OnClick;

        AddonCenterTheme.StyleButton(this.detailsButton);
        this.detailsButton.Text = "Details";
        this.detailsButton.AutoSize = true;
        this.detailsButton.Anchor = AnchorStyles.Right;
        this.detailsButton.MinimumSize = new Size(82, 30);
        this.detailsButton.Margin = new Padding(0, 0, 6, 0);
        this.detailsButton.Click += this.DetailsButton_OnClick;

        AddonCenterTheme.StyleButton(
            this.uninstallButton,
            AddonCenterTheme.Danger);
        this.uninstallButton.Text = "Uninstall";
        this.uninstallButton.AutoSize = true;
        this.uninstallButton.Anchor = AnchorStyles.Right;
        this.uninstallButton.MinimumSize = new Size(82, 30);
        this.uninstallButton.Margin = new Padding(0, 0, 6, 0);
        this.uninstallButton.Click += this.UninstallButton_OnClick;

        AddonCenterTheme.StyleButton(this.folderButton);
        this.folderButton.Text = "Folder";
        this.folderButton.AutoSize = true;
        this.folderButton.Anchor = AnchorStyles.Right;
        this.folderButton.MinimumSize = new Size(82, 30);
        this.folderButton.Margin = Padding.Empty;
        this.folderButton.Click += this.FolderButton_OnClick;

        this.actionsLayout.ColumnCount = 8;
        this.actionsLayout.RowCount = 1;
        this.actionsLayout.Location = new Point(15, 154);
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
            new ColumnStyle(SizeType.AutoSize));
        this.actionsLayout.ColumnStyles.Add(
            new ColumnStyle(SizeType.AutoSize));
        this.actionsLayout.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 100));
        this.actionsLayout.ColumnStyles.Add(
            new ColumnStyle(SizeType.AutoSize));
        this.actionsLayout.ColumnStyles.Add(
            new ColumnStyle(SizeType.AutoSize));
        this.actionsLayout.ColumnStyles.Add(
            new ColumnStyle(SizeType.AutoSize));
        this.actionsLayout.Controls.Add(this.toggleButton, 0, 0);
        this.actionsLayout.Controls.Add(this.updateButton, 1, 0);
        this.actionsLayout.Controls.Add(this.reloadButton, 2, 0);
        this.actionsLayout.Controls.Add(this.developButton, 3, 0);
        this.actionsLayout.Controls.Add(this.detailsButton, 5, 0);
        this.actionsLayout.Controls.Add(this.uninstallButton, 6, 0);
        this.actionsLayout.Controls.Add(this.folderButton, 7, 0);

        this.Controls.Add(this.nameLabel);
        this.Controls.Add(this.sourceLabel);
        this.Controls.Add(this.authorLabel);
        this.Controls.Add(this.descriptionLabel);
        this.Controls.Add(this.versionLabel);
        this.Controls.Add(this.stateLabel);
        this.Controls.Add(this.detailLabel);
        this.Controls.Add(this.actionsLayout);
    }

    public event EventHandler? ToggleRequested;

    public event EventHandler? UpdateRequested;

    public event EventHandler? ReloadRequested;

    public event EventHandler? DevelopRequested;

    public event EventHandler? DetailsRequested;

    public event EventHandler? UninstallRequested;

    public event EventHandler? OpenFolderRequested;

    public AddonRuntimeStatus? Status => this.status;

    public void UpdateStatus(
        AddonRuntimeStatus newStatus,
        bool canPersist,
        bool force = false)
    {
        ArgumentNullException.ThrowIfNull(newStatus);

        var fingerprint = CreateFingerprint(newStatus, canPersist);

        if (!force &&
            string.Equals(
                this.statusFingerprint,
                fingerprint,
                StringComparison.Ordinal))
        {
            return;
        }

        this.status = newStatus;
        this.statusFingerprint = fingerprint;
        this.canPersistSelection = canPersist;

        this.nameLabel.Text = newStatus.Name;
        this.sourceLabel.Text = newStatus.Source.ToUpperInvariant();
        this.sourceLabel.BackColor = GetSourceBackColor(newStatus.Source);
        this.sourceLabel.ForeColor = GetSourceTextColor(newStatus.Source);

        this.authorLabel.Text = string.IsNullOrWhiteSpace(newStatus.Author)
            ? "Publisher unknown"
            : string.Concat("by ", newStatus.Author);

        this.descriptionLabel.Text = string.IsNullOrWhiteSpace(
            newStatus.Description)
                ? "No description has been supplied for this addon yet."
                : newStatus.Description;

        var pinText = newStatus.IsPinned
            ? "  ·  PINNED"
            : "";

        this.versionLabel.Text = newStatus.HasUpdate
            ? string.Concat(
                "Installed ",
                newStatus.Version,
                "  ·  ",
                newStatus.AvailableVersion,
                " available",
                pinText)
            : string.IsNullOrWhiteSpace(newStatus.Version)
                ? "Version unavailable"
                : string.Concat(
                    "Version ",
                    newStatus.Version,
                    pinText);

        this.stateLabel.Text = GetStateText(newStatus);
        this.stateLabel.ForeColor = GetStateColor(newStatus);

        var detail = !string.IsNullOrWhiteSpace(newStatus.Error)
            ? newStatus.Error
            : newStatus.Detail;

        this.detailLabel.Text = detail;
        this.detailLabel.ForeColor = string.IsNullOrWhiteSpace(newStatus.Error)
            ? AddonCenterTheme.MutedText
            : AddonCenterTheme.Danger;

        this.toggleButton.Text = newStatus.IsEnabled
            ? "Disable for client"
            : "Enable for client";

        this.updateButton.Visible =
            newStatus.HasUpdate && !newStatus.IsDevelopment;
        this.reloadButton.Visible = newStatus.IsDevelopment;
        this.developButton.Visible = !string.Equals(
            newStatus.Source,
            "Host",
            StringComparison.Ordinal);
        this.developButton.Text = newStatus.IsDevelopment
            ? "Edit"
            : "Develop";
        var isPackaged = !newStatus.IsDevelopment &&
                         !string.Equals(
                             newStatus.Source,
                             "Host",
                             StringComparison.Ordinal);
        this.detailsButton.Visible = isPackaged;
        this.uninstallButton.Visible = isPackaged || newStatus.IsDevelopment;
        this.uninstallButton.Text = newStatus.IsDevelopment
            ? "Discard"
            : "Uninstall";
        this.folderButton.Visible = newStatus.IsDevelopment;
        this.UpdateButtonState();
        this.Invalidate();
    }

    public void SetOperationsEnabled(bool enabled)
    {
        if (this.operationsEnabled == enabled)
        {
            return;
        }

        this.operationsEnabled = enabled;
        this.UpdateButtonState();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        using var borderPen = new Pen(
            this.status?.HasUpdate == true
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
            this.toggleButton.Click -= this.ToggleButton_OnClick;
            this.updateButton.Click -= this.UpdateButton_OnClick;
            this.reloadButton.Click -= this.ReloadButton_OnClick;
            this.developButton.Click -= this.DevelopButton_OnClick;
            this.detailsButton.Click -= this.DetailsButton_OnClick;
            this.uninstallButton.Click -= this.UninstallButton_OnClick;
            this.folderButton.Click -= this.FolderButton_OnClick;
        }

        base.Dispose(disposing);
    }

    private void UpdateButtonState()
    {
        var currentStatus = this.status;

        this.toggleButton.Enabled =
            this.operationsEnabled &&
            this.canPersistSelection &&
            currentStatus != null &&
            (currentStatus.IsEnabled || currentStatus.IsValid);

        this.updateButton.Enabled =
            this.operationsEnabled &&
            currentStatus is
            {
                IsValid: true,
                HasUpdate: true,
                IsDevelopment: false,
            };

        this.reloadButton.Enabled =
            this.operationsEnabled &&
            currentStatus is
            {
                IsValid: true,
                IsEnabled: true,
                IsDevelopment: true,
            };

        this.developButton.Enabled =
            this.operationsEnabled &&
            currentStatus != null &&
            (currentStatus.IsDevelopment || currentStatus.IsValid) &&
            !string.Equals(
                currentStatus.Source,
                "Host",
                StringComparison.Ordinal);

        this.detailsButton.Enabled =
            this.operationsEnabled &&
            currentStatus is { IsDevelopment: false };
        this.uninstallButton.Enabled =
            this.operationsEnabled &&
            currentStatus != null &&
            !string.Equals(
                currentStatus.Source,
                "Host",
                StringComparison.Ordinal);
        this.folderButton.Enabled = this.operationsEnabled;
    }

    private void ToggleButton_OnClick(
        object? sender,
        EventArgs e)
    {
        this.ToggleRequested?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateButton_OnClick(
        object? sender,
        EventArgs e)
    {
        this.UpdateRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ReloadButton_OnClick(
        object? sender,
        EventArgs e)
    {
        this.ReloadRequested?.Invoke(this, EventArgs.Empty);
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

    private void UninstallButton_OnClick(
        object? sender,
        EventArgs e)
    {
        this.UninstallRequested?.Invoke(this, EventArgs.Empty);
    }

    private void FolderButton_OnClick(
        object? sender,
        EventArgs e)
    {
        this.OpenFolderRequested?.Invoke(this, EventArgs.Empty);
    }

    private static string CreateFingerprint(
        AddonRuntimeStatus status,
        bool canPersist)
    {
        return string.Join(
            "\u001f",
            status.AddonId,
            status.Name,
            status.Version,
            status.AvailableVersion,
            status.Source,
            status.Author,
            status.Description,
            status.IsValid,
            status.IsEnabled,
            status.IsDevelopment,
            status.IsPinned,
            status.PublisherName,
            status.State,
            status.Error,
            status.Detail,
            canPersist);
    }

    private static string GetStateText(AddonRuntimeStatus status)
    {
        if (!status.IsValid)
        {
            return "Unavailable";
        }

        if (!status.IsEnabled)
        {
            return "Disabled for this client";
        }

        return status.State switch
        {
            AddonRuntimeState.Running => "Running",
            AddonRuntimeState.Loading => "Starting",
            AddonRuntimeState.WaitingForContext => "Waiting for game context",
            AddonRuntimeState.Suspended => "Temporarily suspended",
            AddonRuntimeState.Failed => "Failed",
            AddonRuntimeState.Stopping => "Stopping",
            AddonRuntimeState.Unavailable => "Unavailable",
            AddonRuntimeState.Disabled => "Enabled for this client",
            _ => status.State.ToString(),
        };
    }

    private static Color GetStateColor(AddonRuntimeStatus status)
    {
        if (!status.IsValid ||
            status.State is AddonRuntimeState.Failed or
                AddonRuntimeState.Unavailable)
        {
            return AddonCenterTheme.Danger;
        }

        if (!status.IsEnabled)
        {
            return AddonCenterTheme.MutedText;
        }

        return status.State switch
        {
            AddonRuntimeState.Running => AddonCenterTheme.Success,
            AddonRuntimeState.Suspended => AddonCenterTheme.Warning,
            AddonRuntimeState.WaitingForContext => AddonCenterTheme.Accent,
            AddonRuntimeState.Loading or AddonRuntimeState.Stopping =>
                AddonCenterTheme.Warning,
            AddonRuntimeState.Failed or AddonRuntimeState.Unavailable =>
                AddonCenterTheme.Danger,
            AddonRuntimeState.Disabled => AddonCenterTheme.MutedText,
            _ => AddonCenterTheme.MutedText,
        };
    }

    private static Color GetSourceBackColor(string source)
    {
        return source switch
        {
            "Host" => Color.FromArgb(52, 47, 94),
            "Official package" => Color.FromArgb(29, 77, 93),
            "Community package" => Color.FromArgb(57, 68, 91),
            "Development" => Color.FromArgb(91, 66, 30),
            _ => AddonCenterTheme.ElevatedPanel,
        };
    }

    private static Color GetSourceTextColor(string source)
    {
        return source == "Development"
            ? AddonCenterTheme.Warning
            : AddonCenterTheme.Text;
    }
}
