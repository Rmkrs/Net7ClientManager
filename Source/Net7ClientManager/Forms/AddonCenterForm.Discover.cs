// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using System.Globalization;
using Net7ClientManager.Addons.Contracts;
using Net7ClientManager.Addons.Registry;

public sealed partial class AddonCenterForm
{
    private const int MaxDiscoverContentWidth = 1240;

    private readonly Panel discoverContentPanel = new();
    private readonly TextBox discoverSearchTextBox = new();
    private readonly ComboBox discoverFilterComboBox = new();
    private readonly Button discoverRefreshButton = new();
    private readonly CheckBox automaticCheckCheckBox = new();
    private readonly Label discoverStatusLabel = new();
    private readonly Panel discoverScrollPanel = new();
    private readonly TableLayoutPanel discoverCardsGrid = new();
    private readonly Label discoverEmptyLabel = new();
    private readonly Dictionary<string, AddonDiscoverCardControl>
        discoverCardsByAddonId = new(StringComparer.Ordinal);

    private string discoverFingerprint = "";
    private string discoverLayoutFingerprint = "";

    private void BuildDiscoverPage()
    {
        var toolsPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 58,
            BackColor = AddonCenterTheme.Background,
            ColumnCount = 3,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = new Padding(0, 8, 0, 8),
            ColumnStyles =
            {
                new ColumnStyle(SizeType.Percent, 100),
                new ColumnStyle(SizeType.Absolute, 190),
                new ColumnStyle(SizeType.Absolute, 176),
            },
        };

        this.discoverSearchTextBox.PlaceholderText =
            "Search addons, creators, descriptions, categories, or tags...";
        this.discoverSearchTextBox.BackColor = AddonCenterTheme.Button;
        this.discoverSearchTextBox.ForeColor = AddonCenterTheme.Text;
        this.discoverSearchTextBox.BorderStyle = BorderStyle.FixedSingle;
        this.discoverSearchTextBox.Font = new Font("Segoe UI", 9.25f);
        this.discoverSearchTextBox.Dock = DockStyle.Fill;
        this.discoverSearchTextBox.Margin = new Padding(0, 0, 12, 0);

        this.discoverFilterComboBox.DropDownStyle =
            ComboBoxStyle.DropDownList;
        this.discoverFilterComboBox.BackColor = AddonCenterTheme.Button;
        this.discoverFilterComboBox.ForeColor = AddonCenterTheme.Text;
        this.discoverFilterComboBox.FlatStyle = FlatStyle.Flat;
        this.discoverFilterComboBox.Font = new Font("Segoe UI", 9.0f);
        this.discoverFilterComboBox.Dock = DockStyle.Fill;
        this.discoverFilterComboBox.Margin = new Padding(0, 0, 12, 0);
        this.discoverFilterComboBox.Items.AddRange(
        [
            "All addons",
            "Official",
            "Community",
            "Installed",
            "Updates available",
        ]);
        this.discoverFilterComboBox.SelectedIndex = 0;

        AddonCenterTheme.StyleButton(this.discoverRefreshButton);
        this.discoverRefreshButton.Text = "Refresh catalog";
        this.discoverRefreshButton.Dock = DockStyle.Fill;
        this.discoverRefreshButton.Margin = Padding.Empty;

        toolsPanel.Controls.Add(this.discoverSearchTextBox, 0, 0);
        toolsPanel.Controls.Add(this.discoverFilterComboBox, 1, 0);
        toolsPanel.Controls.Add(this.discoverRefreshButton, 2, 0);

