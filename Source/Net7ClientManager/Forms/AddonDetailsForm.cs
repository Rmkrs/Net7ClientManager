// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using System.Globalization;
using Net7ClientManager.Addons.Contracts;
using Net7ClientManager.Addons.Registry;
using Net7ClientManager.Core;

internal sealed class AddonDetailsForm : AddonCenterDialogForm
{
    private readonly ClientManager clientManager;
    private readonly int ownerProcessId;
    private readonly AddonRegistrySummary addon;
    private readonly ComboBox versionComboBox = new();
    private readonly Label releaseDetailLabel = new();
    private readonly Label stateLabel = new();
    private readonly CheckBox pinCheckBox = new();
    private readonly Button installButton = new();
    private readonly Button toggleButton = new();
    private readonly Button uninstallButton = new();

    private bool updatingControls;
    private bool operationInProgress;

    public AddonDetailsForm(
        ClientManager clientManager,
        int ownerProcessId,
        AddonRegistrySummary addon)
        : base("Net7 Addon Details", new Size(760, 620))
    {
        this.clientManager = clientManager;
        this.ownerProcessId = ownerProcessId;
        this.addon = addon;


        this.BuildUi();
        this.PopulateReleases();
        this.RefreshState();
    }

    private void BuildUi()
    {
        var titleLabel = new Label
        {
            Text = this.addon.Name,
            Font = new Font("Segoe UI", 18.0f, FontStyle.Bold),
            ForeColor = AddonCenterTheme.Text,
            Location = new Point(28, 22),
            Size = new Size(520, 38),
            AutoEllipsis = true,
        };

        var badgeLabel = new Label
        {
            Text = this.addon.IsOfficial
                ? "OFFICIAL"
                : "COMMUNITY",
            BackColor = this.addon.IsOfficial
                ? Color.FromArgb(29, 77, 93)
                : Color.FromArgb(57, 68, 91),
            ForeColor = AddonCenterTheme.Text,
            Font = new Font("Segoe UI", 8.0f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(582, 26),
            Size = new Size(142, 26),
        };

        var creator = !string.IsNullOrWhiteSpace(this.addon.Author)
            ? this.addon.Author
            : this.addon.PublisherName;
        var creatorLabel = new Label
        {
            Text = string.Concat(
                "by ",
                creator,
                "  ·  published by ",
                this.addon.PublisherName),
            ForeColor = AddonCenterTheme.MutedText,
            Location = new Point(30, 64),
            Size = new Size(690, 22),
            AutoEllipsis = true,
        };

        var descriptionLabel = new Label
        {
            Text = string.IsNullOrWhiteSpace(this.addon.Description)
                ? "No description has been supplied for this addon yet."
                : this.addon.Description,
            ForeColor = AddonCenterTheme.Text,
            Location = new Point(30, 98),
            Size = new Size(690, 58),
        };

        var terms = this.addon.Categories
            .Concat(this.addon.Tags)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var tagsLabel = new Label
        {
            Text = string.Concat(
                "Categories and tags: ",
                string.Join("  ·  ", terms)),
            ForeColor = AddonCenterTheme.Accent,
            Location = new Point(30, 160),
            Size = new Size(690, 24),
            AutoEllipsis = true,
        };

        var releasePanel = new Panel
        {
            BackColor = AddonCenterTheme.Panel,
            Location = new Point(28, 204),
            Size = new Size(696, 252),
            Anchor = AnchorStyles.Top | AnchorStyles.Left |
                     AnchorStyles.Right,
            Padding = new Padding(18),
        };

        var releaseHeading = new Label
        {
            Text = "VERSION HISTORY",
            Font = new Font("Segoe UI", 11.0f, FontStyle.Bold),
            ForeColor = AddonCenterTheme.Text,
            Location = new Point(18, 16),
            Size = new Size(200, 26),
        };

        this.versionComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        this.versionComboBox.BackColor = AddonCenterTheme.Button;
        this.versionComboBox.ForeColor = AddonCenterTheme.Text;
        this.versionComboBox.FlatStyle = FlatStyle.Flat;
        this.versionComboBox.Font = new Font("Segoe UI", 9.25f);
        this.versionComboBox.Location = new Point(20, 52);
        this.versionComboBox.Size = new Size(240, 28);
        this.versionComboBox.SelectedIndexChanged +=
            this.VersionComboBox_OnSelectedIndexChanged;

        this.releaseDetailLabel.ForeColor = AddonCenterTheme.MutedText;
        this.releaseDetailLabel.Location = new Point(20, 92);
        this.releaseDetailLabel.Size = new Size(650, 96);
        this.releaseDetailLabel.Anchor = AnchorStyles.Top |
                                         AnchorStyles.Left |
                                         AnchorStyles.Right;

        AddonCenterTheme.StyleButton(
            this.installButton,
            AddonCenterTheme.Accent);
        this.installButton.Location = new Point(20, 202);
        this.installButton.Size = new Size(158, 34);
        this.installButton.Click += this.InstallButton_OnClick;

        this.pinCheckBox.Text = "Pin installed version";
        this.pinCheckBox.ForeColor = AddonCenterTheme.Text;
        this.pinCheckBox.Location = new Point(196, 208);
        this.pinCheckBox.Size = new Size(190, 24);
        this.pinCheckBox.CheckedChanged += this.PinCheckBox_OnCheckedChanged;

        releasePanel.Controls.Add(releaseHeading);
        releasePanel.Controls.Add(this.versionComboBox);
        releasePanel.Controls.Add(this.releaseDetailLabel);
        releasePanel.Controls.Add(this.installButton);
        releasePanel.Controls.Add(this.pinCheckBox);

        this.stateLabel.ForeColor = AddonCenterTheme.MutedText;
        this.stateLabel.Location = new Point(30, 474);
        this.stateLabel.Size = new Size(690, 42);
        this.stateLabel.Anchor = AnchorStyles.Left |
                                 AnchorStyles.Right |
                                 AnchorStyles.Bottom;

        AddonCenterTheme.StyleButton(this.toggleButton);
        this.toggleButton.Location = new Point(28, 540);
        this.toggleButton.Size = new Size(146, 38);
        this.toggleButton.Click += this.ToggleButton_OnClick;
        this.toggleButton.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;

        AddonCenterTheme.StyleButton(
            this.uninstallButton,
            AddonCenterTheme.Danger);
        this.uninstallButton.Text = "Uninstall";
        this.uninstallButton.Location = new Point(184, 540);
        this.uninstallButton.Size = new Size(116, 38);
        this.uninstallButton.Click += this.UninstallButton_OnClick;
        this.uninstallButton.Anchor = AnchorStyles.Left |
                                      AnchorStyles.Bottom;

        var closeButton = new Button
        {
            Text = "Close",
            DialogResult = DialogResult.Cancel,
            Location = new Point(608, 540),
            Size = new Size(116, 38),
            Anchor = AnchorStyles.Right | AnchorStyles.Bottom,
        };
        AddonCenterTheme.StyleButton(closeButton);

        this.CancelButton = closeButton;
        this.ContentPanel.Controls.Add(titleLabel);
        this.ContentPanel.Controls.Add(badgeLabel);
        this.ContentPanel.Controls.Add(creatorLabel);
        this.ContentPanel.Controls.Add(descriptionLabel);
        this.ContentPanel.Controls.Add(tagsLabel);
        this.ContentPanel.Controls.Add(releasePanel);
        this.ContentPanel.Controls.Add(this.stateLabel);
        this.ContentPanel.Controls.Add(this.toggleButton);
        this.ContentPanel.Controls.Add(this.uninstallButton);
        this.ContentPanel.Controls.Add(closeButton);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.versionComboBox.SelectedIndexChanged -=
                this.VersionComboBox_OnSelectedIndexChanged;
            this.installButton.Click -= this.InstallButton_OnClick;
            this.pinCheckBox.CheckedChanged -=
                this.PinCheckBox_OnCheckedChanged;
            this.toggleButton.Click -= this.ToggleButton_OnClick;
            this.uninstallButton.Click -= this.UninstallButton_OnClick;
        }

        base.Dispose(disposing);
    }

