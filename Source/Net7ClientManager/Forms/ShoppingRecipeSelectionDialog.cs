// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using System.Globalization;
using Net7ClientManager.Core;
using Net7ClientManager.GalaxyKnowledge;

internal sealed class ShoppingRecipeSelectionDialog : ThemedForm
{
    private readonly GalaxyKnowledgeSnapshot snapshot;
    private readonly GalaxyItemKnowledge item;
    private readonly ListBox recipeListBox = new();
    private readonly Label detailsLabel = new();
    private readonly Button selectButton = new();

    public ShoppingRecipeSelectionDialog(
        GalaxyKnowledgeSnapshot snapshot,
        GalaxyItemKnowledge item,
        string selectedRecipeIdentity)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(item);

        this.snapshot = snapshot;
        this.item = item;

        this.Text = "Choose Recipe";
        this.Icon = ResourceLoader.Net7ClientManagerIcon;
        this.StartPosition = FormStartPosition.CenterParent;
        this.ShowInTaskbar = false;
        this.ClientSize = new Size(720, 470);
        this.MinimumSize = new Size(620, 400);
        this.BackColor = MainWindowTheme.Background;
        this.ForeColor = MainWindowTheme.Text;
        this.Font = MainWindowTheme.CreateBodyFont();
        this.ConfigureWindowChrome(
            allowResize: true,
            showMinimizeButton: false,
            showMaximizeButton: false);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(18),
            BackColor = MainWindowTheme.Background,
        };
        root.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(
            new RowStyle(SizeType.Absolute, 52f));
        root.RowStyles.Add(
            new RowStyle(SizeType.Percent, 45f));
        root.RowStyles.Add(
            new RowStyle(SizeType.Percent, 55f));
        root.RowStyles.Add(
            new RowStyle(SizeType.Absolute, 54f));

        var heading = new Label
        {
            Dock = DockStyle.Fill,
            Text = string.Concat(
                "Choose how to make ",
                this.item.Name),
            ForeColor = MainWindowTheme.Accent,
            Font = MainWindowTheme.CreateHeadingFont(11f),
            TextAlign = ContentAlignment.MiddleLeft,
        };

        this.recipeListBox.Dock = DockStyle.Fill;
        this.recipeListBox.BackColor = MainWindowTheme.Panel;
        this.recipeListBox.ForeColor = MainWindowTheme.Text;
        this.recipeListBox.BorderStyle = BorderStyle.FixedSingle;
        this.recipeListBox.IntegralHeight = false;
        this.recipeListBox.Font = MainWindowTheme.CreateBodyFont();

        this.detailsLabel.Dock = DockStyle.Fill;
        this.detailsLabel.BackColor = MainWindowTheme.Panel;
        this.detailsLabel.ForeColor = MainWindowTheme.Text;
        this.detailsLabel.Padding = new Padding(12);
        this.detailsLabel.TextAlign = ContentAlignment.TopLeft;
        this.detailsLabel.AutoEllipsis = true;

        var cancelButton = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Width = 96,
            Height = 34,
            Margin = Padding.Empty,
        };
        MainWindowTheme.StyleButton(cancelButton);

        this.selectButton.Text = "Use recipe";
        this.selectButton.DialogResult = DialogResult.OK;
        this.selectButton.Width = 112;
        this.selectButton.Height = 34;
        this.selectButton.Margin = new Padding(8, 0, 0, 0);
        MainWindowTheme.StyleButton(
            this.selectButton,
            primary: true);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 12, 0, 0),
            BackColor = MainWindowTheme.Background,
        };
        buttons.Controls.Add(this.selectButton);
        buttons.Controls.Add(cancelButton);

        root.Controls.Add(heading, 0, 0);
        root.Controls.Add(this.recipeListBox, 0, 1);
        root.Controls.Add(this.detailsLabel, 0, 2);
        root.Controls.Add(buttons, 0, 3);
        this.Controls.Add(root);

        var recipes = item.ProducedByRecipes
            .OrderBy(recipe => recipe.Kind)
            .ThenBy(recipe => recipe.Identity, StringComparer.Ordinal)
            .Select(recipe => new RecipeChoice(recipe))
            .ToArray();
        this.recipeListBox.Items.AddRange(recipes);

        var selectedIndex = Array.FindIndex(
            recipes,
            choice => string.Equals(
                choice.Recipe.Identity,
                selectedRecipeIdentity,
                StringComparison.Ordinal));
        this.recipeListBox.SelectedIndex = selectedIndex >= 0
            ? selectedIndex
            : recipes.Length == 0
                ? -1
                : 0;

        this.recipeListBox.SelectedIndexChanged +=
            this.RecipeListBox_OnSelectedIndexChanged;
        this.recipeListBox.DoubleClick +=
            this.RecipeListBox_OnDoubleClick;
        this.AcceptButton = this.selectButton;
        this.CancelButton = cancelButton;
        this.UpdateDetails();
    }

    public string SelectedRecipeIdentity =>
        this.recipeListBox.SelectedItem is RecipeChoice choice
            ? choice.Recipe.Identity
            : "";

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.recipeListBox.SelectedIndexChanged -=
                this.RecipeListBox_OnSelectedIndexChanged;
            this.recipeListBox.DoubleClick -=
                this.RecipeListBox_OnDoubleClick;
        }

        base.Dispose(disposing);
    }

    private void RecipeListBox_OnSelectedIndexChanged(
        object? sender,
        EventArgs e)
    {
        this.UpdateDetails();
    }

    private void RecipeListBox_OnDoubleClick(
        object? sender,
        EventArgs e)
    {
        if (this.recipeListBox.SelectedItem != null)
        {
            this.DialogResult = DialogResult.OK;
        }
    }

    private void UpdateDetails()
    {
        if (this.recipeListBox.SelectedItem is not RecipeChoice choice)
        {
            this.detailsLabel.Text = "No recipe is available.";
            this.selectButton.Enabled = false;
            return;
        }

        var recipe = choice.Recipe;
        var lines = recipe.Ingredients
            .OrderBy(ingredient =>
                this.snapshot.TryGetItem(
                    ingredient.ItemTemplateId,
                    out var ingredientItem)
                    ? ingredientItem.Name
                    : ingredient.ItemTemplateId.ToString(
                        CultureInfo.InvariantCulture))
            .Select(ingredient =>
            {
                var name = this.snapshot.TryGetItem(
                        ingredient.ItemTemplateId,
                        out var ingredientItem)
                    ? ingredientItem.Name
                    : string.Create(
                        CultureInfo.InvariantCulture,
                        $"Unknown item {ingredient.ItemTemplateId}");
                return string.Create(
                    CultureInfo.CurrentCulture,
                    $"{ingredient.Quantity:N0} × {name}");
            })
            .ToArray();

        var kind = recipe.Kind switch
        {
            GalaxyRecipeKind.Manufacture => "Manufacturing",
            GalaxyRecipeKind.Refine => "Refining",
            _ => "Recipe",
        };
        this.detailsLabel.Text = string.Concat(
            kind,
            Environment.NewLine,
            Environment.NewLine,
            lines.Length == 0
                ? "No ingredients were reported."
                : string.Join(Environment.NewLine, lines));
        this.selectButton.Enabled = true;
    }

    private sealed record RecipeChoice(
        GalaxyRecipeKnowledge Recipe)
    {
        public override string ToString()
        {
            var kind = this.Recipe.Kind switch
            {
                GalaxyRecipeKind.Manufacture => "Manufacturing",
                GalaxyRecipeKind.Refine => "Refining",
                _ => "Recipe",
            };

            return string.Create(
                CultureInfo.CurrentCulture,
                $"{kind} · {this.Recipe.Ingredients.Count:N0} ingredients");
        }
    }
}
