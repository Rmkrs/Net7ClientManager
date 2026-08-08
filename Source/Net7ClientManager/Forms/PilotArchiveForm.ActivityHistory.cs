// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using System.Globalization;
using Net7ClientManager.ActivityJournal;
using Net7ClientManager.Models;
using Net7ClientManager.PilotArchive;

public sealed partial class PilotArchiveForm
{
    private readonly SplitContainer activityHistorySplit = new();
    private readonly DataGridView activityHistoryGrid = CreateGrid();
    private readonly TextBox activityHistoryDetailsTextBox = new();
    private readonly ThemedCheckBox activityNavigationFilterCheckBox = new();
    private readonly ThemedCheckBox activityMissionsFilterCheckBox = new();
    private readonly ThemedCheckBox activityReputationFilterCheckBox = new();
    private readonly ThemedCheckBox activityCreditsFilterCheckBox = new();
    private readonly ThemedCheckBox activityLootFilterCheckBox = new();
    private readonly ThemedCheckBox activityCombatFilterCheckBox = new();
    private readonly ThemedCheckBox activityCraftingFilterCheckBox = new();
    private readonly Dictionary<long, ActivityJournalEntry>
        activityHistoryEntriesById = [];
    private bool updatingActivityFilters;
    private bool activityHistorySplitterUserMovePending;

    private ThemedTabPage CreateActivityHistoryTab()
    {
        var tab = CreateTabPage("Activity History");
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            BackColor = MainWindowTheme.Panel,
        };
        var timestamp = CreateTimestampLabel();
        this.sectionTimestampLabels[PilotArchiveSections.ActivityHistory] =
            timestamp;
        timestamp.Dock = DockStyle.Top;
        timestamp.Height = 30;

