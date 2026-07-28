// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using System.Globalization;
using System.Text;
using Net7ClientManager.CombatJournal;
using Net7ClientManager.Models;
using Net7ClientManager.PilotArchive;

public sealed partial class PilotArchiveForm
{
    private readonly SplitContainer combatHistorySplit = new();
    private readonly DataGridView combatHistoryGrid = CreateGrid();
    private readonly TextBox combatHistoryDetailsTextBox = new();
    private readonly Dictionary<string, CombatJournalEncounter>
        combatHistoryEntriesById = new(StringComparer.Ordinal);
    private bool combatHistorySplitterUserMovePending;

    private ThemedTabPage CreateCombatHistoryTab()
    {
        var tab = CreateTabPage("Combat History");
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            BackColor = MainWindowTheme.Panel,
        };
        var timestamp = CreateTimestampLabel();
        this.sectionTimestampLabels[PilotArchiveSections.CombatHistory] =
            timestamp;
        timestamp.Dock = DockStyle.Top;
        timestamp.Height = 30;

        this.combatHistorySplit.Dock = DockStyle.Fill;
        this.combatHistorySplit.Orientation = Orientation.Horizontal;
        this.combatHistorySplit.Size = new Size(1000, 520);
        this.combatHistorySplit.SplitterWidth = 6;
        this.combatHistorySplit.SplitterDistance = 310;
        this.combatHistorySplit.Panel1MinSize = 150;
        this.combatHistorySplit.Panel2MinSize = 120;
        this.combatHistorySplit.BackColor = MainWindowTheme.Border;
        this.combatHistorySplit.Panel1.BackColor = MainWindowTheme.Panel;
        this.combatHistorySplit.Panel2.BackColor = MainWindowTheme.Panel;
        this.combatHistorySplit.Panel2.Padding = new Padding(0, 10, 0, 0);

        this.combatHistoryGrid.Dock = DockStyle.Fill;
        this.combatHistorySplit.Panel1.Controls.Add(this.combatHistoryGrid);

        var detailsHeading = new Label
        {
            Dock = DockStyle.Top,
            Height = 28,
            Text = "Encounter details",
            ForeColor = MainWindowTheme.Text,
            Font = MainWindowTheme.CreateHeadingFont(9.0f),
            TextAlign = ContentAlignment.MiddleLeft,
        };
        this.combatHistoryDetailsTextBox.Dock = DockStyle.Fill;
        this.combatHistoryDetailsTextBox.Multiline = true;
        this.combatHistoryDetailsTextBox.ReadOnly = true;
        this.combatHistoryDetailsTextBox.ScrollBars = ScrollBars.Vertical;
        this.combatHistoryDetailsTextBox.WordWrap = true;
        this.combatHistoryDetailsTextBox.BackColor =
            MainWindowTheme.ElevatedPanel;
        this.combatHistoryDetailsTextBox.ForeColor = MainWindowTheme.Text;
        this.combatHistoryDetailsTextBox.BorderStyle =
            BorderStyle.FixedSingle;
        this.combatHistoryDetailsTextBox.Font =
            MainWindowTheme.CreateBodyFont();
        this.combatHistoryDetailsTextBox.Text =
            "Select an encounter to view its details.";
        this.combatHistorySplit.Panel2.Controls.Add(
            this.combatHistoryDetailsTextBox);
        this.combatHistorySplit.Panel2.Controls.Add(detailsHeading);

