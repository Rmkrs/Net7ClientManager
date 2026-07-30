// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using System.Diagnostics;
using System.Globalization;
using Net7ClientManager.Contributions;
using Net7ClientManager.SkillPlanning;

internal sealed partial class SkillBuildBoardForm
{
    private const int ForgeSearchPageSize = 50;
    private const int ForgeSearchDebounceMilliseconds = 450;

    private bool forgeMode;
    private bool forgeBusy;
    private bool forgeSearchBusy;
    private string forgeStatus = "";
    private string forgeError = "";
    private string forgeQuery = "";
    private string forgePublisher = "";
    private string forgeSort = "stars";
    private bool forgeStarredOnly;
    private bool forgeOwnedOnly;
    private int forgeOffset;
    private ForgeBuildSearchResponse? forgeSearch;
    private ForgeBuildDetailsResponse? forgeDetails;
    private ForgeBuildVersionResponse? forgeVersion;
    private TextBox? forgeSearchTextBox;
    private ComboBox? forgeSortComboBox;
    private Label? forgeSearchSummaryLabel;
    private Button? forgePreviousButton;
    private Button? forgeNextButton;
    private FlowLayoutPanel? forgeSearchResultsHost;
    private CancellationTokenSource? forgeSearchDebounceCancellation;
    private CancellationTokenSource? forgeSearchCancellation;
    private long forgeSearchSerial;
    private CancellationTokenSource? forgeOperationCancellation;
    private long forgeOperationSerial;
    private CancellationTokenSource? forgeKnowledgeCancellation;
    private long forgeKnowledgeSerial;
    private string forgeKnowledgeKey = "";

    private void ResetForgeBrowser()
    {
        this.CancelForgeSearch();
        this.CancelForgeOperation();
        this.CancelForgeKnowledgeRefresh();
        this.forgeMode = false;
        this.forgeBusy = false;
        this.forgeSearchBusy = false;
        this.forgeStatus = "";
        this.forgeError = "";
        this.forgeOffset = 0;
        this.forgeSearch = null;
        this.forgeDetails = null;
        this.forgeVersion = null;
        this.forgeKnowledgeKey = "";
        this.ResetForgeViewBindings();
    }

    private void ResetForgeViewBindings()
    {
        this.forgeSearchTextBox = null;
        this.forgeSortComboBox = null;
        this.forgeSearchSummaryLabel = null;
        this.forgePreviousButton = null;
        this.forgeNextButton = null;
        this.forgeSearchResultsHost = null;
    }