        var filters = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 38,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 2, 0, 4),
            BackColor = MainWindowTheme.Panel,
        };
        filters.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "Show",
            ForeColor = MainWindowTheme.MutedText,
            Margin = new Padding(0, 6, 12, 0),
        });

        this.activityNavigationFilterCheckBox.Text = "Navigation";
        this.activityNavigationFilterCheckBox.AutoSize = true;
        this.activityNavigationFilterCheckBox.ForeColor = MainWindowTheme.Text;
        this.activityNavigationFilterCheckBox.Margin = new Padding(0, 4, 18, 0);
        filters.Controls.Add(this.activityNavigationFilterCheckBox);

        this.activityMissionsFilterCheckBox.Text = "Missions && jobs";
        this.activityMissionsFilterCheckBox.AutoSize = true;
        this.activityMissionsFilterCheckBox.ForeColor = MainWindowTheme.Text;
        this.activityMissionsFilterCheckBox.Margin = new Padding(0, 4, 18, 0);
        filters.Controls.Add(this.activityMissionsFilterCheckBox);

        this.activityReputationFilterCheckBox.Text = "Reputation";
        this.activityReputationFilterCheckBox.AutoSize = true;
        this.activityReputationFilterCheckBox.ForeColor = MainWindowTheme.Text;
        this.activityReputationFilterCheckBox.Margin = new Padding(0, 4, 18, 0);
        filters.Controls.Add(this.activityReputationFilterCheckBox);

        this.activityCreditsFilterCheckBox.Text = "Credits";
        this.activityCreditsFilterCheckBox.AutoSize = true;
        this.activityCreditsFilterCheckBox.ForeColor = MainWindowTheme.Text;
        this.activityCreditsFilterCheckBox.Margin = new Padding(0, 4, 18, 0);
        filters.Controls.Add(this.activityCreditsFilterCheckBox);

        this.activityLootFilterCheckBox.Text = "Loot";
        this.activityLootFilterCheckBox.AutoSize = true;
        this.activityLootFilterCheckBox.ForeColor = MainWindowTheme.Text;
        this.activityLootFilterCheckBox.Margin = new Padding(0, 4, 18, 0);
        filters.Controls.Add(this.activityLootFilterCheckBox);

        this.activityCombatFilterCheckBox.Text = "Combat";
        this.activityCombatFilterCheckBox.AutoSize = true;
        this.activityCombatFilterCheckBox.ForeColor = MainWindowTheme.Text;
        this.activityCombatFilterCheckBox.Margin = new Padding(0, 4, 18, 0);
        filters.Controls.Add(this.activityCombatFilterCheckBox);

        this.activityCraftingFilterCheckBox.Text = "Crafting";
        this.activityCraftingFilterCheckBox.AutoSize = true;
        this.activityCraftingFilterCheckBox.ForeColor = MainWindowTheme.Text;
        this.activityCraftingFilterCheckBox.Margin = new Padding(0, 4, 18, 0);
        filters.Controls.Add(this.activityCraftingFilterCheckBox);

        this.activityHistorySplit.Dock = DockStyle.Fill;
        this.activityHistorySplit.Orientation = Orientation.Horizontal;
        this.activityHistorySplit.Size = new Size(1000, 520);
        this.activityHistorySplit.SplitterWidth = 6;
        this.activityHistorySplit.SplitterDistance = 320;
        this.activityHistorySplit.Panel1MinSize = 150;
        this.activityHistorySplit.Panel2MinSize = 110;
        this.activityHistorySplit.BackColor = MainWindowTheme.Border;
        this.activityHistorySplit.Panel1.BackColor = MainWindowTheme.Panel;
        this.activityHistorySplit.Panel2.BackColor = MainWindowTheme.Panel;
        this.activityHistorySplit.Panel2.Padding = new Padding(0, 10, 0, 0);

        this.activityHistoryGrid.Dock = DockStyle.Fill;
        this.activityHistorySplit.Panel1.Controls.Add(this.activityHistoryGrid);

        var detailsHeading = new Label
        {
            Dock = DockStyle.Top,
            Height = 28,
            Text = "Activity details",
            ForeColor = MainWindowTheme.Text,
            Font = MainWindowTheme.CreateHeadingFont(9.0f),
            TextAlign = ContentAlignment.MiddleLeft,
        };
        this.activityHistoryDetailsTextBox.Dock = DockStyle.Fill;
        this.activityHistoryDetailsTextBox.Multiline = true;
        this.activityHistoryDetailsTextBox.ReadOnly = true;
        this.activityHistoryDetailsTextBox.ScrollBars = ScrollBars.Vertical;
        this.activityHistoryDetailsTextBox.WordWrap = true;
        this.activityHistoryDetailsTextBox.BackColor =
            MainWindowTheme.ElevatedPanel;
        this.activityHistoryDetailsTextBox.ForeColor = MainWindowTheme.Text;
        this.activityHistoryDetailsTextBox.BorderStyle =
            BorderStyle.FixedSingle;
        this.activityHistoryDetailsTextBox.Font =
            MainWindowTheme.CreateBodyFont();
        this.activityHistoryDetailsTextBox.Text =
            "Select an activity to view its details.";
        this.activityHistorySplit.Panel2.Controls.Add(
            this.activityHistoryDetailsTextBox);
        this.activityHistorySplit.Panel2.Controls.Add(detailsHeading);

        panel.Controls.Add(this.activityHistorySplit);
        panel.Controls.Add(filters);
        panel.Controls.Add(timestamp);
        tab.Controls.Add(panel);
        return tab;
    }

    private void ConfigureActivityHistoryGrid()
    {
        AddColumn(this.activityHistoryGrid, "Time", 155);
        AddColumn(this.activityHistoryGrid, "Category", 155);
        AddColumn(this.activityHistoryGrid, "Activity", 500, fill: true);
        AddColumn(this.activityHistoryGrid, "Location", 280);
    }

    private void ApplyActivityHistorySettings()
    {
        if (!this.sectionTabs.TryGetValue(
                PilotArchiveSections.ActivityHistory,
                out var activityHistoryTab))
        {
            return;
        }

        var enabled =
            this.clientManager.HistorySettings.RecordActivityHistory;
        this.detailTabs.SetPageVisible(activityHistoryTab, enabled);

        this.ApplyReputationHistorySettings();

        if (!enabled)
        {
            this.ClearActivityHistory();
            return;
        }

        this.RestoreActivityFilters();

        if (this.selectedCharacterId is { } characterId)
        {
            this.RefreshActivityHistory(characterId);
            this.PopulateActivityHistoryTimestamp(characterId);
        }
    }

    private void RestoreActivityFilters()
    {
        this.updatingActivityFilters = true;

        try
        {
            var settings = this.clientManager.PilotArchiveSettings;
            this.activityNavigationFilterCheckBox.Checked =
                settings.ShowActivityNavigation;
            this.activityMissionsFilterCheckBox.Checked =
                settings.ShowActivityMissions;
            this.activityReputationFilterCheckBox.Checked =
                settings.ShowActivityReputation;
            this.activityCreditsFilterCheckBox.Checked =
                settings.ShowActivityCredits;
            this.activityLootFilterCheckBox.Checked =
                settings.ShowActivityLoot;
            this.activityCombatFilterCheckBox.Checked =
                settings.ShowActivityCombat;
            this.activityCraftingFilterCheckBox.Checked =
                settings.ShowActivityCrafting;
        }
        finally
        {
            this.updatingActivityFilters = false;
        }
    }

    private void RefreshActivityHistory(uint characterId)
    {
        var activities = this.clientManager.GetActivityHistory(
            characterId,
            this.GetSelectedActivityCategories());
        this.PopulateActivityHistory(activities);
    }

    private ActivityJournalCategory GetSelectedActivityCategories()
    {
        var categories = ActivityJournalCategory.None;

        if (this.activityNavigationFilterCheckBox.Checked)
        {
            categories |= ActivityJournalCategory.Navigation;
        }

        if (this.activityMissionsFilterCheckBox.Checked)
        {
            categories |= ActivityJournalCategory.Missions;
        }

        if (this.activityReputationFilterCheckBox.Checked)
        {
            categories |= ActivityJournalCategory.Reputation;
        }

        if (this.activityCreditsFilterCheckBox.Checked)
        {
            categories |= ActivityJournalCategory.Credits;
        }

        if (this.activityLootFilterCheckBox.Checked)
        {
            categories |= ActivityJournalCategory.Loot;
        }

        if (this.activityCombatFilterCheckBox.Checked)
        {
            categories |= ActivityJournalCategory.Combat;
        }

        if (this.activityCraftingFilterCheckBox.Checked)
        {
            categories |= ActivityJournalCategory.Crafting;
        }

        return categories;
    }

    private void PopulateActivityHistory(
        IReadOnlyList<ActivityJournalEntry> activities)
    {
        var selectedEventId = this.activityHistoryGrid.SelectedRows
            .Cast<DataGridViewRow>()
            .Select(row => row.Tag)
            .OfType<long>()
            .FirstOrDefault();
        this.activityHistoryGrid.Rows.Clear();
        this.activityHistoryEntriesById.Clear();

        foreach (var activity in activities)
        {
            this.activityHistoryEntriesById[activity.EventId] = activity;
            var row = this.activityHistoryGrid.Rows[
                this.activityHistoryGrid.Rows.Add(
                    FormatObservedAt(activity.OccurredAt),
                    activity.CategoryDisplay,
                    activity.Summary,
                    FormatActivityLocation(activity))];
            row.Tag = activity.EventId;
            row.DefaultCellStyle.ForeColor = activity.Kind switch
            {
                ActivityJournalKind.ReputationChanged =>
                    activity.Summary.Contains(" +", StringComparison.Ordinal)
                        ? MainWindowTheme.Success
                        : MainWindowTheme.Warning,
                ActivityJournalKind.CreditsGained => MainWindowTheme.Success,
                ActivityJournalKind.VendorSold => MainWindowTheme.Success,
                ActivityJournalKind.CreditsSpent => MainWindowTheme.Warning,
                ActivityJournalKind.VendorPurchased => MainWindowTheme.Warning,
                ActivityJournalKind.Looted => MainWindowTheme.Accent,
                ActivityJournalKind.CombatKilled => MainWindowTheme.Success,
                ActivityJournalKind.CombatDied => MainWindowTheme.Danger,
                ActivityJournalKind.CombatDisengaged => MainWindowTheme.Warning,
                ActivityJournalKind.CombatInterrupted =>
                    MainWindowTheme.MutedText,
                ActivityJournalKind.CraftingRecipeScan => MainWindowTheme.Accent,
                ActivityJournalKind.CraftingAnalyzeSucceeded or
                    ActivityJournalKind.CraftingAnalyzeCritical or
                    ActivityJournalKind.CraftingDismantled or
                    ActivityJournalKind.CraftingDismantleCritical or
                    ActivityJournalKind.CraftingManufactured or
                    ActivityJournalKind.CraftingManufactureCritical =>
                    MainWindowTheme.Success,
                ActivityJournalKind.CraftingAnalyzeFailed or
                    ActivityJournalKind.CraftingDismantleFailed or
                    ActivityJournalKind.CraftingManufactureFailed =>
                    MainWindowTheme.Danger,
                _ when activity.Category.HasFlag(
                    ActivityJournalCategory.Navigation) =>
                    MainWindowTheme.Accent,
                _ => MainWindowTheme.Text,
            };
        }

        this.ApplyStoredGridSort(this.activityHistoryGrid);
        this.activityHistoryGrid.ClearSelection();

        var selectedRow = this.activityHistoryGrid.Rows
            .Cast<DataGridViewRow>()
            .FirstOrDefault(row => row.Tag is long eventId &&
                eventId == selectedEventId)
            ?? this.activityHistoryGrid.Rows
                .Cast<DataGridViewRow>()
                .FirstOrDefault();

        if (selectedRow != null)
        {
            selectedRow.Selected = true;
            this.activityHistoryGrid.CurrentCell = selectedRow.Cells[0];
        }

        this.UpdateActivityHistoryDetails();
    }

    private void PopulateActivityHistoryTimestamp(uint characterId)
    {
        if (!this.sectionTimestampLabels.TryGetValue(
                PilotArchiveSections.ActivityHistory,
                out var label))
        {
            return;
        }

        var recordedAt =
            this.clientManager.GetActivityHistoryLastRecordedAt(characterId);
        label.Text = recordedAt.HasValue
            ? string.Concat(
                "Last updated: ",
                FormatObservedAt(recordedAt.Value))
            : "No activity recorded yet";
    }

    private void UpdateActivityHistoryDetails()
    {
        var eventId = this.activityHistoryGrid.SelectedRows
            .Cast<DataGridViewRow>()
            .Select(row => row.Tag)
            .OfType<long>()
            .FirstOrDefault();

        this.activityHistoryDetailsTextBox.Text =
            eventId > 0 &&
            this.activityHistoryEntriesById.TryGetValue(eventId, out var entry)
                ? BuildActivityHistoryDetails(entry)
                : "Select an activity to view its details.";
    }

    private string BuildActivityHistoryDetails(
        ActivityJournalEntry entry)
    {
        List<string> lines =
        [
            entry.Summary,
            string.Concat(
                entry.CategoryDisplay,
                " · ",
                FormatObservedAt(entry.OccurredAt)),
        ];
        var location = FormatActivityLocation(entry);

        if (!string.IsNullOrWhiteSpace(location))
        {
            lines.Add("");
            lines.Add("Location");
            lines.Add(location);
        }

        if (!string.IsNullOrWhiteSpace(entry.Details))
        {
            lines.Add("");
            lines.Add("Details");
            lines.Add(entry.Details);
        }

        if (!string.IsNullOrWhiteSpace(entry.RelatedLootSessionId))
        {
            var lootSession = this.clientManager.GetLootSession(
                entry.RelatedLootSessionId);

            if (lootSession != null)
            {
                lines.Add("");
                lines.Add("Loot");

                if (lootSession.Credits > 0)
                {
                    lines.Add(string.Concat(
                        lootSession.Credits.ToString(
                            "N0",
                            CultureInfo.CurrentCulture),
                        " credits"));
                }

                lines.AddRange(lootSession.Items.Select(item =>
                    string.Concat(
                        item.Quantity.ToString(CultureInfo.CurrentCulture),
                        " × ",
                        item.Name,
                        item.QualityPercent.HasValue
                            ? string.Concat(
                                " (",
                                item.QualityPercent.Value.ToString(
                                    "0.#",
                                    CultureInfo.CurrentCulture),
                                "%)")
                            : "")));
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatActivityLocation(
        ActivityJournalEntry entry)
    {
        return FormatLocation(
            entry.SystemName,
            entry.SectorName,
            entry.StarbaseName,
            string.IsNullOrWhiteSpace(entry.NearestNavName)
                ? null
                : string.Concat("near ", entry.NearestNavName));
    }

    private void ClearActivityHistory()
    {
        this.activityHistoryGrid.Rows.Clear();
        this.activityHistoryEntriesById.Clear();
        this.activityHistoryDetailsTextBox.Text =
            "Select an activity to view its details.";

        if (this.sectionTimestampLabels.TryGetValue(
                PilotArchiveSections.ActivityHistory,
                out var timestamp))
        {
            timestamp.Text = "No activity recorded yet";
        }
    }

    private void RestoreActivityHistoryUiState(
        PilotArchiveSettings settings)
    {
        this.RestoreActivityFilters();

        if (settings.ActivityHistorySplitterDistance.HasValue)
        {
            SetSplitterDistance(
                this.activityHistorySplit,
                settings.ActivityHistorySplitterDistance.Value);
        }
        else
        {
            settings.ActivityHistorySplitterDistance =
                this.activityHistorySplit.SplitterDistance;
        }
    }

    private void SaveActivityHistoryUiState(PilotArchiveSettings settings)
    {
        if (!settings.ActivityHistorySplitterDistance.HasValue &&
            this.activityHistorySplit.ClientSize.Height > 0)
        {
            settings.ActivityHistorySplitterDistance =
                this.activityHistorySplit.SplitterDistance;
        }
    }

    private void ActivityHistoryGrid_OnSelectionChanged(
        object? sender,
        EventArgs e)
    {
        this.UpdateActivityHistoryDetails();
    }

    private void ActivityFilterCheckBox_OnCheckedChanged(
        object? sender,
        EventArgs e)
    {
        if (this.updatingActivityFilters)
        {
            return;
        }

        var settings = this.clientManager.PilotArchiveSettings;
        settings.ShowActivityNavigation =
            this.activityNavigationFilterCheckBox.Checked;
        settings.ShowActivityMissions =
            this.activityMissionsFilterCheckBox.Checked;
        settings.ShowActivityReputation =
            this.activityReputationFilterCheckBox.Checked;
        settings.ShowActivityCredits =
            this.activityCreditsFilterCheckBox.Checked;
        settings.ShowActivityLoot =
            this.activityLootFilterCheckBox.Checked;
        settings.ShowActivityCombat =
            this.activityCombatFilterCheckBox.Checked;
        settings.ShowActivityCrafting =
            this.activityCraftingFilterCheckBox.Checked;
        this.clientManager.SaveSettings();

        if (this.selectedCharacterId is { } characterId)
        {
            this.RefreshActivityHistory(characterId);
        }
    }

    private void ActivityHistorySplit_OnSplitterMoving(
        object? sender,
        SplitterCancelEventArgs e)
    {
        if (!this.restoringUiState)
        {
            this.activityHistorySplitterUserMovePending = true;
        }
    }

    private void ActivityHistorySplit_OnSplitterMoved(
        object? sender,
        SplitterEventArgs e)
    {
        if (this.restoringUiState ||
            !this.activityHistorySplitterUserMovePending)
        {
            return;
        }

        this.activityHistorySplitterUserMovePending = false;
        this.clientManager.PilotArchiveSettings
            .ActivityHistorySplitterDistance =
            this.activityHistorySplit.SplitterDistance;
        this.clientManager.SaveSettings();
    }

    private void ClientManager_OnActivityJournalChanged(
        object? sender,
        ActivityJournalChangedEventArgs e)
    {
        if (this.IsDisposed ||
            this.Disposing ||
            !this.clientManager.HistorySettings.RecordActivityHistory ||
            this.selectedCharacterId != e.CharacterId)
        {
            return;
        }

        try
        {
            this.BeginInvoke(() =>
            {
                if (!this.clientManager.HistorySettings.RecordActivityHistory ||
                    this.selectedCharacterId != e.CharacterId)
                {
                    return;
                }

                this.RefreshActivityHistory(e.CharacterId);
                this.PopulateActivityHistoryTimestamp(e.CharacterId);
                this.RefreshSelectedReputationHistory();
            });
        }
        catch (InvalidOperationException)
        {
        }
    }
}
