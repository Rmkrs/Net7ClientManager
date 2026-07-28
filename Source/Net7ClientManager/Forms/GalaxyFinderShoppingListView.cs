// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using System.ComponentModel;
using System.Globalization;
using System.Text;
using Net7ClientManager.Core;
using Net7ClientManager.GalaxyKnowledge;
using Net7ClientManager.Navigation;
using Net7ClientManager.Observations.Models;
using Net7ClientManager.Observations.Observers;
using Net7ClientManager.Shopping;

internal sealed class GalaxyFinderShoppingListView : UserControl
{
    private const string IconColumnName = "Icon";
    private const string ItemColumnName = "Item";
    private const string QuantityColumnName = "Quantity";
    private const string UnitColumnName = "Unit";
    private const string DecreaseColumnName = "Decrease";
    private const string IncreaseColumnName = "Increase";
    private const string RecipeColumnName = "Recipe";
    private const string RemoveColumnName = "Remove";
    private const string HopsColumnName = "Hops";
    private const string RouteColumnName = "Route";

    private const long MaximumRequestedQuantity = 99_999;

    private readonly ClientManager clientManager;
    private readonly Func<int?> getSelectedProcessId;
    private readonly Func<int, Size, Image?> resolveIcon;
    private readonly Action<int> openItem;
    private readonly Action? backRequested;
    private readonly DataGridView listGrid = new();
    private readonly Button newListButton = new();
    private readonly Button renameListButton = new();
    private readonly Button deleteListButton = new();
    private readonly TextBox listNameTextBox = new();
    private readonly TextBox listNotesTextBox = new();
    private readonly Label listStatusLabel = new();
    private readonly ThemedCheckBox showCompletedCheckBox = new();
    private readonly EnterCommitDataGridView requestedGrid = new();
    private readonly DataGridView planGrid = new();
    private readonly Label statusLabel = new();
    private readonly System.Windows.Forms.Timer refreshTimer = new();
    private readonly System.Windows.Forms.Timer detailsSaveTimer = new();
    private readonly System.Windows.Forms.Timer transientStatusTimer = new();
    private readonly ActionToolTip itemToolTip = new();
    private readonly ActionToolTip contextualToolTip = new();
    private Button? backButton;

    private bool isRefreshing;
    private bool suppressRequestedQuantityEndEdit;
    private string renderedListRevision = "";
    private string renderedPlanFingerprint = "";
    private int? hoveredItemTemplateId;
    private DataGridView? hoveredGrid;
    private int hoveredRowIndex = -1;
    private int hoveredColumnIndex = -1;
    private string? detailsListId;
    private ShoppingPlanSnapshot? renderedPlan;

    public GalaxyFinderShoppingListView(
        ClientManager clientManager,
        Func<int?> getSelectedProcessId,
        Func<int, Size, Image?> resolveIcon,
        Action<int> openItem,
        Action? backRequested)
    {
        ArgumentNullException.ThrowIfNull(clientManager);
        ArgumentNullException.ThrowIfNull(getSelectedProcessId);
        ArgumentNullException.ThrowIfNull(resolveIcon);
        ArgumentNullException.ThrowIfNull(openItem);

        this.clientManager = clientManager;
        this.getSelectedProcessId = getSelectedProcessId;
        this.resolveIcon = resolveIcon;
        this.openItem = openItem;
        this.backRequested = backRequested;

        this.Dock = DockStyle.Fill;
        this.BackColor = MainWindowTheme.Background;
        this.ForeColor = MainWindowTheme.Text;
        this.Font = MainWindowTheme.CreateBodyFont();

        this.BuildUi();
        this.WireEvents();
        this.RefreshListChoices(
            this.clientManager.GetActiveShoppingListId());
        this.RefreshNow(force: true);

        this.refreshTimer.Interval = 1500;
        this.refreshTimer.Tick += this.RefreshTimer_OnTick;
        this.refreshTimer.Start();

        this.detailsSaveTimer.Interval = 500;
        this.detailsSaveTimer.Tick += this.DetailsSaveTimer_OnTick;

        this.transientStatusTimer.Interval = 2400;
        this.transientStatusTimer.Tick +=
            this.TransientStatusTimer_OnTick;
    }


    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ShowBackButton
    {
        get => this.backButton?.Visible == true;
        set
        {
            if (this.backButton != null)
            {
                this.backButton.Visible = value;
            }
        }
    }

    public void RefreshNow(bool force)
    {
        if (this.IsDisposed || this.Disposing)
        {
            return;
        }

        this.RefreshListChoices(this.SelectedListId);
        this.RefreshPlan(force);
    }

    public void ShowList(string listId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(listId);
        this.RefreshListChoices(listId);
        this.SelectList(listId);
        this.RefreshPlan(force: true);
    }

    public void AddRequestedOutput(
        int itemTemplateId,
        long quantity = 1,
        string? listId = null)
    {
        if (itemTemplateId <= 0 || quantity <= 0)
        {
            return;
        }

        ShoppingListDocument saved;
        try
        {
            saved = this.clientManager.AddShoppingListRequestedOutput(
                itemTemplateId,
                quantity,
                listId ?? this.SelectedListId);
        }
        catch (OverflowException)
        {
            ThemedMessageDialog.ShowWarning(
                this,
                "Shopping List",
                "That quantity is too large for one shopping-list entry.");
            return;
        }

        this.RefreshListChoices(saved.ListId);
        this.SelectList(saved.ListId);
        var itemName = this.clientManager.GalaxyKnowledge.TryGetItem(
                itemTemplateId,
                out var item)
            ? item.Name
            : "item";
        this.ShowListStatus(string.Format(
            CultureInfo.CurrentCulture,
            "Added {0:N0} × {1} ✓",
            quantity,
            itemName));
        this.RefreshNow(force: true);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.SavePendingListDetails();
            this.refreshTimer.Stop();
            this.refreshTimer.Tick -= this.RefreshTimer_OnTick;
            this.refreshTimer.Dispose();
            this.detailsSaveTimer.Stop();
            this.detailsSaveTimer.Tick -=
                this.DetailsSaveTimer_OnTick;
            this.detailsSaveTimer.Dispose();
            this.transientStatusTimer.Stop();
            this.transientStatusTimer.Tick -=
                this.TransientStatusTimer_OnTick;
            this.transientStatusTimer.Dispose();
            this.clientManager.ShoppingListsChanged -=
                this.ClientManager_OnShoppingListsChanged;
            this.clientManager.GalaxyKnowledgeChanged -=
                this.ClientManager_OnGalaxyKnowledgeChanged;
            this.listGrid.SelectionChanged -=
                this.ListGrid_OnSelectionChanged;
            this.listGrid.CellClick -= this.ListGrid_OnCellClick;
            this.newListButton.Click -= this.NewListButton_OnClick;
            this.renameListButton.Click -= this.RenameListButton_OnClick;
            this.deleteListButton.Click -= this.DeleteListButton_OnClick;
            this.listNameTextBox.TextChanged -=
                this.ListDetails_OnTextChanged;
            this.listNotesTextBox.TextChanged -=
                this.ListDetails_OnTextChanged;
            this.showCompletedCheckBox.CheckedChanged -=
                this.ShowCompletedCheckBox_OnCheckedChanged;
            this.requestedGrid.CellClick -=
                this.RequestedGrid_OnCellContentClick;
            this.requestedGrid.CellEndEdit -=
                this.RequestedGrid_OnCellEndEdit;
            this.requestedGrid.CommitCurrentEdit = null;
            this.requestedGrid.CellDoubleClick -=
                this.ItemGrid_OnCellDoubleClick;
            this.planGrid.CellClick -=
                this.PlanGrid_OnCellContentClick;
            this.planGrid.CellDoubleClick -=
                this.ItemGrid_OnCellDoubleClick;
            this.requestedGrid.DataError -= this.Grid_OnDataError;
            this.planGrid.DataError -= this.Grid_OnDataError;
            this.requestedGrid.MouseMove -= this.Grid_OnMouseMove;
            this.planGrid.MouseMove -= this.Grid_OnMouseMove;
            this.requestedGrid.MouseLeave -= this.Grid_OnMouseLeave;
            this.planGrid.MouseLeave -= this.Grid_OnMouseLeave;
            this.requestedGrid.Scroll -= this.Grid_OnScroll;
            this.planGrid.Scroll -= this.Grid_OnScroll;
            this.requestedGrid.KeyDown -= this.ItemGrid_OnKeyDown;
            this.planGrid.KeyDown -= this.ItemGrid_OnKeyDown;
            this.itemToolTip.Dispose();
            this.contextualToolTip.Dispose();
        }

