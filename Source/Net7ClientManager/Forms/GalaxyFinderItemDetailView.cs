// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using System.Globalization;
using Net7ClientManager.Core;
using Net7ClientManager.GalaxyKnowledge;
using Net7ClientManager.Shopping;
using Net7ClientManager.Services;
using Net7ClientManager.SkillPlanning;

internal sealed class GalaxyFinderItemDetailView : UserControl
{
    private const int ContentInset = 18;

    private readonly ClientManager clientManager;
    private readonly GalaxyKnowledgeSnapshot snapshot;
    private readonly GalaxyItemKnowledge item;
    private readonly Func<int, Size, Image?> resolveIcon;
    private readonly Action<int> openItem;
    private readonly Func<GalaxyItemSourceKnowledge,
        IReadOnlyList<GalaxyFinderResolvedRoute>> resolveSourceRoutes;
    private readonly Action<GalaxyFinderResolvedRoute> setSourceDestination;
    private readonly Action<string> openShoppingList;
    private readonly Action? backRequested;
    private readonly Panel scrollPanel = new();
    private readonly TableLayoutPanel content = new();
    private readonly FlowLayoutPanel shoppingMembershipBody = new();
    private readonly Button addToShoppingListButton = new();
    private readonly Label addStatusLabel = new();
    private readonly System.Windows.Forms.Timer addStatusTimer = new();

    public GalaxyFinderItemDetailView(
        ClientManager clientManager,
        GalaxyKnowledgeSnapshot snapshot,
        GalaxyItemKnowledge item,
        Func<int, Size, Image?> resolveIcon,
        Action<int> openItem,
        Func<GalaxyItemSourceKnowledge,
            IReadOnlyList<GalaxyFinderResolvedRoute>> resolveSourceRoutes,
        Action<GalaxyFinderResolvedRoute> setSourceDestination,
        Action<string> openShoppingList,
        Action? backRequested)
    {
        ArgumentNullException.ThrowIfNull(clientManager);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(resolveIcon);
        ArgumentNullException.ThrowIfNull(openItem);
        ArgumentNullException.ThrowIfNull(resolveSourceRoutes);
        ArgumentNullException.ThrowIfNull(setSourceDestination);
        ArgumentNullException.ThrowIfNull(openShoppingList);

        this.clientManager = clientManager;
        this.snapshot = snapshot;
        this.item = item;
        this.resolveIcon = resolveIcon;
        this.openItem = openItem;
        this.resolveSourceRoutes = resolveSourceRoutes;
        this.setSourceDestination = setSourceDestination;
        this.openShoppingList = openShoppingList;
        this.backRequested = backRequested;

        this.Dock = DockStyle.Fill;
        this.BackColor = MainWindowTheme.Background;
        this.ForeColor = MainWindowTheme.Text;
        this.Font = MainWindowTheme.CreateBodyFont();

        this.BuildUi();
        this.clientManager.ShoppingListsChanged +=
            this.ClientManager_OnShoppingListsChanged;
        this.addStatusTimer.Interval = 2400;
        this.addStatusTimer.Tick += this.AddStatusTimer_OnTick;
        this.RefreshShoppingListPresentation();
    }