    private void PopulateReleases()
    {
        this.versionComboBox.Items.Clear();

        foreach (var release in this.addon.Releases)
        {
            this.versionComboBox.Items.Add(
                new ReleaseOption(release));
        }

        var installation = this.clientManager
            .GetAddonInstallations()
            .FirstOrDefault(candidate => string.Equals(
                candidate.AddonId,
                this.addon.Id,
                StringComparison.Ordinal));
        var preferredVersion = installation?.IsPinned == true
            ? installation.Version
            : AddonRegistryCompatibility
                .GetLatestCompatibleRelease(this.addon)?
                .Version;
        var preferredIndex = this.versionComboBox.Items
            .Cast<ReleaseOption>()
            .Select((option, index) => new
            {
                option.Release.Version,
                Index = index,
            })
            .FirstOrDefault(item => string.Equals(
                item.Version,
                preferredVersion,
                StringComparison.Ordinal))?
            .Index ?? 0;

        if (this.versionComboBox.Items.Count > 0)
        {
            this.versionComboBox.SelectedIndex = preferredIndex;
        }
    }

    private void RefreshState()
    {
        this.updatingControls = true;

        try
        {
            var installation = this.clientManager
                .GetAddonInstallations()
                .FirstOrDefault(candidate => string.Equals(
                    candidate.AddonId,
                    this.addon.Id,
                    StringComparison.Ordinal));
            var status = this.clientManager
                .GetAddonStatuses(this.ownerProcessId)
                .FirstOrDefault(candidate => string.Equals(
                    candidate.AddonId,
                    this.addon.Id,
                    StringComparison.Ordinal));

            this.pinCheckBox.Checked =
                installation?.IsPinned == true;
            this.pinCheckBox.Enabled =
                !this.operationInProgress && installation != null;

            this.toggleButton.Text = status?.IsEnabled == true
                ? "Disable for slot"
                : "Enable for slot";
            this.toggleButton.Enabled =
                !this.operationInProgress &&
                installation != null &&
                this.clientManager.CanPersistAddonSelection(
                    this.ownerProcessId);

            this.uninstallButton.Enabled =
                !this.operationInProgress && installation != null;

            var selectedRelease =
                (this.versionComboBox.SelectedItem as ReleaseOption)?
                .Release;
            var selectedIsInstalled =
                selectedRelease != null &&
                installation != null &&
                string.Equals(
                    selectedRelease.Version,
                    installation.Version,
                    StringComparison.Ordinal);
            var compatible = selectedRelease?.ApiVersion ==
                             AddonApiVersion.Current;

            this.installButton.Text = selectedIsInstalled
                ? "Installed"
                : installation == null
                    ? "Install version"
                    : "Switch version";
            this.installButton.Enabled =
                !this.operationInProgress &&
                selectedRelease != null &&
                compatible &&
                !selectedIsInstalled &&
                status?.IsDevelopment != true;

            var installedText = installation == null
                ? "Not installed on this machine."
                : string.Concat(
                    "Installed version ",
                    installation.Version,
                    installation.IsPinned
                        ? " is pinned."
                        : ".");
            var slotText = status?.IsEnabled == true
                ? " Enabled for this client slot."
                : " Disabled for this client slot.";
            var developmentText = status?.IsDevelopment == true
                ? " A local development workspace currently overrides the installed package."
                : "";

            this.stateLabel.Text = string.Concat(
                installedText,
                slotText,
                developmentText);
            this.UpdateReleaseDetails();
        }
        finally
        {
            this.updatingControls = false;
        }
    }

