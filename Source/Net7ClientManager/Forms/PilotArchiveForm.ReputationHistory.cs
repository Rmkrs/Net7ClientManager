// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using System.Globalization;
using Net7ClientManager.ActivityJournal;
using Net7ClientManager.Models;
using Net7ClientManager.PilotArchive;

public sealed partial class PilotArchiveForm
{
    private readonly SplitContainer reputationHistorySplit = new();
    private readonly DataGridView reputationHistoryGrid = CreateGrid();
    private bool reputationHistorySplitterUserMovePending;
    private bool populatingReputations;

    private ThemedTabPage CreateReputationsTab()
    {
        var tab = CreateTabPage("Reputations");
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            BackColor = MainWindowTheme.Panel,
        };
        var timestamp = CreateTimestampLabel();
        this.sectionTimestampLabels[PilotArchiveSections.Reputations] =
            timestamp;
        timestamp.Dock = DockStyle.Top;
        timestamp.Height = 30;

        this.reputationHistorySplit.Dock = DockStyle.Fill;
        this.reputationHistorySplit.Orientation = Orientation.Horizontal;
        this.reputationHistorySplit.Size = new Size(1000, 520);
        this.reputationHistorySplit.SplitterWidth = 6;
        this.reputationHistorySplit.SplitterDistance = 300;
        this.reputationHistorySplit.Panel1MinSize = 140;
        this.reputationHistorySplit.Panel2MinSize = 110;
        this.reputationHistorySplit.BackColor = MainWindowTheme.Border;
        this.reputationHistorySplit.Panel1.BackColor = MainWindowTheme.Panel;
        this.reputationHistorySplit.Panel2.BackColor = MainWindowTheme.Panel;
        this.reputationHistorySplit.Panel2.Padding = new Padding(0, 10, 0, 0);

        this.reputationsGrid.Dock = DockStyle.Fill;
        this.reputationHistorySplit.Panel1.Controls.Add(this.reputationsGrid);

        var historyHeading = new Label
        {
            Dock = DockStyle.Top,
            Height = 28,
            Text = "Reputation history",
            ForeColor = MainWindowTheme.Text,
            Font = MainWindowTheme.CreateHeadingFont(9.0f),
            TextAlign = ContentAlignment.MiddleLeft,
        };
        this.reputationHistoryGrid.Dock = DockStyle.Fill;
        this.reputationHistorySplit.Panel2.Controls.Add(
            this.reputationHistoryGrid);
        this.reputationHistorySplit.Panel2.Controls.Add(historyHeading);

