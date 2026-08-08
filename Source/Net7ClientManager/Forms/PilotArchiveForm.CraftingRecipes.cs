// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using System.Globalization;
using Net7ClientManager.Observations.Models;
using Net7ClientManager.Observations.Observers;
using Net7ClientManager.PilotArchive;
using Net7ClientManager.RecipeMapping;

public sealed partial class PilotArchiveForm
{
    private readonly DataGridView craftingRecipesGrid = CreateGrid();
    private readonly DataGridView craftingRecipeHistoryGrid = CreateGrid();
    private readonly TextBox craftingRecipeDetailsTextBox = new();
    private readonly TextBox craftingRecipeHistoryDetailsTextBox = new();
    private readonly FlowLayoutPanel craftingSkillSummaryPanel = new();
    private readonly Label craftingHelperLabel = new();
    private readonly Label craftingScanStatusLabel = new();
    private readonly Button craftingScanButton = new();
    private readonly FlowLayoutPanel craftingFilterPanel = new();
    private readonly TableLayoutPanel craftingContentPanel = new();
    private readonly Label craftingEmptyLabel = new();
    private readonly HashSet<uint> craftingScansStartedFromArchive = [];
    private readonly Dictionary<long, RecipeMappingHistoryEventRecord>
        craftingRecipeHistoryEventsById = [];

    private uint? craftingObservedCharacterId;
    private CraftingObservationRefreshKey? craftingLastObservationRefreshKey;
    private bool craftingCommandBusy;
    private bool updatingCraftingRecipeSelection;
    private string? craftingSkillFilter;

    public void SelectCraftingRecipes(uint? characterId = null)
    {
        if (characterId.HasValue)
        {
            this.SelectPilot(characterId.Value);
        }

        this.SelectArchiveSection(PilotArchiveSections.CraftingRecipes);
        this.SyncCraftingObservation();

        if (this.selectedCharacterId is { } selected)
        {
            this.RefreshCrafting(selected);
        }
    }

    internal void RefreshCraftingForCharacter(
        uint characterId,
        RecipeMappingPresentation? observedPresentation = null)
    {
        if (this.IsDisposed || this.Disposing)
        {
            return;
        }

        if (this.InvokeRequired)
        {
            try
            {
                this.BeginInvoke(() =>
                    this.RefreshCraftingForCharacter(
                        characterId,
                        observedPresentation));
            }
            catch (InvalidOperationException)
            {
            }

            return;
        }

        if (this.selectedCharacterId != characterId ||
            this.GetSelectedSection() != PilotArchiveSections.CraftingRecipes)
        {
            return;
        }

        if (observedPresentation != null)
        {
            var refreshKey = new CraftingObservationRefreshKey(
                observedPresentation.CharacterId,
                observedPresentation.BaselineStatus,
                observedPresentation.RequiredBuildSkillCount,
                observedPresentation.KnownZeroBuildSkillCount,
                observedPresentation.CompletedCategoryCount,
                observedPresentation.TotalCategoryCount,
                observedPresentation.KnownRecipeCount,
                observedPresentation.ManufacturingPanelActive,
                observedPresentation.ManufacturingReadyForAutomatedScan);

            if (this.craftingLastObservationRefreshKey == refreshKey)
            {
                return;
            }

            this.craftingLastObservationRefreshKey = refreshKey;
        }

        this.RefreshCrafting(characterId);
    }

    private ThemedTabPage CreateCraftingRecipesTab()
    {
        var tab = CreateTabPage("Crafting Recipes");
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            BackColor = MainWindowTheme.Panel,
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            BackColor = MainWindowTheme.Panel,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var timestamp = CreateTimestampLabel();
        this.sectionTimestampLabels[PilotArchiveSections.CraftingRecipes] =
            timestamp;
        timestamp.Dock = DockStyle.Fill;
        layout.Controls.Add(timestamp, 0, 0);

        this.craftingSkillSummaryPanel.Dock = DockStyle.Fill;
        this.craftingSkillSummaryPanel.AutoSize = true;
        this.craftingSkillSummaryPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        this.craftingSkillSummaryPanel.FlowDirection = FlowDirection.TopDown;
        this.craftingSkillSummaryPanel.WrapContents = false;
        this.craftingSkillSummaryPanel.Margin = new Padding(0, 4, 0, 8);
        this.craftingSkillSummaryPanel.BackColor = MainWindowTheme.Panel;
        layout.Controls.Add(this.craftingSkillSummaryPanel, 0, 1);

        var actionRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = MainWindowTheme.Panel,
        };
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        this.craftingHelperLabel.AutoSize = true;
        this.craftingHelperLabel.Dock = DockStyle.Fill;
        this.craftingHelperLabel.ForeColor = MainWindowTheme.MutedText;
        this.craftingHelperLabel.TextAlign = ContentAlignment.MiddleLeft;
        this.craftingHelperLabel.Text =
            "Open a Manufacturing terminal to enable scanning of recipes.";
        actionRow.Controls.Add(this.craftingHelperLabel, 0, 0);

        this.craftingScanButton.Dock = DockStyle.Fill;
        this.craftingScanButton.Margin = new Padding(8, 7, 0, 7);
        this.craftingScanButton.Text = "Scan";
        MainWindowTheme.StyleButton(this.craftingScanButton, primary: true);
        this.craftingScanButton.Click += this.CraftingScanButton_OnClick;
        actionRow.Controls.Add(this.craftingScanButton, 1, 0);
        layout.Controls.Add(actionRow, 0, 2);