        var preferencesPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 68,
            BackColor = AddonCenterTheme.Panel,
            Padding = new Padding(14, 8, 14, 8),
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            ColumnStyles =
            {
                new ColumnStyle(SizeType.Percent, 100),
            },
            RowStyles =
            {
                new RowStyle(SizeType.Absolute, 24),
                new RowStyle(SizeType.Absolute, 28),
            },
        };

        var preferencesHeading = new Label
        {
            Text = "UPDATE PREFERENCES",
            Font = new Font("Segoe UI", 9.0f, FontStyle.Bold),
            ForeColor = AddonCenterTheme.Text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = Padding.Empty,
        };

        this.automaticCheckCheckBox.Text =
            "Check for addon updates automatically";
        this.automaticCheckCheckBox.ForeColor = AddonCenterTheme.Text;
        this.automaticCheckCheckBox.Dock = DockStyle.Fill;
        this.automaticCheckCheckBox.Margin = Padding.Empty;
        this.automaticCheckCheckBox.Checked =
            this.clientManager.CheckAddonUpdatesAutomatically;

        preferencesPanel.Controls.Add(preferencesHeading, 0, 0);
        preferencesPanel.Controls.Add(
            this.automaticCheckCheckBox,
            0,
            1);

        this.discoverStatusLabel.Dock = DockStyle.Top;
        this.discoverStatusLabel.Height = 34;
        this.discoverStatusLabel.ForeColor = AddonCenterTheme.MutedText;
        this.discoverStatusLabel.Font = new Font("Segoe UI", 8.75f);
        this.discoverStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
        this.discoverStatusLabel.Padding = new Padding(2, 0, 0, 0);

        this.discoverScrollPanel.Dock = DockStyle.Fill;
        this.discoverScrollPanel.AutoScroll = true;
        this.discoverScrollPanel.BackColor = AddonCenterTheme.Background;
        this.discoverScrollPanel.Padding = new Padding(0, 0, 8, 24);

        ConfigureCardsGrid(this.discoverCardsGrid);
        this.discoverCardsGrid.Padding = new Padding(0, 8, 0, 20);

        this.discoverEmptyLabel.Text =
            "No addons match this search or filter.";
        this.discoverEmptyLabel.Font = new Font("Segoe UI", 10.0f);
        this.discoverEmptyLabel.ForeColor = AddonCenterTheme.MutedText;
        this.discoverEmptyLabel.TextAlign = ContentAlignment.MiddleCenter;
        this.discoverEmptyLabel.Dock = DockStyle.Top;
        this.discoverEmptyLabel.Height = 110;
        this.discoverEmptyLabel.Visible = false;

        this.discoverScrollPanel.Controls.Add(this.discoverEmptyLabel);
        this.discoverScrollPanel.Controls.Add(this.discoverCardsGrid);

        this.discoverContentPanel.BackColor = AddonCenterTheme.Background;
        this.discoverContentPanel.Controls.Add(this.discoverScrollPanel);
        this.discoverContentPanel.Controls.Add(this.discoverStatusLabel);
        this.discoverContentPanel.Controls.Add(preferencesPanel);
        this.discoverContentPanel.Controls.Add(toolsPanel);
        this.discoverPage.Controls.Add(this.discoverContentPanel);
        this.UpdateDiscoverContentBounds();
    }

    private void WireDiscoverEvents()
    {
        this.discoverSearchTextBox.TextChanged +=
            this.DiscoverSearchTextBox_OnTextChanged;
        this.discoverFilterComboBox.SelectedIndexChanged +=
            this.DiscoverFilterComboBox_OnSelectedIndexChanged;
        this.discoverRefreshButton.Click +=
            this.DiscoverRefreshButton_OnClick;
        this.automaticCheckCheckBox.CheckedChanged +=
            this.AutomaticCheckCheckBox_OnCheckedChanged;
        this.discoverScrollPanel.Resize +=
            this.DiscoverScrollPanel_OnResize;
        this.discoverPage.Resize += this.DiscoverPage_OnResize;
    }

    private void UnwireDiscoverEvents()
    {
        this.discoverSearchTextBox.TextChanged -=
            this.DiscoverSearchTextBox_OnTextChanged;
        this.discoverFilterComboBox.SelectedIndexChanged -=
            this.DiscoverFilterComboBox_OnSelectedIndexChanged;
        this.discoverRefreshButton.Click -=
            this.DiscoverRefreshButton_OnClick;
        this.automaticCheckCheckBox.CheckedChanged -=
            this.AutomaticCheckCheckBox_OnCheckedChanged;
        this.discoverScrollPanel.Resize -=
            this.DiscoverScrollPanel_OnResize;
        this.discoverPage.Resize -= this.DiscoverPage_OnResize;
    }

    private void DisposeDiscoverCards()
    {
        foreach (var card in this.discoverCardsByAddonId.Values)
        {
            this.UnwireDiscoverCard(card);
            card.Dispose();
        }

        this.discoverCardsByAddonId.Clear();
    }

    private void RefreshDiscoverView(bool force = false)
    {
        var addons = this.clientManager.AddonRegistry;
        var statuses = this.currentStatuses
            .ToDictionary(
                status => status.AddonId,
                StringComparer.Ordinal);
        var installations = this.clientManager
            .GetAddonInstallations()
            .ToDictionary(
                installation => installation.AddonId,
                StringComparer.Ordinal);
        var registryError = this.clientManager.AddonRegistryError;
        var fetchedAt = this.clientManager.AddonRegistryFetchedAt;
        var addonFingerprint = string.Join(
            "\u001e",
            addons.Select(addon => string.Join(
                "\u001f",
                addon.Id,
                addon.Name,
                addon.Description,
                addon.Author,
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
                            release.PackageSha256))))));

        var fingerprint = string.Concat(
            addonFingerprint,
            "|",
            registryError,
            "|",
            fetchedAt?.UtcDateTime.Ticks,
            "|",
            this.statusFingerprint,
            "|",
            string.Join(
                ',',
                installations.Values.Select(installation => string.Concat(
                    installation.AddonId,
                    "@",
                    installation.Version,
                    "@",
                    installation.IsPinned))));

        if (!force &&
            string.Equals(
                this.discoverFingerprint,
                fingerprint,
                StringComparison.Ordinal))
        {
            return;
        }

        this.discoverFingerprint = fingerprint;
        var addonIds = addons
            .Select(addon => addon.Id)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var removedId in this.discoverCardsByAddonId.Keys
                     .Where(addonId => !addonIds.Contains(addonId))
                     .ToArray())
        {
            var card = this.discoverCardsByAddonId[removedId];
            this.UnwireDiscoverCard(card);
            this.discoverCardsByAddonId.Remove(removedId);
            card.Dispose();
        }

        foreach (var addon in addons)
        {
            if (!this.discoverCardsByAddonId.TryGetValue(
                    addon.Id,
                    out var card))
            {
                card = new AddonDiscoverCardControl
                {
                    AccessibleName = addon.Name,
                };
                this.WireDiscoverCard(card);
                this.discoverCardsByAddonId.Add(addon.Id, card);
            }

            statuses.TryGetValue(
                addon.Id,
                out var installedStatus);
            installations.TryGetValue(
                addon.Id,
                out var installation);
            card.UpdateAddon(
                addon,
                installedStatus,
                installation,
                force);
            card.SetOperationsEnabled(!this.operationInProgress);
        }

        this.discoverStatusLabel.Text = BuildRegistryStatusText(
            addons.Count,
            fetchedAt,
            registryError);
        this.discoverStatusLabel.ForeColor =
            string.IsNullOrWhiteSpace(registryError)
                ? AddonCenterTheme.MutedText
                : AddonCenterTheme.Warning;

        this.RebuildDiscoverLayout(force);
    }

    private void RebuildDiscoverLayout(bool force = false)
    {
        var statuses = this.currentStatuses
            .ToDictionary(
                status => status.AddonId,
                StringComparer.Ordinal);
        var installations = this.clientManager
            .GetAddonInstallations()
            .ToDictionary(
                installation => installation.AddonId,
                StringComparer.Ordinal);
        var filtered = this.clientManager.AddonRegistry
            .Where(addon => this.MatchesDiscoverFilter(
                addon,
                statuses.GetValueOrDefault(addon.Id),
                installations.GetValueOrDefault(addon.Id)))
            .OrderBy(addon => addon.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // Resize and filter events can fire while the form is being shown,
        // before RefreshDiscoverView has synchronized the card controls with
        // a registry catalog that was restored from cache. Defer layout until
        // every visible registry entry has a corresponding card.
        if (filtered.Any(addon =>
                !this.discoverCardsByAddonId.ContainsKey(addon.Id)))
        {
            return;
        }

        var availableWidth = Math.Max(
            360,
            this.discoverScrollPanel.ClientSize.Width -
            this.discoverScrollPanel.Padding.Horizontal -
            SystemInformation.VerticalScrollBarWidth);
        var columnCount = availableWidth >= 900 ? 2 : 1;
        var fingerprint = string.Concat(
            columnCount,
            "|",
            string.Join(',', filtered.Select(addon => addon.Id)),
            "|",
            this.discoverSearchTextBox.Text,
            "|",
            this.discoverFilterComboBox.SelectedIndex);

        if (!force &&
            string.Equals(
                this.discoverLayoutFingerprint,
                fingerprint,
                StringComparison.Ordinal))
        {
            return;
        }

        this.discoverLayoutFingerprint = fingerprint;
        this.discoverEmptyLabel.Visible = filtered.Length == 0;
        this.discoverCardsGrid.Visible = filtered.Length > 0;

        PopulateDiscoverCardsGrid(
            this.discoverCardsGrid,
            filtered,
            this.discoverCardsByAddonId,
            columnCount);
        this.discoverCardsGrid.Width = availableWidth;
    }

    private bool MatchesDiscoverFilter(
        AddonRegistrySummary addon,
        AddonRuntimeStatus? installedStatus,
        AddonInstallationInfo? installation)
    {
        var search = this.discoverSearchTextBox.Text.Trim();

        if (search.Length > 0 &&
            !ContainsIgnoreCase(addon.Name, search) &&
            !ContainsIgnoreCase(addon.Id, search) &&
            !ContainsIgnoreCase(addon.Author, search) &&
            !ContainsIgnoreCase(addon.PublisherName, search) &&
            !ContainsIgnoreCase(addon.Description, search) &&
            !addon.Categories.Any(value =>
                ContainsIgnoreCase(value, search)) &&
            !addon.Tags.Any(value =>
                ContainsIgnoreCase(value, search)))
        {
            return false;
        }

        var compatible =
            AddonRegistryCompatibility.GetLatestCompatibleRelease(addon);
        var hasUpdate = compatible != null &&
                        installation != null &&
                        installedStatus?.IsDevelopment != true &&
                        AddonRegistryCompatibility.IsNewer(
                            compatible.Version,
                            installation.Version);

        return this.discoverFilterComboBox.SelectedIndex switch
        {
            1 => addon.IsOfficial,
            2 => !addon.IsOfficial,
            3 => installation != null,
            4 => hasUpdate,
            _ => true,
        };
    }

    private void WireDiscoverCard(AddonDiscoverCardControl card)
    {
        card.PrimaryActionRequested +=
            this.DiscoverCard_OnPrimaryActionRequested;
        card.DevelopRequested +=
            this.DiscoverCard_OnDevelopRequested;
        card.DetailsRequested +=
            this.DiscoverCard_OnDetailsRequested;
    }

    private void UnwireDiscoverCard(AddonDiscoverCardControl card)
    {
        card.PrimaryActionRequested -=
            this.DiscoverCard_OnPrimaryActionRequested;
        card.DevelopRequested -=
            this.DiscoverCard_OnDevelopRequested;
        card.DetailsRequested -=
            this.DiscoverCard_OnDetailsRequested;
    }

    private async void DiscoverCard_OnPrimaryActionRequested(
        object? sender,
        EventArgs e)
    {
        if (sender is not AddonDiscoverCardControl
            {
                Addon: { } addon,
            })
        {
            return;
        }

        var release =
            AddonRegistryCompatibility.GetLatestCompatibleRelease(addon);

        if (release == null)
        {
            return;
        }

        var action = sender is AddonDiscoverCardControl
            {
                Installation: { } installed,
            } &&
            AddonRegistryCompatibility.IsNewer(
                release.Version,
                installed.Version)
                ? "Updating"
                : "Installing";

        await this.RunCommandAsync(
            string.Concat(
                action,
                " ",
                addon.Name,
                "..."),
            () => this.clientManager.InstallAddonVersionAsync(
                this.ownerProcessId,
                addon.Id,
                release.Version));
    }

    private async void DiscoverCard_OnDevelopRequested(
        object? sender,
        EventArgs e)
    {
        if (sender is not AddonDiscoverCardControl
            {
                Addon: { } addon,
            } card)
        {
            return;
        }

        if (!this.ConfirmDevelopmentWorkspaceSwitch(
                addon.Id,
                out var discardCurrentChanges))
        {
            return;
        }

        if (card.InstalledStatus?.IsDevelopment == true)
        {
            this.OpenDevelopmentWorkspace(
                addon.Id,
                discardCurrentChanges);
            return;
        }

        var release =
            AddonRegistryCompatibility.GetLatestCompatibleRelease(addon);

        if (release == null)
        {
            return;
        }

        var succeeded = await this.RunCommandAsync(
            string.Concat(
                "Preparing ",
                addon.Name,
                " for development..."),
            () => this.clientManager
                .CreateAddonDevelopmentWorkspaceFromRegistryAsync(
                    addon.Id,
                    release.Version));

        if (succeeded)
        {
            this.OpenDevelopmentWorkspace(
                addon.Id,
                discardCurrentChanges);
        }
    }

    private void DiscoverCard_OnDetailsRequested(
        object? sender,
        EventArgs e)
    {
        if (sender is AddonDiscoverCardControl
            {
                Addon: { } addon,
            })
        {
            this.ShowAddonDetails(addon);
        }
    }

    private void ShowAddonDetails(AddonRegistrySummary addon)
    {
        using var detailsForm = new AddonDetailsForm(
            this.clientManager,
            this.ownerProcessId,
            addon);
        _ = detailsForm.ShowDialog(this);
        this.RefreshView(force: true);
    }

    private async void DiscoverRefreshButton_OnClick(
        object? sender,
        EventArgs e)
    {
        await this.RefreshCatalogAndViewAsync();
    }

    private void DiscoverSearchTextBox_OnTextChanged(
        object? sender,
        EventArgs e)
    {
        this.RebuildDiscoverLayout(force: true);
    }

    private void DiscoverFilterComboBox_OnSelectedIndexChanged(
        object? sender,
        EventArgs e)
    {
        this.RebuildDiscoverLayout(force: true);
    }

    private void AutomaticCheckCheckBox_OnCheckedChanged(
        object? sender,
        EventArgs e)
    {
        this.clientManager.SetCheckAddonUpdatesAutomatically(
            this.automaticCheckCheckBox.Checked);
    }

    private void DiscoverScrollPanel_OnResize(
        object? sender,
        EventArgs e)
    {
        this.RebuildDiscoverLayout(force: true);
    }

    private void DiscoverPage_OnResize(
        object? sender,
        EventArgs e)
    {
        this.UpdateDiscoverContentBounds();
    }

    private void UpdateDiscoverContentBounds()
    {
        var availableSize = this.discoverPage.ClientSize;
        var contentWidth = Math.Min(
            MaxDiscoverContentWidth,
            Math.Max(0, availableSize.Width));
        var left = Math.Max(
            0,
            (availableSize.Width - contentWidth) / 2);

        this.discoverContentPanel.SetBounds(
            left,
            0,
            contentWidth,
            Math.Max(0, availableSize.Height));
    }

    private void SetDiscoverOperationsEnabled(bool enabled)
    {
        this.discoverRefreshButton.Enabled = enabled;
        this.automaticCheckCheckBox.Enabled = enabled;

        foreach (var card in this.discoverCardsByAddonId.Values)
        {
            card.SetOperationsEnabled(enabled);
        }
    }

    private static string BuildRegistryStatusText(
        int addonCount,
        DateTimeOffset? fetchedAt,
        string error)
    {
        var countText = string.Create(
            CultureInfo.InvariantCulture,
            $"{addonCount} addon(s) available from Net7 Forge.");

        if (!string.IsNullOrWhiteSpace(error))
        {
            return addonCount > 0
                ? string.Concat(
                    countText,
                    " Showing the last validated catalog because refresh failed: ",
                    error)
                : string.Concat(
                    "Net7 Forge could not be reached: ",
                    error);
        }

        if (fetchedAt == null)
        {
            return addonCount == 0
                ? "No cached Net7 Forge catalog is available. Select Refresh catalog to load it."
                : countText;
        }

        return string.Concat(
            countText,
            " Last refreshed ",
            fetchedAt.Value.ToLocalTime().ToString(
                "g",
                CultureInfo.CurrentCulture),
            ".");
    }

    private static void PopulateDiscoverCardsGrid(
        TableLayoutPanel grid,
        IReadOnlyList<AddonRegistrySummary> addons,
        IReadOnlyDictionary<string, AddonDiscoverCardControl> cards,
        int columnCount)
    {
        grid.SuspendLayout();

        try
        {
            grid.Controls.Clear();
            grid.ColumnStyles.Clear();
            grid.RowStyles.Clear();
            grid.ColumnCount = columnCount;
            grid.RowCount = Math.Max(
                1,
                (int)Math.Ceiling(addons.Count / (double)columnCount));

            for (var column = 0; column < columnCount; column++)
            {
                grid.ColumnStyles.Add(
                    new ColumnStyle(
                        SizeType.Percent,
                        100f / columnCount));
            }

            for (var row = 0; row < grid.RowCount; row++)
            {
                grid.RowStyles.Add(
                    new RowStyle(SizeType.AutoSize));
            }

            for (var index = 0; index < addons.Count; index++)
            {
                var addon = addons[index];
                var card = cards[addon.Id];
                card.Dock = DockStyle.Top;
                card.Margin = new Padding(
                    index % columnCount == columnCount - 1 ? 6 : 0,
                    0,
                    index % columnCount == columnCount - 1 ? 0 : 6,
                    12);

                grid.Controls.Add(
                    card,
                    index % columnCount,
                    index / columnCount);
            }
        }
        finally
        {
            grid.ResumeLayout(performLayout: true);
        }
    }
}