    private void BuildUi()
    {
        this.scrollPanel.Dock = DockStyle.Fill;
        this.scrollPanel.AutoScroll = true;
        this.scrollPanel.BackColor = MainWindowTheme.Background;
        this.scrollPanel.Padding = new Padding(ContentInset);

        this.content.AutoSize = true;
        this.content.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        this.content.ColumnCount = 1;
        this.content.RowCount = 0;
        this.content.Dock = DockStyle.Top;
        this.content.BackColor = MainWindowTheme.Background;
        this.content.Margin = Padding.Empty;
        this.content.Padding = Padding.Empty;
        this.content.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 100f));

        this.AddContent(this.CreateHeader());
        this.AddContent(this.CreateOverviewSection());

        var statistics = this.BuildStatisticRows();
        if (statistics.Count != 0)
        {
            this.AddContent(
                this.CreateFieldSection(
                    GalaxyFinderItemSearchIndex.IsEquipment(this.item.Family) ||
                    this.item.Family == GalaxyItemFamily.Ammo
                        ? "Base stats at 100% quality"
                        : "Item details",
                    statistics));
        }

        this.AddEffectSections();
        this.AddDescriptionSections();
        this.AddContent(this.CreateShoppingListsSection());
        this.AddAcquisitionSections();
        this.AddManufacturingSections();

        this.scrollPanel.Controls.Add(this.content);
        this.Controls.Add(this.scrollPanel);
        this.scrollPanel.ClientSizeChanged += (_, _) =>
            this.UpdateContentWidth();
        this.UpdateContentWidth();
    }

    private Control CreateHeader()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = MainWindowTheme.Panel,
            Padding = new Padding(16),
            Margin = new Padding(0, 0, 0, 12),
        };
        panel.ColumnStyles.Add(
            new ColumnStyle(SizeType.Absolute, 94f));
        panel.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 100f));

        panel.ColumnStyles.Add(
            new ColumnStyle(SizeType.Absolute, 206f));

        var icon = new PictureBox
        {
            Size = new Size(72, 72),
            Margin = new Padding(0, 2, 16, 2),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = MainWindowTheme.ElevatedPanel,
            Image = this.resolveIcon(
                this.item.ItemTemplateId,
                new Size(72, 72)),
        };

        var text = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 4,
            Margin = Padding.Empty,
        };
        text.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 100f));

        var title = new Label
        {
            AutoSize = true,
            Text = this.item.Name,
            ForeColor = MainWindowTheme.Accent,
            Font = MainWindowTheme.CreateHeadingFont(16f),
            Margin = new Padding(0, 0, 0, 3),
        };
        var subtitle = new Label
        {
            AutoSize = true,
            Text = this.BuildSubtitle(),
            ForeColor = MainWindowTheme.Text,
            Font = MainWindowTheme.CreateHeadingFont(10f),
            Margin = new Padding(0, 0, 0, 4),
        };
        var manufacturer = new Label
        {
            AutoSize = true,
            Text = string.IsNullOrWhiteSpace(this.item.Manufacturer)
                ? "Manufacturer unknown"
                : string.Concat("Manufactured by ", this.item.Manufacturer.Trim()),
            ForeColor = MainWindowTheme.MutedText,
            Margin = new Padding(0, 0, 0, 3),
        };
        var hasKnownAcquisition = this.HasKnownAcquisition();
        var acquisition = new Label
        {
            AutoSize = true,
            Text = hasKnownAcquisition
                ? this.BuildAcquisitionSummary()
                : "",
            ForeColor = MainWindowTheme.Success,
            Margin = Padding.Empty,
        };

        text.Controls.Add(title, 0, 0);
        text.Controls.Add(subtitle, 0, 1);
        text.Controls.Add(manufacturer, 0, 2);
        text.Controls.Add(acquisition, 0, 3);

        panel.Controls.Add(icon, 0, 0);
        panel.Controls.Add(text, 1, 0);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = new Padding(8, 0, 0, 0),
            Padding = Padding.Empty,
            BackColor = MainWindowTheme.Panel,
        };
        this.addToShoppingListButton.Width = 190;
        this.addToShoppingListButton.Height = 34;
        this.addToShoppingListButton.Margin = Padding.Empty;
        MainWindowTheme.StyleButton(
            this.addToShoppingListButton,
            primary: true);
        this.addToShoppingListButton.Click +=
            this.AddToShoppingListButton_OnClick;
        actions.Controls.Add(this.addToShoppingListButton);

        this.addStatusLabel.AutoSize = false;
        this.addStatusLabel.Width = 190;
        this.addStatusLabel.Height = 24;
        this.addStatusLabel.ForeColor = MainWindowTheme.Success;
        this.addStatusLabel.TextAlign = ContentAlignment.MiddleCenter;
        this.addStatusLabel.Margin = new Padding(0, 3, 0, 0);
        actions.Controls.Add(this.addStatusLabel);

        var backRequested = this.backRequested;
        if (backRequested != null)
        {
            var backButton = new Button
            {
                Text = "← Back",
                Width = 190,
                Height = 34,
                Margin = new Padding(0, 8, 0, 0),
            };
            MainWindowTheme.StyleButton(backButton);
            backButton.Click += (_, _) => backRequested();
            actions.Controls.Add(backButton);
        }

        panel.Controls.Add(actions, 2, 0);
        return panel;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.clientManager.ShoppingListsChanged -=
                this.ClientManager_OnShoppingListsChanged;
            this.addStatusTimer.Stop();
            this.addStatusTimer.Tick -= this.AddStatusTimer_OnTick;
            this.addStatusTimer.Dispose();
            this.addToShoppingListButton.Click -=
                this.AddToShoppingListButton_OnClick;
        }

        base.Dispose(disposing);
    }

    private Control CreateShoppingListsSection()
    {
        this.shoppingMembershipBody.Dock = DockStyle.Top;
        this.shoppingMembershipBody.AutoSize = true;
        this.shoppingMembershipBody.AutoSizeMode =
            AutoSizeMode.GrowAndShrink;
        this.shoppingMembershipBody.FlowDirection =
            FlowDirection.TopDown;
        this.shoppingMembershipBody.WrapContents = false;
        this.shoppingMembershipBody.BackColor =
            MainWindowTheme.Panel;
        this.shoppingMembershipBody.Margin = Padding.Empty;
        this.shoppingMembershipBody.Padding = Padding.Empty;
        return this.CreateSection(
            "Shopping lists",
            this.shoppingMembershipBody);
    }

    private void ClientManager_OnShoppingListsChanged(
        object? sender,
        ShoppingListsChangedEventArgs e)
    {
        if (this.IsDisposed ||
            this.Disposing ||
            !this.IsHandleCreated)
        {
            return;
        }

        this.BeginInvoke(() =>
            this.RefreshShoppingListPresentation());
    }

    private void AddToShoppingListButton_OnClick(
        object? sender,
        EventArgs e)
    {
        var preferred = this.clientManager.GetPreferredShoppingList();
        ShoppingListDocument saved;
        try
        {
            saved = this.clientManager.AddShoppingListRequestedOutput(
                this.item.ItemTemplateId,
                quantity: 1,
                preferred?.ListId);
        }
        catch (OverflowException)
        {
            ThemedMessageDialog.ShowWarning(
                this,
                "Shopping List",
                "That quantity is too large for one shopping-list entry.");
            return;
        }

        this.addStatusLabel.Text = string.Concat(
            this.item.Family == GalaxyItemFamily.Ammo
                ? "Added 1 stack to "
                : "Added 1 to ",
            saved.Name,
            " ✓");
        this.addStatusTimer.Stop();
        this.addStatusTimer.Start();
        this.RefreshShoppingListPresentation();
    }

    private void AddStatusTimer_OnTick(
        object? sender,
        EventArgs e)
    {
        this.addStatusTimer.Stop();
        this.addStatusLabel.Text = "";
    }

    private void RefreshShoppingListPresentation()
    {
        if (this.IsDisposed || this.Disposing)
        {
            return;
        }

        var preferred = this.clientManager.GetPreferredShoppingList();
        this.addToShoppingListButton.Text = string.Concat(
            "Add to ",
            preferred?.Name ?? "Shopping List");

        this.shoppingMembershipBody.SuspendLayout();
        try
        {
            while (this.shoppingMembershipBody.Controls.Count != 0)
            {
                var control = this.shoppingMembershipBody.Controls[0];
                this.shoppingMembershipBody.Controls.RemoveAt(0);
                control.Dispose();
            }

            var memberships = this.clientManager.GetShoppingLists()
                .Select(summary => new
                {
                    Summary = summary,
                    Document = this.clientManager.GetShoppingList(
                        summary.ListId),
                })
                .Where(entry =>
                    entry.Document?.RequestedOutputs.Any(output =>
                        output.ItemTemplateId ==
                        this.item.ItemTemplateId) == true)
                .Select(entry => new
                {
                    entry.Summary,
                    Quantity = entry.Document!.RequestedOutputs
                        .First(output =>
                            output.ItemTemplateId ==
                            this.item.ItemTemplateId)
                        .Quantity,
                })
                .OrderBy(entry =>
                    entry.Summary.Name,
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (memberships.Length == 0)
            {
                this.shoppingMembershipBody.Controls.Add(
                    new Label
                    {
                        AutoSize = true,
                        Text = "Not on any shopping list.",
                        ForeColor = MainWindowTheme.MutedText,
                        Margin = new Padding(0, 2, 0, 4),
                    });
                return;
            }

            foreach (var membership in memberships)
            {
                var row = new TableLayoutPanel
                {
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    ColumnCount = 2,
                    RowCount = 1,
                    Width = 820,
                    Margin = new Padding(0, 0, 0, 5),
                    BackColor = MainWindowTheme.Panel,
                };
                row.ColumnStyles.Add(
                    new ColumnStyle(SizeType.Percent, 100f));
                row.ColumnStyles.Add(
                    new ColumnStyle(SizeType.Absolute, 100f));

                var name = new Label
                {
                    AutoSize = true,
                    Text = membership.Summary.Name,
                    ForeColor = MainWindowTheme.Accent,
                    Cursor = Cursors.Hand,
                    Font = MainWindowTheme.CreateHeadingFont(9.5f),
                    Margin = new Padding(0, 2, 0, 2),
                    Tag = membership.Summary.ListId,
                };
                name.Click += this.ShoppingListMembership_OnClick;
                row.Controls.Add(name, 0, 0);
                row.Controls.Add(
                    new Label
                    {
                        Dock = DockStyle.Fill,
                        Text = this.item.Family ==
                               GalaxyItemFamily.Ammo
                            ? string.Format(
                                CultureInfo.CurrentCulture,
                                "{0:N0} {1}",
                                membership.Quantity,
                                membership.Quantity == 1
                                    ? "stack"
                                    : "stacks")
                            : membership.Quantity.ToString(
                                "N0",
                                CultureInfo.CurrentCulture),
                        ForeColor = MainWindowTheme.Text,
                        TextAlign = ContentAlignment.MiddleRight,
                        Margin = new Padding(0, 2, 0, 2),
                    },
                    1,
                    0);
                this.shoppingMembershipBody.Controls.Add(row);
            }
        }
        finally
        {
            this.shoppingMembershipBody.ResumeLayout(
                performLayout: true);
        }
    }

    private void ShoppingListMembership_OnClick(
        object? sender,
        EventArgs e)
    {
        if (sender is Label { Tag: string listId })
        {
            this.openShoppingList(listId);
        }
    }

    private Control CreateOverviewSection()
    {
        List<ItemFieldRow> rows =
        [
            new("Category", GalaxyFinderItemSearchIndex.GetFamilyDisplayName(
                this.item.Family)),
            new("Type", GalaxyFinderItemFacts.GetTypeText(this.item)),
            new("Tech level", GalaxyFinderItemFacts.GetLevelText(this.item)),
        ];

        if (this.item.MaximumStack > 1)
        {
            rows.Add(
                new ItemFieldRow(
                    "Maximum stack",
                    this.item.MaximumStack.ToString(
                        "N0",
                        CultureInfo.CurrentCulture)));
        }

        var requirements = this.BuildLevelRequirements();
        if (requirements.Length != 0)
        {
            rows.Add(new ItemFieldRow("Level requirements", requirements));
        }

        var restrictions = this.BuildRestrictionText();
        if (restrictions.Length != 0)
        {
            rows.Add(new ItemFieldRow("Restrictions", restrictions));
        }

        return this.CreateFieldSection("Overview", rows);
    }

    private IReadOnlyList<ItemFieldRow> BuildStatisticRows()
    {
        List<ItemFieldRow> rows = [];

        switch (this.item.Family)
        {
            case GalaxyItemFamily.Weapon:
                this.AddAttributeRow(rows, "Ammo", 0x01);
                this.AddAttributeRow(rows, "Damage", 0x05, " units");
                this.AddAttributeRow(rows, "Energy use", 0x09, " units");
                this.AddAttributeRow(rows, "Range", 0x07, " units");
                this.AddAttributeRow(rows, "Targeting range", 0x13, " units");
                this.AddAttributeRow(rows, "Reload time", 0x15, " seconds");
                this.AddAttributeRow(rows, "Rounds fired", 0x16, " rounds");
                this.AddAttributeRow(rows, "Critical chance", 0x25, "%");
                break;

            case GalaxyItemFamily.Engine:
                this.AddAttributeRow(rows, "Signature", 0x1d, " units");
                this.AddAttributeRow(rows, "Thrust", 0x1f, " units");
                this.AddAttributeRow(rows, "Warp", 0x21, " units");
                this.AddAttributeRow(rows, "Warp drain", 0x22, " units/sec");
                break;

            case GalaxyItemFamily.Shield:
                this.AddAttributeRow(rows, "Capacity", 0x18, " units");
                this.AddAttributeRow(rows, "Recharge", 0x1a, " units/sec");
                this.AddAttributeRow(rows, "Shield use", 0x17, " units");
                this.AddAttributeRow(rows, "Shield drain", 0x19, " units/sec");
                break;

            case GalaxyItemFamily.Reactor:
                this.AddAttributeRow(rows, "Maximum power", 0x10, " units");
                this.AddAttributeRow(rows, "Recharge", 0x14, " units/sec");
                break;

            case GalaxyItemFamily.Device:
                this.AddAttributeRow(rows, "Range", 0x07, " units");
                this.AddAttributeRow(rows, "Targeting range", 0x13, " units");
                this.AddAttributeRow(rows, "Energy use", 0x09, " units");
                break;

            case GalaxyItemFamily.Ammo:
                this.AddAttributeRow(rows, "Ammo type", 0x01);
                this.AddAttributeRow(rows, "Damage", 0x05, " units");
                break;
        }

        return rows;
    }

    private void AddEffectSections()
    {
        var activated = this.item.Effects
            .Where(effect =>
                effect.Trigger == GalaxyItemEffectTrigger.Activated)
            .OrderBy(effect => effect.Index)
            .ToArray();
        var equipped = this.item.Effects
            .Where(effect =>
                effect.Trigger == GalaxyItemEffectTrigger.Equipped)
            .OrderBy(effect => effect.Index)
            .ToArray();

        if (activated.Length != 0)
        {
            this.AddContent(
                this.CreateEffectsSection(
                    "Activated effects",
                    activated));
        }

        if (equipped.Length != 0)
        {
            this.AddContent(
                this.CreateEffectsSection(
                    "Equipped effects",
                    equipped));
        }
    }

    private void AddDescriptionSections()
    {
        var description = NormalizeText(this.item.Description);
        if (description.Length != 0)
        {
            this.AddContent(
                this.CreateTextSection("Description", description));
        }

        var notes = this.item.AdditionalText
            .Select(NormalizeText)
            .Where(value => value.Length != 0)
            .Where(value => !string.Equals(
                value,
                description,
                StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (notes.Length != 0)
        {
            this.AddContent(
                this.CreateTextSection(
                    "Catalog notes",
                    string.Join(Environment.NewLine, notes)));
        }
    }

    private void AddAcquisitionSections()
    {
        var vendors = this.item.Sources
            .Where(source => source.Kind == GalaxyItemSourceKind.Vendor)
            .OrderBy(source => source.SourceName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var loot = this.item.Sources
            .Where(source => source.Kind == GalaxyItemSourceKind.MobLoot)
            .OrderBy(source => source.SourceName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var harvesting = this.item.Sources
            .Where(source => source.Kind == GalaxyItemSourceKind.Harvesting)
            .OrderBy(source => source.SourceName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var missions = this.item.Sources
            .Where(source =>
                source.Kind == GalaxyItemSourceKind.MissionReward)
            .OrderBy(
                source => source.SourceName,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (vendors.Length != 0)
        {
            this.AddContent(
                this.CreateVendorSourceSection(vendors));
        }

        if (loot.Length != 0)
        {
            this.AddContent(
                this.CreateSourceSection("Looted from", loot));
        }

        if (harvesting.Length != 0)
        {
            this.AddContent(
                this.CreateSourceSection("Harvested from", harvesting));
        }

        if (missions.Length != 0)
        {
            this.AddContent(
                this.CreateSourceSection("Mission rewards", missions));
        }
    }

    private void AddManufacturingSections()
    {
        var manufacturingRecipes = this.item.ProducedByRecipes
            .Where(recipe => recipe.Kind == GalaxyRecipeKind.Manufacture)
            .OrderBy(recipe => recipe.Identity, StringComparer.Ordinal)
            .ToArray();
        var refiningRecipes = this.item.ProducedByRecipes
            .Where(recipe => recipe.Kind == GalaxyRecipeKind.Refine)
            .OrderBy(recipe => recipe.Identity, StringComparer.Ordinal)
            .ToArray();

        if (manufacturingRecipes.Length != 0)
        {
            this.AddContent(
                this.CreateRecipeIngredientsSection(
                    "Crafted from",
                    manufacturingRecipes));
        }

        if (refiningRecipes.Length != 0)
        {
            this.AddContent(
                this.CreateRecipeIngredientsSection(
                    "Refined from",
                    refiningRecipes));
        }

        var recipeIngredientIds = refiningRecipes
            .SelectMany(recipe => recipe.Ingredients)
            .Select(ingredient => ingredient.ItemTemplateId)
            .ToHashSet();
        var additionalRefiningInputs = this.item.RefinedFrom
            .Select(relationship => relationship.InputItemTemplateId)
            .Where(itemTemplateId =>
                !recipeIngredientIds.Contains(itemTemplateId))
            .Distinct()
            .ToArray();

        if (additionalRefiningInputs.Length != 0)
        {
            this.AddContent(
                this.CreateRelatedItemSection(
                    refiningRecipes.Length == 0
                        ? "Refined from"
                        : "Other refining links",
                    additionalRefiningInputs));
        }

        if (this.item.RefinesTo.Count != 0)
        {
            this.AddContent(
                this.CreateRelatedItemSection(
                    "Refines into",
                    this.item.RefinesTo
                        .Select(relationship =>
                            relationship.OutputItemTemplateId)
                        .Distinct()
                        .ToArray()));
        }

        if (this.item.UsedByRecipes.Count != 0)
        {
            this.AddContent(
                this.CreateRelatedItemSection(
                    "Used to make",
                    this.item.UsedByRecipes
                        .Select(recipe => recipe.OutputItemTemplateId)
                        .Distinct()
                        .ToArray()));
        }

        if (!this.HasKnownAcquisition())
        {
            this.AddContent(
                this.CreateTextSection(
                    "Sources",
                    "No source has been contributed for this item yet.",
                    MainWindowTheme.MutedText));
        }
    }

    private Control CreateRecipeIngredientsSection(
        string heading,
        IReadOnlyList<GalaxyRecipeKnowledge> recipes)
    {
        var body = this.CreateSectionBody();

        foreach (var recipe in recipes)
        {
            foreach (var ingredient in recipe.Ingredients
                         .OrderBy(value => value.ItemTemplateId))
            {
                body.Controls.Add(
                    this.CreateItemLink(
                        ingredient.ItemTemplateId,
                        string.Create(
                            CultureInfo.CurrentCulture,
                            $"{ingredient.Quantity:N0} × ")));
            }

            if (!ReferenceEquals(recipe, recipes[^1]))
            {
                body.Controls.Add(CreateSeparator());
            }
        }

        return this.CreateSection(heading, body);
    }

    private Control CreateRelatedItemSection(
        string heading,
        IReadOnlyList<int> itemTemplateIds)
    {
        var body = this.CreateSectionBody();

        foreach (var itemTemplateId in itemTemplateIds)
        {
            body.Controls.Add(this.CreateItemLink(itemTemplateId));
        }

        return this.CreateSection(heading, body);
    }

    private Control CreateVendorSourceSection(
        IReadOnlyList<GalaxyItemSourceKnowledge> sources)
    {
        var rows = sources
            .SelectMany(this.BuildVendorSourceRows)
            .OrderBy(row => row.HopSortValue)
            .ThenBy(row => row.SystemName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.SectorName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.StationName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.VendorName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var grid = new DataGridView
        {
            Dock = DockStyle.Top,
            Height = Math.Min(
                420,
                36 + rows.Length * 34 + 2),
            Margin = Padding.Empty,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AutoGenerateColumns = false,
            BackgroundColor = MainWindowTheme.Panel,
            BorderStyle = BorderStyle.None,
            CellBorderStyle =
                DataGridViewCellBorderStyle.SingleHorizontal,
            ColumnHeadersBorderStyle =
                DataGridViewHeaderBorderStyle.Single,
            ColumnHeadersHeight = 36,
            ColumnHeadersHeightSizeMode =
                DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            EnableHeadersVisualStyles = false,
            GridColor = MainWindowTheme.Border,
            MultiSelect = false,
            ReadOnly = true,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        };
        grid.RowTemplate.Height = 34;
        grid.RowTemplate.MinimumHeight = 34;
        grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = MainWindowTheme.Panel,
            ForeColor = MainWindowTheme.Text,
            SelectionBackColor = MainWindowTheme.ButtonHover,
            SelectionForeColor = MainWindowTheme.Text,
            Font = MainWindowTheme.CreateBodyFont(),
            Padding = new Padding(5, 0, 5, 0),
        };
        grid.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = MainWindowTheme.ElevatedPanel,
            ForeColor = MainWindowTheme.Text,
            SelectionBackColor = MainWindowTheme.ButtonHover,
            SelectionForeColor = MainWindowTheme.Text,
            Font = MainWindowTheme.CreateBodyFont(),
            Padding = new Padding(5, 0, 5, 0),
        };
        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = MainWindowTheme.Header,
            ForeColor = MainWindowTheme.Accent,
            SelectionBackColor = MainWindowTheme.Header,
            SelectionForeColor = MainWindowTheme.Accent,
            Font = MainWindowTheme.CreateHeadingFont(9f),
            Alignment = DataGridViewContentAlignment.MiddleLeft,
            Padding = new Padding(5, 0, 5, 0),
        };

        grid.Columns.Add(
            CreateVendorTextColumn(
                "System",
                "System",
                15f));
        grid.Columns.Add(
            CreateVendorTextColumn(
                "Sector",
                "Sector",
                20f));
        grid.Columns.Add(
            CreateVendorTextColumn(
                "Station",
                "Station",
                25f));
        grid.Columns.Add(
            CreateVendorTextColumn(
                "Vendor",
                "Vendor name",
                25f));
        grid.Columns.Add(
            new DataGridViewTextBoxColumn
            {
                Name = "Hops",
                HeaderText = "Hops",
                Width = 72,
                MinimumWidth = 62,
                SortMode = DataGridViewColumnSortMode.Automatic,
                ValueType = typeof(int),
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    Alignment = DataGridViewContentAlignment.MiddleCenter,
                },
            });
        grid.Columns.Add(
            new DataGridViewTextBoxColumn
            {
                Name = "Destination",
                HeaderText = "Route",
                Width = 142,
                MinimumWidth = 132,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    Alignment = DataGridViewContentAlignment.MiddleCenter,
                    ForeColor = MainWindowTheme.Accent,
                    SelectionForeColor = MainWindowTheme.Accent,
                },
            });

        foreach (var rowModel in rows)
        {
            var rowIndex = grid.Rows.Add(
                rowModel.SystemName,
                rowModel.SectorName,
                rowModel.StationName,
                rowModel.VendorName,
                rowModel.Hops ?? int.MaxValue,
                rowModel.Route == null
                    ? "Unavailable"
                    : "Set destination");
            var row = grid.Rows[rowIndex];
            row.Tag = rowModel;

            if (rowModel.Route == null)
            {
                row.Cells["Destination"].Style.ForeColor =
                    MainWindowTheme.DisabledText;
                row.Cells["Destination"].Style.SelectionForeColor =
                    MainWindowTheme.DisabledText;
            }
        }

        grid.CellFormatting += (_, e) =>
        {
            if (e.ColumnIndex < 0 ||
                !string.Equals(
                    grid.Columns[e.ColumnIndex].Name,
                    "Hops",
                    StringComparison.Ordinal) ||
                e.Value is not int hops)
            {
                return;
            }

            e.Value = hops switch
            {
                int.MaxValue => "—",
                0 => "Here",
                _ => hops.ToString(
                    CultureInfo.InvariantCulture),
            };
            e.FormattingApplied = true;
        };
        grid.CellClick += (_, e) =>
        {
            if (e.RowIndex < 0 ||
                e.ColumnIndex < 0 ||
                !string.Equals(
                    grid.Columns[e.ColumnIndex].Name,
                    "Destination",
                    StringComparison.Ordinal) ||
                grid.Rows[e.RowIndex].Tag is not
                    VendorSourceRow { Route: { } route })
            {
                return;
            }

            this.setSourceDestination(route);
        };
        grid.CellMouseEnter += (_, e) =>
        {
            grid.Cursor = e.RowIndex >= 0 &&
                          e.ColumnIndex >= 0 &&
                          string.Equals(
                              grid.Columns[e.ColumnIndex].Name,
                              "Destination",
                              StringComparison.Ordinal) &&
                          grid.Rows[e.RowIndex].Tag is
                              VendorSourceRow { Route: not null }
                ? Cursors.Hand
                : Cursors.Default;
        };
        grid.MouseLeave += (_, _) =>
            grid.Cursor = Cursors.Default;

        return this.CreateSection("Vendors", grid);
    }

    private IEnumerable<VendorSourceRow> BuildVendorSourceRows(
        GalaxyItemSourceKnowledge source)
    {
        var routes = this.resolveSourceRoutes(source);

        if (routes.Count == 0)
        {
            yield return new VendorSourceRow(
                SystemName: "—",
                SectorName: FirstNonEmpty(
                    source.SectorName,
                    source.Locations
                        .Select(location => location.SectorName)
                        .FirstOrDefault(value =>
                            !string.IsNullOrWhiteSpace(value))),
                StationName: FirstNonEmpty(
                    source.LocationName,
                    source.Locations
                        .Select(location => location.LocationName)
                        .FirstOrDefault(value =>
                            !string.IsNullOrWhiteSpace(value))),
                VendorName:
                    GalaxyFinderSourcePresentation
                        .GetSourceDisplayName(source),
                Hops: null,
                HopSortValue: int.MaxValue,
                Route: null);
            yield break;
        }

        foreach (var route in routes)
        {
            yield return new VendorSourceRow(
                SystemName: FirstNonEmpty(
                    route.Destination.SystemName,
                    "—"),
                SectorName: FirstNonEmpty(
                    route.Destination.SectorName,
                    source.SectorName,
                    "—"),
                StationName: FirstNonEmpty(
                    route.Destination.TargetName,
                    source.LocationName,
                    "—"),
                VendorName:
                    GalaxyFinderSourcePresentation
                        .GetSourceDisplayName(source),
                Hops: route.Hops,
                HopSortValue: route.HopSortValue,
                Route: route);
        }
    }

    private static DataGridViewTextBoxColumn CreateVendorTextColumn(
        string name,
        string heading,
        float fillWeight)
    {
        return new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = heading,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = fillWeight,
            MinimumWidth = 110,
            SortMode = DataGridViewColumnSortMode.Automatic,
        };
    }

    private Control CreateSourceSection(
        string heading,
        IReadOnlyList<GalaxyItemSourceKnowledge> sources)
    {
        var body = this.CreateSectionBody();

        foreach (var source in sources)
        {
            var routes = this.resolveSourceRoutes(source);

            if (routes.Count == 0)
            {
                body.Controls.Add(
                    this.CreateSourceOccurrencePanel(
                        source,
                        route: null,
                        includeSummary: true));
                continue;
            }

            for (var index = 0; index < routes.Count; index++)
            {
                body.Controls.Add(
                    this.CreateSourceOccurrencePanel(
                        source,
                        routes[index],
                        includeSummary: index == 0));
            }
        }

        return this.CreateSection(heading, body);
    }

    private Control CreateSourceOccurrencePanel(
        GalaxyItemSourceKnowledge source,
        GalaxyFinderResolvedRoute? route,
        bool includeSummary)
    {
        var sourcePanel = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = route == null ? 1 : 2,
            RowCount = 1,
            Width = 820,
            BackColor = MainWindowTheme.Panel,
            Margin = new Padding(0, 0, 0, 8),
            Padding = Padding.Empty,
        };
        sourcePanel.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 100f));

        if (route != null)
        {
            sourcePanel.ColumnStyles.Add(
                new ColumnStyle(SizeType.Absolute, 142f));
        }

        var textPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = MainWindowTheme.Panel,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        textPanel.Controls.Add(
            new Label
            {
                AutoSize = true,
                Text = string.IsNullOrWhiteSpace(source.SourceName)
                    ? "Source"
                    : source.SourceName.Trim(),
                ForeColor = MainWindowTheme.Text,
                Font = MainWindowTheme.CreateHeadingFont(9.5f),
                Margin = new Padding(0, 2, 0, 1),
            });

        var detail = BuildSourceLocationText(source, route);
        if (detail.Length != 0)
        {
            textPanel.Controls.Add(
                new Label
                {
                    AutoSize = true,
                    MaximumSize = new Size(650, 0),
                    Text = detail,
                    ForeColor = MainWindowTheme.MutedText,
                    Margin = new Padding(0, 0, 0, 5),
                });
        }

        if (includeSummary &&
            !string.IsNullOrWhiteSpace(source.Summary))
        {
            textPanel.Controls.Add(
                new Label
                {
                    AutoSize = true,
                    MaximumSize = new Size(650, 0),
                    Text = NormalizeText(source.Summary),
                    ForeColor = MainWindowTheme.Text,
                    Margin = new Padding(0, 0, 0, 5),
                });
        }

        sourcePanel.Controls.Add(textPanel, 0, 0);

        if (route != null)
        {
            var routeButton = new Button
            {
                Text = "Set destination",
                Width = 132,
                Height = 32,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Margin = new Padding(10, 2, 0, 0),
            };
            MainWindowTheme.StyleButton(routeButton);
            routeButton.Click += (_, _) =>
                this.setSourceDestination(route);
            sourcePanel.Controls.Add(routeButton, 1, 0);
        }

        return sourcePanel;
    }

    private Control CreateEffectsSection(
        string heading,
        IReadOnlyList<GalaxyItemEffectKnowledge> effects)
    {
        var body = this.CreateSectionBody();

        foreach (var effect in effects)
        {
            var resolved = ItemEffectTextFormatter.Resolve(
                effect.NameFormat,
                effect.NameValues,
                effect.DescriptionFormat,
                effect.DescriptionValues);
            var name = string.IsNullOrWhiteSpace(resolved.Name)
                ? effect.Identity
                : resolved.Name;

            body.Controls.Add(
                new Label
                {
                    AutoSize = true,
                    Text = name,
                    ForeColor = MainWindowTheme.Success,
                    Font = MainWindowTheme.CreateHeadingFont(9.5f),
                    Margin = new Padding(0, 2, 0, 1),
                });

            if (!string.IsNullOrWhiteSpace(resolved.Description))
            {
                body.Controls.Add(
                    new Label
                    {
                        AutoSize = true,
                        MaximumSize = new Size(820, 0),
                        Text = resolved.Description,
                        ForeColor = MainWindowTheme.Text,
                        Margin = new Padding(0, 0, 0, 7),
                    });
            }
        }

        return this.CreateSection(heading, body);
    }

    private Control CreateFieldSection(
        string heading,
        IReadOnlyList<ItemFieldRow> rows)
    {
        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = rows.Count,
            BackColor = MainWindowTheme.Panel,
            Margin = Padding.Empty,
        };
        body.ColumnStyles.Add(
            new ColumnStyle(SizeType.Absolute, 190f));
        body.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 100f));

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            body.Controls.Add(
                new Label
                {
                    AutoSize = true,
                    Text = row.Label,
                    ForeColor = MainWindowTheme.MutedText,
                    Margin = new Padding(0, 3, 14, 3),
                },
                0,
                index);
            body.Controls.Add(
                new Label
                {
                    AutoSize = true,
                    MaximumSize = new Size(650, 0),
                    Text = row.Value,
                    ForeColor = MainWindowTheme.Text,
                    Margin = new Padding(0, 3, 0, 3),
                },
                1,
                index);
        }

        return this.CreateSection(heading, body);
    }

    private Control CreateTextSection(
        string heading,
        string text,
        Color? color = null)
    {
        var label = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(820, 0),
            Text = text,
            ForeColor = color ?? MainWindowTheme.Text,
            Margin = Padding.Empty,
        };
        return this.CreateSection(heading, label);
    }

    private Control CreateSection(string heading, Control body)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = MainWindowTheme.Panel,
            Padding = new Padding(16, 12, 16, 14),
            Margin = new Padding(0, 0, 0, 12),
        };
        panel.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 100f));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        panel.Controls.Add(
            new Label
            {
                AutoSize = true,
                Text = heading,
                ForeColor = MainWindowTheme.Accent,
                Font = MainWindowTheme.CreateHeadingFont(11f),
                Margin = new Padding(0, 0, 0, 8),
            },
            0,
            0);
        body.Dock = DockStyle.Top;
        panel.Controls.Add(body, 0, 1);
        return panel;
    }

    private FlowLayoutPanel CreateSectionBody()
    {
        return new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = MainWindowTheme.Panel,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
    }

    private Control CreateItemLink(
        int itemTemplateId,
        string prefix = "")
    {
        var name = this.snapshot.ItemsByTemplateId.TryGetValue(
            itemTemplateId,
            out var related)
            ? related.Name
            : string.Create(
                CultureInfo.InvariantCulture,
                $"Item {itemTemplateId}");
        var link = new LinkLabel
        {
            AutoSize = true,
            Text = string.Concat(prefix, name),
            LinkColor = MainWindowTheme.Accent,
            ActiveLinkColor = MainWindowTheme.Text,
            VisitedLinkColor = MainWindowTheme.Accent,
            BackColor = MainWindowTheme.Panel,
            Margin = new Padding(0, 2, 0, 4),
            Cursor = Cursors.Hand,
        };
        link.LinkClicked += (_, _) => this.openItem(itemTemplateId);
        return link;
    }

    private static Control CreateSeparator()
    {
        return new Panel
        {
            Height = 1,
            Width = 760,
            BackColor = MainWindowTheme.Border,
            Margin = new Padding(0, 6, 0, 6),
        };
    }

    private void AddAttributeRow(
        ICollection<ItemFieldRow> rows,
        string label,
        uint itemInfoId,
        string suffix = "")
    {
        var value = GalaxyFinderItemFacts.GetAttributeText(
            this.item,
            itemInfoId,
            suffix);

        if (!string.Equals(value, "—", StringComparison.Ordinal))
        {
            rows.Add(new ItemFieldRow(label, value));
        }
    }

    private string BuildSubtitle()
    {
        var level = this.item.TechLevel > 0
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"Level {this.item.TechLevel} ")
            : "";
        return string.Concat(
            level,
            GalaxyFinderItemFacts.GetTypeText(this.item));
    }

    private string BuildAcquisitionSummary()
    {
        List<string> labels = [];

        if (this.item.Sources.Any(source =>
                source.Kind == GalaxyItemSourceKind.Vendor))
        {
            labels.Add("Vendor");
        }

        if (this.item.Sources.Any(source =>
                source.Kind == GalaxyItemSourceKind.MobLoot))
        {
            labels.Add("Loot");
        }

        if (this.item.ProducedByRecipes.Any(recipe =>
                recipe.Kind == GalaxyRecipeKind.Manufacture))
        {
            labels.Add("Crafted");
        }

        if (this.item.ProducedByRecipes.Any(recipe =>
                recipe.Kind == GalaxyRecipeKind.Refine) ||
            this.item.RefinedFrom.Count != 0)
        {
            labels.Add("Refined");
        }

        if (this.item.Sources.Any(source =>
                source.Kind == GalaxyItemSourceKind.Harvesting))
        {
            labels.Add("Harvested");
        }

        if (this.item.Sources.Any(source =>
                source.Kind == GalaxyItemSourceKind.MissionReward))
        {
            labels.Add("Mission");
        }

        return string.Join(" · ", labels);
    }

    private bool HasKnownAcquisition()
    {
        return this.item.Sources.Count != 0 ||
               this.item.ProducedByRecipes.Count != 0 ||
               this.item.RefinedFrom.Count != 0;
    }

    private string BuildLevelRequirements()
    {
        List<string> values = [];
        AddRequirement(values, "Combat", this.item.RequiredCombatLevel);
        AddRequirement(values, "Explore", this.item.RequiredExploreLevel);
        AddRequirement(values, "Trade", this.item.RequiredTradeLevel);
        AddRequirement(values, "Overall", this.item.RequiredOverallLevel);
        return string.Join(" · ", values);
    }

    private string BuildRestrictionText()
    {
        return ItemTemplateRestrictionEvaluator.FormatRestrictionText(
            this.item.ProfessionRestrictionMask,
            this.item.RaceRestrictionMask,
            this.item.LoreRestriction);
    }

    private static void AddRequirement(
        ICollection<string> values,
        string name,
        int level)
    {
        if (level > 0)
        {
            values.Add(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{name} {level}"));
        }
    }

    private static string BuildSourceLocationText(
        GalaxyItemSourceKnowledge source,
        GalaxyFinderResolvedRoute? route)
    {
        List<string> metadata = [];

        if (source.CombatLevel is > 0)
        {
            metadata.Add(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Combat level {source.CombatLevel.Value}"));
        }

        if (source.TechLevel is > 0)
        {
            metadata.Add(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Tech level {source.TechLevel.Value}"));
        }

        if (source.Quantity is > 0)
        {
            metadata.Add(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Reward quantity {source.Quantity.Value:N0}"));
        }

        List<string> location = [];

        if (route != null)
        {
            if (!string.IsNullOrWhiteSpace(
                    route.Destination.SystemName))
            {
                location.Add(
                    $"System: {route.Destination.SystemName.Trim()}");
            }

            if (!string.IsNullOrWhiteSpace(
                    route.Destination.SectorName))
            {
                location.Add(
                    $"Sector: {route.Destination.SectorName.Trim()}");
            }

            if (!string.IsNullOrWhiteSpace(
                    route.Destination.TargetName))
            {
                var targetLabel = source.Kind is
                    GalaxyItemSourceKind.Vendor or
                    GalaxyItemSourceKind.MissionReward
                        ? "Station"
                        : "Near nav";
                location.Add(
                    $"{targetLabel}: {route.Destination.TargetName.Trim()}");
            }
        }
        else
        {
            var sector = FirstNonEmpty(
                source.SectorName,
                source.Locations
                    .Select(value => value.SectorName)
                    .FirstOrDefault(value =>
                        !string.IsNullOrWhiteSpace(value)));
            if (sector.Length != 0)
            {
                location.Add($"Sector: {sector}");
            }

            if (!string.IsNullOrWhiteSpace(source.LocationName))
            {
                location.Add($"Location: {source.LocationName.Trim()}");
            }
        }

        var metadataText = string.Join(" · ", metadata);
        var locationText = string.Join(" · ", location);

        if (metadataText.Length == 0)
        {
            return locationText;
        }

        return locationText.Length == 0
            ? metadataText
            : string.Concat(metadataText, Environment.NewLine, locationText);
    }

    private void AddContent(Control control)
    {
        var row = this.content.RowCount++;
        this.content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        control.Dock = DockStyle.Top;
        this.content.Controls.Add(control, 0, row);
    }

    private void UpdateContentWidth()
    {
        var width = Math.Max(
            420,
            this.scrollPanel.ClientSize.Width -
            this.scrollPanel.Padding.Horizontal -
            SystemInformation.VerticalScrollBarWidth - 4);
        this.content.Width = width;
    }

    private static string NormalizeText(string? text)
    {
        return string.IsNullOrWhiteSpace(text)
            ? ""
            : text
                .Replace("\\n", Environment.NewLine, StringComparison.Ordinal)
                .Replace("\r\n", Environment.NewLine, StringComparison.Ordinal)
                .Trim();
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value =>
            !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";
    }

    private sealed record VendorSourceRow(
        string SystemName,
        string SectorName,
        string StationName,
        string VendorName,
        int? Hops,
        int HopSortValue,
        GalaxyFinderResolvedRoute? Route);

    private sealed record ItemFieldRow(string Label, string Value);
}