        this.craftingScanStatusLabel.Dock = DockStyle.Fill;
        this.craftingScanStatusLabel.ForeColor = MainWindowTheme.MutedText;
        this.craftingScanStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
        layout.Controls.Add(this.craftingScanStatusLabel, 0, 3);

        this.craftingFilterPanel.Dock = DockStyle.Fill;
        this.craftingFilterPanel.AutoSize = true;
        this.craftingFilterPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        this.craftingFilterPanel.FlowDirection = FlowDirection.LeftToRight;
        this.craftingFilterPanel.WrapContents = false;
        this.craftingFilterPanel.AutoScroll = true;
        this.craftingFilterPanel.Margin = Padding.Empty;
        this.craftingFilterPanel.Padding = new Padding(0, 5, 0, 5);
        this.craftingFilterPanel.BackColor = MainWindowTheme.Panel;
        layout.Controls.Add(this.craftingFilterPanel, 0, 4);

        this.craftingContentPanel.Dock = DockStyle.Fill;
        this.craftingContentPanel.ColumnCount = 3;
        this.craftingContentPanel.RowCount = 1;
        this.craftingContentPanel.Margin = Padding.Empty;
        this.craftingContentPanel.Padding = Padding.Empty;
        this.craftingContentPanel.BackColor = MainWindowTheme.Border;
        this.craftingContentPanel.Visible = false;
        this.craftingContentPanel.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 50));
        this.craftingContentPanel.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 28));
        this.craftingContentPanel.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 22));
        this.craftingContentPanel.RowStyles.Add(
            new RowStyle(SizeType.Percent, 100));

        var recipeContent = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 6, 0),
            BackColor = MainWindowTheme.Panel,
        };
        this.craftingRecipesGrid.Dock = DockStyle.Fill;
        recipeContent.Controls.Add(this.craftingRecipesGrid);

        this.craftingEmptyLabel.Dock = DockStyle.Fill;
        this.craftingEmptyLabel.ForeColor = MainWindowTheme.MutedText;
        this.craftingEmptyLabel.TextAlign = ContentAlignment.MiddleCenter;
        this.craftingEmptyLabel.Text = "No mapped recipes.";
        this.craftingEmptyLabel.Visible = false;
        recipeContent.Controls.Add(this.craftingEmptyLabel);
        this.craftingEmptyLabel.BringToFront();
        this.craftingContentPanel.Controls.Add(recipeContent, 0, 0);

        var historyPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 6, 0),
            Padding = Padding.Empty,
            BackColor = MainWindowTheme.Panel,
        };
        var historyHeading = new Label
        {
            Dock = DockStyle.Top,
            Height = 30,
            Text = "Recipe history",
            ForeColor = MainWindowTheme.Text,
            Font = MainWindowTheme.CreateHeadingFont(9.0f),
            TextAlign = ContentAlignment.MiddleLeft,
        };
        this.craftingRecipeHistoryGrid.Dock = DockStyle.Fill;
        historyPanel.Controls.Add(this.craftingRecipeHistoryGrid);
        historyPanel.Controls.Add(historyHeading);
        this.craftingContentPanel.Controls.Add(historyPanel, 1, 0);

        var detailsLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = MainWindowTheme.Panel,
        };
        detailsLayout.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 100));
        detailsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        detailsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        detailsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        detailsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 55));

        detailsLayout.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "Recipe details",
            ForeColor = MainWindowTheme.Text,
            Font = MainWindowTheme.CreateHeadingFont(9.0f),
            TextAlign = ContentAlignment.MiddleLeft,
        }, 0, 0);
        ConfigureCraftingDetailsTextBox(this.craftingRecipeDetailsTextBox);
        this.craftingRecipeDetailsTextBox.Text =
            "Select a mapped recipe to view its details.";
        detailsLayout.Controls.Add(this.craftingRecipeDetailsTextBox, 0, 1);

        detailsLayout.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "History details",
            ForeColor = MainWindowTheme.Text,
            Font = MainWindowTheme.CreateHeadingFont(9.0f),
            TextAlign = ContentAlignment.MiddleLeft,
        }, 0, 2);
        ConfigureCraftingDetailsTextBox(
            this.craftingRecipeHistoryDetailsTextBox);
        this.craftingRecipeHistoryDetailsTextBox.Text =
            "Select a history entry to view its details.";
        detailsLayout.Controls.Add(
            this.craftingRecipeHistoryDetailsTextBox,
            0,
            3);
        this.craftingContentPanel.Controls.Add(detailsLayout, 2, 0);

        layout.Controls.Add(this.craftingContentPanel, 0, 5);

        panel.Controls.Add(layout);
        tab.Controls.Add(panel);
        return tab;
    }

    private void ConfigureCraftingRecipesGrid()
    {
        AddIconColumn(this.craftingRecipesGrid);
        AddColumn(this.craftingRecipesGrid, "Recipe", 310);
        AddColumn(this.craftingRecipesGrid, "Level", 65);
        AddColumn(this.craftingRecipesGrid, "Primary", 135);
        AddColumn(this.craftingRecipesGrid, "Group", 190);
        AddColumn(
            this.craftingRecipesGrid,
            "Category",
            220,
            fill: true);

        this.craftingRecipesGrid.ShowCellToolTips = false;
        this.craftingRecipesGrid.CellMouseEnter +=
            this.CraftingRecipesGrid_OnCellMouseEnter;
        this.craftingRecipesGrid.CellMouseLeave +=
            this.InventoryGrid_OnCellMouseLeave;
        this.craftingRecipesGrid.MouseLeave +=
            this.InventoryGrid_OnMouseLeave;
        this.craftingRecipesGrid.Scroll +=
            this.InventoryGrid_OnScroll;

        AddColumn(this.craftingRecipeHistoryGrid, "Time", 145);
        AddColumn(this.craftingRecipeHistoryGrid, "Event", 120);
        AddColumn(
            this.craftingRecipeHistoryGrid,
            "Result",
            200,
            fill: true);
    }

    private static void ConfigureCraftingDetailsTextBox(TextBox textBox)
    {
        textBox.Dock = DockStyle.Fill;
        textBox.Multiline = true;
        textBox.ReadOnly = true;
        textBox.ScrollBars = ScrollBars.Vertical;
        textBox.WordWrap = true;
        textBox.BackColor = MainWindowTheme.ElevatedPanel;
        textBox.ForeColor = MainWindowTheme.Text;
        textBox.BorderStyle = BorderStyle.FixedSingle;
        textBox.Font = MainWindowTheme.CreateBodyFont();
    }

    private void UnconfigureCraftingRecipesToolTips()
    {
        this.craftingRecipesGrid.CellMouseEnter -=
            this.CraftingRecipesGrid_OnCellMouseEnter;
        this.craftingRecipesGrid.CellMouseLeave -=
            this.InventoryGrid_OnCellMouseLeave;
        this.craftingRecipesGrid.MouseLeave -=
            this.InventoryGrid_OnMouseLeave;
        this.craftingRecipesGrid.Scroll -=
            this.InventoryGrid_OnScroll;
        this.itemToolTip.Remove(this.craftingRecipesGrid);
    }

    private void RefreshCrafting(uint characterId)
    {
        if (this.GetSelectedSection() != PilotArchiveSections.CraftingRecipes)
        {
            return;
        }

        var presentation =
            this.clientManager.GetPilotArchiveCraftingPresentation(characterId);
        var scanState =
            this.clientManager.GetPilotArchiveCraftingScanState(characterId);
        var buildSkills = this.GetArchivedCraftingBuildSkills(characterId);
        var learnedBuildSkills = buildSkills
            .Where(skill => skill.CurrentRank > 0)
            .Select(skill => skill.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (this.craftingSkillFilter != null &&
            !learnedBuildSkills.Contains(this.craftingSkillFilter))
        {
            this.craftingSkillFilter = null;
        }

        this.PopulateCraftingSkillSummary(
            presentation,
            scanState,
            buildSkills);
        this.PopulateCraftingFilters(presentation, buildSkills);
        this.PopulateCraftingRecipes(presentation);

        this.craftingScanButton.Text = scanState.IsRunning ? "Cancel" : "Scan";
        this.craftingScanButton.Enabled = scanState.IsRunning ||
            (!this.craftingCommandBusy &&
             learnedBuildSkills.Count > 0 &&
             presentation.ManufacturingPanelActive);

        this.craftingScanStatusLabel.Text = this.ResolveCraftingScanStatus(
            presentation,
            scanState);
        this.SetCraftingTimestamp(
            presentation.KnownRecipeCount == 1
                ? "1 mapped recipe"
                : string.Create(
                    CultureInfo.CurrentCulture,
                    $"{presentation.KnownRecipeCount:N0} mapped recipes"));
    }

    private IReadOnlyList<PilotArchiveSkill> GetArchivedCraftingBuildSkills(
        uint characterId)
    {
        var details = this.clientManager.PilotArchive.GetPilot(characterId);
        if (details == null)
        {
            return [];
        }

        return details.Skills
            .Where(skill =>
                skill.MaximumRank > 0 &&
                RecipeMappingBuildSkillCatalog.SkillNames.Contains(
                    skill.Name,
                    StringComparer.OrdinalIgnoreCase))
            .OrderBy(skill => skill.Index)
            .ToArray();
    }

    private void PopulateCraftingSkillSummary(
        RecipeMappingPresentation presentation,
        CraftingBaselineScanState scanState,
        IReadOnlyList<PilotArchiveSkill> buildSkills)
    {
        this.craftingSkillSummaryPanel.SuspendLayout();
        try
        {
            this.craftingSkillSummaryPanel.Controls.Clear();

            if (buildSkills.Count == 0)
            {
                var none = new Label
                {
                    AutoSize = true,
                    ForeColor = MainWindowTheme.MutedText,
                    Text = "No Build skills are available to this pilot.",
                    Margin = new Padding(0, 2, 0, 6),
                };
                this.craftingSkillSummaryPanel.Controls.Add(none);
                return;
            }

            foreach (var skill in buildSkills)
            {
                var applicableCategories = presentation.Categories
                    .Where(category =>
                        RecipeMappingBuildSkillCatalog
                            .GetApplicableSkillNames(category.CategoryId)
                            .Contains(
                                skill.Name,
                                StringComparer.OrdinalIgnoreCase))
                    .ToArray();
                var isLearned = skill.CurrentRank > 0;
                var isScanned = isLearned &&
                    applicableCategories.Length > 0 &&
                    applicableCategories.All(category => category.IsVisited);
                var recipeCount = presentation.Recipes.Count(recipe =>
                    recipe.ApplicableBuildSkillNames.Contains(
                        skill.Name,
                        StringComparer.OrdinalIgnoreCase));
                var isScanning = scanState.IsRunning &&
                    scanState.ActiveCategoryId is { } activeCategoryId &&
                    RecipeMappingBuildSkillCatalog
                        .GetApplicableSkillNames(activeCategoryId)
                        .Contains(
                            skill.Name,
                            StringComparer.OrdinalIgnoreCase);

                var row = new TableLayoutPanel
                {
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    ColumnCount = 3,
                    RowCount = 1,
                    Margin = new Padding(0, 0, 0, 3),
                    BackColor = MainWindowTheme.Panel,
                };
                row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
                row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58));
                row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));

                var name = new Label
                {
                    Dock = DockStyle.Fill,
                    AutoSize = true,
                    ForeColor = MainWindowTheme.Text,
                    Text = skill.Name,
                    TextAlign = ContentAlignment.MiddleLeft,
                };
                var rank = new Label
                {
                    Dock = DockStyle.Fill,
                    AutoSize = true,
                    ForeColor = MainWindowTheme.MutedText,
                    Text = string.Create(
                        CultureInfo.InvariantCulture,
                        $"{skill.CurrentRank}/{skill.MaximumRank}"),
                    TextAlign = ContentAlignment.MiddleLeft,
                };
                var status = new Label
                {
                    Dock = DockStyle.Fill,
                    AutoSize = true,
                    ForeColor = isScanning
                        ? MainWindowTheme.Accent
                        : MainWindowTheme.MutedText,
                    Text = !isLearned
                        ? "Skill not learned"
                        : isScanning
                            ? "Scanning..."
                            : !isScanned
                                ? "Recipes not scanned"
                                : recipeCount == 1
                                    ? "1 recipe scanned"
                                    : string.Create(
                                        CultureInfo.CurrentCulture,
                                        $"{recipeCount:N0} recipes scanned"),
                    TextAlign = ContentAlignment.MiddleLeft,
                };

                row.Controls.Add(name, 0, 0);
                row.Controls.Add(rank, 1, 0);
                row.Controls.Add(status, 2, 0);
                this.craftingSkillSummaryPanel.Controls.Add(row);
            }
        }
        finally
        {
            this.craftingSkillSummaryPanel.ResumeLayout();
        }
    }

    private void PopulateCraftingFilters(
        RecipeMappingPresentation presentation,
        IReadOnlyList<PilotArchiveSkill> buildSkills)
    {
        this.craftingFilterPanel.SuspendLayout();
        try
        {
            this.craftingFilterPanel.Controls.Clear();

            if (presentation.Recipes.Count == 0)
            {
                this.craftingSkillFilter = null;
                this.craftingFilterPanel.Visible = false;
                return;
            }

            this.craftingFilterPanel.Visible = true;
            var learned = buildSkills
                .Where(skill => skill.CurrentRank > 0)
                .Select(skill => skill.Name)
                .ToArray();

            this.craftingFilterPanel.Controls.Add(
                this.CreateCraftingFilterButton("All", filter: null));

            foreach (var skillName in learned)
            {
                this.craftingFilterPanel.Controls.Add(
                    this.CreateCraftingFilterButton(skillName, skillName));
            }
        }
        finally
        {
            this.craftingFilterPanel.ResumeLayout();
        }
    }

    private Button CreateCraftingFilterButton(
        string text,
        string? filter)
    {
        var selected = string.Equals(
            this.craftingSkillFilter,
            filter,
            StringComparison.OrdinalIgnoreCase);
        var button = new Button
        {
            AutoSize = true,
            Height = 30,
            MinimumSize = new Size(72, 30),
            Text = text,
            Tag = filter,
            Margin = new Padding(0, 0, 6, 0),
        };
        MainWindowTheme.StyleButton(button, primary: selected);
        button.Click += this.CraftingFilterButton_OnClick;
        return button;
    }

    private void CraftingFilterButton_OnClick(object? sender, EventArgs e)
    {
        this.craftingSkillFilter = (sender as Button)?.Tag as string;

        if (this.selectedCharacterId is { } characterId)
        {
            this.RefreshCrafting(characterId);
        }
    }

    private void PopulateCraftingRecipes(
        RecipeMappingPresentation presentation)
    {
        var selectedItemTemplateId = this.GetSelectedCraftingRecipe()?.ItemTemplateId;
        this.updatingCraftingRecipeSelection = true;

        try
        {
            this.itemToolTip.Remove(this.craftingRecipesGrid);
            this.craftingRecipesGrid.Rows.Clear();
            var iconOutputDirectory =
                this.clientManager.LocateGameOutputDirectory();
            IReadOnlyList<RecipeMappingRecipeRow> recipes =
                this.craftingSkillFilter == null
                    ? presentation.Recipes
                    : presentation.Recipes
                        .Where(recipe =>
                            recipe.ApplicableBuildSkillNames.Contains(
                                this.craftingSkillFilter,
                                StringComparer.OrdinalIgnoreCase))
                        .ToArray();

            foreach (var recipe in recipes)
            {
                var rowIndex = this.craftingRecipesGrid.Rows.Add(
                    this.clientManager.GetPilotArchiveItemIcon(
                        iconOutputDirectory,
                        recipe.ItemTemplateId,
                        ArchiveIconSize),
                    recipe.Name,
                    recipe.TechLevel,
                    recipe.PrimaryCategory,
                    recipe.SecondaryCategory,
                    recipe.Category);
                var row = this.craftingRecipesGrid.Rows[rowIndex];
                row.Tag = recipe;
                var context = new CraftingRecipeItemCellContext(
                    recipe,
                    iconOutputDirectory);
                row.Cells[0].Tag = context;
                row.Cells[1].Tag = context;
            }

            this.ApplyStoredGridSort(this.craftingRecipesGrid);
            var hasMappedRecipes = presentation.Recipes.Count != 0;
            this.craftingContentPanel.Visible = hasMappedRecipes;
            this.craftingRecipesGrid.Visible =
                hasMappedRecipes && recipes.Count != 0;
            this.craftingEmptyLabel.Visible =
                hasMappedRecipes && recipes.Count == 0;
            this.craftingEmptyLabel.Text = string.Concat(
                "No mapped recipes for ",
                this.craftingSkillFilter,
                ".");

            this.craftingRecipesGrid.ClearSelection();
            var selectedRow = this.craftingRecipesGrid.Rows
                .Cast<DataGridViewRow>()
                .FirstOrDefault(row =>
                    row.Tag is RecipeMappingRecipeRow recipe &&
                    recipe.ItemTemplateId == selectedItemTemplateId)
                ?? this.craftingRecipesGrid.Rows
                    .Cast<DataGridViewRow>()
                    .FirstOrDefault();
            if (selectedRow != null)
            {
                selectedRow.Selected = true;
                this.craftingRecipesGrid.CurrentCell = selectedRow.Cells[1];
            }
        }
        finally
        {
            this.updatingCraftingRecipeSelection = false;
        }

        this.UpdateCraftingRecipeDetails();
    }

    private RecipeMappingRecipeRow? GetSelectedCraftingRecipe()
    {
        return this.craftingRecipesGrid.SelectedRows
            .Cast<DataGridViewRow>()
            .Select(row => row.Tag)
            .OfType<RecipeMappingRecipeRow>()
            .FirstOrDefault();
    }

    private void CraftingRecipesGrid_OnSelectionChanged(
        object? sender,
        EventArgs e)
    {
        if (!this.updatingCraftingRecipeSelection)
        {
            this.UpdateCraftingRecipeDetails();
        }
    }

    private void UpdateCraftingRecipeDetails()
    {
        var recipe = this.GetSelectedCraftingRecipe();
        if (recipe == null || this.selectedCharacterId is not { } characterId)
        {
            this.craftingRecipeDetailsTextBox.Text =
                "Select a mapped recipe to view its details.";
            this.ClearCraftingRecipeHistory();
            return;
        }

        var details = this.clientManager.GetPilotArchiveCraftingRecipeDetails(
            characterId,
            recipe.ItemTemplateId);
        this.craftingRecipeDetailsTextBox.Text =
            BuildCraftingRecipeDetailsText(recipe, details);
        this.PopulateCraftingRecipeHistory(details.History);
    }

    private static string BuildCraftingRecipeDetailsText(
        RecipeMappingRecipeRow recipe,
        RecipeMappingRecipeDetailsPresentation details)
    {
        List<string> lines =
        [
            recipe.Name,
            recipe.TechLevel.HasValue
                ? string.Create(
                    CultureInfo.CurrentCulture,
                    $"Level {recipe.TechLevel.Value}")
                : "Level unknown",
        ];

        var categoryParts = new[]
            {
                recipe.PrimaryCategory,
                recipe.SecondaryCategory,
                recipe.Category,
            }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (categoryParts.Length > 0)
        {
            lines.Add(string.Concat("Category: ", string.Join(" · ", categoryParts)));
        }

        lines.Add("");
        lines.Add("Recipe knowledge");
        if (details.FirstObservedAtUtc.HasValue)
        {
            lines.Add(string.Concat(
                "First recorded: ",
                FormatObservedAt(details.FirstObservedAtUtc.Value)));
        }

        lines.Add(string.Concat(
            "First seen via: ",
            details.AcquisitionSource switch
            {
                "scan" => "Recipe scan",
                "analyze" => "Analyze",
                "observed" => "Existing observation",
                _ => "Unknown",
            }));

        lines.Add("");
        lines.Add(details.ComponentsSource switch
        {
            "observed" => "Required components · observed in game",
            "forge" => "Required components · Forge catalogue",
            _ => "Required components",
        });
        if (details.Components.Count == 0)
        {
            lines.Add("Not observed yet.");
            lines.Add(
                "Select this recipe in the Manufacturing terminal to capture its component list.");
        }
        else
        {
            lines.AddRange(details.Components.Select(component => string.Concat(
                component.Quantity.ToString(CultureInfo.CurrentCulture),
                " × ",
                component.Name)));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private void PopulateCraftingRecipeHistory(
        IReadOnlyList<RecipeMappingHistoryEventRecord> history)
    {
        var selectedEventId = this.craftingRecipeHistoryGrid.SelectedRows
            .Cast<DataGridViewRow>()
            .Select(row => row.Tag)
            .OfType<long>()
            .FirstOrDefault();

        this.updatingCraftingRecipeSelection = true;
        try
        {
            this.craftingRecipeHistoryGrid.Rows.Clear();
            this.craftingRecipeHistoryEventsById.Clear();

            foreach (var historyEvent in history)
            {
                this.craftingRecipeHistoryEventsById[historyEvent.EventId] =
                    historyEvent;
                var row = this.craftingRecipeHistoryGrid.Rows[
                    this.craftingRecipeHistoryGrid.Rows.Add(
                        FormatObservedAt(historyEvent.ObservedAtUtc),
                        GetCraftingHistoryEventName(historyEvent.Kind),
                        GetCraftingHistoryResult(historyEvent))];
                row.Tag = historyEvent.EventId;
                row.DefaultCellStyle.ForeColor =
                    GetCraftingHistoryEventColor(historyEvent.Kind);
            }

            this.craftingRecipeHistoryGrid.ClearSelection();
            var selectedRow = this.craftingRecipeHistoryGrid.Rows
                .Cast<DataGridViewRow>()
                .FirstOrDefault(row => row.Tag is long eventId &&
                    eventId == selectedEventId)
                ?? this.craftingRecipeHistoryGrid.Rows
                    .Cast<DataGridViewRow>()
                    .FirstOrDefault();
            if (selectedRow != null)
            {
                selectedRow.Selected = true;
                this.craftingRecipeHistoryGrid.CurrentCell = selectedRow.Cells[0];
            }
        }
        finally
        {
            this.updatingCraftingRecipeSelection = false;
        }

        this.UpdateCraftingRecipeHistoryDetails();
    }

    private static string GetCraftingHistoryEventName(
        RecipeMappingHistoryEventKind kind)
    {
        return kind switch
        {
            RecipeMappingHistoryEventKind.RecipeLearnedByScan =>
                "Recipe recorded",
            RecipeMappingHistoryEventKind.AnalyzeFailed or
                RecipeMappingHistoryEventKind.AnalyzeFailedDamaged or
                RecipeMappingHistoryEventKind.AnalyzeSucceeded or
                RecipeMappingHistoryEventKind.AnalyzeCriticalSucceeded =>
                "Analyze",
            RecipeMappingHistoryEventKind.DismantleFailed or
                RecipeMappingHistoryEventKind.DismantleFailedDamaged or
                RecipeMappingHistoryEventKind.DismantleSucceeded or
                RecipeMappingHistoryEventKind.DismantleCriticalSucceeded =>
                "Dismantle",
            RecipeMappingHistoryEventKind.ManufactureFailed or
                RecipeMappingHistoryEventKind.ManufactureFailedDamaged or
                RecipeMappingHistoryEventKind.ManufactureSucceeded or
                RecipeMappingHistoryEventKind.ManufactureCriticalSucceeded =>
                "Manufacture",
            _ => "Crafting",
        };
    }

    private static string GetCraftingHistoryResult(
        RecipeMappingHistoryEventRecord historyEvent)
    {
        var result = historyEvent.Kind switch
        {
            RecipeMappingHistoryEventKind.RecipeLearnedByScan => "Mapped recipe discovered",
            RecipeMappingHistoryEventKind.AnalyzeFailed or
                RecipeMappingHistoryEventKind.DismantleFailed or
                RecipeMappingHistoryEventKind.ManufactureFailed => "Failed",
            RecipeMappingHistoryEventKind.AnalyzeFailedDamaged or
                RecipeMappingHistoryEventKind.DismantleFailedDamaged or
                RecipeMappingHistoryEventKind.ManufactureFailedDamaged =>
                "Failed · item damaged",
            RecipeMappingHistoryEventKind.AnalyzeSucceeded =>
                "Success · recipe mapped",
            RecipeMappingHistoryEventKind.AnalyzeCriticalSucceeded =>
                "Critical success · recipe mapped",
            RecipeMappingHistoryEventKind.DismantleSucceeded or
                RecipeMappingHistoryEventKind.ManufactureSucceeded => "Success",
            RecipeMappingHistoryEventKind.DismantleCriticalSucceeded or
                RecipeMappingHistoryEventKind.ManufactureCriticalSucceeded =>
                "Critical success",
            _ => "Completed",
        };

        if (historyEvent.OutcomeQualityPercent.HasValue &&
            (historyEvent.Kind is
                RecipeMappingHistoryEventKind.ManufactureSucceeded or
                RecipeMappingHistoryEventKind.ManufactureCriticalSucceeded))
        {
            result = string.Create(
                CultureInfo.CurrentCulture,
                $"{result} · {historyEvent.OutcomeQualityPercent.Value:0.#}% quality");
        }

        return result;
    }

    private static Color GetCraftingHistoryEventColor(
        RecipeMappingHistoryEventKind kind)
    {
        return kind switch
        {
            RecipeMappingHistoryEventKind.RecipeLearnedByScan =>
                MainWindowTheme.Accent,
            RecipeMappingHistoryEventKind.AnalyzeFailed or
                RecipeMappingHistoryEventKind.AnalyzeFailedDamaged or
                RecipeMappingHistoryEventKind.DismantleFailed or
                RecipeMappingHistoryEventKind.DismantleFailedDamaged or
                RecipeMappingHistoryEventKind.ManufactureFailed or
                RecipeMappingHistoryEventKind.ManufactureFailedDamaged =>
                MainWindowTheme.Danger,
            RecipeMappingHistoryEventKind.AnalyzeSucceeded or
                RecipeMappingHistoryEventKind.AnalyzeCriticalSucceeded or
                RecipeMappingHistoryEventKind.DismantleSucceeded or
                RecipeMappingHistoryEventKind.DismantleCriticalSucceeded or
                RecipeMappingHistoryEventKind.ManufactureSucceeded or
                RecipeMappingHistoryEventKind.ManufactureCriticalSucceeded =>
                MainWindowTheme.Success,
            _ => MainWindowTheme.Text,
        };
    }

    private void CraftingRecipeHistoryGrid_OnSelectionChanged(
        object? sender,
        EventArgs e)
    {
        if (!this.updatingCraftingRecipeSelection)
        {
            this.UpdateCraftingRecipeHistoryDetails();
        }
    }

    private void UpdateCraftingRecipeHistoryDetails()
    {
        var eventId = this.craftingRecipeHistoryGrid.SelectedRows
            .Cast<DataGridViewRow>()
            .Select(row => row.Tag)
            .OfType<long>()
            .FirstOrDefault();
        this.craftingRecipeHistoryDetailsTextBox.Text =
            eventId > 0 &&
            this.craftingRecipeHistoryEventsById.TryGetValue(
                eventId,
                out var historyEvent)
                ? BuildCraftingRecipeHistoryDetails(historyEvent)
                : "Select a history entry to view its details.";
    }

    private static string BuildCraftingRecipeHistoryDetails(
        RecipeMappingHistoryEventRecord historyEvent)
    {
        List<string> lines =
        [
            GetCraftingHistoryEventName(historyEvent.Kind),
            FormatObservedAt(historyEvent.ObservedAtUtc),
            "",
            string.Concat("Result: ", GetCraftingHistoryResult(historyEvent)),
        ];

        if (historyEvent.Kind == RecipeMappingHistoryEventKind.RecipeLearnedByScan)
        {
            lines.Add("Source: Manufacturing recipe scan");
        }

        if (historyEvent.Kind is
            RecipeMappingHistoryEventKind.AnalyzeSucceeded or
            RecipeMappingHistoryEventKind.AnalyzeCriticalSucceeded)
        {
            lines.Add("Recipe mapped: Yes");
        }

        if (historyEvent.CreditsSpent.HasValue)
        {
            lines.Add(string.Create(
                CultureInfo.CurrentCulture,
                $"Cost: {historyEvent.CreditsSpent.Value:N0} credits"));
        }

        if (historyEvent.SuccessProbabilityPercent.HasValue)
        {
            lines.Add(string.Create(
                CultureInfo.CurrentCulture,
                $"Success chance: {historyEvent.SuccessProbabilityPercent.Value:0.#}%"));
        }

        if (historyEvent.CriticalSuccessProbabilityPercent.HasValue)
        {
            lines.Add(string.Create(
                CultureInfo.CurrentCulture,
                $"Critical chance: {historyEvent.CriticalSuccessProbabilityPercent.Value:0.#}%"));
        }

        if (historyEvent.OutputQuantity > 0)
        {
            lines.Add(string.Create(
                CultureInfo.CurrentCulture,
                $"Output quantity: {historyEvent.OutputQuantity:N0}"));
        }

        if (historyEvent.OutcomeQualityPercent.HasValue)
        {
            lines.Add(string.Create(
                CultureInfo.CurrentCulture,
                $"Output quality: {historyEvent.OutcomeQualityPercent.Value:0.#}%"));
        }

        if (historyEvent.ResultItems.Count > 0)
        {
            lines.Add("");
            lines.Add("Components recovered");
            foreach (var item in historyEvent.ResultItems)
            {
                var name = ClientItemTemplateNameResolver.GetKnownName(
                    item.ItemTemplateId) ??
                    string.Concat("Item ", item.ItemTemplateId);
                lines.Add(string.Concat(
                    item.Quantity.ToString(CultureInfo.CurrentCulture),
                    " × ",
                    name,
                    item.QualityPercent.HasValue
                        ? string.Create(
                            CultureInfo.CurrentCulture,
                            $" ({item.QualityPercent.Value:0.#}% quality)")
                        : ""));
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    private void ClearCraftingRecipeHistory()
    {
        this.craftingRecipeHistoryGrid.Rows.Clear();
        this.craftingRecipeHistoryEventsById.Clear();
        this.craftingRecipeHistoryDetailsTextBox.Text =
            "Select a history entry to view its details.";
    }

    private void CraftingRecipesGrid_OnCellMouseEnter(
        object? sender,
        DataGridViewCellEventArgs e)
    {
        if (sender is not DataGridView grid ||
            e.RowIndex < 0 ||
            e.ColumnIndex is < 0 or > 1 ||
            grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Tag is not
                CraftingRecipeItemCellContext context ||
            context.Recipe.ItemTemplateId <= 0)
        {
            if (sender is DataGridView invalidGrid)
            {
                this.itemToolTip.Remove(invalidGrid);
            }

            return;
        }

        ClientRuntimeItemTemplateObservation? template =
            this.clientManager.PilotArchive.GetItemTemplate(
                context.Recipe.ItemTemplateId);

        if (template == null &&
            ClientItemTemplateCatalog.TryGetRuntimeObservation(
                context.Recipe.ItemTemplateId,
                out var staticTemplate))
        {
            template = staticTemplate;
        }

        var icon = this.clientManager.GetPilotArchiveItemIcon(
            context.IconOutputDirectory,
            context.Recipe.ItemTemplateId,
            new Size(36, 36));
        var recipeMapping = this.clientManager
            .ResolvePilotArchiveRecipeMappingItemPresentation(
                context.Recipe.ItemTemplateId);
        var content = PilotArchiveItemToolTipBuilder.BuildCatalogItem(
            context.Recipe.ItemTemplateId,
            context.Recipe.Name,
            template,
            icon,
            recipeMapping);

        this.itemToolTip.SetHoveredToolTip(
            grid,
            content,
            SystemInformation.MouseHoverTime);
    }

    private string ResolveCraftingScanStatus(
        RecipeMappingPresentation presentation,
        CraftingBaselineScanState scanState)
    {
        if (scanState.IsRunning)
        {
            if (scanState.ActiveCategoryId is { } activeCategoryId)
            {
                var active = presentation.Categories.FirstOrDefault(category =>
                    category.CategoryId == activeCategoryId);
                if (active != null)
                {
                    return string.Concat("Scanning ", active.DisplayName, "...");
                }
            }

            return "Scanning recipes...";
        }

        return scanState.IsPaused
            ? scanState.Status
            : "";
    }

    private async void CraftingScanButton_OnClick(
        object? sender,
        EventArgs e)
    {
        if (this.selectedCharacterId is not { } characterId)
        {
            return;
        }

        var state =
            this.clientManager.GetPilotArchiveCraftingScanState(characterId);
        if (state.IsRunning)
        {
            this.clientManager.CancelPilotArchiveCraftingScan(characterId);
            this.RefreshCrafting(characterId);
            return;
        }

        if (this.craftingCommandBusy)
        {
            return;
        }

        this.craftingCommandBusy = true;
        this.craftingScansStartedFromArchive.Add(characterId);
        this.RefreshCrafting(characterId);

        try
        {
            await this.clientManager
                .StartPilotArchiveCraftingScanAsync(characterId)
                .ConfigureAwait(true);
        }
        finally
        {
            this.craftingCommandBusy = false;
            this.craftingScansStartedFromArchive.Remove(characterId);

            if (!this.IsDisposed && !this.Disposing)
            {
                this.SyncCraftingObservation();

                if (this.selectedCharacterId == characterId)
                {
                    this.RefreshCrafting(characterId);
                }
            }
        }
    }

    private void SyncCraftingObservation()
    {
        var desired =
            this.GetSelectedSection() == PilotArchiveSections.CraftingRecipes
                ? this.selectedCharacterId
                : null;

        if (this.craftingObservedCharacterId == desired)
        {
            if (desired is { } current)
            {
                this.clientManager.SetPilotArchiveCraftingObservation(
                    current,
                    enabled: true);
            }

            return;
        }

        if (this.craftingObservedCharacterId is { } previous)
        {
            if (previous != desired &&
                this.craftingScansStartedFromArchive.Contains(previous))
            {
                this.clientManager.CancelPilotArchiveCraftingScan(previous);
            }

            this.clientManager.SetPilotArchiveCraftingObservation(
                previous,
                enabled: false);
        }

        this.craftingObservedCharacterId = desired;

        if (desired is { } current2)
        {
            this.clientManager.SetPilotArchiveCraftingObservation(
                current2,
                enabled: true);
        }
    }

    private void StopCraftingArchiveObservation(bool cancelRunningScans)
    {
        if (cancelRunningScans)
        {
            foreach (var characterId in
                     this.craftingScansStartedFromArchive.ToArray())
            {
                this.clientManager.CancelPilotArchiveCraftingScan(
                    characterId);
            }
        }

        if (this.craftingObservedCharacterId is { } observed)
        {
            this.clientManager.SetPilotArchiveCraftingObservation(
                observed,
                enabled: false);
            this.craftingObservedCharacterId = null;
        }
    }

    private void ClearCrafting()
    {
        this.craftingLastObservationRefreshKey = null;
        this.craftingSkillFilter = null;
        this.craftingSkillSummaryPanel.Controls.Clear();
        this.craftingFilterPanel.Controls.Clear();
        this.craftingFilterPanel.Visible = false;
        this.itemToolTip.Remove(this.craftingRecipesGrid);
        this.craftingRecipesGrid.Rows.Clear();
        this.craftingRecipeDetailsTextBox.Text =
            "Select a mapped recipe to view its details.";
        this.ClearCraftingRecipeHistory();
        this.craftingScanStatusLabel.Text = "";
        this.craftingScanButton.Text = "Scan";
        this.craftingScanButton.Enabled = false;
        this.craftingRecipesGrid.Visible = false;
        this.craftingEmptyLabel.Text = "Select an archived pilot.";
        this.craftingEmptyLabel.Visible = true;
        this.SetCraftingTimestamp("");
        this.SyncCraftingObservation();
    }

    private sealed record CraftingRecipeItemCellContext(
        RecipeMappingRecipeRow Recipe,
        string? IconOutputDirectory);

    private readonly record struct CraftingObservationRefreshKey(
        uint CharacterId,
        RecipeMappingBaselineStatus BaselineStatus,
        int RequiredBuildSkillCount,
        int KnownZeroBuildSkillCount,
        int CompletedCategoryCount,
        int TotalCategoryCount,
        int KnownRecipeCount,
        bool ManufacturingPanelActive,
        bool ManufacturingReadyForAutomatedScan);

    private void SetCraftingTimestamp(string text)
    {
        if (this.sectionTimestampLabels.TryGetValue(
                PilotArchiveSections.CraftingRecipes,
                out var label))
        {
            label.Text = text;
        }
    }
}