    private void UpdateReleaseDetails()
    {
        if (this.versionComboBox.SelectedItem is not ReleaseOption option)
        {
            this.releaseDetailLabel.Text = "";
            return;
        }

        var release = option.Release;
        var compatibility = release.ApiVersion == AddonApiVersion.Current
            ? string.Concat(
                "Compatible with addon API ",
                release.ApiVersion,
                ".")
            : string.Concat(
                "Requires addon API ",
                release.ApiVersion,
                "; this client supports ",
                AddonApiVersion.Current,
                ".");

        this.releaseDetailLabel.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"Published {release.PublishedAt.ToLocalTime():d MMMM yyyy HH:mm} " +
            $"· {FormatPackageSize(release.PackageSize)}\r\n" +
            $"{compatibility}\r\n\r\n" +
            $"{release.Summary ?? "No release notes were supplied."}");
    }

    private void SetOperationState(
        bool inProgress,
        string? message = null)
    {
        this.operationInProgress = inProgress;
        this.RefreshState();

        if (!string.IsNullOrWhiteSpace(message))
        {
            this.stateLabel.Text = message;
            this.stateLabel.ForeColor = AddonCenterTheme.Accent;
        }
    }

    private async void InstallButton_OnClick(
        object? sender,
        EventArgs e)
    {
        if (this.versionComboBox.SelectedItem is not ReleaseOption option)
        {
            return;
        }

        await this.RunCommandAsync(
            string.Concat(
                "Installing ",
                this.addon.Name,
                " ",
                option.Release.Version,
                "..."),
            () => this.clientManager.InstallAddonVersionAsync(
                this.ownerProcessId,
                this.addon.Id,
                option.Release.Version));
    }

    private async void PinCheckBox_OnCheckedChanged(
        object? sender,
        EventArgs e)
    {
        if (this.updatingControls)
        {
            return;
        }

        await this.RunCommandAsync(
            this.pinCheckBox.Checked
                ? "Pinning installed version..."
                : "Removing version pin...",
            () => this.clientManager.SetAddonPinnedAsync(
                this.addon.Id,
                this.pinCheckBox.Checked));
    }

    private async void ToggleButton_OnClick(
        object? sender,
        EventArgs e)
    {
        var status = this.clientManager
            .GetAddonStatuses(this.ownerProcessId)
            .FirstOrDefault(candidate => string.Equals(
                candidate.AddonId,
                this.addon.Id,
                StringComparison.Ordinal));

        await this.RunCommandAsync(
            status?.IsEnabled == true
                ? "Disabling addon for this client..."
                : "Enabling addon for this client...",
            () => status?.IsEnabled == true
                ? this.clientManager.DisableAddonAsync(
                    this.ownerProcessId,
                    this.addon.Id)
                : this.clientManager.EnableAddonAsync(
                    this.ownerProcessId,
                    this.addon.Id));
    }

    private async void UninstallButton_OnClick(
        object? sender,
        EventArgs e)
    {
        using var dialog = new AddonUninstallDialog(
            this.addon.Name);

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        this.SetOperationState(
            inProgress: true,
            "Uninstalling addon...");

        string? error = null;

        try
        {
            var result = await this.clientManager.UninstallAddonAsync(
                this.addon.Id,
                dialog.RemoveSettings);

            if (!result.Succeeded)
            {
                error = result.Error;
            }
            else
            {
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }
        finally
        {
            if (!this.IsDisposed)
            {
                this.SetOperationState(inProgress: false);
            }
        }

        if (!string.IsNullOrWhiteSpace(error) &&
            !this.IsDisposed)
        {
            this.stateLabel.Text = error;
            this.stateLabel.ForeColor = AddonCenterTheme.Danger;
        }
    }

    private void VersionComboBox_OnSelectedIndexChanged(
        object? sender,
        EventArgs e)
    {
        this.RefreshState();
    }

    private async Task RunCommandAsync(
        string operationText,
        Func<Task<AddonCommandResult>> command)
    {
        this.SetOperationState(
            inProgress: true,
            operationText);

        string? error = null;

        try
        {
            var result = await command();

            if (!result.Succeeded)
            {
                error = result.Error;
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }
        finally
        {
            this.SetOperationState(inProgress: false);
        }

        if (!string.IsNullOrWhiteSpace(error))
        {
            this.stateLabel.Text = error;
            this.stateLabel.ForeColor = AddonCenterTheme.Danger;
        }
    }

    private static string FormatPackageSize(long bytes)
    {
        return bytes < 1024
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{bytes} B")
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{bytes / 1024d:0.0} KiB");
    }

    private sealed record ReleaseOption(AddonRegistryRelease Release)
    {
        public override string ToString()
        {
            return this.Release.ApiVersion == AddonApiVersion.Current
                ? this.Release.Version
                : string.Concat(
                    this.Release.Version,
                    "  (API ",
                    this.Release.ApiVersion,
                    ")");
        }
    }
}
