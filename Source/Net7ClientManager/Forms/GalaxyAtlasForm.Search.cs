// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using System.Globalization;
using Net7ClientManager.Core;
using Net7ClientManager.Models;
using Net7ClientManager.Navigation;
using Net7ClientManager.Observations;
using Net7ClientManager.Observations.Models;
using Net7ClientManager.Social;

public sealed partial class GalaxyAtlasForm
{
    private const int MaximumAtlasSearchResults = 8;
    private const int AtlasSearchResultHeight = 30;

    private readonly TextBox atlasSearchTextBox = new();
    private readonly Panel atlasSearchResultsPanel = new();
    private readonly ListBox atlasSearchResultsListBox = new();
    private WorldSearchIndex atlasSearchIndex;
    private string atlasSearchFingerprint = "";

    private void RefreshAtlasSearchResults(bool force)
    {
        var query = this.atlasSearchTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(query))
        {
            this.atlasSearchFingerprint = "";
            this.atlasSearchResultsListBox.Items.Clear();
            this.HideAtlasSearchResults();
            return;
        }

        var fingerprint = this.BuildAtlasSearchFingerprint(query);

        if (!force &&
            string.Equals(
                this.atlasSearchFingerprint,
                fingerprint,
                StringComparison.Ordinal))
        {
            return;
        }

        this.atlasSearchFingerprint = fingerprint;
        var results = this.SearchAtlas(query);

        this.atlasSearchResultsListBox.BeginUpdate();

        try
        {
            this.atlasSearchResultsListBox.Items.Clear();
            this.atlasSearchResultsListBox.Items.AddRange(
                results.Cast<object>().ToArray());
            this.atlasSearchResultsListBox.SelectedIndex = -1;
        }
        finally
        {
            this.atlasSearchResultsListBox.EndUpdate();
        }

        if (results.Count == 0)
        {
            this.HideAtlasSearchResults();
            return;
        }