    private void CancelForgeOperation()
    {
        this.forgeOperationSerial++;
        var cancellation = this.forgeOperationCancellation;
        this.forgeOperationCancellation = null;
        this.forgeBusy = false;
        this.forgeStatus = "";
        if (cancellation == null)
        {
            return;
        }

        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The operation completed while the Board was closing.
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    private void OpenForgeBrowser()
    {
        if (this.forgeBusy)
        {
            return;
        }

        this.forgeMode = true;
        this.forgeError = "";
        this.editMode = false;
        this.draft = null;
        this.forgeDetails = null;
        this.forgeVersion = null;
        this.RebuildContent();
        this.QueueForgeSearch(immediate: true);
    }

    private void BuildForgeView()
    {
        if (this.forgeDetails != null && this.forgeVersion != null)
        {
            this.BuildForgeDetailView();
            return;
        }

        this.BuildForgeSearchView();
    }

    private void BuildForgeSearchView()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Color.Transparent,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100.0f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 80.0f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34.0f));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100.0f));

        root.Controls.Add(this.CreateForgeSearchBar(), 0, 0);
        root.Controls.Add(this.CreateForgeSearchSummary(), 0, 1);
        root.Controls.Add(this.CreateForgeSearchResults(), 0, 2);
        this.contentPanel.Controls.Add(root);
    }

    private Control CreateForgeSearchBar()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = new Padding(0, 2, 0, 6),
            BackColor = Color.Transparent,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82.0f));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45.0f));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88.0f));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55.0f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36.0f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36.0f));

        var local = CreateActionButton("Local", width: 76);
        local.Dock = DockStyle.Fill;
        local.Margin = new Padding(0, 4, 8, 4);
        local.Enabled = !this.forgeBusy;
        local.Click += (_, _) =>
        {
            this.CancelForgeSearch();
            this.forgeMode = false;
            this.forgeDetails = null;
            this.forgeVersion = null;
            this.RebuildContent();
        };

        var search = new TextBox
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            AutoSize = true,
            Margin = new Padding(0, 0, 10, 0),
            Text = this.forgeQuery,
            MaxLength = 200,
            PlaceholderText = "Search name, purpose, notes, or publisher...",
        };
        MainWindowTheme.StyleTextBox(search);
        search.Font = MainWindowTheme.CreateBodyFont(9.5f);
        this.forgeSearchTextBox = search;
        search.Enabled = !this.forgeBusy;
        search.TextChanged += (_, _) =>
        {
            this.forgeQuery = search.Text;
            this.QueueForgeSearch();
        };
        search.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter)
            {
                return;
            }

            e.SuppressKeyPress = true;
            this.QueueForgeSearch(immediate: true);
        };

        var publisherLabel = CreateFieldLabel("Publisher");
        publisherLabel.Padding = new Padding(8, 0, 0, 0);
        var publisher = new TextBox
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            AutoSize = true,
            Margin = Padding.Empty,
            Text = this.forgePublisher,
            MaxLength = 64,
            PlaceholderText = "Exact pilot name",
        };
        MainWindowTheme.StyleTextBox(publisher);
        publisher.Font = MainWindowTheme.CreateBodyFont(9.5f);
        publisher.Enabled = !this.forgeBusy;
        publisher.TextChanged += (_, _) =>
        {
            this.forgePublisher = publisher.Text;
            this.QueueForgeSearch();
        };
        publisher.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter)
            {
                return;
            }

            e.SuppressKeyPress = true;
            this.QueueForgeSearch(immediate: true);
        };

        var sortLabel = CreateFieldLabel("Sort");
        var sort = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Margin = new Padding(0, 4, 10, 4),
        };
        MainWindowTheme.StyleComboBox(sort);
        this.forgeSortComboBox = sort;
        sort.Enabled = !this.forgeBusy;
        sort.Items.AddRange(
        [
            new ForgeSortChoice("Most starred", "stars"),
            new ForgeSortChoice("Newest", "newest"),
            new ForgeSortChoice("Name", "name"),
            new ForgeSortChoice("Relevance", "relevance"),
        ]);
        sort.DisplayMember = nameof(ForgeSortChoice.Text);
        var selectedSortIndex = sort.Items
            .Cast<ForgeSortChoice>()
            .Select((value, index) => new { value, index })
            .FirstOrDefault(candidate => string.Equals(
                candidate.value.Value,
                this.forgeSort,
                StringComparison.Ordinal))?.index ?? 0;
        sort.SelectedIndex = selectedSortIndex;
        sort.SelectedIndexChanged += (_, _) =>
        {
            if (sort.SelectedItem is ForgeSortChoice choice &&
                !string.Equals(
                    this.forgeSort,
                    choice.Value,
                    StringComparison.Ordinal))
            {
                this.forgeSort = choice.Value;
                this.QueueForgeSearch(immediate: true);
            }
        };

        var starred = CreateForgeCheckBox(
            "Starred",
            this.forgeStarredOnly);
        starred.Enabled =
            !this.forgeBusy && this.forgeContributionCoordinator.HasIdentity;
        if (!starred.Enabled)
        {
            starred.Checked = false;
            this.forgeStarredOnly = false;
        }
        starred.CheckedChanged += (_, _) =>
        {
            this.forgeStarredOnly = starred.Checked;
            this.QueueForgeSearch(immediate: true);
        };

        var mine = CreateForgeCheckBox(
            "My builds",
            this.forgeOwnedOnly);
        mine.Enabled =
            !this.forgeBusy && this.forgeContributionCoordinator.HasIdentity;
        if (!mine.Enabled)
        {
            mine.Checked = false;
            this.forgeOwnedOnly = false;
        }
        mine.CheckedChanged += (_, _) =>
        {
            this.forgeOwnedOnly = mine.Checked;
            this.QueueForgeSearch(immediate: true);
        };

        var filters = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = new Padding(8, 2, 0, 0),
            BackColor = Color.Transparent,
        };
        filters.Controls.Add(starred);
        filters.Controls.Add(mine);

        root.Controls.Add(local, 0, 0);
        root.Controls.Add(search, 1, 0);
        root.Controls.Add(publisherLabel, 2, 0);
        root.Controls.Add(publisher, 3, 0);
        root.Controls.Add(sortLabel, 0, 1);
        root.Controls.Add(sort, 1, 1);
        root.Controls.Add(filters, 2, 1);
        root.SetColumnSpan(filters, 2);
        return root;
    }

    private Control CreateForgeSearchSummary()
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = new Padding(4, 0, 0, 4),
            BackColor = MainWindowTheme.Header,
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100.0f));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88.0f));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88.0f));

        var summary = new Label
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(8, 0, 0, 0),
            TextAlign = ContentAlignment.MiddleLeft,
            Font = MainWindowTheme.CreateHeadingFont(9.0f),
            BackColor = Color.Transparent,
        };
        this.forgeSearchSummaryLabel = summary;
        row.Controls.Add(summary, 0, 0);

        var previous = CreateActionButton("Previous", width: 82);
        previous.Dock = DockStyle.Fill;
        previous.Height = 28;
        previous.Margin = new Padding(4, 3, 0, 3);
        previous.Click += (_, _) =>
        {
            this.forgeOffset = Math.Max(0, this.forgeOffset - ForgeSearchPageSize);
            _ = this.SearchForgeAsync(resetOffset: false);
        };
        this.forgePreviousButton = previous;

        var next = CreateActionButton("Next", width: 82);
        next.Dock = DockStyle.Fill;
        next.Height = 28;
        next.Margin = new Padding(4, 3, 0, 3);
        next.Click += (_, _) =>
        {
            this.forgeOffset += ForgeSearchPageSize;
            _ = this.SearchForgeAsync(resetOffset: false);
        };
        this.forgeNextButton = next;

        row.Controls.Add(previous, 1, 0);
        row.Controls.Add(next, 2, 0);
        this.RefreshForgeSearchChrome();
        return row;
    }

    private Control CreateForgeSearchResults()
    {
        var results = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = new Padding(0, 8, 0, 0),
            BackColor = MainWindowTheme.Background,
        };
        this.RegisterDarkScrollbarTheme(results);
        results.SizeChanged += (_, _) => ResizeFlowChildren(results);
        this.forgeSearchResultsHost = results;
        this.RefreshForgeSearchResults();
        return results;
    }

    private void RefreshForgeSearchResults()
    {
        var results = this.forgeSearchResultsHost;
        if (results is not { IsDisposed: false })
        {
            return;
        }

        var suppressRedraw = results.IsHandleCreated;
        if (suppressRedraw)
        {
            Net7ClientManager.Win32.NativeMethods.SetWindowRedraw(
                results.Handle,
                enabled: false);
        }

        results.SuspendLayout();
        try
        {
            foreach (Control child in results.Controls.Cast<Control>().ToArray())
            {
                results.Controls.Remove(child);
                child.Dispose();
            }

            if (this.forgeError.Length != 0)
            {
                results.Controls.Add(
                    CreateForgeMessageCard(
                        "Could not load Forge builds",
                        this.forgeError));
            }
            else if (this.forgeSearchBusy && this.forgeSearch == null)
            {
                results.Controls.Add(
                    CreateForgeMessageCard(
                        "Searching the Forge...",
                        "Published builds are being loaded for this pilot."));
            }
            else if (this.forgeSearch == null ||
                     this.forgeSearch.Builds.Count == 0)
            {
                results.Controls.Add(
                    CreateForgeMessageCard(
                        "No builds found",
                        "Try a broader search or remove one of the filters."));
            }
            else
            {
                foreach (var build in this.forgeSearch.Builds)
                {
                    results.Controls.Add(this.CreateForgeResultCard(build));
                }
            }

            ResizeFlowChildren(results);
        }
        finally
        {
            results.ResumeLayout(performLayout: true);
            if (suppressRedraw)
            {
                Net7ClientManager.Win32.NativeMethods.SetWindowRedraw(
                    results.Handle,
                    enabled: true);
            }
        }
    }

    private Control CreateForgeResultCard(ForgeBuildSummaryResponse build)
    {
        var card = new TableLayoutPanel
        {
            Height = 96,
            Width = 900,
            ColumnCount = 4,
            RowCount = 3,
            Margin = new Padding(0, 0, 0, 7),
            Padding = new Padding(10, 7, 8, 7),
            BackColor = MainWindowTheme.ElevatedPanel,
        };
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100.0f));
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110.0f));
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92.0f));
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72.0f));
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 30.0f));
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 26.0f));
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 100.0f));

        var title = new Label
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Text = build.Title,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = MainWindowTheme.CreateHeadingFont(10.0f),
            ForeColor = MainWindowTheme.Text,
            BackColor = Color.Transparent,
            AutoEllipsis = true,
        };
        var author = new Label
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Text = string.Concat(
                "by ",
                build.PublisherPilotName,
                " · ",
                this.GetForgeProfessionName(build.ProfessionIndex)),
            TextAlign = ContentAlignment.MiddleLeft,
            Font = MainWindowTheme.CreateBodyFont(8.5f),
            ForeColor = MainWindowTheme.MutedText,
            BackColor = Color.Transparent,
            AutoEllipsis = true,
        };
        var version = CreateForgeChip(string.Concat("v", build.LatestVersion), MainWindowTheme.Accent);
        var stars = CreateForgeChip(
            string.Create(CultureInfo.CurrentCulture, $"★ {build.StarCount:N0}"),
            MainWindowTheme.Warning);
        var open = CreateActionButton("Open", primary: true, width: 66);
        open.Dock = DockStyle.Fill;
        open.Margin = new Padding(5, 0, 0, 0);
        open.Enabled = !this.forgeBusy;
        open.Click += (_, _) => _ = this.OpenForgeBuildAsync(build.BuildId);

        var summary = new Label
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Text = string.IsNullOrWhiteSpace(build.Summary)
                ? "No purpose supplied."
                : build.Summary,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = MainWindowTheme.CreateBodyFont(8.5f),
            ForeColor = MainWindowTheme.MutedText,
            BackColor = Color.Transparent,
            AutoEllipsis = true,
        };
        var flags = new Label
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Text = build.IsOwnedByMe
                ? "Your build"
                : build.IsStarredByMe
                    ? "Starred"
                    : "",
            TextAlign = ContentAlignment.MiddleCenter,
            Font = MainWindowTheme.CreateBodyFont(8.0f),
            ForeColor = build.IsOwnedByMe
                ? MainWindowTheme.Accent
                : MainWindowTheme.Warning,
            BackColor = Color.Transparent,
        };

        card.Controls.Add(title, 0, 0);
        card.Controls.Add(version, 1, 0);
        card.Controls.Add(stars, 2, 0);
        card.Controls.Add(open, 3, 0);
        card.Controls.Add(summary, 0, 1);
        card.SetColumnSpan(summary, 4);
        card.Controls.Add(author, 0, 2);
        card.SetColumnSpan(author, 3);
        card.Controls.Add(flags, 3, 2);
        return card;
    }

    private void BuildForgeDetailView()
    {
        var localPreview = SkillBuildForgeDocumentCodec.ToLocalBuild(
            this.forgeVersion!.Content,
            string.Concat("forge-preview-", this.forgeVersion.BuildId));
        var analysis = this.workspace.Board.Analyze(
            localPreview,
            this.presentation.Baseline,
            this.presentation.EquipmentBaseline);
        var root = CreateBoardRoot(commandHeight: 94);
        root.Controls.Add(this.CreateForgeDetailCommandBar(), 0, 0);
        this.AddBoard(root, analysis, editable: false, rowOffset: 1);
        this.contentPanel.Controls.Add(root);
    }

    private Control CreateForgeDetailCommandBar()
    {
        var details = this.forgeDetails!;
        var version = this.forgeVersion!;
        var bar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 6,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = new Padding(0, 2, 0, 6),
            BackColor = Color.Transparent,
        };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86.0f));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100.0f));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 176.0f));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104.0f));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88.0f));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104.0f));
        bar.RowStyles.Add(new RowStyle(SizeType.Absolute, 40.0f));
        bar.RowStyles.Add(new RowStyle(SizeType.Percent, 100.0f));

        var back = CreateActionButton("Back", width: 80);
        back.Dock = DockStyle.Fill;
        back.Margin = new Padding(0, 0, 6, 2);
        back.Enabled = !this.forgeBusy;
        back.Click += (_, _) =>
        {
            this.forgeDetails = null;
            this.forgeVersion = null;
            this.RebuildContent();
        };

        var title = new Label
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(6, 0, 0, 0),
            Text = version.Content.Title,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = MainWindowTheme.CreateHeadingFont(11.0f),
            ForeColor = MainWindowTheme.Text,
            BackColor = MainWindowTheme.ElevatedPanel,
            AutoEllipsis = true,
        };

        var versions = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Margin = new Padding(6, 2, 6, 2),
            DisplayMember = nameof(ForgeVersionChoice.Text),
        };
        MainWindowTheme.StyleComboBox(versions);
        versions.Enabled = !this.forgeBusy;
        foreach (var value in details.Versions)
        {
            versions.Items.Add(new ForgeVersionChoice(value));
        }
        versions.SelectedItem = versions.Items
            .Cast<ForgeVersionChoice>()
            .First(value => value.Version.Version == version.Version);
        versions.SelectedIndexChanged += (_, _) =>
        {
            if (versions.SelectedItem is ForgeVersionChoice choice &&
                choice.Version.Version != this.forgeVersion?.Version)
            {
                _ = this.LoadForgeVersionAsync(choice.Version.Version);
            }
        };

        var star = CreateActionButton(
            details.IsOwnedByMe
                ? "Your build"
                : details.IsStarredByMe
                    ? "★ Unstar"
                    : "☆ Star",
            width: 98);
        star.Dock = DockStyle.Fill;
        star.Margin = new Padding(0, 2, 6, 2);
        star.Enabled = !details.IsOwnedByMe && !this.forgeBusy;
        star.Click += (_, _) =>
            _ = this.SetForgeStarAsync(!details.IsStarredByMe);

        var count = CreateForgeChip(
            string.Create(CultureInfo.CurrentCulture, $"★ {details.StarCount:N0}"),
            MainWindowTheme.Warning);
        count.Margin = new Padding(0, 2, 6, 2);

        var compatible = this.presentation.Baseline?.ProfessionIndex ==
            version.Content.ProfessionIndex;
        var use = CreateActionButton(
            compatible
                ? string.Concat("Use v", version.Version)
                : "Other profession",
            primary: compatible,
            width: 98);
        use.Dock = DockStyle.Fill;
        use.Margin = new Padding(0, 2, 0, 2);
        use.Enabled = compatible && !this.forgeBusy;
        use.Click += (_, _) => this.InstallForgeVersion();

        var metadata = new Label
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(4, 1, 0, 0),
            Text = this.BuildForgeMetadataText(details, version),
            TextAlign = ContentAlignment.MiddleLeft,
            Font = MainWindowTheme.CreateBodyFont(8.5f),
            ForeColor = MainWindowTheme.MutedText,
            BackColor = Color.Transparent,
            AutoEllipsis = true,
        };

        bar.Controls.Add(back, 0, 0);
        bar.Controls.Add(title, 1, 0);
        bar.Controls.Add(versions, 2, 0);
        bar.Controls.Add(star, 3, 0);
        bar.Controls.Add(count, 4, 0);
        bar.Controls.Add(use, 5, 0);
        bar.Controls.Add(metadata, 0, 1);
        bar.SetColumnSpan(metadata, 6);
        return bar;
    }

    private BuildChoice CreateLocalBuildChoice(SkillBuildDocument build)
    {
        var link = this.library.GetForgeLink(build.BuildId);
        var label = link == null
            ? build.Title
            : link.IsPublisherSource
                ? string.Concat(build.Title, " · published v", link.Version)
                : string.Concat(build.Title, " · Forge v", link.Version);
        if (link?.HasNewerVersion == true)
        {
            label = string.Concat(
                label,
                " · v",
                link.LatestKnownVersion,
                " available");
        }

        return new BuildChoice(build, label);
    }

    private void RefreshLocalPublicationChrome(SkillBuildDocument build)
    {
        var picker = this.localBuildPicker;
        if (picker is { IsDisposed: false })
        {
            var selectedBuildId = build.BuildId;
            picker.BeginUpdate();
            try
            {
                for (var index = 0; index < picker.Items.Count; index++)
                {
                    if (picker.Items[index] is not BuildChoice choice)
                    {
                        continue;
                    }

                    var refreshedBuild = this.library.Builds.FirstOrDefault(
                        candidate => string.Equals(
                            candidate.BuildId,
                            choice.Build.BuildId,
                            StringComparison.OrdinalIgnoreCase)) ?? choice.Build;
                    picker.Items[index] = this.CreateLocalBuildChoice(
                        refreshedBuild);
                }

                var selectedIndex = picker.Items
                    .Cast<BuildChoice>()
                    .Select((value, itemIndex) => new { value, itemIndex })
                    .FirstOrDefault(candidate => string.Equals(
                        candidate.value.Build.BuildId,
                        selectedBuildId,
                        StringComparison.OrdinalIgnoreCase))?.itemIndex ?? -1;
                if (selectedIndex >= 0)
                {
                    picker.SelectedIndex = selectedIndex;
                }
            }
            finally
            {
                picker.EndUpdate();
            }

            picker.Enabled = !this.forgeBusy;
        }

        var publication = this.localPublicationHost;
        if (publication is not { IsDisposed: false })
        {
            return;
        }

        publication.SuspendLayout();
        try
        {
            foreach (Control child in publication.Controls.Cast<Control>().ToArray())
            {
                publication.Controls.Remove(child);
                child.Dispose();
            }
            this.PopulateLocalPublicationControls(publication, build);
        }
        finally
        {
            publication.ResumeLayout(performLayout: true);
        }
    }

    private void PopulateLocalPublicationControls(
        FlowLayoutPanel publication,
        SkillBuildDocument build)
    {
        if (this.forgeBusy && !string.IsNullOrWhiteSpace(this.forgeStatus))
        {
            publication.Controls.Add(
                CreateForgeChip(this.forgeStatus, MainWindowTheme.Accent));
        }

        var link = this.library.GetForgeLink(build.BuildId);
        if (link == null)
        {
            var publish = CreatePublicationButton("Publish");
            publish.Enabled = !this.forgeBusy;
            publish.Click += (_, _) => _ = this.PublishLocalBuildAsync(build);
            publication.Controls.Add(publish);

            var delete = CreatePublicationButton(
                "Delete",
                danger: true,
                width: 66);
            delete.Enabled = !this.forgeBusy;
            delete.Click += (_, _) => this.QueueBuildCommand(
                "delete the build",
                () => this.DeleteBuild(build, link: null));
            publication.Controls.Add(delete);
            publication.Controls.Add(CreateForgeChip("Local draft", MainWindowTheme.MutedText));
            return;
        }

        publication.Controls.Add(
            CreateForgeChip(
                string.Create(CultureInfo.CurrentCulture, $"★ {link.StarCount:N0}"),
                MainWindowTheme.Warning));
        publication.Controls.Add(
            CreateForgeChip(
                string.Concat("by ", link.PublisherPilotName),
                MainWindowTheme.MutedText));

        if (link.HasNewerVersion)
        {
            if (link.IsPublisherSource)
            {
                publication.Controls.Add(
                    CreateForgeChip(
                        string.Concat(
                            "v",
                            link.LatestKnownVersion,
                            " available"),
                        MainWindowTheme.Accent));
            }
            else
            {
                var useLatest = CreatePublicationButton(
                    string.Concat("Use v", link.LatestKnownVersion));
                useLatest.Enabled = !this.forgeBusy;
                useLatest.Click += (_, _) =>
                    _ = this.UseLatestForgeVersionAsync(build, link);
                publication.Controls.Add(useLatest);
            }
        }

        if (!link.IsPublisherSource)
        {
            var delete = CreatePublicationButton(
                "Delete",
                danger: true,
                width: 66);
            delete.Enabled = !this.forgeBusy;
            delete.Click += (_, _) => this.QueueBuildCommand(
                "delete the build",
                () => this.DeleteBuild(build, link));
            publication.Controls.Add(delete);
            publication.Controls.Add(
                CreateForgeChip(
                    string.Concat("Forge v", link.Version),
                    MainWindowTheme.Accent));
            return;
        }

        var currentHash = SkillBuildForgeDocumentCodec.Prepare(build).ContentSha256;
        var changed = !string.Equals(
            currentHash,
            link.ContentSha256,
            StringComparison.OrdinalIgnoreCase);
        if (changed || link.HasNewerVersion)
        {
            var publish = CreatePublicationButton("Publish");
            publish.Enabled = !this.forgeBusy;
            publish.Click += (_, _) => _ = this.PublishLocalBuildAsync(build);
            publication.Controls.Add(publish);
        }

        publication.Controls.Add(
            CreateForgeChip(
                changed
                    ? string.Concat("Published v", link.Version, " · changed")
                    : string.Concat("Published v", link.Version),
                changed ? MainWindowTheme.Warning : MainWindowTheme.Success));
    }

    private static Button CreatePublicationButton(
        string text,
        bool danger = false,
        int width = 74)
    {
        var button = CreateActionButton(
            text,
            primary: !danger,
            danger: danger,
            width: width);
        button.Height = 22;
        button.Margin = new Padding(5, 0, 0, 0);
        button.Padding = new Padding(4, 0, 4, 0);
        button.Font = MainWindowTheme.CreateBodyFont(8.0f);
        return button;
    }

    private void DeleteBuild(
        SkillBuildDocument build,
        SkillBuildForgeLink? link)
    {
        if (link?.IsPublisherSource == true ||
            (link != null &&
             !string.Equals(
                 build.BuildId,
                 link.LocalBuildId,
                 StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var message = string.Create(
            CultureInfo.CurrentCulture,
            $"Are you sure you want to delete “{build.Title}”?");
        if (!ThemedMessageDialog.Confirm(
                this,
                "Delete build",
                message,
                "Delete"))
        {
            return;
        }

        this.suppressWorkspaceRefresh = true;
        try
        {
            _ = this.workspace.DeleteBuild(
                this.presentation.CharacterId,
                build.BuildId);
        }
        finally
        {
            this.suppressWorkspaceRefresh = false;
        }

        if (string.Equals(
                this.selectedBuildId,
                build.BuildId,
                StringComparison.OrdinalIgnoreCase))
        {
            this.selectedBuildId = null;
        }

        this.ReloadLibrary();
        this.RebuildContent();
    }

    private void QueueForgeSearch(bool immediate = false)
    {
        if (!this.forgeMode ||
            this.forgeDetails != null ||
            this.presentation.Baseline == null)
        {
            return;
        }

        this.forgeOffset = 0;
        this.CancelForgeSearchDebounce();
        this.CancelActiveForgeSearch();

        var cancellation = new CancellationTokenSource();
        this.forgeSearchDebounceCancellation = cancellation;
        _ = this.RunForgeSearchDebounceAsync(
            cancellation,
            immediate ? 0 : ForgeSearchDebounceMilliseconds);
    }

    private async Task RunForgeSearchDebounceAsync(
        CancellationTokenSource cancellation,
        int delayMilliseconds)
    {
        try
        {
            if (delayMilliseconds > 0)
            {
                await Task.Delay(
                    delayMilliseconds,
                    cancellation.Token);
            }

            if (cancellation.IsCancellationRequested ||
                !ReferenceEquals(
                    this.forgeSearchDebounceCancellation,
                    cancellation))
            {
                return;
            }

            await this.SearchForgeAsync(resetOffset: true);
        }
        catch (OperationCanceledException) when (
            cancellation.IsCancellationRequested)
        {
            // A newer search superseded this debounce window.
        }
        finally
        {
            if (ReferenceEquals(
                    this.forgeSearchDebounceCancellation,
                    cancellation))
            {
                this.forgeSearchDebounceCancellation = null;
            }
            cancellation.Dispose();
        }
    }

    private void CancelForgeSearchDebounce()
    {
        var cancellation = this.forgeSearchDebounceCancellation;
        this.forgeSearchDebounceCancellation = null;
        if (cancellation == null)
        {
            return;
        }

        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The debounce completed while the Board was changing view.
        }
    }

    private void CancelActiveForgeSearch()
    {
        this.forgeSearchSerial++;
        var cancellation = this.forgeSearchCancellation;
        this.forgeSearchCancellation = null;
        this.forgeSearchBusy = false;
        if (cancellation == null)
        {
            return;
        }

        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The request completed while a newer search was queued.
        }
    }

    private void CancelForgeSearch()
    {
        this.CancelForgeSearchDebounce();
        this.CancelActiveForgeSearch();
    }

    private async Task SearchForgeAsync(bool resetOffset)
    {
        if (!this.forgeMode || this.presentation.Baseline == null)
        {
            return;
        }

        if (resetOffset)
        {
            this.forgeOffset = 0;
        }

        this.CancelForgeSearchDebounce();
        var previous = this.forgeSearchCancellation;
        if (previous != null)
        {
            try
            {
                previous.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The earlier request completed as this one began.
            }
        }

        var operationSerial = ++this.forgeSearchSerial;
        var cancellation = new CancellationTokenSource();
        this.forgeSearchCancellation = cancellation;
        this.forgeSearchBusy = true;
        this.forgeError = "";
        this.RefreshForgeSearchChrome();
        if (this.forgeSearch == null)
        {
            this.RefreshForgeSearchResults();
        }

        try
        {
            var response = await this.forgeContributionCoordinator
                .SearchBuildsAsync(
                    this.forgeQuery,
                    this.presentation.Baseline.ProfessionIndex,
                    this.forgePublisher,
                    this.forgeStarredOnly,
                    this.forgeOwnedOnly,
                    this.forgeSort,
                    this.forgeOffset,
                    ForgeSearchPageSize,
                    cancellation.Token);

            if (operationSerial != this.forgeSearchSerial ||
                cancellation.IsCancellationRequested)
            {
                return;
            }

            this.forgeSearch = response;
            this.forgeDetails = null;
            this.forgeVersion = null;
        }
        catch (OperationCanceledException) when (
            cancellation.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            Debug.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"[Builds] Could not search published builds: {exception}"));
            if (operationSerial == this.forgeSearchSerial)
            {
                this.forgeSearch = null;
                this.forgeError =
                    "The Forge could not load published builds. Try again.";
            }
        }
        finally
        {
            var isCurrentOperation =
                operationSerial == this.forgeSearchSerial;
            cancellation.Dispose();

            if (isCurrentOperation)
            {
                if (ReferenceEquals(
                        this.forgeSearchCancellation,
                        cancellation))
                {
                    this.forgeSearchCancellation = null;
                }
                this.forgeSearchBusy = false;
                if (!this.IsDisposed && !this.Disposing)
                {
                    this.RefreshForgeSearchChrome();
                    this.RefreshForgeSearchResults();
                }
            }
        }
    }

    private void RefreshForgeSearchChrome()
    {
        var summary = this.forgeSearchSummaryLabel;
        if (summary is { IsDisposed: false })
        {
            summary.Text = this.forgeSearchBusy
                ? "Searching the Forge..."
                : this.forgeError.Length != 0
                    ? "Forge search could not be completed"
                    : this.forgeSearch == null
                        ? "Forge builds"
                        : this.forgeSearch.TotalCount == 1
                            ? "1 published build"
                            : string.Create(
                                CultureInfo.CurrentCulture,
                                $"{this.forgeSearch.TotalCount:N0} published builds");
            summary.ForeColor = this.forgeSearchBusy
                ? MainWindowTheme.Accent
                : this.forgeError.Length != 0
                    ? MainWindowTheme.Danger
                    : MainWindowTheme.Text;
        }

        if (this.forgePreviousButton is { IsDisposed: false } previous)
        {
            previous.Enabled =
                !this.forgeSearchBusy &&
                !this.forgeBusy &&
                this.forgeOffset > 0;
        }

        if (this.forgeNextButton is { IsDisposed: false } next)
        {
            next.Enabled =
                !this.forgeSearchBusy &&
                !this.forgeBusy &&
                this.forgeSearch != null &&
                this.forgeOffset + ForgeSearchPageSize <
                this.forgeSearch.TotalCount;
        }
    }

    private void QueueForgeKnowledgeRefresh()
    {
        if (!this.presentation.HasBuildContext ||
            this.presentation.CharacterId == 0 ||
            string.IsNullOrWhiteSpace(this.presentation.PilotName))
        {
            return;
        }

        var forgeBuildIds = this.library.ForgeLinks.Values
            .Select(value => value.ForgeBuildId)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        if (forgeBuildIds.Length == 0)
        {
            return;
        }

        var key = string.Concat(
            this.presentation.CharacterId.ToString(
                CultureInfo.InvariantCulture),
            ":",
            string.Join("|", forgeBuildIds));
        if (string.Equals(
                key,
                this.forgeKnowledgeKey,
                StringComparison.Ordinal))
        {
            return;
        }

        this.CancelForgeKnowledgeRefresh();
        this.forgeKnowledgeKey = key;
        var cancellation = new CancellationTokenSource();
        this.forgeKnowledgeCancellation = cancellation;
        var operationSerial = ++this.forgeKnowledgeSerial;
        _ = this.RefreshForgeKnowledgeAsync(
            forgeBuildIds,
            this.presentation.CharacterId,
            this.presentation.PilotName,
            operationSerial,
            cancellation);
    }

    private void CancelForgeKnowledgeRefresh()
    {
        this.forgeKnowledgeSerial++;
        var cancellation = this.forgeKnowledgeCancellation;
        this.forgeKnowledgeCancellation = null;
        if (cancellation == null)
        {
            return;
        }

        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The refresh completed while the pilot context changed.
        }
    }

    private async Task RefreshForgeKnowledgeAsync(
        IReadOnlyList<string> forgeBuildIds,
        uint characterId,
        string pilotName,
        long operationSerial,
        CancellationTokenSource cancellation)
    {
        List<ForgeBuildDetailsResponse> updates = [];
        try
        {
            foreach (var forgeBuildId in forgeBuildIds)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                try
                {
                    updates.Add(await this.forgeContributionCoordinator
                        .GetBuildDetailsAsync(
                            forgeBuildId,
                            cancellation.Token));
                }
                catch (OperationCanceledException) when (
                    cancellation.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    Debug.WriteLine(string.Create(
                        CultureInfo.InvariantCulture,
                        $"[Builds] Could not refresh Forge build {forgeBuildId}: {exception}"));
                }
            }

            if (operationSerial != this.forgeKnowledgeSerial ||
                cancellation.IsCancellationRequested ||
                this.presentation.CharacterId != characterId ||
                updates.Count == 0)
            {
                return;
            }

            this.suppressWorkspaceRefresh = true;
            try
            {
                foreach (var details in updates)
                {
                    this.workspace.UpdateForgeKnowledge(
                        details.BuildId,
                        details.LatestVersion,
                        details.StarCount,
                        details.IsStarredByMe,
                        details.IsOwnedByMe);
                }
            }
            finally
            {
                this.suppressWorkspaceRefresh = false;
            }

            this.ReloadLibrary();
            if (!this.forgeMode &&
                !this.editMode &&
                this.GetSelectedBuild() is { } selected)
            {
                this.RefreshLocalPublicationChrome(selected);
            }
        }
        catch (OperationCanceledException) when (
            cancellation.IsCancellationRequested)
        {
            // The pilot or Board changed before the background refresh finished.
        }
        finally
        {
            if (operationSerial == this.forgeKnowledgeSerial &&
                ReferenceEquals(this.forgeKnowledgeCancellation, cancellation))
            {
                this.forgeKnowledgeCancellation = null;
            }
            cancellation.Dispose();
        }
    }

    private async Task OpenForgeBuildAsync(string buildId)
    {
        this.CancelForgeSearch();
        await this.RunForgeOperationAsync(
            "open the published build",
            async cancellationToken =>
            {
                var details = await this.forgeContributionCoordinator
                    .GetBuildDetailsAsync(
                        buildId,
                        cancellationToken);
                var version = await this.forgeContributionCoordinator
                    .GetBuildVersionAsync(
                        buildId,
                        details.LatestVersion,
                        cancellationToken);
                this.forgeDetails = details;
                this.forgeVersion = version;
                this.workspace.UpdateForgeKnowledge(
                    buildId,
                    details.LatestVersion,
                    details.StarCount,
                    details.IsStarredByMe,
                    details.IsOwnedByMe);
            },
            "Opening the published build...");
    }

    private async Task LoadForgeVersionAsync(int version)
    {
        var buildId = this.forgeDetails?.BuildId;
        if (string.IsNullOrWhiteSpace(buildId))
        {
            return;
        }

        await this.RunForgeOperationAsync(
            "load the selected build version",
            async cancellationToken =>
            {
                this.forgeVersion = await this.forgeContributionCoordinator
                    .GetBuildVersionAsync(
                        buildId,
                        version,
                        cancellationToken);
            },
            string.Concat("Loading v", version, "..."));
    }

    private async Task SetForgeStarAsync(bool starred)
    {
        var details = this.forgeDetails;
        if (details == null)
        {
            return;
        }

        await this.RunForgeOperationAsync(
            starred ? "star the build" : "unstar the build",
            async cancellationToken =>
            {
                var response = await this.forgeContributionCoordinator
                    .SetBuildStarAsync(
                        details.BuildId,
                        starred,
                        cancellationToken);
                this.forgeDetails = details with
                {
                    IsStarredByMe = response.Starred,
                    StarCount = response.StarCount,
                };
                if (this.forgeSearch != null)
                {
                    var updatedBuilds = this.forgeSearch.Builds
                        .Select(build => string.Equals(
                            build.BuildId,
                            details.BuildId,
                            StringComparison.Ordinal)
                            ? build with
                            {
                                IsStarredByMe = response.Starred,
                                StarCount = response.StarCount,
                            }
                            : build)
                        .ToArray();
                    if (this.forgeStarredOnly && !response.Starred)
                    {
                        updatedBuilds = updatedBuilds
                            .Where(build => !string.Equals(
                                build.BuildId,
                                details.BuildId,
                                StringComparison.Ordinal))
                            .ToArray();
                    }

                    this.forgeSearch = this.forgeSearch with
                    {
                        TotalCount = this.forgeStarredOnly && !response.Starred
                            ? Math.Max(0, this.forgeSearch.TotalCount - 1)
                            : this.forgeSearch.TotalCount,
                        Builds = updatedBuilds,
                    };
                }
                var currentVersion = this.forgeVersion;
                if (currentVersion != null)
                {
                    this.forgeVersion = currentVersion with
                    {
                        IsStarredByMe = response.Starred,
                        StarCount = response.StarCount,
                    };
                }
                this.workspace.UpdateForgeKnowledge(
                    details.BuildId,
                    details.LatestVersion,
                    response.StarCount,
                    response.Starred,
                    details.IsOwnedByMe);
            },
            starred ? "Adding star..." : "Removing star...");
    }

    private async Task UseLatestForgeVersionAsync(
        SkillBuildDocument build,
        SkillBuildForgeLink link)
    {
        if (this.forgeBusy ||
            !link.HasNewerVersion ||
            link.LatestKnownVersion <= link.Version)
        {
            return;
        }

        var targetVersion = link.LatestKnownVersion;
        var operationSerial = ++this.forgeOperationSerial;
        var cancellation = new CancellationTokenSource();
        this.forgeOperationCancellation = cancellation;
        this.forgeBusy = true;
        this.forgeStatus = string.Concat(
            "Loading v",
            targetVersion,
            "...");
        this.RefreshLocalPublicationChrome(build);

        var installed = false;
        try
        {
            var version = await this.forgeContributionCoordinator
                .GetBuildVersionAsync(
                    link.ForgeBuildId,
                    targetVersion,
                    cancellation.Token);

            if (operationSerial != this.forgeOperationSerial ||
                cancellation.IsCancellationRequested)
            {
                return;
            }

            installed = this.StoreAndActivateForgeVersion(version);
        }
        catch (OperationCanceledException) when (
            cancellation.IsCancellationRequested)
        {
            // The Board closed or another Forge operation took over.
        }
        catch (Exception exception)
        {
            Debug.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"[Builds] Could not use Forge build v{targetVersion}: {exception}"));
            if (!this.IsDisposed && !this.Disposing)
            {
                ThemedMessageDialog.ShowWarning(
                    this,
                    "Could not update build",
                    string.Concat(
                        "The Forge could not load v",
                        targetVersion,
                        ". Try again."));
            }
        }
        finally
        {
            cancellation.Dispose();
            if (operationSerial == this.forgeOperationSerial)
            {
                if (ReferenceEquals(
                        this.forgeOperationCancellation,
                        cancellation))
                {
                    this.forgeOperationCancellation = null;
                }

                this.forgeBusy = false;
                this.forgeStatus = "";
                if (!this.IsDisposed && !this.Disposing)
                {
                    this.ReloadLibrary();
                    if (installed)
                    {
                        this.RebuildContent();
                    }
                    else
                    {
                        this.RefreshLocalPublicationChrome(build);
                    }
                }
            }
        }
    }

    private async Task PublishLocalBuildAsync(SkillBuildDocument build)
    {
        var link = this.library.GetForgeLink(build.BuildId);
        if (link is { IsPublisherSource: false })
        {
            ThemedMessageDialog.ShowWarning(
                this,
                "Forge build",
                "Use Copy & edit to make a local build before publishing.");
            return;
        }

        if (!SkillBuildPublishDialog.Confirm(
                this,
                build.Title,
                isNewPublication: link == null) ||
            this.forgeBusy)
        {
            return;
        }

        var operationSerial = ++this.forgeOperationSerial;
        this.forgeBusy = true;
        this.forgeStatus = "Publishing...";
        this.RefreshLocalPublicationChrome(build);

        try
        {
            var response = await this.forgeContributionCoordinator
                .PublishBuildAsync(
                    build,
                    link?.ForgeBuildId,
                    this.presentation.PilotName,
                    CancellationToken.None);

            this.suppressWorkspaceRefresh = true;
            try
            {
                this.workspace.SaveForgeLink(
                    new SkillBuildForgeLink
                    {
                        LocalBuildId = build.BuildId,
                        ForgeBuildId = response.BuildId,
                        Version = response.Version,
                        ContentSha256 = response.ContentSha256,
                        PublisherPilotName = response.PublisherPilotName,
                        IsPublisherSource = true,
                        LatestKnownVersion = response.Version,
                        StarCount = response.StarCount,
                        IsStarredByMe = false,
                        IsOwnedByMe = true,
                        UpdatedAtUtc = response.PublishedAtUtc,
                    });
                this.workspace.UpdateForgeKnowledge(
                    response.BuildId,
                    response.Version,
                    response.StarCount,
                    isStarredByMe: false,
                    isOwnedByMe: true);
            }
            finally
            {
                this.suppressWorkspaceRefresh = false;
            }

            this.ReloadLibrary();
        }
        catch (ForgeBuildApiException exception) when (
            string.Equals(
                exception.Code,
                "build_unchanged",
                StringComparison.Ordinal))
        {
            if (!this.IsDisposed && !this.Disposing)
            {
                ThemedMessageDialog.ShowWarning(
                    this,
                    "Already published",
                    "This build is already the latest published version.");
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"[Builds] Could not publish the build: {exception}"));
            if (!this.IsDisposed && !this.Disposing)
            {
                ThemedMessageDialog.ShowWarning(
                    this,
                    "Could not publish build",
                    DescribeForgeIdentityFailure(
                        exception,
                        "The Forge could not publish this build. Try again.",
                        publishingArchivedPilot: this.presentation.IsArchivedContext));
            }
        }
        finally
        {
            if (operationSerial == this.forgeOperationSerial)
            {
                this.forgeBusy = false;
                this.forgeStatus = "";
                if (!this.IsDisposed && !this.Disposing)
                {
                    this.RefreshLocalPublicationChrome(build);
                }
            }
        }
    }

    private void InstallForgeVersion()
    {
        var version = this.forgeVersion;
        if (version == null ||
            !this.StoreAndActivateForgeVersion(version))
        {
            return;
        }

        this.ReloadLibrary();
        this.RebuildContent();
    }

    private bool StoreAndActivateForgeVersion(
        ForgeBuildVersionResponse version)
    {
        if (this.presentation.Baseline == null)
        {
            return false;
        }

        if (version.Content.ProfessionIndex !=
            this.presentation.Baseline.ProfessionIndex)
        {
            ThemedMessageDialog.ShowWarning(
                this,
                "Different profession",
                "This build cannot be used by the selected pilot.");
            return false;
        }

        var localBuildId = string.Create(
            CultureInfo.InvariantCulture,
            $"forge-{version.BuildId}-{version.Version}");
        var localBuild = SkillBuildForgeDocumentCodec.ToLocalBuild(
            version.Content,
            localBuildId);
        this.suppressWorkspaceRefresh = true;
        try
        {
            this.workspace.SaveAndActivateForgeVersion(
                this.presentation.CharacterId,
                localBuild,
                new SkillBuildForgeLink
                {
                    LocalBuildId = localBuildId,
                    ForgeBuildId = version.BuildId,
                    Version = version.Version,
                    ContentSha256 = version.ContentSha256,
                    PublisherPilotName = version.PublisherPilotName,
                    IsPublisherSource = false,
                    LatestKnownVersion = version.LatestVersion,
                    StarCount = version.StarCount,
                    IsStarredByMe = version.IsStarredByMe,
                    IsOwnedByMe = version.IsOwnedByMe,
                    UpdatedAtUtc = version.PublishedAtUtc,
                });
        }
        finally
        {
            this.suppressWorkspaceRefresh = false;
        }

        this.selectedBuildId = localBuildId;
        this.forgeMode = false;
        return true;
    }

    private static string DescribeForgeIdentityFailure(
        Exception exception,
        string fallback,
        bool publishingArchivedPilot = false)
    {
        if (exception is ForgeIdentityRequiredException &&
            publishingArchivedPilot)
        {
            return "Log into this archived character once, then publish again.";
        }

        if (exception is ForgeIdentityException)
        {
            return exception.Message;
        }

        if (exception is ForgeBuildApiException buildException &&
            string.Equals(
                buildException.Code,
                "pilot_not_claimed",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Log into this character once, then publish again.";
        }

        return fallback;
    }

    private async Task RunForgeOperationAsync(
        string operation,
        Func<CancellationToken, Task> command,
        string busyStatus,
        bool cancellable = true,
        bool failureInline = false)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (this.forgeBusy)
        {
            return;
        }

        var operationSerial = ++this.forgeOperationSerial;
        var cancellation = cancellable
            ? new CancellationTokenSource()
            : null;
        this.forgeOperationCancellation = cancellation;
        this.forgeBusy = true;
        this.forgeStatus = busyStatus;
        this.forgeError = "";
        this.RebuildContent();

        try
        {
            await command(cancellation?.Token ?? CancellationToken.None);
        }
        catch (OperationCanceledException) when (
            cancellation?.IsCancellationRequested == true)
        {
            return;
        }
        catch (Exception exception)
        {
            Debug.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"[Builds] Could not {operation}: {exception}"));
            var message = DescribeForgeIdentityFailure(
                exception,
                GetForgeFailureMessage(operation));
            if (failureInline)
            {
                this.forgeError = message;
            }
            else if (!this.IsDisposed && !this.Disposing)
            {
                ThemedMessageDialog.ShowWarning(
                    this,
                    "Forge",
                    message);
            }
        }
        finally
        {
            cancellation?.Dispose();
            if (operationSerial == this.forgeOperationSerial)
            {
                this.forgeOperationCancellation = null;
                this.forgeBusy = false;
                this.forgeStatus = "";
                if (!this.IsDisposed && !this.Disposing)
                {
                    this.ReloadLibrary();
                    this.RebuildContent();
                }
            }
        }
    }

    private static string GetForgeFailureMessage(string operation) =>
        operation switch
        {
            "search published builds" =>
                "The Forge could not load published builds. Try again.",
            "open the published build" =>
                "The Forge could not open this build. Try again.",
            "load the selected build version" =>
                "The Forge could not load this build version. Try again.",
            "star the build" or "unstar the build" =>
                "The Forge could not update the star. Try again.",
            _ => "The Forge could not complete this action. Try again.",
        };

    private static ThemedCheckBox CreateForgeCheckBox(
        string text,
        bool isChecked) =>
        new()
        {
            AutoSize = true,
            Margin = new Padding(0, 1, 18, 0),
            Text = text,
            Checked = isChecked,
            Font = MainWindowTheme.CreateBodyFont(8.5f),
            ForeColor = MainWindowTheme.Text,
        };

    private static Label CreateForgeChip(string text, Color color) =>
        new()
        {
            AutoSize = true,
            Margin = Padding.Empty,
            Padding = new Padding(8, 3, 8, 3),
            Text = text,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = MainWindowTheme.CreateBodyFont(8.0f),
            ForeColor = color,
            BackColor = MainWindowTheme.ElevatedPanel,
        };

    private static Control CreateForgeMessageCard(
        string title,
        string body)
    {
        var card = new Panel
        {
            Height = 92,
            Width = 800,
            Margin = Padding.Empty,
            Padding = new Padding(12, 8, 12, 8),
            BackColor = MainWindowTheme.ElevatedPanel,
        };
        card.Controls.Add(
            new Label
            {
                Dock = DockStyle.Fill,
                Text = body,
                TextAlign = ContentAlignment.TopLeft,
                Font = MainWindowTheme.CreateBodyFont(9.0f),
                ForeColor = MainWindowTheme.MutedText,
                BackColor = Color.Transparent,
            });
        card.Controls.Add(
            new Label
            {
                Dock = DockStyle.Top,
                Height = 30,
                Text = title,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = MainWindowTheme.CreateHeadingFont(10.0f),
                ForeColor = MainWindowTheme.Text,
                BackColor = Color.Transparent,
            });
        return card;
    }

    private string BuildForgeMetadataText(
        ForgeBuildDetailsResponse details,
        ForgeBuildVersionResponse version)
    {
        if (this.forgeBusy && !string.IsNullOrWhiteSpace(this.forgeStatus))
        {
            return this.forgeStatus;
        }

        var newer = version.Version < details.LatestVersion
            ? string.Concat(
                "  ·  v",
                details.LatestVersion,
                " available")
            : "";
        var purpose = string.IsNullOrWhiteSpace(version.Content.Summary)
            ? ""
            : string.Concat("  ·  Purpose: ", version.Content.Summary.Trim());
        return string.Concat(
            "Published by ",
            details.PublisherPilotName,
            "  ·  ",
            this.GetForgeProfessionName(version.Content.ProfessionIndex),
            "  ·  Immutable v",
            version.Version,
            newer,
            purpose);
    }

    private string GetForgeProfessionName(int professionIndex)
    {
        if (!this.workspace.Catalog.TryGetProfession(
                professionIndex,
                out var profession))
        {
            return string.Concat("Profession ", professionIndex);
        }

        return string.Concat(
            profession.RaceName,
            " ",
            profession.ProfessionName);
    }

    private sealed record ForgeSortChoice(string Text, string Value)
    {
        public override string ToString() => this.Text;
    }

    private sealed record ForgeVersionChoice(
        ForgeBuildVersionSummaryResponse Version)
    {
        public string Text => string.Create(
            CultureInfo.CurrentCulture,
            $"v{this.Version.Version} · {this.Version.Title}");

        public override string ToString() => this.Text;
    }
}