        base.Dispose(disposing);
    }

    private string? SelectedListId =>
        this.listGrid.SelectedRows.Count > 0 &&
        this.listGrid.SelectedRows[0].Tag is ShoppingListChoice choice
            ? choice.Summary.ListId
            : null;

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(10),
            BackColor = MainWindowTheme.Background,
        };
        root.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 245f));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));

        root.Controls.Add(this.CreateListWorkspace(), 0, 0);
        root.Controls.Add(this.CreatePlanningWorkspace(), 0, 1);
        root.Controls.Add(this.CreateFooter(), 0, 2);
        this.Controls.Add(root);
    }

    private Control CreateListWorkspace()
    {
        var workspace = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 8),
            BackColor = MainWindowTheme.Background,
        };
        workspace.ColumnStyles.Add(
            new ColumnStyle(SizeType.Absolute, 300f));
        workspace.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 100f));
        workspace.Controls.Add(this.CreateListSelectorPanel(), 0, 0);
        workspace.Controls.Add(this.CreateSelectedListPanel(), 1, 0);
        return workspace;
    }

    private Control CreateListSelectorPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = MainWindowTheme.Panel,
            Padding = new Padding(12),
            Margin = new Padding(0, 0, 8, 0),
        };
        panel.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 100f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));

        panel.Controls.Add(
            this.CreateSectionHeading("Shopping lists"),
            0,
            0);
        panel.Controls.Add(this.ConfigureListGrid(), 0, 1);

        var buttons = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0, 8, 0, 0),
            BackColor = MainWindowTheme.Panel,
        };
        buttons.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 33.333f));
        buttons.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 33.333f));
        buttons.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 33.334f));
        this.ConfigureToolbarButton(this.newListButton, "New");
        this.ConfigureToolbarButton(this.renameListButton, "Rename");
        this.ConfigureToolbarButton(this.deleteListButton, "Delete");
        buttons.Controls.Add(this.newListButton, 0, 0);
        buttons.Controls.Add(this.renameListButton, 1, 0);
        buttons.Controls.Add(this.deleteListButton, 2, 0);
        panel.Controls.Add(buttons, 0, 2);
        return panel;
    }

    private Control CreateSelectedListPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = MainWindowTheme.Panel,
            Padding = new Padding(14),
            Margin = Padding.Empty,
        };
        panel.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 100f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            BackColor = MainWindowTheme.Panel,
        };
        header.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 100f));
        header.ColumnStyles.Add(
            new ColumnStyle(SizeType.Absolute, 100f));
        header.Controls.Add(
            this.CreateSectionHeading("Selected list"),
            0,
            0);

        var backRequested = this.backRequested;
        if (backRequested != null)
        {
            var button = new Button
            {
                Text = "← Back",
                Dock = DockStyle.Fill,
                Margin = new Padding(6, 1, 0, 1),
            };
            MainWindowTheme.StyleButton(button);
            button.Click += (_, _) => backRequested();
            this.backButton = button;
            header.Controls.Add(button, 1, 0);
        }

        panel.Controls.Add(header, 0, 0);

        var nameRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            BackColor = MainWindowTheme.Panel,
        };
        nameRow.ColumnStyles.Add(
            new ColumnStyle(SizeType.Absolute, 64f));
        nameRow.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 100f));
        nameRow.Controls.Add(this.CreateToolbarLabel("Name"), 0, 0);
        this.listNameTextBox.Dock = DockStyle.Fill;
        this.listNameTextBox.Margin = new Padding(0, 3, 0, 3);
        MainWindowTheme.StyleTextBox(this.listNameTextBox);
        nameRow.Controls.Add(this.listNameTextBox, 1, 0);
        panel.Controls.Add(nameRow, 0, 1);

        var notesRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            BackColor = MainWindowTheme.Panel,
        };
        notesRow.ColumnStyles.Add(
            new ColumnStyle(SizeType.Absolute, 64f));
        notesRow.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 100f));
        notesRow.Controls.Add(this.CreateToolbarLabel("Notes"), 0, 0);
        this.listNotesTextBox.Dock = DockStyle.Fill;
        this.listNotesTextBox.Multiline = true;
        this.listNotesTextBox.AcceptsReturn = true;
        this.listNotesTextBox.ScrollBars = ScrollBars.Vertical;
        this.listNotesTextBox.MaxLength = 4000;
        this.listNotesTextBox.Margin = new Padding(0, 3, 0, 3);
        MainWindowTheme.StyleTextBox(this.listNotesTextBox);
        notesRow.Controls.Add(this.listNotesTextBox, 1, 0);
        panel.Controls.Add(notesRow, 0, 2);
        return panel;
    }

    private Control CreatePlanningWorkspace()
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterWidth = 8,
            BackColor = MainWindowTheme.Background,
            Margin = Padding.Empty,
        };

        const int minimumOutputsWidth = 340;
        const int minimumPlanWidth = 560;
        var splitInitialized = false;
        split.SizeChanged += (_, _) =>
        {
            if (splitInitialized ||
                split.ClientSize.Width <= 0)
            {
                return;
            }

            var maximumOutputsWidth =
                split.ClientSize.Width -
                minimumPlanWidth -
                split.SplitterWidth;
            if (maximumOutputsWidth < minimumOutputsWidth)
            {
                return;
            }

            // SplitContainer starts at a tiny construction-time width. Apply
            // the distance and minima only after Dock layout supplies the real
            // client size; assigning minima in the initializer can throw.
            split.SplitterDistance = Math.Clamp(
                (int)Math.Round(split.ClientSize.Width * 0.34d),
                minimumOutputsWidth,
                maximumOutputsWidth);
            split.Panel1MinSize = minimumOutputsWidth;
            split.Panel2MinSize = minimumPlanWidth;
            splitInitialized = true;
        };
        split.Panel1.BackColor = MainWindowTheme.Background;
        split.Panel2.BackColor = MainWindowTheme.Background;
        split.Panel1.Padding = new Padding(0, 0, 4, 0);
        split.Panel2.Padding = new Padding(4, 0, 0, 0);
        split.Panel1.Controls.Add(this.CreateRequestedOutputsPanel());
        split.Panel2.Controls.Add(this.CreatePlanPanel());
        return split;
    }

    private Control CreateRequestedOutputsPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = MainWindowTheme.Background,
            Margin = Padding.Empty,
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var heading = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            BackColor = MainWindowTheme.Background,
        };
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220f));
        heading.Controls.Add(
            this.CreateSectionHeading("Requested outputs"),
            0,
            0);
        this.listStatusLabel.Dock = DockStyle.Fill;
        this.listStatusLabel.ForeColor = MainWindowTheme.Success;
        this.listStatusLabel.TextAlign = ContentAlignment.MiddleRight;
        this.listStatusLabel.AutoEllipsis = true;
        heading.Controls.Add(this.listStatusLabel, 1, 0);

        panel.Controls.Add(heading, 0, 0);
        panel.Controls.Add(this.ConfigureRequestedGrid(), 0, 1);
        return panel;
    }

    private DataGridView ConfigureListGrid()
    {
        this.ConfigureGridBase(this.listGrid, readOnly: true);
        this.listGrid.RowTemplate.Height = 36;
        this.listGrid.RowTemplate.MinimumHeight = 36;
        this.listGrid.Columns.Add(
            new DataGridViewTextBoxColumn
            {
                Name = "Active",
                HeaderText = "Active",
                Width = 62,
                MinimumWidth = 58,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    Alignment =
                        DataGridViewContentAlignment.MiddleCenter,
                    ForeColor = MainWindowTheme.Success,
                    SelectionForeColor = MainWindowTheme.Success,
                },
            });
        this.listGrid.Columns.Add(
            new DataGridViewTextBoxColumn
            {
                Name = "ListName",
                HeaderText = "Shopping list",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                MinimumWidth = 140,
                SortMode = DataGridViewColumnSortMode.NotSortable,
            });
        this.listGrid.Columns.Add(
            new DataGridViewTextBoxColumn
            {
                Name = "Items",
                HeaderText = "Items",
                Width = 62,
                MinimumWidth = 56,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    Alignment =
                        DataGridViewContentAlignment.MiddleRight,
                },
            });
        return this.listGrid;
    }

    private Control CreatePlanPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = MainWindowTheme.Background,
            Margin = Padding.Empty,
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var heading = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = MainWindowTheme.Background,
            Margin = Padding.Empty,
        };
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160f));
        heading.Controls.Add(this.CreateSectionHeading("Plan"), 0, 0);

        this.showCompletedCheckBox.Text = "Show completed";
        this.showCompletedCheckBox.Dock = DockStyle.Fill;
        this.showCompletedCheckBox.Margin = new Padding(0, 5, 0, 0);
        this.showCompletedCheckBox.ForeColor = MainWindowTheme.Text;
        heading.Controls.Add(this.showCompletedCheckBox, 1, 0);

        panel.Controls.Add(heading, 0, 0);
        panel.Controls.Add(this.ConfigurePlanGrid(), 0, 1);
        return panel;
    }

    private Control CreateFooter()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = MainWindowTheme.Header,
            Padding = new Padding(12, 4, 12, 4),
            Margin = new Padding(0, 8, 0, 0),
        };
        this.statusLabel.Dock = DockStyle.Fill;
        this.statusLabel.ForeColor = MainWindowTheme.MutedText;
        this.statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        this.statusLabel.AutoEllipsis = true;
        panel.Controls.Add(this.statusLabel);
        return panel;
    }

    private Label CreateSectionHeading(string text)
    {
        return new Label
        {
            Dock = DockStyle.Fill,
            Text = text,
            ForeColor = MainWindowTheme.Accent,
            Font = MainWindowTheme.CreateHeadingFont(10f),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(4, 0, 0, 0),
        };
    }

    private Label CreateToolbarLabel(string text)
    {
        return new Label
        {
            Dock = DockStyle.Fill,
            Text = text,
            ForeColor = MainWindowTheme.MutedText,
            TextAlign = ContentAlignment.MiddleLeft,
        };
    }

    private void ConfigureToolbarButton(
        Button button,
        string text)
    {
        button.Text = text;
        button.Dock = DockStyle.Fill;
        button.Margin = new Padding(4, 2, 0, 2);
        MainWindowTheme.StyleButton(button);
    }

    private DataGridView ConfigureRequestedGrid()
    {
        this.ConfigureGridBase(this.requestedGrid, readOnly: false);
        this.requestedGrid.EditMode =
            DataGridViewEditMode.EditOnEnter;
        this.requestedGrid.Columns.Add(this.CreateIconColumn());
        this.requestedGrid.Columns.Add(
            new DataGridViewTextBoxColumn
            {
                Name = ItemColumnName,
                HeaderText = "Item",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                MinimumWidth = 220,
                ReadOnly = true,
            });
        this.requestedGrid.Columns.Add(
            this.CreateActionColumn(
                DecreaseColumnName,
                "",
                42));
        this.requestedGrid.Columns.Add(
            new DataGridViewTextBoxColumn
            {
                Name = QuantityColumnName,
                HeaderText = "Quantity",
                Width = 108,
                MinimumWidth = 96,
                ValueType = typeof(long),
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    Alignment =
                        DataGridViewContentAlignment.MiddleRight,
                    Format = "N0",
                },
            });
        this.requestedGrid.Columns.Add(
            new DataGridViewTextBoxColumn
            {
                Name = UnitColumnName,
                HeaderText = "Unit",
                Width = 74,
                MinimumWidth = 66,
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    Alignment =
                        DataGridViewContentAlignment.MiddleLeft,
                },
            });
        this.requestedGrid.Columns.Add(
            this.CreateActionColumn(
                IncreaseColumnName,
                "",
                42));
        this.requestedGrid.Columns.Add(
            this.CreateActionColumn(
                RecipeColumnName,
                "Recipe",
                170));
        this.requestedGrid.Columns.Add(
            this.CreateActionColumn(
                RemoveColumnName,
                "",
                84));
        return this.requestedGrid;
    }

    private DataGridView ConfigurePlanGrid()
    {
        this.ConfigureGridBase(this.planGrid, readOnly: true);
        this.planGrid.Columns.Add(this.CreateIconColumn());
        this.planGrid.Columns.Add(
            new DataGridViewTextBoxColumn
            {
                Name = ItemColumnName,
                HeaderText = "Item",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                MinimumWidth = 250,
            });
        this.planGrid.Columns.Add(
            this.CreateRightAlignedTextColumn(
                "Need",
                "Need",
                104));
        this.planGrid.Columns.Add(
            this.CreateNumberColumn(
                "Available",
                96,
                "Have now"));
        this.planGrid.Columns.Add(
            this.CreateNumberColumn(
                "Elsewhere",
                96));
        this.planGrid.Columns.Add(
            this.CreateRightAlignedTextColumn(
                "Missing",
                "Still needed",
                108));
        this.planGrid.Columns.Add(
            this.CreateRightAlignedTextColumn(
                "GlobalMissing",
                "After all pilots",
                120));
        this.planGrid.Columns.Add(
            new DataGridViewTextBoxColumn
            {
                Name = "Plan",
                HeaderText = "Plan",
                Width = 152,
                MinimumWidth = 126,
            });
        this.planGrid.Columns.Add(
            this.CreateActionColumn(
                RecipeColumnName,
                "Recipe",
                150));
        this.planGrid.Columns.Add(
            new DataGridViewTextBoxColumn
            {
                Name = "Sources",
                HeaderText = "Sources",
                Width = 154,
                MinimumWidth = 130,
            });
        this.planGrid.Columns.Add(
            new DataGridViewTextBoxColumn
            {
                Name = HopsColumnName,
                HeaderText = "Hops",
                Width = 72,
                MinimumWidth = 64,
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    Alignment = DataGridViewContentAlignment.MiddleCenter,
                },
            });
        this.planGrid.Columns.Add(
            this.CreateActionColumn(
                RouteColumnName,
                "Route",
                118));
        return this.planGrid;
    }

    private void ConfigureGridBase(
        DataGridView grid,
        bool readOnly)
    {
        grid.Dock = DockStyle.Fill;
        grid.Margin = Padding.Empty;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
        grid.AutoGenerateColumns = false;
        grid.BackgroundColor = MainWindowTheme.Panel;
        grid.BorderStyle = BorderStyle.None;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        grid.EnableHeadersVisualStyles = false;
        grid.GridColor = MainWindowTheme.Border;
        grid.MultiSelect = false;
        grid.ReadOnly = readOnly;
        grid.RowHeadersVisible = false;
        grid.ShowCellToolTips = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.RowTemplate.Height = 38;
        grid.RowTemplate.MinimumHeight = 38;
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
        grid.ColumnHeadersHeight = 34;
        grid.ColumnHeadersHeightSizeMode =
            DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
    }

    private DataGridViewImageColumn CreateIconColumn()
    {
        return new DataGridViewImageColumn
        {
            Name = IconColumnName,
            HeaderText = "",
            Width = 42,
            MinimumWidth = 42,
            ImageLayout = DataGridViewImageCellLayout.Zoom,
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                NullValue = null,
                Padding = new Padding(4),
            },
        };
    }

    private DataGridViewTextBoxColumn CreateNumberColumn(
        string name,
        int width,
        string? headerText = null)
    {
        return new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = headerText ?? name,
            Width = width,
            MinimumWidth = Math.Min(width, 80),
            ValueType = typeof(long),
            DefaultCellStyle = new DataGridViewCellStyle
            {
                Alignment = DataGridViewContentAlignment.MiddleRight,
                Format = "N0",
            },
        };
    }

    private DataGridViewTextBoxColumn CreateRightAlignedTextColumn(
        string name,
        string headerText,
        int width)
    {
        return new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = headerText,
            Width = width,
            MinimumWidth = Math.Min(width, 82),
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                Alignment = DataGridViewContentAlignment.MiddleRight,
            },
        };
    }

    private DataGridViewTextBoxColumn CreateActionColumn(
        string name,
        string headerText,
        int width)
    {
        return new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = headerText,
            Width = width,
            MinimumWidth = Math.Min(width, 42),
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = MainWindowTheme.Button,
                ForeColor = MainWindowTheme.Accent,
                SelectionBackColor = MainWindowTheme.Button,
                SelectionForeColor = MainWindowTheme.Accent,
                Alignment = DataGridViewContentAlignment.MiddleCenter,
                Padding = new Padding(3),
                NullValue = "",
            },
        };
    }

    private void WireEvents()
    {
        this.clientManager.ShoppingListsChanged +=
            this.ClientManager_OnShoppingListsChanged;
        this.clientManager.GalaxyKnowledgeChanged +=
            this.ClientManager_OnGalaxyKnowledgeChanged;
        this.listGrid.SelectionChanged +=
            this.ListGrid_OnSelectionChanged;
        this.listGrid.CellClick += this.ListGrid_OnCellClick;
        this.newListButton.Click += this.NewListButton_OnClick;
        this.renameListButton.Click += this.RenameListButton_OnClick;
        this.deleteListButton.Click += this.DeleteListButton_OnClick;
        this.listNameTextBox.TextChanged +=
            this.ListDetails_OnTextChanged;
        this.listNotesTextBox.TextChanged +=
            this.ListDetails_OnTextChanged;
        this.showCompletedCheckBox.CheckedChanged +=
            this.ShowCompletedCheckBox_OnCheckedChanged;
        this.requestedGrid.CellClick +=
            this.RequestedGrid_OnCellContentClick;
        this.requestedGrid.CellEndEdit +=
            this.RequestedGrid_OnCellEndEdit;
        this.requestedGrid.CommitCurrentEdit =
            this.CommitRequestedQuantityEdit;
        this.requestedGrid.CellDoubleClick +=
            this.ItemGrid_OnCellDoubleClick;
        this.planGrid.CellClick +=
            this.PlanGrid_OnCellContentClick;
        this.planGrid.CellDoubleClick +=
            this.ItemGrid_OnCellDoubleClick;
        this.requestedGrid.DataError += this.Grid_OnDataError;
        this.planGrid.DataError += this.Grid_OnDataError;
        this.requestedGrid.MouseMove += this.Grid_OnMouseMove;
        this.planGrid.MouseMove += this.Grid_OnMouseMove;
        this.requestedGrid.MouseLeave += this.Grid_OnMouseLeave;
        this.planGrid.MouseLeave += this.Grid_OnMouseLeave;
        this.requestedGrid.Scroll += this.Grid_OnScroll;
        this.planGrid.Scroll += this.Grid_OnScroll;
        this.requestedGrid.KeyDown += this.ItemGrid_OnKeyDown;
        this.planGrid.KeyDown += this.ItemGrid_OnKeyDown;
    }

    private void RefreshTimer_OnTick(object? sender, EventArgs e)
    {
        if (this.Visible)
        {
            this.RefreshPlan(force: false);
        }
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
        {
            this.RefreshListChoices(e.ListId ?? this.SelectedListId);
            this.RefreshPlan(force: true);
        });
    }

    private void ClientManager_OnGalaxyKnowledgeChanged(
        object? sender,
        GalaxyKnowledgeSnapshotChangedEventArgs e)
    {
        if (this.IsDisposed ||
            this.Disposing ||
            !this.IsHandleCreated)
        {
            return;
        }

        this.BeginInvoke(() => this.RefreshPlan(force: true));
    }

    private void ListGrid_OnSelectionChanged(
        object? sender,
        EventArgs e)
    {
        if (this.isRefreshing)
        {
            return;
        }

        this.SavePendingListDetails();
        this.renderedListRevision = "";
        this.renderedPlanFingerprint = "";
        this.RefreshSelectedListDetails();
        this.RefreshPlan(force: true);
    }

    private void NewListButton_OnClick(
        object? sender,
        EventArgs e)
    {
        using var dialog = new ShoppingListNameDialog(
            "New Shopping List",
            "Choose a name for the shopping list.",
            "Shopping List");
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var created = this.clientManager.CreateShoppingList(
            dialog.ListName);
        this.clientManager.SetActiveShoppingList(created.ListId);
        this.RefreshListChoices(created.ListId);
        this.RefreshPlan(force: true);
    }

    private void RenameListButton_OnClick(
        object? sender,
        EventArgs e)
    {
        var list = this.GetSelectedList();
        if (list == null)
        {
            return;
        }

        using var dialog = new ShoppingListNameDialog(
            "Rename Shopping List",
            "Choose a new name for this shopping list.",
            list.Name);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var saved = this.clientManager.SaveShoppingList(
            list with { Name = dialog.ListName });
        this.RefreshListChoices(saved.ListId);
        this.RefreshPlan(force: true);
    }

    private void DeleteListButton_OnClick(
        object? sender,
        EventArgs e)
    {
        var list = this.GetSelectedList();
        if (list == null ||
            !ThemedMessageDialog.Confirm(
                this,
                "Delete Shopping List",
                string.Concat(
                    "Delete ‘",
                    list.Name,
                    "’?"),
                "Delete"))
        {
            return;
        }

        this.detailsSaveTimer.Stop();
        this.detailsListId = null;
        this.clientManager.DeleteShoppingList(list.ListId);
        this.RefreshListChoices(
            this.clientManager.GetActiveShoppingListId());
        this.RefreshPlan(force: true);
    }

    private void ListGrid_OnCellClick(
        object? sender,
        DataGridViewCellEventArgs e)
    {
        if (this.isRefreshing ||
            e.RowIndex < 0 ||
            e.ColumnIndex < 0 ||
            !string.Equals(
                this.listGrid.Columns[e.ColumnIndex].Name,
                "Active",
                StringComparison.Ordinal) ||
            this.listGrid.Rows[e.RowIndex].Tag is not
                ShoppingListChoice choice)
        {
            return;
        }

        var listId = choice.Summary.ListId;
        this.QueueGridMutation(() =>
        {
            this.clientManager.SetActiveShoppingList(listId);
            this.RefreshListChoices(listId);
        });
    }

    private void ListDetails_OnTextChanged(
        object? sender,
        EventArgs e)
    {
        if (this.isRefreshing ||
            this.detailsListId == null)
        {
            return;
        }

        this.detailsSaveTimer.Stop();
        this.detailsSaveTimer.Start();
    }

    private void DetailsSaveTimer_OnTick(
        object? sender,
        EventArgs e)
    {
        this.detailsSaveTimer.Stop();
        this.SavePendingListDetails();
    }

    private void TransientStatusTimer_OnTick(
        object? sender,
        EventArgs e)
    {
        this.transientStatusTimer.Stop();
        this.listStatusLabel.Text = "";
    }

    private void SavePendingListDetails()
    {
        this.detailsSaveTimer.Stop();
        if (this.isRefreshing ||
            string.IsNullOrWhiteSpace(this.detailsListId))
        {
            return;
        }

        var list = this.clientManager.GetShoppingList(
            this.detailsListId);
        if (list == null)
        {
            return;
        }

        var name = this.listNameTextBox.Text.Trim();
        if (name.Length == 0)
        {
            name = list.Name;
        }

        name = name.Length <= 120
            ? name
            : name[..120];
        var notes = this.listNotesTextBox.Text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
        if (notes.Length > 4000)
        {
            notes = notes[..4000];
        }

        if (string.Equals(
                list.Name,
                name,
                StringComparison.Ordinal) &&
            string.Equals(
                list.Notes,
                notes,
                StringComparison.Ordinal))
        {
            return;
        }

        var saved = this.clientManager.SaveShoppingList(
            list with
            {
                Name = name,
                Notes = notes,
            });
        this.detailsListId = saved.ListId;
        this.RefreshListChoices(saved.ListId);
        this.ShowListStatus("Saved ✓");
    }

    private void ShowListStatus(string text)
    {
        this.listStatusLabel.Text = text;
        this.transientStatusTimer.Stop();
        this.transientStatusTimer.Start();
    }

    private void ShowCompletedCheckBox_OnCheckedChanged(
        object? sender,
        EventArgs e)
    {
        this.RefreshPlan(force: true);
    }

    private void QueueGridMutation(Action mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);

        if (this.IsDisposed ||
            this.Disposing ||
            !this.IsHandleCreated)
        {
            return;
        }

        try
        {
            // DataGridView is still formatting and committing the clicked
            // cell while its event callback is running. Rebuilding rows from
            // that callback can make the grid format a value against a cell
            // type that no longer belongs to the same row. Let the callback
            // unwind before changing the list and rebuilding either grid.
            this.BeginInvoke((Action)(() =>
            {
                if (!this.IsDisposed && !this.Disposing)
                {
                    mutation();
                }
            }));
        }
        catch (InvalidOperationException)
        {
            // The window may lose its handle while the application closes.
        }
    }

    private void RequestedGrid_OnCellContentClick(
        object? sender,
        DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 ||
            e.ColumnIndex < 0 ||
            this.requestedGrid.Rows[e.RowIndex].Tag is not RequestedRow row)
        {
            return;
        }

        var columnName = this.requestedGrid.Columns[e.ColumnIndex].Name;
        if (string.Equals(
                columnName,
                DecreaseColumnName,
                StringComparison.Ordinal))
        {
            if (row.Output.Quantity > 1)
            {
                var itemTemplateId = row.Item.ItemTemplateId;
                var quantity = row.Output.Quantity - 1;
                this.QueueGridMutation(() =>
                    this.SetRequestedOutputQuantity(
                        itemTemplateId,
                        quantity));
            }
        }
        else if (string.Equals(
                     columnName,
                     IncreaseColumnName,
                     StringComparison.Ordinal))
        {
            if (row.Output.Quantity < MaximumRequestedQuantity)
            {
                var itemTemplateId = row.Item.ItemTemplateId;
                var quantity = row.Output.Quantity + 1;
                this.QueueGridMutation(() =>
                    this.SetRequestedOutputQuantity(
                        itemTemplateId,
                        quantity));
            }
        }
        else if (string.Equals(
                     columnName,
                     RemoveColumnName,
                     StringComparison.Ordinal))
        {
            var itemTemplateId = row.Item.ItemTemplateId;
            this.QueueGridMutation(() =>
                this.RemoveRequestedOutput(itemTemplateId));
        }
        else if (string.Equals(
                     columnName,
                     RecipeColumnName,
                     StringComparison.Ordinal))
        {
            var itemTemplateId = row.Item.ItemTemplateId;
            this.QueueGridMutation(() =>
                this.ChooseRecipe(itemTemplateId));
        }
    }

    private bool CommitRequestedQuantityEdit()
    {
        if (!this.requestedGrid.IsCurrentCellInEditMode ||
            this.requestedGrid.CurrentCell is not { } cell ||
            !string.Equals(
                cell.OwningColumn.Name,
                QuantityColumnName,
                StringComparison.Ordinal) ||
            cell.RowIndex < 0 ||
            this.requestedGrid.Rows[cell.RowIndex].Tag is not
                RequestedRow row)
        {
            return false;
        }

        var editedText = this.requestedGrid.EditingControl is
                TextBoxBase editor
            ? editor.Text
            : Convert.ToString(
                cell.EditedFormattedValue,
                CultureInfo.CurrentCulture);
        if (!TryParseRequestedQuantity(editedText, out var quantity))
        {
            this.suppressRequestedQuantityEndEdit = true;
            try
            {
                this.requestedGrid.CancelEdit();
            }
            finally
            {
                this.suppressRequestedQuantityEndEdit = false;
            }

            ThemedMessageDialog.ShowWarning(
                this,
                "Shopping List",
                "Quantity must be a whole number from 1 to 99,999.");
            this.QueueGridMutation(() =>
                this.RefreshPlan(force: true));
            return true;
        }

        var itemTemplateId = row.Item.ItemTemplateId;
        this.suppressRequestedQuantityEndEdit = true;
        try
        {
            this.requestedGrid.EndEdit();
            cell.Value = quantity;
        }
        finally
        {
            this.suppressRequestedQuantityEndEdit = false;
        }

        this.QueueGridMutation(() =>
            this.SetRequestedOutputQuantity(
                itemTemplateId,
                quantity));
        this.requestedGrid.Focus();
        return true;
    }

    private void RequestedGrid_OnCellEndEdit(
        object? sender,
        DataGridViewCellEventArgs e)
    {
        if (this.suppressRequestedQuantityEndEdit)
        {
            return;
        }

        if (e.RowIndex < 0 ||
            e.ColumnIndex < 0 ||
            !string.Equals(
                this.requestedGrid.Columns[e.ColumnIndex].Name,
                QuantityColumnName,
                StringComparison.Ordinal) ||
            this.requestedGrid.Rows[e.RowIndex].Tag is not RequestedRow row)
        {
            return;
        }

        var value = this.requestedGrid.Rows[e.RowIndex]
            .Cells[e.ColumnIndex]
            .Value;
        if (!TryParseRequestedQuantity(
                Convert.ToString(
                    value,
                    CultureInfo.CurrentCulture),
                out var quantity))
        {
            ThemedMessageDialog.ShowWarning(
                this,
                "Shopping List",
                "Quantity must be a whole number from 1 to 99,999.");
            this.QueueGridMutation(() =>
                this.RefreshPlan(force: true));
            return;
        }

        var itemTemplateId = row.Item.ItemTemplateId;
        this.QueueGridMutation(() =>
            this.SetRequestedOutputQuantity(
                itemTemplateId,
                quantity));
    }

    private static bool TryParseRequestedQuantity(
        string? value,
        out long quantity)
    {
        return long.TryParse(
                   value,
                   NumberStyles.Integer | NumberStyles.AllowThousands,
                   CultureInfo.CurrentCulture,
                   out quantity) &&
               quantity is > 0 and <= MaximumRequestedQuantity;
    }

    private void PlanGrid_OnCellContentClick(
        object? sender,
        DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 ||
            e.ColumnIndex < 0 ||
            this.planGrid.Rows[e.RowIndex].Tag is not PlanRow row)
        {
            return;
        }

        var columnName = this.planGrid.Columns[e.ColumnIndex].Name;
        if (string.Equals(
                columnName,
                RecipeColumnName,
                StringComparison.Ordinal))
        {
            var itemTemplateId = row.Item.ItemTemplateId;
            this.QueueGridMutation(() =>
                this.ChooseRecipe(itemTemplateId));
        }
        else if (string.Equals(
                     columnName,
                     RouteColumnName,
                     StringComparison.Ordinal))
        {
            this.SetDestination(row.Route);
        }
    }

    private void ItemGrid_OnCellDoubleClick(
        object? sender,
        DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 ||
            e.ColumnIndex < 0 ||
            sender is not DataGridView grid)
        {
            return;
        }

        var columnName = grid.Columns[e.ColumnIndex].Name;
        if (string.Equals(
                columnName,
                RecipeColumnName,
                StringComparison.Ordinal) ||
            string.Equals(
                columnName,
                RemoveColumnName,
                StringComparison.Ordinal) ||
            string.Equals(
                columnName,
                DecreaseColumnName,
                StringComparison.Ordinal) ||
            string.Equals(
                columnName,
                IncreaseColumnName,
                StringComparison.Ordinal) ||
            string.Equals(
                columnName,
                RouteColumnName,
                StringComparison.Ordinal))
        {
            return;
        }

        var itemTemplateId = grid.Rows[e.RowIndex].Tag switch
        {
            RequestedRow requested => requested.Item.ItemTemplateId,
            PlanRow plan => plan.Item.ItemTemplateId,
            _ => 0,
        };

        if (itemTemplateId > 0)
        {
            this.openItem(itemTemplateId);
        }
    }

    private void ItemGrid_OnKeyDown(
        object? sender,
        KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter ||
            sender is not DataGridView grid ||
            grid.SelectedRows.Count == 0)
        {
            return;
        }

        var itemTemplateId = grid.SelectedRows[0].Tag switch
        {
            RequestedRow requested => requested.Item.ItemTemplateId,
            PlanRow plan => plan.Item.ItemTemplateId,
            _ => 0,
        };
        if (itemTemplateId <= 0)
        {
            return;
        }

        this.openItem(itemTemplateId);
        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    private void Grid_OnDataError(
        object? sender,
        DataGridViewDataErrorEventArgs e)
    {
        e.ThrowException = false;
        e.Cancel = true;
    }

    private void Grid_OnMouseMove(
        object? sender,
        MouseEventArgs e)
    {
        if (sender is not DataGridView grid)
        {
            return;
        }

        var hit = grid.HitTest(e.X, e.Y);
        if (hit.RowIndex < 0 ||
            hit.ColumnIndex < 0 ||
            hit.RowIndex >= grid.Rows.Count)
        {
            this.ClearHoveredCellStyle();
            this.HideGridToolTips();
            grid.Cursor = Cursors.Default;
            return;
        }

        var columnName = grid.Columns[hit.ColumnIndex].Name;
        var cell = grid.Rows[hit.RowIndex].Cells[hit.ColumnIndex];
        var isAction = IsActionColumn(columnName) &&
            !string.IsNullOrWhiteSpace(
                Convert.ToString(
                    cell.Value,
                    CultureInfo.CurrentCulture));

        if (!ReferenceEquals(this.hoveredGrid, grid) ||
            this.hoveredRowIndex != hit.RowIndex ||
            this.hoveredColumnIndex != hit.ColumnIndex)
        {
            this.ClearHoveredCellStyle();
            this.HideGridToolTips();
            this.hoveredGrid = grid;
            this.hoveredRowIndex = hit.RowIndex;
            this.hoveredColumnIndex = hit.ColumnIndex;
        }

        if (isAction)
        {
            cell.Style.BackColor = MainWindowTheme.ButtonHover;
            cell.Style.ForeColor = MainWindowTheme.Accent;
            cell.Style.SelectionBackColor = MainWindowTheme.ButtonHover;
            cell.Style.SelectionForeColor = MainWindowTheme.Accent;
            grid.Cursor = Cursors.Hand;
        }
        else
        {
            grid.Cursor = Cursors.Default;
        }

        var rowTag = grid.Rows[hit.RowIndex].Tag;
        var item = ResolveRowItem(rowTag);
        if ((string.Equals(
                    columnName,
                    IconColumnName,
                    StringComparison.Ordinal) ||
             string.Equals(
                    columnName,
                    ItemColumnName,
                    StringComparison.Ordinal)) &&
            item != null)
        {
            this.ShowItemToolTip(grid, item);
            return;
        }

        var contextualContent = this.BuildContextualToolTip(
            rowTag,
            columnName);
        if (contextualContent != null)
        {
            this.contextualToolTip.SetHoveredToolTip(
                grid,
                contextualContent,
                this.GetItemToolTipDelayMilliseconds());
        }
    }

    private static GalaxyItemKnowledge? ResolveRowItem(
        object? rowTag)
    {
        return rowTag switch
        {
            RequestedRow requested => requested.Item,
            PlanRow plan => plan.Item,
            _ => null,
        };
    }

    private void ShowItemToolTip(
        DataGridView grid,
        GalaxyItemKnowledge item)
    {
        if (this.hoveredItemTemplateId == item.ItemTemplateId)
        {
            return;
        }

        this.itemToolTip.Remove(grid);
        ClientRuntimeItemTemplateObservation? template = null;
        if (ClientItemTemplateCatalog.TryGetRuntimeObservation(
                item.ItemTemplateId,
                out var resolvedTemplate))
        {
            template = resolvedTemplate;
        }

        var content = PilotArchiveItemToolTipBuilder.BuildCatalogItem(
            item.ItemTemplateId,
            item.Name,
            template,
            this.resolveIcon(
                item.ItemTemplateId,
                new Size(36, 36)));
        this.hoveredItemTemplateId = item.ItemTemplateId;
        this.itemToolTip.SetHoveredToolTip(
            grid,
            content,
            this.GetItemToolTipDelayMilliseconds());
    }

    private ActionToolTipContent? BuildContextualToolTip(
        object? rowTag,
        string columnName)
    {
        if (rowTag is PlanRow planRow)
        {
            var ownership = this.renderedPlan?
                .Ownership
                .GetItem(planRow.Item.ItemTemplateId);

            if (string.Equals(
                    columnName,
                    "Available",
                    StringComparison.Ordinal) &&
                ownership != null)
            {
                return BuildOwnershipToolTipContent(
                    "Available now",
                    ownership,
                    activePilot: true);
            }

            if (string.Equals(
                    columnName,
                    "Elsewhere",
                    StringComparison.Ordinal) &&
                ownership != null)
            {
                return BuildOwnershipToolTipContent(
                    "Owned elsewhere",
                    ownership,
                    activePilot: false);
            }

            if (string.Equals(
                    columnName,
                    RecipeColumnName,
                    StringComparison.Ordinal))
            {
                return BuildRecipeToolTipContent(
                    planRow.Item,
                    planRow.Line);
            }

            if (string.Equals(
                    columnName,
                    "Sources",
                    StringComparison.Ordinal))
            {
                var details =
                    GalaxyFinderSourcePresentation.BuildDetails(
                        planRow.Item);
                return details.Length == 0
                    ? null
                    : BuildTextToolTipContent(
                        "Known sources",
                        details);
            }

            if (string.Equals(
                    columnName,
                    HopsColumnName,
                    StringComparison.Ordinal) &&
                planRow.Route != null)
            {
                return BuildTextToolTipContent(
                    "Nearest source",
                    planRow.Route.Description);
            }

            if (string.Equals(
                    columnName,
                    RouteColumnName,
                    StringComparison.Ordinal) &&
                planRow.Route != null)
            {
                return BuildTextToolTipContent(
                    "Route",
                    planRow.Route.Description);
            }

            if (string.Equals(
                    columnName,
                    "Plan",
                    StringComparison.Ordinal) &&
                planRow.Line.ActiveUnresolvedRecipeQuantity > 0)
            {
                return BuildTextToolTipContent(
                    "Recipe unavailable",
                    "The selected recipe cannot be expanded with the current Galaxy data.");
            }
        }
        else if (rowTag is RequestedRow requestedRow &&
                 string.Equals(
                     columnName,
                     RecipeColumnName,
                     StringComparison.Ordinal))
        {
            return BuildRecipeToolTipContent(
                requestedRow.Item,
                requestedRow.PlanLine);
        }

        return null;
    }

    private void Grid_OnMouseLeave(object? sender, EventArgs e)
    {
        this.ClearHoveredCellStyle();
        this.HideGridToolTips();
        if (sender is DataGridView grid)
        {
            grid.Cursor = Cursors.Default;
        }
    }

    private void Grid_OnScroll(object? sender, ScrollEventArgs e)
    {
        this.ClearHoveredCellStyle();
        this.HideGridToolTips();
    }

    private void HideGridToolTips()
    {
        this.itemToolTip.Remove(this.requestedGrid);
        this.itemToolTip.Remove(this.planGrid);
        this.contextualToolTip.Remove(this.requestedGrid);
        this.contextualToolTip.Remove(this.planGrid);
        this.hoveredItemTemplateId = null;
    }

    private void ClearHoveredCellStyle()
    {
        if (this.hoveredGrid != null &&
            this.hoveredRowIndex >= 0 &&
            this.hoveredColumnIndex >= 0 &&
            this.hoveredRowIndex < this.hoveredGrid.Rows.Count &&
            this.hoveredColumnIndex < this.hoveredGrid.Columns.Count)
        {
            var cell = this.hoveredGrid.Rows[this.hoveredRowIndex]
                .Cells[this.hoveredColumnIndex];
            if (IsActionColumn(
                    this.hoveredGrid.Columns[
                        this.hoveredColumnIndex].Name))
            {
                cell.Style.BackColor = MainWindowTheme.Button;
                cell.Style.ForeColor = MainWindowTheme.Accent;
                cell.Style.SelectionBackColor =
                    MainWindowTheme.Button;
                cell.Style.SelectionForeColor =
                    MainWindowTheme.Accent;
            }
        }

        this.hoveredGrid = null;
        this.hoveredRowIndex = -1;
        this.hoveredColumnIndex = -1;
    }

    private static bool IsActionColumn(string columnName)
    {
        return string.Equals(
                   columnName,
                   DecreaseColumnName,
                   StringComparison.Ordinal) ||
               string.Equals(
                   columnName,
                   IncreaseColumnName,
                   StringComparison.Ordinal) ||
               string.Equals(
                   columnName,
                   RecipeColumnName,
                   StringComparison.Ordinal) ||
               string.Equals(
                   columnName,
                   RemoveColumnName,
                   StringComparison.Ordinal) ||
               string.Equals(
                   columnName,
                   RouteColumnName,
                   StringComparison.Ordinal);
    }

    private static ActionToolTipContent BuildOwnershipToolTipContent(
        string title,
        ShoppingItemOwnership ownership,
        bool activePilot)
    {
        var locations = ownership.Locations
            .Where(location =>
                location.IsActivePilot == activePilot)
            .OrderBy(location =>
                location.PilotName,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(location =>
                location.Collection,
                StringComparer.Ordinal)
            .ToArray();

        List<ActionToolTipParagraph> paragraphs =
        [
            new(
                [new ActionToolTipRun(
                    title,
                    ActionToolTipTextRole.Accent,
                    Bold: true)],
                ActionToolTipParagraphStyle.Header),
        ];

        if (locations.Length == 0)
        {
            paragraphs.Add(
                new ActionToolTipParagraph(
                    [new ActionToolTipRun(
                        "None known.",
                        ActionToolTipTextRole.Muted)]));
        }
        else
        {
            foreach (var location in locations)
            {
                paragraphs.Add(
                    new ActionToolTipParagraph(
                        [new ActionToolTipRun(string.Format(
                            CultureInfo.CurrentCulture,
                            "{0:N0} · {1} · {2}",
                            location.Quantity,
                            location.PilotName,
                            location.Collection))],
                        SpaceBefore: 3));
            }
        }

        return new ActionToolTipContent(paragraphs, Icon: null);
    }

    private ActionToolTipContent? BuildRecipeToolTipContent(
        GalaxyItemKnowledge item,
        ShoppingPlanLine? line)
    {
        if (line == null ||
            string.IsNullOrWhiteSpace(line.SelectedRecipeIdentity))
        {
            return BuildTextToolTipContent(
                "Recipe",
                "No recipe is known for this item.");
        }

        var selected = item.ProducedByRecipes.FirstOrDefault(recipe =>
            string.Equals(
                recipe.Identity,
                line.SelectedRecipeIdentity,
                StringComparison.Ordinal));
        if (selected == null)
        {
            return BuildTextToolTipContent(
                "Recipe",
                "The selected recipe is not available in the current Forge catalogue.");
        }

        List<ActionToolTipParagraph> paragraphs =
        [
            new(
                [new ActionToolTipRun(
                    selected.Kind == GalaxyRecipeKind.Refine
                        ? "Refining recipe"
                        : "Manufacturing recipe",
                    ActionToolTipTextRole.Accent,
                    Bold: true)],
                ActionToolTipParagraphStyle.Header),
        ];

        foreach (var ingredient in selected.Ingredients)
        {
            paragraphs.Add(
                new ActionToolTipParagraph(
                    [new ActionToolTipRun(string.Format(
                        CultureInfo.CurrentCulture,
                        "{0:N0} × {1}",
                        ingredient.Quantity,
                        this.clientManager.GalaxyKnowledge.TryGetItem(
                            ingredient.ItemTemplateId,
                            out var ingredientItem)
                            ? ingredientItem.Name
                            : string.Format(
                                CultureInfo.InvariantCulture,
                                "Item {0}",
                                ingredient.ItemTemplateId)))],
                    SpaceBefore: 3));
        }

        if (line.AlternativeRecipeIdentities.Count > 1)
        {
            paragraphs.Add(
                new ActionToolTipParagraph(
                    [new ActionToolTipRun(
                        string.Format(
                            CultureInfo.CurrentCulture,
                            "{0:N0} known recipe choices.",
                            line.AlternativeRecipeIdentities.Count),
                        ActionToolTipTextRole.Muted)],
                    SpaceBefore: 8));
        }

        return new ActionToolTipContent(paragraphs, Icon: null);
    }

    private static ActionToolTipContent BuildTextToolTipContent(
        string title,
        string text)
    {
        return new ActionToolTipContent(
        [
            new(
                [new ActionToolTipRun(
                    title,
                    ActionToolTipTextRole.Accent,
                    Bold: true)],
                ActionToolTipParagraphStyle.Header),
            new(
                [new ActionToolTipRun(text)],
                SpaceBefore: 6),
        ],
        Icon: null);
    }

    private int GetItemToolTipDelayMilliseconds()
    {
        if (this.getSelectedProcessId() is not { } processId)
        {
            return ClientTooltipDelayObservation.DefaultDelayMilliseconds;
        }

        var snapshot = this.clientManager
            .GetClientObservationSnapshots()
            .FirstOrDefault(candidate =>
                candidate.ProcessId == processId);
        return snapshot?.TooltipDelay.IsAvailable == true
            ? snapshot.TooltipDelay.DelayMilliseconds
            : ClientTooltipDelayObservation.DefaultDelayMilliseconds;
    }

    private void RefreshListChoices(string? preferredListId)
    {
        var summaries = this.clientManager.GetShoppingLists();
        var selectedId = preferredListId ?? this.SelectedListId;
        var activeListId = this.clientManager.GetActiveShoppingListId();

        this.isRefreshing = true;
        try
        {
            this.listGrid.Rows.Clear();
            foreach (var summary in summaries)
            {
                var rowIndex = this.listGrid.Rows.Add(
                    string.Equals(
                        activeListId,
                        summary.ListId,
                        StringComparison.Ordinal)
                        ? "✓"
                        : "",
                    summary.Name,
                    summary.RequestedOutputCount);
                var row = this.listGrid.Rows[rowIndex];
                row.Tag = new ShoppingListChoice(summary);
                row.Cells["Active"].Style.ForeColor =
                    MainWindowTheme.Success;
                row.Cells["Active"].Style.SelectionForeColor =
                    MainWindowTheme.Success;
                if (string.Equals(
                        selectedId,
                        summary.ListId,
                        StringComparison.Ordinal))
                {
                    row.Selected = true;
                    this.listGrid.CurrentCell = row.Cells["ListName"];
                }
            }

            if (this.listGrid.SelectedRows.Count == 0 &&
                this.listGrid.Rows.Count > 0)
            {
                this.listGrid.Rows[0].Selected = true;
                this.listGrid.CurrentCell =
                    this.listGrid.Rows[0].Cells["ListName"];
            }
        }
        finally
        {
            this.isRefreshing = false;
        }

        var hasList = this.SelectedListId != null;
        this.renameListButton.Enabled = hasList;
        this.deleteListButton.Enabled = hasList;
        this.listNameTextBox.Enabled = hasList;
        this.listNotesTextBox.Enabled = hasList;
        this.RefreshSelectedListDetails();
    }

    private void SelectList(string listId)
    {
        this.isRefreshing = true;
        try
        {
            this.listGrid.ClearSelection();
            foreach (DataGridViewRow row in this.listGrid.Rows)
            {
                if (row.Tag is not ShoppingListChoice choice ||
                    !string.Equals(
                        choice.Summary.ListId,
                        listId,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                row.Selected = true;
                this.listGrid.CurrentCell = row.Cells["ListName"];
                break;
            }
        }
        finally
        {
            this.isRefreshing = false;
        }

        this.RefreshSelectedListDetails();
    }

    private void RefreshSelectedListDetails()
    {
        var list = this.GetSelectedList();
        this.isRefreshing = true;
        try
        {
            this.detailsListId = list?.ListId;
            this.listNameTextBox.Text = list?.Name ?? "";
            this.listNotesTextBox.Text = list?.Notes ?? "";
            this.listNameTextBox.Enabled = list != null;
            this.listNotesTextBox.Enabled = list != null;
        }
        finally
        {
            this.isRefreshing = false;
        }
    }

    private ShoppingListDocument? GetSelectedList()
    {
        return this.SelectedListId is { } listId
            ? this.clientManager.GetShoppingList(listId)
            : null;
    }

    private void RefreshPlan(bool force)
    {
        if (this.isRefreshing)
        {
            return;
        }

        var list = this.GetSelectedList();
        if (list == null)
        {
            this.requestedGrid.Rows.Clear();
            this.planGrid.Rows.Clear();
            this.statusLabel.Text =
                "Create a shopping list, then add requested outputs from Search.";
            this.renderedListRevision = "";
            this.renderedPlanFingerprint = "";
            this.renderedPlan = null;
            this.RefreshSelectedListDetails();
            return;
        }

        var plan = this.clientManager.BuildShoppingPlan(
            list.ListId,
            activeProcessId: this.getSelectedProcessId());
        if (plan == null)
        {
            return;
        }

        var listRevision = string.Concat(
            list.ListId,
            ":",
            list.UpdatedAtUtc.ToUnixTimeMilliseconds().ToString(
                CultureInfo.InvariantCulture));
        var planFingerprint = string.Concat(
            BuildPlanFingerprint(plan),
            "|",
            this.BuildRouteContextFingerprint());
        if (!force &&
            string.Equals(
                listRevision,
                this.renderedListRevision,
                StringComparison.Ordinal) &&
            string.Equals(
                planFingerprint,
                this.renderedPlanFingerprint,
                StringComparison.Ordinal))
        {
            return;
        }

        this.HideGridToolTips();
        this.isRefreshing = true;
        try
        {
            if (force ||
                !string.Equals(
                    listRevision,
                    this.renderedListRevision,
                    StringComparison.Ordinal))
            {
                this.RenderRequestedOutputs(plan);
                this.RefreshSelectedListDetails();
            }

            this.RenderPlan(plan);
            this.renderedPlan = plan;
            this.renderedListRevision = listRevision;
            this.renderedPlanFingerprint = planFingerprint;
            this.UpdateStatus(plan);
        }
        finally
        {
            this.isRefreshing = false;
        }
    }

    private void RenderRequestedOutputs(ShoppingPlanSnapshot plan)
    {
        var selectedItemId = this.requestedGrid.SelectedRows.Count > 0 &&
            this.requestedGrid.SelectedRows[0].Tag is RequestedRow selected
                ? selected.Item.ItemTemplateId
                : 0;
        this.requestedGrid.Rows.Clear();

        foreach (var output in plan.ShoppingList.RequestedOutputs
                     .OrderBy(output =>
                         this.clientManager.GalaxyKnowledge.TryGetItem(
                             output.ItemTemplateId,
                             out var item)
                             ? item.Name
                             : output.ItemTemplateId.ToString(
                                 CultureInfo.InvariantCulture)))
        {
            if (!this.clientManager.GalaxyKnowledge.TryGetItem(
                    output.ItemTemplateId,
                    out var item))
            {
                continue;
            }

            var line = plan.Lines.FirstOrDefault(candidate =>
                candidate.ItemTemplateId == item.ItemTemplateId);
            var rowModel = new RequestedRow(item, output, line);
            var rowIndex = this.requestedGrid.Rows.Add();
            var gridRow = this.requestedGrid.Rows[rowIndex];
            gridRow.Tag = rowModel;
            gridRow.Cells[IconColumnName].Value = this.resolveIcon(
                item.ItemTemplateId,
                new Size(30, 30));
            gridRow.Cells[ItemColumnName].Value = item.Name;
            gridRow.Cells[DecreaseColumnName].Value =
                output.Quantity > 1
                    ? "−"
                    : "";
            gridRow.Cells[QuantityColumnName].Value = output.Quantity;
            gridRow.Cells[UnitColumnName].Value =
                item.Family == GalaxyItemFamily.Ammo
                    ? "stacks"
                    : "items";
            gridRow.Cells[IncreaseColumnName].Value = "+";
            gridRow.Cells[RecipeColumnName].Value =
                BuildRecipeText(line);
            gridRow.Cells[RemoveColumnName].Value = "Remove";
            gridRow.Cells[DecreaseColumnName].ReadOnly = true;
            gridRow.Cells[IncreaseColumnName].ReadOnly = true;
            gridRow.Cells[RecipeColumnName].ReadOnly = true;
            gridRow.Cells[RemoveColumnName].ReadOnly = true;
            if (output.Quantity <= 1)
            {
                gridRow.Cells[DecreaseColumnName].Style.ForeColor =
                    MainWindowTheme.MutedText;
                gridRow.Cells[DecreaseColumnName].Style.BackColor =
                    MainWindowTheme.Panel;
            }
            if (item.ItemTemplateId == selectedItemId)
            {
                gridRow.Selected = true;
            }
        }
    }

    private void RenderPlan(ShoppingPlanSnapshot plan)
    {
        var selectedItemId = this.planGrid.SelectedRows.Count > 0 &&
            this.planGrid.SelectedRows[0].Tag is PlanRow selected
                ? selected.Item.ItemTemplateId
                : 0;
        var firstDisplayed = this.planGrid.FirstDisplayedScrollingRowIndex;
        this.planGrid.Rows.Clear();

        var depths = BuildDepths(plan);
        var distances = this.getSelectedProcessId() is { } processId
            ? this.clientManager.GetNavigationRouteDistances(processId)
            : GalaxyRouteDistanceResult.Failure(
                "Select a hosted pilot to calculate hops.");
        var rows = plan.Lines
            .Where(line =>
                line.IsRequestedOutput ||
                this.showCompletedCheckBox.Checked ||
                line.ActiveShortfallQuantity > 0)
            .Select(line =>
            {
                this.clientManager.GalaxyKnowledge.TryGetItem(
                    line.ItemTemplateId,
                    out var item);
                var route = item == null
                    ? null
                    : GalaxyFinderRouteResolver.FindBestRoute(
                        this.clientManager.NavigationData,
                        item,
                        distances);
                var alreadyAtDestination =
                    this.IsAlreadyAtDestination(route);
                return item == null
                    ? null
                    : new PlanRow(
                        line,
                        item,
                        depths.GetValueOrDefault(line.ItemTemplateId),
                        route,
                        alreadyAtDestination);
            })
            .Where(row => row != null)
            .Cast<PlanRow>()
            .OrderBy(row => row.Depth)
            .ThenByDescending(row => row.Line.IsRequestedOutput)
            .ThenBy(row => row.Item.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.Item.ItemTemplateId)
            .ToArray();

        foreach (var rowModel in rows)
        {
            var rowIndex = this.planGrid.Rows.Add(
                this.resolveIcon(
                    rowModel.Item.ItemTemplateId,
                    new Size(30, 30)),
                rowModel.Depth <= 0
                    ? rowModel.Item.Name
                    : string.Concat("↳ ", rowModel.Item.Name),
                BuildNeedText(
                    rowModel.Line,
                    rowModel.Item),
                rowModel.Line.OwnedAvailableNow,
                rowModel.Line.OwnedElsewhere,
                BuildStillNeededText(
                    rowModel.Line,
                    rowModel.Item,
                    global: false),
                BuildStillNeededText(
                    rowModel.Line,
                    rowModel.Item,
                    global: true),
                BuildPlanText(
                    rowModel.Line,
                    rowModel.Item),
                BuildRecipeText(rowModel.Line),
                GalaxyFinderSourcePresentation.BuildCompactSummary(
                    rowModel.Item,
                    rowModel.Route),
                BuildHopsText(
                    rowModel.Route,
                    rowModel.AlreadyAtDestination),
                rowModel.Route == null ||
                rowModel.AlreadyAtDestination
                    ? ""
                    : "Set destination");
            var gridRow = this.planGrid.Rows[rowIndex];
            gridRow.Tag = rowModel;
            gridRow.Cells[ItemColumnName].Style.Padding = new Padding(
                5 + (Math.Min(rowModel.Depth, 8) * 18),
                0,
                5,
                0);
            if (rowModel.Item.ItemTemplateId == selectedItemId)
            {
                gridRow.Selected = true;
            }
        }

        if (this.planGrid.Rows.Count > 0 && firstDisplayed >= 0)
        {
            this.planGrid.FirstDisplayedScrollingRowIndex = Math.Min(
                firstDisplayed,
                this.planGrid.Rows.Count - 1);
        }
    }

    private void UpdateStatus(ShoppingPlanSnapshot plan)
    {
        var missingNow = plan.Lines.Count(line =>
            line.ActiveShortfallQuantity > 0);
        var missingGlobally = plan.Lines.Count(line =>
            line.GlobalShortfallQuantity > 0);
        var issueText = plan.Issues.Count == 0
            ? ""
            : string.Format(
                CultureInfo.CurrentCulture,
                " · {0:N0} planning warnings",
                plan.Issues.Count);
        var activePilot = string.IsNullOrWhiteSpace(
                plan.Ownership.ActivePilotName)
            ? "No active pilot selected"
            : string.Concat(
                "Available now: ",
                plan.Ownership.ActivePilotName);

        this.statusLabel.Text = string.Format(
            CultureInfo.CurrentCulture,
            "{0:N0} outputs · {1:N0} plan items · " +
            "{2:N0} item types still needed now · " +
            "{3:N0} after all archived pilots · {4}{5}",
            plan.ShoppingList.RequestedOutputs.Count,
            plan.Lines.Count,
            missingNow,
            missingGlobally,
            activePilot,
            issueText);
        var issueDetails = BuildIssueText(plan.Issues);
        this.contextualToolTip.SetToolTip(
            this.statusLabel,
            issueDetails.Length == 0
                ? null
                : BuildTextToolTipContent(
                    "Planning warnings",
                    issueDetails),
            delayMilliseconds: 300);
    }

    private void SetRequestedOutputQuantity(
        int itemTemplateId,
        long quantity)
    {
        var list = this.GetSelectedList();
        if (list == null)
        {
            return;
        }

        var outputs = list.RequestedOutputs
            .Select(output =>
                output.ItemTemplateId == itemTemplateId
                    ? output with { Quantity = quantity }
                    : output)
            .ToArray();
        this.clientManager.SaveShoppingList(
            list with { RequestedOutputs = outputs });
        this.RefreshPlan(force: true);
    }

    private void RemoveRequestedOutput(int itemTemplateId)
    {
        var list = this.GetSelectedList();
        if (list == null)
        {
            return;
        }

        this.clientManager.SaveShoppingList(
            list with
            {
                RequestedOutputs = list.RequestedOutputs
                    .Where(output =>
                        output.ItemTemplateId != itemTemplateId)
                    .ToArray(),
            });
        this.RefreshPlan(force: true);
    }

    private void ChooseRecipe(int itemTemplateId)
    {
        var list = this.GetSelectedList();
        if (list == null ||
            !this.clientManager.GalaxyKnowledge.TryGetItem(
                itemTemplateId,
                out var item) ||
            item.ProducedByRecipes.Count == 0)
        {
            return;
        }

        var current = list.RecipeSelections
            .LastOrDefault(selection =>
                selection.OutputItemTemplateId == itemTemplateId)?
            .RecipeIdentity ?? "";
        using var dialog = new ShoppingRecipeSelectionDialog(
            this.clientManager.GalaxyKnowledge,
            item,
            current);
        if (dialog.ShowDialog(this) != DialogResult.OK ||
            string.IsNullOrWhiteSpace(dialog.SelectedRecipeIdentity))
        {
            return;
        }

        var selections = list.RecipeSelections
            .Where(selection =>
                selection.OutputItemTemplateId != itemTemplateId)
            .Append(new ShoppingListRecipeSelection
            {
                OutputItemTemplateId = itemTemplateId,
                RecipeIdentity = dialog.SelectedRecipeIdentity,
            })
            .ToArray();
        this.clientManager.SaveShoppingList(
            list with { RecipeSelections = selections });
        this.RefreshPlan(force: true);
    }

    private void SetDestination(GalaxyFinderResolvedRoute? route)
    {
        if (route == null ||
            this.IsAlreadyAtDestination(route))
        {
            return;
        }

        if (this.getSelectedProcessId() is not { } processId)
        {
            ThemedMessageDialog.ShowWarning(
                this,
                "Shopping List",
                "Select a hosted pilot before setting a destination.");
            return;
        }

        var result = this.clientManager.SetNavigationDestination(
            processId,
            route.Destination);
        if (!result.Succeeded)
        {
            ThemedMessageDialog.ShowWarning(
                this,
                "Shopping List",
                result.Error);
            return;
        }

        this.statusLabel.Text = string.Concat(
            "Destination set: ",
            route.Destination.DisplayName,
            ". Start Auto Pilot when ready.");
    }

    private bool IsAlreadyAtDestination(
        GalaxyFinderResolvedRoute? route)
    {
        if (route == null ||
            this.getSelectedProcessId() is not { } processId)
        {
            return false;
        }

        var current = this.clientManager.GetNavigationCurrentLocation(
            processId);
        if (current == null ||
            !string.Equals(
                current.SectorKey,
                route.Destination.SectorKey,
                StringComparison.Ordinal))
        {
            return false;
        }

        if (route.Destination.Kind !=
                NavigationDestinationKind.Target)
        {
            return current.Kind != NavigationDestinationKind.Target;
        }

        if (current.Kind != NavigationDestinationKind.Target ||
            current.TargetKind != route.Destination.TargetKind)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(
                route.Destination.TargetKey) &&
            string.Equals(
                current.TargetKey,
                route.Destination.TargetKey,
                StringComparison.Ordinal))
        {
            return true;
        }

        return string.Equals(
            NormalizeDestinationName(current.TargetName),
            NormalizeDestinationName(route.Destination.TargetName),
            StringComparison.Ordinal);
    }

    private static string NormalizeDestinationName(string? value)
    {
        var normalized = GalaxyTopology.NormalizeName(value ?? "");
        const string suffix = " station";
        return normalized.EndsWith(
                suffix,
                StringComparison.Ordinal)
            ? normalized[..^suffix.Length]
            : normalized;
    }

    private static string BuildHopsText(
        GalaxyFinderResolvedRoute? route,
        bool alreadyAtDestination)
    {
        if (alreadyAtDestination)
        {
            return "Here";
        }

        if (route == null)
        {
            return "";
        }

        return route.Hops switch
        {
            0 => "Local",
            { } hops => hops.ToString(
                CultureInfo.CurrentCulture),
            _ => "—",
        };
    }

    private static string BuildRecipeText(ShoppingPlanLine? line)
    {
        if (line == null ||
            string.IsNullOrWhiteSpace(line.SelectedRecipeIdentity))
        {
            return "";
        }

        var kind = line.SelectedRecipeKind switch
        {
            GalaxyRecipeKind.Manufacture => "Manufacture",
            GalaxyRecipeKind.Refine => "Refine",
            _ => "Recipe",
        };
        return line.AlternativeRecipeIdentities.Count > 1
            ? string.Format(
                CultureInfo.CurrentCulture,
                "{0} · {1:N0} choices",
                kind,
                line.AlternativeRecipeIdentities.Count)
            : kind;
    }

    private static string BuildNeedText(
        ShoppingPlanLine line,
        GalaxyItemKnowledge item)
    {
        if (line.IsRequestedOutput &&
            item.Family == GalaxyItemFamily.Ammo)
        {
            return FormatStacks(line.RequestedQuantity);
        }

        return line.ActiveDemandQuantity.ToString(
            "N0",
            CultureInfo.CurrentCulture);
    }

    private static string BuildStillNeededText(
        ShoppingPlanLine line,
        GalaxyItemKnowledge item,
        bool global)
    {
        var quantity = global
            ? line.GlobalShortfallQuantity
            : line.ActiveShortfallQuantity;
        if (line.IsRequestedOutput &&
            item.Family == GalaxyItemFamily.Ammo &&
            item.MaximumStack > 0)
        {
            var stacks = DivideRoundUp(
                quantity,
                item.MaximumStack);
            return FormatStacks(stacks);
        }

        return quantity.ToString(
            "N0",
            CultureInfo.CurrentCulture);
    }

    private static string BuildPlanText(
        ShoppingPlanLine line,
        GalaxyItemKnowledge item)
    {
        if (line.ActiveShortfallQuantity <= 0)
        {
            return "Complete";
        }

        if (line.ActiveUnresolvedRecipeQuantity > 0)
        {
            return "Recipe unavailable";
        }

        var produceText = "";
        if (line.ActiveProduceQuantity > 0)
        {
            produceText = item.Family == GalaxyItemFamily.Ammo &&
                          item.MaximumStack > 0
                ? string.Concat(
                    "Make ",
                    FormatStacks(
                        DivideRoundUp(
                            line.ActiveProduceQuantity,
                            item.MaximumStack)))
                : string.Format(
                    CultureInfo.CurrentCulture,
                    "Make {0:N0}",
                    line.ActiveProduceQuantity);
        }

        var acquireText = line.ActiveAcquireQuantity > 0
            ? item.Family == GalaxyItemFamily.Ammo &&
              item.MaximumStack > 0
                ? string.Concat(
                    "Find ",
                    FormatStacks(
                        DivideRoundUp(
                            line.ActiveAcquireQuantity,
                            item.MaximumStack)))
                : string.Format(
                    CultureInfo.CurrentCulture,
                    "Find {0:N0}",
                    line.ActiveAcquireQuantity)
            : "";

        if (produceText.Length != 0 &&
            acquireText.Length != 0)
        {
            return string.Concat(
                produceText,
                " · ",
                acquireText);
        }

        if (produceText.Length != 0)
        {
            return produceText;
        }

        return acquireText.Length != 0
            ? acquireText
            : "Needed";
    }

    private static string FormatStacks(long quantity)
    {
        return string.Format(
            CultureInfo.CurrentCulture,
            "{0:N0} {1}",
            quantity,
            quantity == 1
                ? "stack"
                : "stacks");
    }

    private static long DivideRoundUp(
        long quantity,
        uint divisor)
    {
        if (quantity <= 0 || divisor == 0)
        {
            return 0;
        }

        return checked(
            (quantity / divisor) +
            (quantity % divisor == 0 ? 0 : 1));
    }

    private static IReadOnlyDictionary<int, int> BuildDepths(
        ShoppingPlanSnapshot plan)
    {
        Dictionary<int, int> depths = [];
        Queue<int> pending = new();
        foreach (var output in plan.ShoppingList.RequestedOutputs)
        {
            if (depths.TryAdd(output.ItemTemplateId, 0))
            {
                pending.Enqueue(output.ItemTemplateId);
            }
        }

        var edgesByParent = plan.Edges
            .GroupBy(edge => edge.ParentItemTemplateId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        while (pending.Count != 0)
        {
            var parent = pending.Dequeue();
            if (!edgesByParent.TryGetValue(parent, out var edges))
            {
                continue;
            }

            var depth = depths[parent] + 1;
            foreach (var edge in edges)
            {
                if (!depths.TryGetValue(
                        edge.IngredientItemTemplateId,
                        out var existing) ||
                    depth < existing)
                {
                    depths[edge.IngredientItemTemplateId] = depth;
                    pending.Enqueue(edge.IngredientItemTemplateId);
                }
            }
        }

        return depths;
    }

    private string BuildRouteContextFingerprint()
    {
        if (this.getSelectedProcessId() is not { } processId)
        {
            return string.Concat(
                "no-pilot:",
                this.clientManager.NavigationData.Revision.ToString(
                    CultureInfo.InvariantCulture));
        }

        var current = this.clientManager.GetNavigationCurrentLocation(
            processId);
        return string.Concat(
            processId.ToString(CultureInfo.InvariantCulture),
            ":",
            current?.SectorKey ?? "",
            ":",
            current?.Kind.ToString() ?? "",
            ":",
            current?.TargetKind.ToString() ?? "",
            ":",
            current?.TargetKey ?? "",
            ":",
            current?.TargetName ?? "",
            ":",
            this.clientManager.NavigationData.Revision.ToString(
                CultureInfo.InvariantCulture));
    }

    private static string BuildPlanFingerprint(
        ShoppingPlanSnapshot plan)
    {
        StringBuilder builder = new();
        builder.Append(plan.ShoppingList.ListId)
            .Append('|')
            .Append(plan.GalaxyKnowledgeIdentity)
            .Append('|')
            .Append(plan.Ownership.ActiveCharacterId)
            .Append('|');
        foreach (var line in plan.Lines)
        {
            builder.Append(line.ItemTemplateId).Append(':')
                .Append(line.RequestedQuantity).Append(':')
                .Append(line.OwnedAvailableNow).Append(':')
                .Append(line.OwnedElsewhere).Append(':')
                .Append(line.ActiveDemandQuantity).Append(':')
                .Append(line.ActiveShortfallQuantity).Append(':')
                .Append(line.ActiveProduceQuantity).Append(':')
                .Append(line.ActiveAcquireQuantity).Append(':')
                .Append(line.ActiveUnresolvedRecipeQuantity).Append(':')
                .Append(line.GlobalShortfallQuantity).Append(':')
                .Append(line.SelectedRecipeIdentity).Append(';');
        }

        foreach (var line in plan.Lines)
        {
            var ownership = plan.Ownership.GetItem(
                line.ItemTemplateId);
            foreach (var location in ownership.Locations)
            {
                builder.Append(location.CharacterId).Append(':')
                    .Append(location.Collection).Append(':')
                    .Append(location.Quantity).Append(':')
                    .Append(location.IsActivePilot).Append(';');
            }
        }

        foreach (var issue in plan.Issues)
        {
            builder.Append((int)issue.Kind).Append(':')
                .Append(issue.ItemTemplateId).Append(':')
                .Append(issue.RecipeIdentity).Append(';');
        }

        return builder.ToString();
    }

    private static string BuildIssueText(
        IReadOnlyList<ShoppingPlanIssue> issues)
    {
        if (issues.Count == 0)
        {
            return "";
        }

        return string.Join(
            Environment.NewLine,
            issues
                .Take(12)
                .Select(issue => issue.Message));
    }

    private sealed class EnterCommitDataGridView : DataGridView
    {
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Func<bool>? CommitCurrentEdit { get; set; }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            if ((keyData & Keys.KeyCode) == Keys.Enter &&
                (keyData & Keys.Modifiers) == Keys.None &&
                this.CommitCurrentEdit?.Invoke() == true)
            {
                return true;
            }

            return base.ProcessDialogKey(keyData);
        }

        protected override bool ProcessDataGridViewKey(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter &&
                e.Modifiers == Keys.None &&
                this.CommitCurrentEdit?.Invoke() == true)
            {
                return true;
            }

            return base.ProcessDataGridViewKey(e);
        }
    }

    private sealed record ShoppingListChoice(
        ShoppingListSummary Summary)
    {
        public override string ToString() => this.Summary.Name;
    }

    private sealed record RequestedRow(
        GalaxyItemKnowledge Item,
        ShoppingListRequestedOutput Output,
        ShoppingPlanLine? PlanLine);

    private sealed record PlanRow(
        ShoppingPlanLine Line,
        GalaxyItemKnowledge Item,
        int Depth,
        GalaxyFinderResolvedRoute? Route,
        bool AlreadyAtDestination);
}