        panel.Controls.Add(this.combatHistorySplit);
        panel.Controls.Add(timestamp);
        tab.Controls.Add(panel);
        return tab;
    }

    private void ConfigureCombatHistoryGrid()
    {
        AddColumn(this.combatHistoryGrid, "Started", 155);
        AddColumn(this.combatHistoryGrid, "Target", 245, fill: true);
        AddColumn(this.combatHistoryGrid, "Level", 70);
        AddColumn(this.combatHistoryGrid, "Outcome", 110);
        AddColumn(this.combatHistoryGrid, "Outgoing", 110);
        AddColumn(this.combatHistoryGrid, "Incoming", 110);
        AddColumn(this.combatHistoryGrid, "Duration", 90);
        AddColumn(this.combatHistoryGrid, "Location", 270);
    }

    private void ApplyCombatHistorySettings()
    {
        if (!this.sectionTabs.TryGetValue(
                PilotArchiveSections.CombatHistory,
                out var combatHistoryTab))
        {
            return;
        }

        var enabled = this.clientManager.HistorySettings.RecordCombatHistory;
        this.detailTabs.SetPageVisible(combatHistoryTab, enabled);

        if (!enabled)
        {
            this.ClearCombatHistory();
            return;
        }

        if (this.selectedCharacterId is { } characterId)
        {
            this.RefreshCombatHistory(characterId);
            this.PopulateCombatHistoryTimestamp(characterId);
        }
    }

    private void RefreshCombatHistory(uint characterId)
    {
        var encounters = this.clientManager.GetCombatHistory(characterId);
        this.PopulateCombatHistory(encounters);
    }

    private void PopulateCombatHistory(
        IReadOnlyList<CombatJournalEncounter> encounters)
    {
        var selectedEncounterId = this.combatHistoryGrid.SelectedRows
            .Cast<DataGridViewRow>()
            .Select(row => row.Tag as string)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        this.combatHistoryGrid.Rows.Clear();
        this.combatHistoryEntriesById.Clear();

        foreach (var encounter in encounters)
        {
            this.combatHistoryEntriesById[encounter.EncounterId] = encounter;
            var row = this.combatHistoryGrid.Rows[
                this.combatHistoryGrid.Rows.Add(
                    FormatCombatTimestamp(encounter.StartedAt),
                    encounter.TargetName,
                    encounter.TargetCombatLevel?.ToString(
                        CultureInfo.CurrentCulture) ?? "",
                    encounter.OutcomeDisplay,
                    FormatDamage(encounter.OutgoingDamage),
                    FormatDamage(encounter.IncomingDamage),
                    FormatCombatDuration(encounter.Duration),
                    FormatCombatLocation(encounter))];
            row.Tag = encounter.EncounterId;
            ApplyCombatOutcomeStyle(row, encounter.Outcome);
        }

        this.ApplyStoredGridSort(this.combatHistoryGrid);

        var selectedRow = this.combatHistoryGrid.Rows
            .Cast<DataGridViewRow>()
            .FirstOrDefault(row => string.Equals(
                row.Tag as string,
                selectedEncounterId,
                StringComparison.Ordinal))
            ?? this.combatHistoryGrid.Rows.Cast<DataGridViewRow>()
                .FirstOrDefault();

        this.combatHistoryGrid.ClearSelection();

        if (selectedRow != null)
        {
            selectedRow.Selected = true;
            this.combatHistoryGrid.CurrentCell = selectedRow.Cells[0];
            this.ShowCombatHistoryDetails(selectedRow.Tag as string);
        }
        else
        {
            this.combatHistoryDetailsTextBox.Text =
                "No combat encounters have been recorded for this pilot.";
        }
    }

    private void CombatHistoryGrid_OnSelectionChanged(
        object? sender,
        EventArgs e)
    {
        this.ShowCombatHistoryDetails(
            this.combatHistoryGrid.CurrentRow?.Tag as string);
    }

    private void ShowCombatHistoryDetails(string? encounterId)
    {
        if (string.IsNullOrWhiteSpace(encounterId) ||
            !this.combatHistoryEntriesById.TryGetValue(
                encounterId,
                out var summary))
        {
            this.combatHistoryDetailsTextBox.Text =
                "Select an encounter to view its details.";
            return;
        }

        var encounter = this.clientManager.GetCombatEncounter(encounterId) ??
            summary;
        this.combatHistoryDetailsTextBox.Text =
            FormatCombatHistoryDetails(encounter);
    }

    private static string FormatCombatHistoryDetails(
        CombatJournalEncounter encounter)
    {
        var builder = new StringBuilder();
        builder.Append(encounter.TargetName);

        if (encounter.TargetCombatLevel.HasValue)
        {
            builder.Append(" · Level ");
            builder.Append(encounter.TargetCombatLevel.Value.ToString(
                CultureInfo.CurrentCulture));
        }

        builder.AppendLine();
        builder.AppendLine(encounter.OutcomeDisplay);
        builder.AppendLine();
        builder.Append("Started: ");
        builder.AppendLine(FormatCombatTimestamp(encounter.StartedAt));

        if (encounter.EndedAt.HasValue)
        {
            builder.Append("Ended: ");
            builder.AppendLine(FormatCombatTimestamp(encounter.EndedAt.Value));
        }

        builder.Append("Duration: ");
        builder.AppendLine(FormatCombatDuration(encounter.Duration));
        var location = FormatCombatLocation(encounter);

        if (!string.IsNullOrWhiteSpace(location))
        {
            builder.Append("Location: ");
            builder.AppendLine(location);
        }

        builder.AppendLine();
        builder.AppendLine("Damage");
        builder.Append("Outgoing: ");
        builder.Append(FormatDamage(encounter.OutgoingDamage));
        builder.Append(" across ");
        builder.Append(encounter.OutgoingHitCount.ToString(
            CultureInfo.CurrentCulture));
        builder.Append(" hit");
        builder.Append(encounter.OutgoingHitCount == 1 ? "" : "s");
        AppendCriticalSummary(
            builder,
            encounter.OutgoingCriticalCount,
            encounter.LargestOutgoingHit);
        builder.AppendLine();
        builder.Append("Incoming: ");
        builder.Append(FormatDamage(encounter.IncomingDamage));
        builder.Append(" across ");
        builder.Append(encounter.IncomingHitCount.ToString(
            CultureInfo.CurrentCulture));
        builder.Append(" hit");
        builder.Append(encounter.IncomingHitCount == 1 ? "" : "s");
        AppendCriticalSummary(
            builder,
            encounter.IncomingCriticalCount,
            encounter.LargestIncomingHit);
        builder.AppendLine();

        AppendDamageTypeBreakdown(builder, encounter);
        AppendCombatTimeline(builder, encounter);
        return builder.ToString().TrimEnd();
    }

    private static void AppendCriticalSummary(
        StringBuilder builder,
        int criticalCount,
        float largestHit)
    {
        if (criticalCount > 0)
        {
            builder.Append(" · ");
            builder.Append(criticalCount.ToString(CultureInfo.CurrentCulture));
            builder.Append(" critical");
            builder.Append(criticalCount == 1 ? "" : "s");
        }

        if (largestHit > 0)
        {
            builder.Append(" · largest ");
            builder.Append(FormatDamage(largestHit));
        }
    }

    private static void AppendDamageTypeBreakdown(
        StringBuilder builder,
        CombatJournalEncounter encounter)
    {
        var groups = encounter.Events
            .GroupBy(item => new { item.Direction, item.DamageType })
            .Select(group => new
            {
                group.Key.Direction,
                group.Key.DamageType,
                Total = group.Sum(item => (double)item.Damage),
            })
            .OrderBy(item => item.Direction)
            .ThenByDescending(item => item.Total)
            .ToArray();

        if (groups.Length == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("Damage types");

        foreach (var direction in new[]
                 {
                     CombatJournalDirection.Outgoing,
                     CombatJournalDirection.Incoming,
                 })
        {
            var directionGroups = groups
                .Where(item => item.Direction == direction)
                .ToArray();

            if (directionGroups.Length == 0)
            {
                continue;
            }

            builder.Append(direction == CombatJournalDirection.Outgoing
                ? "Outgoing: "
                : "Incoming: ");
            builder.AppendLine(string.Join(
                ", ",
                directionGroups.Select(item => string.Concat(
                    string.IsNullOrWhiteSpace(item.DamageType)
                        ? "Unknown"
                        : item.DamageType,
                    " ",
                    FormatDamage(item.Total)))));
        }
    }

    private static void AppendCombatTimeline(
        StringBuilder builder,
        CombatJournalEncounter encounter)
    {
        if (encounter.Events.Count == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("Timeline");

        foreach (var item in encounter.Events.OrderBy(item => item.OccurredAt))
        {
            var elapsed = item.OccurredAt - encounter.StartedAt;
            builder.Append('+');
            builder.Append(elapsed.TotalSeconds.ToString(
                "0.0",
                CultureInfo.CurrentCulture));
            builder.Append("s · ");
            builder.Append(item.Direction == CombatJournalDirection.Outgoing
                ? "OUT"
                : "IN");
            builder.Append(" · ");
            builder.Append(FormatDamage(item.Damage));

            if (!string.IsNullOrWhiteSpace(item.DamageType) &&
                !string.Equals(
                    item.DamageType,
                    "Unknown",
                    StringComparison.OrdinalIgnoreCase))
            {
                builder.Append(' ');
                builder.Append(item.DamageType);
            }

            if (item.IsCritical)
            {
                builder.Append(" · Critical");
            }

            builder.AppendLine();
        }
    }

    private void PopulateCombatHistoryTimestamp(uint characterId)
    {
        if (!this.sectionTimestampLabels.TryGetValue(
                PilotArchiveSections.CombatHistory,
                out var label))
        {
            return;
        }

        var observedAt = this.clientManager.GetCombatHistoryLastRecordedAt(
            characterId);
        label.Text = observedAt.HasValue
            ? string.Concat(
                "Last updated: ",
                FormatCombatTimestamp(observedAt.Value))
            : "No combat encounters recorded yet";
    }

    private void ClearCombatHistory()
    {
        this.combatHistoryGrid.Rows.Clear();
        this.combatHistoryEntriesById.Clear();
        this.combatHistoryDetailsTextBox.Text =
            "Select an encounter to view its details.";

        if (this.sectionTimestampLabels.TryGetValue(
                PilotArchiveSections.CombatHistory,
                out var timestamp))
        {
            timestamp.Text = "No combat encounters recorded yet";
        }
    }

    private void RestoreCombatHistoryUiState(PilotArchiveSettings settings)
    {
        if (settings.CombatHistorySplitterDistance.HasValue)
        {
            SetSplitterDistance(
                this.combatHistorySplit,
                settings.CombatHistorySplitterDistance.Value);
        }
        else
        {
            settings.CombatHistorySplitterDistance =
                this.combatHistorySplit.SplitterDistance;
        }
    }

    private void SaveCombatHistoryUiState(PilotArchiveSettings settings)
    {
        if (!settings.CombatHistorySplitterDistance.HasValue &&
            this.combatHistorySplit.ClientSize.Height > 0)
        {
            settings.CombatHistorySplitterDistance =
                this.combatHistorySplit.SplitterDistance;
        }
    }

    private void CombatHistorySplit_OnSplitterMoving(
        object? sender,
        SplitterCancelEventArgs e)
    {
        if (!this.restoringUiState)
        {
            this.combatHistorySplitterUserMovePending = true;
        }
    }

    private void CombatHistorySplit_OnSplitterMoved(
        object? sender,
        SplitterEventArgs e)
    {
        if (this.restoringUiState ||
            !this.combatHistorySplitterUserMovePending)
        {
            return;
        }

        this.combatHistorySplitterUserMovePending = false;
        this.clientManager.PilotArchiveSettings
            .CombatHistorySplitterDistance =
            this.combatHistorySplit.SplitterDistance;
        this.clientManager.SaveSettings();
    }

    private void ClientManager_OnCombatJournalChanged(
        object? sender,
        CombatJournalChangedEventArgs e)
    {
        if (this.IsDisposed ||
            this.Disposing ||
            !this.clientManager.HistorySettings.RecordCombatHistory ||
            this.selectedCharacterId != e.CharacterId)
        {
            return;
        }

        void Refresh()
        {
            if (this.IsDisposed || this.Disposing)
            {
                return;
            }

            this.RefreshCombatHistory(e.CharacterId);
            this.PopulateCombatHistoryTimestamp(e.CharacterId);
        }

        if (this.InvokeRequired)
        {
            this.BeginInvoke(Refresh);
        }
        else
        {
            Refresh();
        }
    }

    private static void ApplyCombatOutcomeStyle(
        DataGridViewRow row,
        CombatJournalOutcome outcome)
    {
        var color = outcome switch
        {
            CombatJournalOutcome.Killed => MainWindowTheme.Success,
            CombatJournalOutcome.Died => MainWindowTheme.Danger,
            CombatJournalOutcome.Disengaged => MainWindowTheme.Warning,
            CombatJournalOutcome.Interrupted => MainWindowTheme.MutedText,
            _ => MainWindowTheme.Text,
        };
        row.DefaultCellStyle.ForeColor = color;
        row.DefaultCellStyle.SelectionForeColor = color;
    }

    private static string FormatCombatTimestamp(DateTimeOffset value) =>
        value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    private static string FormatCombatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        if (duration.TotalHours >= 1)
        {
            return string.Concat(
                ((int)duration.TotalHours).ToString(
                    CultureInfo.CurrentCulture),
                "h ",
                duration.Minutes.ToString(CultureInfo.CurrentCulture),
                "m");
        }

        if (duration.TotalMinutes >= 1)
        {
            return string.Concat(
                ((int)duration.TotalMinutes).ToString(
                    CultureInfo.CurrentCulture),
                "m ",
                duration.Seconds.ToString(CultureInfo.CurrentCulture),
                "s");
        }

        return string.Concat(
            Math.Max(0, (int)Math.Round(duration.TotalSeconds)).ToString(
                CultureInfo.CurrentCulture),
            "s");
    }

    private static string FormatDamage(double damage) =>
        Math.Max(0, damage).ToString("N0", CultureInfo.CurrentCulture);

    private static string FormatCombatLocation(
        CombatJournalEncounter encounter)
    {
        return string.Join(
            " · ",
            new[]
            {
                encounter.SystemName,
                encounter.SectorName,
                encounter.StarbaseName,
                string.IsNullOrWhiteSpace(encounter.NearestNavName)
                    ? ""
                    : string.Concat("near ", encounter.NearestNavName),
            }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }
}