        if (this.atlasSearchTextBox.Focused ||
            this.atlasSearchResultsListBox.ContainsFocus)
        {
            this.ShowAtlasSearchResults(results.Count);
        }
    }

    private IReadOnlyList<GalaxyAtlasSearchResult> SearchAtlas(string query)
    {
        var placeMatches = new[]
        {
            WorldSearchKind.Sector,
            WorldSearchKind.Station,
            WorldSearchKind.Gate,
            WorldSearchKind.Planet,
            WorldSearchKind.NavigationPoint,
        }
            .SelectMany(kind => this.atlasSearchIndex.Search(
                query,
                kind,
                MaximumAtlasSearchResults))
            .Select(match => CreateAtlasPlaceSearchResult(match));
        var pilotMatches = this.BuildAtlasPilotSearchResults(query);

        return
        [
            .. placeMatches
                .Concat(pilotMatches)
                .GroupBy(
                    result => result.Identity,
                    StringComparer.Ordinal)
                .Select(group => group
                    .OrderBy(result => result.Rank)
                    .First())
                .OrderBy(result => result.Rank)
                .ThenBy(result => GetAtlasSearchKindOrder(result.Kind))
                .ThenBy(result => result.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(result => result.SystemName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(result => result.SectorName, StringComparer.OrdinalIgnoreCase)
                .Take(MaximumAtlasSearchResults),
        ];
    }

    private IReadOnlyList<GalaxyAtlasSearchResult>
        BuildAtlasPilotSearchResults(string query)
    {
        var normalizedQuery = GalaxyTopology.NormalizeName(query);
        var pilots = new Dictionary<string, AtlasPilotSearchSource>(
            StringComparer.OrdinalIgnoreCase);
        var selectedProcessId = this.SelectedProcessId;
        var clients = this.clientManager.Clients
            .OrderBy(client => client.ProcessId)
            .ToArray();
        NavigationRouteSnapshot? selectedRoute = null;
        ClientObservationSnapshot? selectedObservation = null;
        HashSet<string> groupMemberNames = [];
        HashSet<uint> groupMemberObjectIds = [];

        if (selectedProcessId.HasValue)
        {
            selectedRoute = this.clientManager.GetNavigationRouteSnapshot(
                selectedProcessId.Value);
            selectedObservation = this.clientManager
                .GetClientObservationSnapshots()
                .FirstOrDefault(snapshot =>
                    snapshot.ProcessId == selectedProcessId.Value);
            groupMemberNames = BuildGroupMemberNameSet(selectedObservation);
            groupMemberObjectIds = BuildGroupMemberObjectIdSet(
                selectedObservation);
        }

        if (this.showCurrentLocationCheckBox.Checked &&
            selectedProcessId.HasValue &&
            selectedRoute is { IsAvailable: true, CurrentSector: not null })
        {
            var selectedClient = clients.FirstOrDefault(client =>
                client.ProcessId == selectedProcessId.Value);

            if (selectedClient != null)
            {
                var pilotName = GetPilotName(selectedClient, selectedRoute);
                pilots[pilotName] = new AtlasPilotSearchSource(
                    pilotName,
                    selectedRoute.CurrentSector,
                    "Me");
            }
        }

        if (this.showGroupMembersCheckBox.Checked &&
            selectedProcessId.HasValue)
        {
            var observations = this.clientManager
                .GetClientObservationSnapshots()
                .ToDictionary(
                    snapshot => snapshot.ProcessId,
                    snapshot => snapshot);

            foreach (var client in clients)
            {
                if (client.ProcessId == selectedProcessId.Value ||
                    !observations.TryGetValue(
                        client.ProcessId,
                        out var observation))
                {
                    continue;
                }

                var route = this.clientManager.GetNavigationRouteSnapshot(
                    client.ProcessId);

                if (!route.IsAvailable ||
                    route.CurrentSector == null ||
                    !IsObservedGroupMember(
                        client,
                        route,
                        observation,
                        groupMemberNames,
                        groupMemberObjectIds))
                {
                    continue;
                }

                var pilotName = GetPilotName(client, route);
                pilots.TryAdd(
                    pilotName,
                    new AtlasPilotSearchSource(
                        pilotName,
                        route.CurrentSector,
                        "Group"));
            }
        }

        if (this.showSocialPilotsCheckBox.Checked)
        {
            foreach (var presence in this.clientManager
                         .GetSocialSnapshot()
                         .Presence)
            {
                if (presence.AtlasVisibility ==
                        SocialAtlasVisibilityMode.None ||
                    this.clientManager.GetSocialPresenceFreshness(
                        presence.UpdatedAtUtc) !=
                        SocialPresenceFreshness.Online ||
                    string.IsNullOrWhiteSpace(presence.SectorKey) ||
                    !this.clientManager.NavigationData.Topology.TryGetByKey(
                        presence.SectorKey,
                        out var sector))
                {
                    continue;
                }

                var detail = !string.IsNullOrWhiteSpace(
                                 presence.StationName)
                    ? string.Concat("docked at ", presence.StationName)
                    : presence.AtlasVisibility >=
                          SocialAtlasVisibilityMode.NearNav &&
                      !string.IsNullOrWhiteSpace(
                          presence.NearestNavName)
                        ? string.Concat("near ", presence.NearestNavName)
                        : "Social";

                pilots.TryAdd(
                    presence.PilotName,
                    new AtlasPilotSearchSource(
                        presence.PilotName,
                        sector,
                        detail));
            }
        }

        return
        [
            .. pilots.Values
                .Select(pilot => new
                {
                    Pilot = pilot,
                    Rank = ScoreAtlasSearchTerm(
                        GalaxyTopology.NormalizeName(pilot.Name),
                        normalizedQuery),
                })
                .Where(candidate => candidate.Rank != int.MaxValue)
                .Select(candidate => new GalaxyAtlasSearchResult
                {
                    Kind = GalaxyAtlasSearchResultKind.Pilot,
                    Identity = string.Concat(
                        "pilot:",
                        GalaxyTopology.NormalizeName(
                            candidate.Pilot.Name)),
                    Name = candidate.Pilot.Name,
                    SectorKey = candidate.Pilot.Sector.Key,
                    SectorName = candidate.Pilot.Sector.Name,
                    SystemName = candidate.Pilot.Sector.SystemName,
                    Detail = candidate.Pilot.Detail,
                    Rank = candidate.Rank,
                }),
        ];
    }

    private string BuildAtlasSearchFingerprint(string query)
    {
        var routes = string.Join(
            ';',
            this.clientManager.Clients
                .OrderBy(client => client.ProcessId)
                .Select(client =>
                {
                    var route = this.clientManager
                        .GetNavigationRouteSnapshot(client.ProcessId);
                    return string.Create(
                        CultureInfo.InvariantCulture,
                        $"{client.ProcessId}:{route.IsAvailable}:" +
                        $"{route.CharacterName}:{route.CurrentSector?.Key}");
                }));
        var selectedObservation = this.SelectedProcessId is { } processId
            ? this.clientManager
                .GetClientObservationSnapshots()
                .FirstOrDefault(snapshot => snapshot.ProcessId == processId)
            : null;
        var group = selectedObservation?.Group is
            { IsAvailable: true, IsValid: true } observedGroup
                ? string.Join(
                    ',',
                    observedGroup.Members
                        .Where(member => member.IsPresent)
                        .OrderBy(member => member.ObjectId)
                        .Select(member => string.Create(
                            CultureInfo.InvariantCulture,
                            $"{member.ObjectId}:{member.Name}")))
                : "";

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{query}:{this.clientManager.NavigationData.Revision}:" +
            $"{this.clientManager.GetSocialSnapshot().Version}:" +
            $"{this.SelectedProcessId}:" +
            $"{this.showCurrentLocationCheckBox.Checked}:" +
            $"{this.showGroupMembersCheckBox.Checked}:" +
            $"{this.showSocialPilotsCheckBox.Checked}:" +
            $"{routes}:{group}");
    }

    private static GalaxyAtlasSearchResult CreateAtlasPlaceSearchResult(
        WorldSearchMatch match)
    {
        var kind = match.Entry.Kind switch
        {
            WorldSearchKind.Sector => GalaxyAtlasSearchResultKind.Sector,
            WorldSearchKind.Station => GalaxyAtlasSearchResultKind.Station,
            WorldSearchKind.Gate => GalaxyAtlasSearchResultKind.Gate,
            WorldSearchKind.Planet => GalaxyAtlasSearchResultKind.Planet,
            WorldSearchKind.NavigationPoint =>
                GalaxyAtlasSearchResultKind.NavigationPoint,
            _ => throw new InvalidOperationException(
                $"Unsupported Atlas search kind '{match.Entry.Kind}'."),
        };

        return new GalaxyAtlasSearchResult
        {
            Kind = kind,
            Identity = match.Entry.Identity,
            Name = match.Entry.Name,
            SectorKey = match.Entry.SectorKey,
            SectorName = match.Entry.SectorName,
            SystemName = match.Entry.SystemName,
            Rank = match.Rank,
        };
    }

    private static int ScoreAtlasSearchTerm(string term, string query)
    {
        if (string.Equals(term, query, StringComparison.Ordinal))
        {
            return 0;
        }

        if (term.StartsWith(query, StringComparison.Ordinal))
        {
            return 10;
        }

        if (term.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries)
            .Any(word => word.StartsWith(
                query,
                StringComparison.Ordinal)))
        {
            return 20;
        }

        return term.Contains(query, StringComparison.Ordinal)
            ? 30
            : int.MaxValue;
    }

    private static int GetAtlasSearchKindOrder(
        GalaxyAtlasSearchResultKind kind)
    {
        return kind switch
        {
            GalaxyAtlasSearchResultKind.Sector => 0,
            GalaxyAtlasSearchResultKind.Station => 1,
            GalaxyAtlasSearchResultKind.Gate => 2,
            GalaxyAtlasSearchResultKind.Planet => 3,
            GalaxyAtlasSearchResultKind.NavigationPoint => 4,
            GalaxyAtlasSearchResultKind.Pilot => 5,
            _ => 4,
        };
    }

    private void ActivateAtlasSearchResult(
        GalaxyAtlasSearchResult result)
    {
        this.HideAtlasSearchResults();
        this.atlasSearchTextBox.Clear();
        this.atlasSearchResultsListBox.Items.Clear();
        this.atlasSearchFingerprint = "";
        this.NavigateToSector(result.SectorKey);
        this.atlasCanvas.ResetView();
        this.atlasCanvas.FocusSearchResult(result);
        this.RestoreAtlasSearchFocus();
    }

    private void RestoreAtlasSearchFocus()
    {
        if (this.IsDisposed || !this.IsHandleCreated)
        {
            return;
        }

        this.BeginInvoke(new Action(() =>
        {
            if (this.IsDisposed || !this.atlasSearchTextBox.CanFocus)
            {
                return;
            }

            this.atlasSearchTextBox.Focus();
            this.atlasSearchTextBox.SelectionStart =
                this.atlasSearchTextBox.TextLength;
            this.atlasSearchTextBox.SelectionLength = 0;
        }));
    }

    private bool NavigateToSector(string sectorKey)
    {
        if (string.IsNullOrWhiteSpace(sectorKey) ||
            string.Equals(
                sectorKey,
                this.viewedSectorKey,
                StringComparison.Ordinal) ||
            !this.clientManager.NavigationRoutes.Topology.TryGetByKey(
                sectorKey,
                out _))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(this.viewedSectorKey))
        {
            this.backStack.Push(this.viewedSectorKey);
        }

        this.viewedSectorKey = sectorKey;
        this.isFollowingCurrentLocation = false;
        this.atlasFingerprint = "";
        this.RefreshAtlas(force: true);
        return true;
    }

    private void ShowAtlasSearchResults(int resultCount)
    {
        if (resultCount <= 0)
        {
            this.HideAtlasSearchResults();
            return;
        }

        var visibleResults = Math.Min(
            resultCount,
            MaximumAtlasSearchResults);
        this.atlasSearchResultsPanel.Height =
            visibleResults * AtlasSearchResultHeight + 2;
        this.PositionAtlasSearchResultsPanel();
        this.atlasSearchResultsPanel.Visible = true;
        this.atlasSearchResultsPanel.BringToFront();
    }

    private void HideAtlasSearchResults()
    {
        this.atlasSearchResultsPanel.Visible = false;
        this.atlasSearchResultsListBox.SelectedIndex = -1;
    }

    private void PositionAtlasSearchResultsPanel()
    {
        if (!this.IsHandleCreated ||
            this.atlasSearchTextBox.Parent == null)
        {
            return;
        }

        var belowSearch = this.atlasSearchTextBox.PointToScreen(
            new Point(0, this.atlasSearchTextBox.Height + 3));
        var location = this.PointToClient(belowSearch);
        var desiredHeight = Math.Max(
            AtlasSearchResultHeight + 2,
            this.atlasSearchResultsPanel.Height);
        var availableHeight = Math.Max(
            AtlasSearchResultHeight + 2,
            this.ClientSize.Height - location.Y - 12);

        this.atlasSearchResultsPanel.SetBounds(
            location.X,
            location.Y,
            this.atlasSearchTextBox.Width,
            Math.Min(desiredHeight, availableHeight));
    }

    private void MoveAtlasSearchSelection(int direction)
    {
        if (this.atlasSearchResultsListBox.Items.Count == 0)
        {
            return;
        }

        if (!this.atlasSearchResultsPanel.Visible)
        {
            this.ShowAtlasSearchResults(
                this.atlasSearchResultsListBox.Items.Count);
        }

        var selectedIndex = this.atlasSearchResultsListBox.SelectedIndex;
        var nextIndex = selectedIndex < 0
            ? direction > 0
                ? 0
                : this.atlasSearchResultsListBox.Items.Count - 1
            : Math.Clamp(
                selectedIndex + direction,
                0,
                this.atlasSearchResultsListBox.Items.Count - 1);

        this.atlasSearchResultsListBox.SelectedIndex = nextIndex;
        this.atlasSearchResultsListBox.TopIndex = Math.Max(
            0,
            nextIndex - MaximumAtlasSearchResults + 1);
    }

    private void AtlasSearchTextBox_OnTextChanged(
        object? sender,
        EventArgs e)
    {
        this.RefreshAtlasSearchResults(force: true);
    }

    private void AtlasSearchTextBox_OnEnter(
        object? sender,
        EventArgs e)
    {
        this.RefreshAtlasSearchResults(force: true);
    }

    private void AtlasSearchControl_OnLeave(
        object? sender,
        EventArgs e)
    {
        if (this.IsDisposed)
        {
            return;
        }

        this.BeginInvoke(new Action(() =>
        {
            if (!this.atlasSearchTextBox.Focused &&
                !this.atlasSearchResultsListBox.ContainsFocus)
            {
                this.HideAtlasSearchResults();
            }
        }));
    }

    private void AtlasSearchTextBox_OnKeyDown(
        object? sender,
        KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Down)
        {
            this.MoveAtlasSearchSelection(1);
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }

        if (e.KeyCode == Keys.Up)
        {
            this.MoveAtlasSearchSelection(-1);
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }

        if (e.KeyCode == Keys.Enter)
        {
            var result = this.atlasSearchResultsListBox.SelectedItem as
                GalaxyAtlasSearchResult ??
                this.atlasSearchResultsListBox.Items
                    .OfType<GalaxyAtlasSearchResult>()
                    .FirstOrDefault();

            if (result != null)
            {
                this.ActivateAtlasSearchResult(result);
            }

            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }

        if (e.KeyCode == Keys.Escape &&
            this.atlasSearchResultsPanel.Visible)
        {
            this.HideAtlasSearchResults();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
    }

    private void AtlasSearchResultsListBox_OnMouseClick(
        object? sender,
        MouseEventArgs e)
    {
        var index = this.atlasSearchResultsListBox.IndexFromPoint(
            e.Location);

        if (index < 0 ||
            index >= this.atlasSearchResultsListBox.Items.Count ||
            this.atlasSearchResultsListBox.Items[index] is not
                GalaxyAtlasSearchResult result)
        {
            return;
        }

        this.atlasSearchResultsListBox.SelectedIndex = index;
        this.ActivateAtlasSearchResult(result);
    }

    private void AtlasSearchResultsListBox_OnMouseMove(
        object? sender,
        MouseEventArgs e)
    {
        var index = this.atlasSearchResultsListBox.IndexFromPoint(
            e.Location);

        if (index >= 0 &&
            index < this.atlasSearchResultsListBox.Items.Count &&
            index != this.atlasSearchResultsListBox.SelectedIndex)
        {
            this.atlasSearchResultsListBox.SelectedIndex = index;
        }
    }

    private void AtlasSearchResultsListBox_OnDrawItem(
        object? sender,
        DrawItemEventArgs e)
    {
        if (e.Index < 0 ||
            e.Index >= this.atlasSearchResultsListBox.Items.Count ||
            this.atlasSearchResultsListBox.Items[e.Index] is not
                GalaxyAtlasSearchResult result)
        {
            return;
        }

        var selected = (e.State & DrawItemState.Selected) != 0;
        var itemBackground = selected
            ? Color.FromArgb(36, 82, 103)
            : backgroundColor;
        using var backgroundBrush = new SolidBrush(itemBackground);
        e.Graphics.FillRectangle(backgroundBrush, e.Bounds);

        var typeColor = result.Kind switch
        {
            GalaxyAtlasSearchResultKind.Sector => accentColor,
            GalaxyAtlasSearchResultKind.Station => conditionalColor,
            GalaxyAtlasSearchResultKind.Gate => accessibleColor,
            GalaxyAtlasSearchResultKind.Planet => accentColor,
            GalaxyAtlasSearchResultKind.NavigationPoint => accessibleColor,
            GalaxyAtlasSearchResultKind.Pilot => socialPilotLocationColor,
            _ => mutedTextColor,
        };
        var typeBounds = new Rectangle(
            e.Bounds.Left + 10,
            e.Bounds.Top,
            68,
            e.Bounds.Height);
        var contentBounds = new Rectangle(
            typeBounds.Right,
            e.Bounds.Top,
            Math.Max(0, e.Bounds.Width - typeBounds.Width - 20),
            e.Bounds.Height);
        var contextWidth = Math.Min(
            Math.Max(150, contentBounds.Width / 2),
            280);
        var nameBounds = new Rectangle(
            contentBounds.Left,
            contentBounds.Top,
            Math.Max(0, contentBounds.Width - contextWidth - 12),
            contentBounds.Height);
        var contextBounds = new Rectangle(
            nameBounds.Right + 12,
            contentBounds.Top,
            contextWidth,
            contentBounds.Height);

        TextRenderer.DrawText(
            e.Graphics,
            result.KindLabel,
            this.Font,
            typeBounds,
            typeColor,
            TextFormatFlags.Left |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPrefix |
            TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(
            e.Graphics,
            result.Name,
            this.Font,
            nameBounds,
            textColor,
            TextFormatFlags.Left |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPrefix |
            TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(
            e.Graphics,
            result.ContextText,
            this.Font,
            contextBounds,
            mutedTextColor,
            TextFormatFlags.Right |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPrefix |
            TextFormatFlags.EndEllipsis);

        if (selected)
        {
            e.DrawFocusRectangle();
        }
    }

    private sealed record AtlasPilotSearchSource(
        string Name,
        GalaxySectorDefinition Sector,
        string? Detail);

}