        panel.Controls.Add(this.reputationHistorySplit);
        panel.Controls.Add(timestamp);
        tab.Controls.Add(panel);
        return tab;
    }

    private void ConfigureReputationHistoryGrid()
    {
        AddColumn(this.reputationHistoryGrid, "Time", 155);
        AddColumn(this.reputationHistoryGrid, "Change", 90);
        AddColumn(this.reputationHistoryGrid, "New value", 100);
        AddColumn(this.reputationHistoryGrid, "Location", 260);
        AddColumn(this.reputationHistoryGrid, "Reason", 360, fill: true);
    }

    private void ApplyReputationHistorySettings()
    {
        var enabled = this.clientManager.HistorySettings.RecordActivityHistory;
        this.reputationHistorySplit.Panel2Collapsed = !enabled;

        if (!enabled)
        {
            this.reputationHistoryGrid.Rows.Clear();
            return;
        }

        if (this.selectedCharacterId.HasValue)
        {
            this.RefreshSelectedReputationHistory();
        }
    }

    private void PopulateReputations(
        IReadOnlyList<PilotArchiveReputation> reputations)
    {
        var selectedFactionKey = this.reputationsGrid.SelectedRows
            .Cast<DataGridViewRow>()
            .Select(row => row.HeaderCell.Tag as string)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        this.populatingReputations = true;

        try
        {
            this.reputationsGrid.Rows.Clear();

            foreach (var reputation in reputations)
            {
                var row = this.reputationsGrid.Rows[
                    this.reputationsGrid.Rows.Add(
                        reputation.DisplayName,
                        reputation.Reaction?.ToString(
                            "0",
                            CultureInfo.CurrentCulture) ?? "",
                        reputation.Description)];
                row.Tag = reputation.Slot.ToString(CultureInfo.InvariantCulture);
                row.HeaderCell.Tag = reputation.FactionKey;
            }

            this.ApplyStoredGridSort(this.reputationsGrid);
            this.reputationsGrid.ClearSelection();
            var selectedRow = this.reputationsGrid.Rows
                .Cast<DataGridViewRow>()
                .FirstOrDefault(row =>
                    row.HeaderCell.Tag is string factionKey &&
                    string.Equals(
                        factionKey,
                        selectedFactionKey,
                        StringComparison.Ordinal))
                ?? this.reputationsGrid.Rows
                    .Cast<DataGridViewRow>()
                    .FirstOrDefault();

            if (selectedRow != null)
            {
                selectedRow.Selected = true;
                this.reputationsGrid.CurrentCell = selectedRow.Cells[0];
            }
        }
        finally
        {
            this.populatingReputations = false;
        }

        this.RefreshSelectedReputationHistory();
    }

    private void RefreshSelectedReputationHistory()
    {
        this.reputationHistoryGrid.Rows.Clear();

        if (!this.clientManager.HistorySettings.RecordActivityHistory ||
            this.selectedCharacterId is not { } characterId ||
            this.reputationsGrid.CurrentRow?.HeaderCell.Tag is not string factionKey ||
            string.IsNullOrWhiteSpace(factionKey))
        {
            return;
        }

        var history = this.clientManager.GetReputationHistory(
            characterId,
            factionKey);

        foreach (var entry in history)
        {
            var row = this.reputationHistoryGrid.Rows[
                this.reputationHistoryGrid.Rows.Add(
                    FormatObservedAt(entry.OccurredAt),
                    FormatSignedReputation(entry.Delta),
                    entry.CurrentReaction.ToString(
                        "0.##",
                        CultureInfo.CurrentCulture),
                    FormatLocation(
                        entry.SystemName,
                        entry.SectorName,
                        entry.StarbaseName,
                        string.IsNullOrWhiteSpace(entry.NearestNavName)
                            ? null
                            : string.Concat(
                                "near ",
                                entry.NearestNavName)),
                    entry.Reason)];
            row.DefaultCellStyle.ForeColor = entry.Delta >= 0
                ? MainWindowTheme.Success
                : MainWindowTheme.Warning;
        }

        this.ApplyStoredGridSort(this.reputationHistoryGrid);
    }

    private void ReputationsGrid_OnSelectionChanged(
        object? sender,
        EventArgs e)
    {
        if (!this.populatingReputations)
        {
            this.RefreshSelectedReputationHistory();
        }
    }

    private void RestoreReputationHistoryUiState(
        PilotArchiveSettings settings)
    {
        if (settings.ReputationHistorySplitterDistance.HasValue)
        {
            SetSplitterDistance(
                this.reputationHistorySplit,
                settings.ReputationHistorySplitterDistance.Value);
        }
        else
        {
            settings.ReputationHistorySplitterDistance =
                this.reputationHistorySplit.SplitterDistance;
        }
    }

    private void SaveReputationHistoryUiState(PilotArchiveSettings settings)
    {
        if (!settings.ReputationHistorySplitterDistance.HasValue &&
            this.reputationHistorySplit.ClientSize.Height > 0)
        {
            settings.ReputationHistorySplitterDistance =
                this.reputationHistorySplit.SplitterDistance;
        }
    }

    private void ReputationHistorySplit_OnSplitterMoving(
        object? sender,
        SplitterCancelEventArgs e)
    {
        if (!this.restoringUiState)
        {
            this.reputationHistorySplitterUserMovePending = true;
        }
    }

    private void ReputationHistorySplit_OnSplitterMoved(
        object? sender,
        SplitterEventArgs e)
    {
        if (this.restoringUiState ||
            !this.reputationHistorySplitterUserMovePending)
        {
            return;
        }

        this.reputationHistorySplitterUserMovePending = false;
        this.clientManager.PilotArchiveSettings
            .ReputationHistorySplitterDistance =
            this.reputationHistorySplit.SplitterDistance;
        this.clientManager.SaveSettings();
    }

    private static string FormatSignedReputation(float value)
    {
        return value >= 0
            ? string.Concat(
                "+",
                value.ToString("0.##", CultureInfo.CurrentCulture))
            : value.ToString("0.##", CultureInfo.CurrentCulture);
    }
}
