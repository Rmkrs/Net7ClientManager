// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using System.Diagnostics;
using System.Globalization;
using Net7ClientManager.Core;
using Net7ClientManager.Observations.Models;
using Net7ClientManager.Observations.Observers;
using Net7ClientManager.SkillPlanning;
using Net7ClientManager.Win32;

internal enum SkillBuildEquipmentPickerMode
{
    Choose,
    ReplaceOrAlternative,
    Alternative,
}

internal enum SkillBuildEquipmentPickerAction
{
    None,
    Choose,
    Replace,
    AddAlternative,
}

internal sealed class SkillBuildEquipmentPickerForm : ThemedForm
{
    private const int LevelColumnIndex = 0;
    private const int IconColumnIndex = 1;
    private const int NameColumnIndex = 2;
    private const int TypeColumnIndex = 3;

    private readonly SkillBuildEquipmentCatalog catalog;
    private readonly SkillBuildEquipmentKind kind;
    private readonly int professionIndex;
    private readonly SkillBuildEquipmentPickerMode mode;
    private readonly Func<int?, Size, Image?> resolveItemIcon;
    private readonly TextBox searchBox = new();
    private readonly ListView list = new();
    private readonly Button primaryButton = new();
    private readonly Button alternativeButton = new();
    private readonly ActionToolTip itemToolTip = new();
    private readonly Dictionary<ItemIconKey, Image?> itemIcons = [];
    private readonly ImageList rowHeightImages = new();
    private IReadOnlyList<SkillBuildEquipmentChoice> choices = [];
    private int sortColumn = NameColumnIndex;
    private SortOrder sortOrder = SortOrder.Ascending;
    private int hoveredItemTemplateId;

    public SkillBuildEquipmentPickerForm(
        SkillBuildEquipmentCatalog catalog,
        SkillBuildEquipmentKind kind,
        int professionIndex,
        Func<int?, Size, Image?> resolveItemIcon,
        SkillBuildEquipmentPickerMode mode =
            SkillBuildEquipmentPickerMode.Choose)
    {
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        this.kind = kind;
        this.professionIndex = professionIndex;
        this.mode = mode;
        this.resolveItemIcon = resolveItemIcon ??
            throw new ArgumentNullException(nameof(resolveItemIcon));

        this.Text = $"Choose {FormatKind(kind)}";
        this.Icon = ResourceLoader.Net7ClientManagerIcon;
        this.StartPosition = FormStartPosition.CenterParent;
        this.ShowInTaskbar = false;
        this.ClientSize = new Size(840, 620);
        this.MinimumSize = new Size(700, 520);
        this.BackColor = MainWindowTheme.Background;
        this.ForeColor = MainWindowTheme.Text;
        this.Font = MainWindowTheme.CreateBodyFont(10.0f);
        this.ConfigureWindowChrome(
            allowResize: true,
            showMinimizeButton: false,
            showMaximizeButton: false);

        this.ConfigureControls();
        this.choices = this.catalog.GetChoices(kind);
        this.RebuildList();
    }

    public SkillBuildEquipmentChoice? SelectedChoice { get; private set; }

    public SkillBuildEquipmentPickerAction SelectedAction { get; private set; }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.itemToolTip.Dispose();
            this.list.SmallImageList = null;
            this.rowHeightImages.Dispose();
            this.itemIcons.Clear();
        }

        base.Dispose(disposing);
    }

    private void ConfigureControls()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12),
            BackColor = MainWindowTheme.Background,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100.0f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42.0f));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100.0f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70.0f));

        this.searchBox.Dock = DockStyle.Fill;
        this.searchBox.Margin = new Padding(0, 0, 0, 8);
        this.searchBox.PlaceholderText = "Search equipment";
        MainWindowTheme.StyleTextBox(this.searchBox);
        this.searchBox.TextChanged += (_, _) => this.RunPickerCommand(
            "filter equipment",
            this.RebuildList);

        this.ConfigureList();

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = new Padding(0, 14, 0, 0),
            BackColor = Color.Transparent,
        };
        this.primaryButton.Text = this.mode switch
        {
            SkillBuildEquipmentPickerMode.ReplaceOrAlternative => "Replace",
            SkillBuildEquipmentPickerMode.Alternative => "Add alternative",
            _ => "Choose",
        };
        this.primaryButton.Width = this.mode == SkillBuildEquipmentPickerMode.Alternative
            ? 130
            : 110;
        this.primaryButton.Height = 34;
        this.primaryButton.Enabled = false;
        MainWindowTheme.StyleButton(this.primaryButton, primary: true);
        this.primaryButton.Click += (_, _) => this.RunPickerCommand(
            "select equipment",
            () => this.SelectCurrent(this.mode switch
            {
                SkillBuildEquipmentPickerMode.ReplaceOrAlternative =>
                    SkillBuildEquipmentPickerAction.Replace,
                SkillBuildEquipmentPickerMode.Alternative =>
                    SkillBuildEquipmentPickerAction.AddAlternative,
                _ => SkillBuildEquipmentPickerAction.Choose,
            }));

        if (this.mode == SkillBuildEquipmentPickerMode.ReplaceOrAlternative)
        {
            this.alternativeButton.Text = "Add alternative";
            this.alternativeButton.Width = 130;
            this.alternativeButton.Height = 34;
            this.alternativeButton.Enabled = false;
            MainWindowTheme.StyleButton(this.alternativeButton);
            this.alternativeButton.Click += (_, _) => this.RunPickerCommand(
                "add alternative equipment",
                () => this.SelectCurrent(
                    SkillBuildEquipmentPickerAction.AddAlternative));
        }

        var cancel = new Button
        {
            Text = "Cancel",
            Width = 90,
            Height = 34,
        };
        MainWindowTheme.StyleButton(cancel);
        cancel.Click += (_, _) => this.RunPickerCommand(
            "cancel equipment selection",
            () =>
            {
                this.DialogResult = DialogResult.Cancel;
                this.Close();
            });
        actions.Controls.Add(this.primaryButton);
        if (this.mode == SkillBuildEquipmentPickerMode.ReplaceOrAlternative)
        {
            actions.Controls.Add(this.alternativeButton);
        }
        actions.Controls.Add(cancel);

        root.Controls.Add(this.searchBox, 0, 0);
        root.Controls.Add(this.list, 0, 1);
        root.Controls.Add(actions, 0, 2);
        this.Controls.Add(root);
        this.AcceptButton = this.primaryButton;
        this.CancelButton = cancel;
    }

    private void ConfigureList()
    {
        this.list.Dock = DockStyle.Fill;
        this.list.Margin = Padding.Empty;
        this.list.View = View.Details;
        this.list.FullRowSelect = true;
        this.list.MultiSelect = false;
        this.list.HideSelection = false;
        this.list.HeaderStyle = ColumnHeaderStyle.Clickable;
        this.list.BorderStyle = BorderStyle.FixedSingle;
        this.list.BackColor = MainWindowTheme.ElevatedPanel;
        this.list.ForeColor = MainWindowTheme.Text;
        this.list.Font = MainWindowTheme.CreateBodyFont(10.0f);
        this.list.OwnerDraw = true;

        this.rowHeightImages.ColorDepth = ColorDepth.Depth32Bit;
        this.rowHeightImages.ImageSize = new Size(1, 34);
        this.rowHeightImages.Images.Add(new Bitmap(1, 34));
        this.list.SmallImageList = this.rowHeightImages;

        this.list.Columns.Add("Level", 64, HorizontalAlignment.Center);
        this.list.Columns.Add("", 48, HorizontalAlignment.Center);
        this.list.Columns.Add("Name", 430, HorizontalAlignment.Left);
        if (this.kind == SkillBuildEquipmentKind.Weapon)
        {
            this.list.Columns.Add("Type", 180, HorizontalAlignment.Left);
        }

        this.list.HandleCreated += (_, _) =>
            _ = NativeMethods.TryApplyDarkControlTheme(this.list.Handle);
        this.list.SizeChanged += (_, _) => this.ResizeColumns();
        this.list.ColumnClick += (_, e) => this.RunPickerCommand(
            "sort equipment",
            () => this.ChangeSort(e.Column));
        this.list.DrawColumnHeader += this.List_OnDrawColumnHeader;
        this.list.DrawItem += (_, e) => e.DrawDefault = false;
        this.list.DrawSubItem += this.List_OnDrawSubItem;
        this.list.SelectedIndexChanged += (_, _) =>
        {
            var enabled = this.list.SelectedItems.Count == 1 &&
                          this.list.SelectedItems[0].Tag is SkillBuildEquipmentChoice;
            this.primaryButton.Enabled = enabled;
            this.alternativeButton.Enabled = enabled;
        };
        this.list.DoubleClick += (_, _) => this.RunPickerCommand(
            "select equipment",
            () => this.SelectCurrent(this.mode switch
            {
                SkillBuildEquipmentPickerMode.ReplaceOrAlternative =>
                    SkillBuildEquipmentPickerAction.Replace,
                SkillBuildEquipmentPickerMode.Alternative =>
                    SkillBuildEquipmentPickerAction.AddAlternative,
                _ => SkillBuildEquipmentPickerAction.Choose,
            }));
        this.list.MouseMove += this.List_OnMouseMove;
        this.list.MouseLeave += (_, _) => this.ClearHoveredToolTip();
        this.list.MouseDown += (_, _) => this.ClearHoveredToolTip();
        this.list.MouseWheel += (_, _) => this.ClearHoveredToolTip();
    }

    private void RebuildList()
    {
        var query = this.searchBox.Text.Trim();
        var visible = this.choices
            .Where(this.IsCompatible)
            .Where(choice =>
                query.Length == 0 ||
                choice.Name.Contains(
                    query,
                    StringComparison.CurrentCultureIgnoreCase) ||
                choice.TypeDisplayName.Contains(
                    query,
                    StringComparison.CurrentCultureIgnoreCase))
            .ToArray();
        visible = this.SortChoices(visible).ToArray();

        this.ClearHoveredToolTip();
        this.list.BeginUpdate();
        try
        {
            this.primaryButton.Enabled = false;
            this.alternativeButton.Enabled = false;
            this.list.Items.Clear();
            foreach (var choice in visible)
            {
                var item = new ListViewItem(
                    choice.TechLevel > 0
                        ? choice.TechLevel.ToString(CultureInfo.CurrentCulture)
                        : "")
                {
                    Tag = choice,
                    ForeColor = MainWindowTheme.Text,
                    ImageIndex = 0,
                };
                item.SubItems.Add("");
                item.SubItems.Add(choice.Name);
                if (this.kind == SkillBuildEquipmentKind.Weapon)
                {
                    item.SubItems.Add(choice.TypeDisplayName);
                }
                this.list.Items.Add(item);
            }

            if (this.list.Items.Count == 0)
            {
                var empty = new ListViewItem("")
                {
                    ForeColor = MainWindowTheme.MutedText,
                    ImageIndex = 0,
                };
                empty.SubItems.Add("");
                empty.SubItems.Add(
                    this.choices.Count == 0
                        ? "The local equipment catalogue is not available."
                        : "No equipment matches the current filters.");
                if (this.kind == SkillBuildEquipmentKind.Weapon)
                {
                    empty.SubItems.Add("");
                }
                this.list.Items.Add(empty);
            }
        }
        finally
        {
            this.list.EndUpdate();
            this.UpdateColumnHeaders();
            this.ResizeColumns();
            if (this.list.IsHandleCreated)
            {
                _ = NativeMethods.TryApplyDarkControlTheme(this.list.Handle);
            }
        }
    }

    private IEnumerable<SkillBuildEquipmentChoice> SortChoices(
        IEnumerable<SkillBuildEquipmentChoice> source)
    {
        Func<SkillBuildEquipmentChoice, object> selector = this.sortColumn switch
        {
            LevelColumnIndex => value => value.TechLevel,
            IconColumnIndex => value => value.Name,
            TypeColumnIndex when this.kind == SkillBuildEquipmentKind.Weapon =>
                value => value.TypeDisplayName,
            _ => value => value.Name,
        };

        return this.sortOrder == SortOrder.Descending
            ? source
                .OrderByDescending(selector)
                .ThenBy(value => value.Name, StringComparer.CurrentCultureIgnoreCase)
            : source
                .OrderBy(selector)
                .ThenBy(value => value.Name, StringComparer.CurrentCultureIgnoreCase);
    }

    private void ChangeSort(int column)
    {
        if (column == IconColumnIndex)
        {
            return;
        }

        if (column < 0 || column >= this.list.Columns.Count)
        {
            return;
        }

        if (this.sortColumn == column)
        {
            this.sortOrder = this.sortOrder == SortOrder.Ascending
                ? SortOrder.Descending
                : SortOrder.Ascending;
        }
        else
        {
            this.sortColumn = column;
            this.sortOrder = SortOrder.Ascending;
        }

        this.RebuildList();
    }

    private void UpdateColumnHeaders()
    {
        var baseNames = this.kind == SkillBuildEquipmentKind.Weapon
            ? new[] { "Level", "", "Name", "Type" }
            : new[] { "Level", "", "Name" };
        for (var index = 0; index < this.list.Columns.Count; index++)
        {
            var suffix = index == this.sortColumn
                ? this.sortOrder == SortOrder.Descending
                    ? "  ▼"
                    : "  ▲"
                : "";
            this.list.Columns[index].Text = string.Concat(
                baseNames[index],
                suffix);
        }
    }

    private bool IsCompatible(SkillBuildEquipmentChoice choice) =>
        this.catalog.IsCompatible(choice, this.professionIndex);

    private void ResizeColumns()
    {
        var count = this.kind == SkillBuildEquipmentKind.Weapon ? 4 : 3;
        if (this.list.Columns.Count != count || this.list.ClientSize.Width <= 0)
        {
            return;
        }

        var available = Math.Max(
            360,
            this.list.ClientSize.Width -
            SystemInformation.VerticalScrollBarWidth - 6);
        const int level = 66;
        const int icon = 48;
        var type = this.kind == SkillBuildEquipmentKind.Weapon
            ? Math.Max(145, (int)Math.Round(available * 0.24))
            : 0;
        var name = Math.Max(180, available - level - icon - type);

        this.list.Columns[LevelColumnIndex].Width = level;
        this.list.Columns[IconColumnIndex].Width = icon;
        this.list.Columns[NameColumnIndex].Width = name;
        if (this.kind == SkillBuildEquipmentKind.Weapon)
        {
            this.list.Columns[TypeColumnIndex].Width = type;
        }
    }

    private void List_OnDrawColumnHeader(
        object? sender,
        DrawListViewColumnHeaderEventArgs e)
    {
        using var background = new SolidBrush(MainWindowTheme.Panel);
        e.Graphics.FillRectangle(background, e.Bounds);
        using var border = new Pen(MainWindowTheme.Border);
        e.Graphics.DrawRectangle(
            border,
            e.Bounds.X,
            e.Bounds.Y,
            Math.Max(0, e.Bounds.Width - 1),
            Math.Max(0, e.Bounds.Height - 1));

        var flags = TextFormatFlags.VerticalCenter |
                    TextFormatFlags.EndEllipsis |
                    TextFormatFlags.NoPadding;
        if (e.Header.TextAlign == HorizontalAlignment.Center)
        {
            flags |= TextFormatFlags.HorizontalCenter;
        }
        else if (e.Header.TextAlign == HorizontalAlignment.Right)
        {
            flags |= TextFormatFlags.Right;
        }
        TextRenderer.DrawText(
            e.Graphics,
            e.Header.Text,
            this.list.Font,
            Rectangle.Inflate(e.Bounds, -7, 0),
            MainWindowTheme.Text,
            flags);
    }

    private void List_OnDrawSubItem(
        object? sender,
        DrawListViewSubItemEventArgs e)
    {
        var selected = e.Item.Selected;
        using var background = new SolidBrush(
            selected
                ? MainWindowTheme.ButtonHover
                : e.ItemIndex % 2 == 0
                    ? MainWindowTheme.ElevatedPanel
                    : MainWindowTheme.Panel);
        e.Graphics.FillRectangle(background, e.Bounds);

        if (e.ColumnIndex == IconColumnIndex &&
            e.Item.Tag is SkillBuildEquipmentChoice choice)
        {
            var icon = this.GetItemIcon(
                choice.ItemTemplateId,
                new Size(28, 28));
            if (icon != null)
            {
                var x = e.Bounds.X + Math.Max(0, (e.Bounds.Width - 28) / 2);
                var y = e.Bounds.Y + Math.Max(0, (e.Bounds.Height - 28) / 2);
                e.Graphics.DrawImage(icon, new Rectangle(x, y, 28, 28));
            }
        }
        else
        {
            var flags = TextFormatFlags.VerticalCenter |
                        TextFormatFlags.EndEllipsis |
                        TextFormatFlags.NoPadding;
            if (e.Header.TextAlign == HorizontalAlignment.Center)
            {
                flags |= TextFormatFlags.HorizontalCenter;
            }
            TextRenderer.DrawText(
                e.Graphics,
                e.SubItem.Text,
                this.list.Font,
                Rectangle.Inflate(e.Bounds, -7, 0),
                e.Item.Tag is SkillBuildEquipmentChoice
                    ? MainWindowTheme.Text
                    : MainWindowTheme.MutedText,
                flags);
        }

        using var separator = new Pen(MainWindowTheme.Border);
        e.Graphics.DrawLine(
            separator,
            e.Bounds.Left,
            e.Bounds.Bottom - 1,
            e.Bounds.Right,
            e.Bounds.Bottom - 1);
        if (selected && e.ColumnIndex == this.list.Columns.Count - 1)
        {
            using var focus = new Pen(MainWindowTheme.AccentBorder);
            e.Graphics.DrawRectangle(
                focus,
                e.Item.Bounds.X,
                e.Item.Bounds.Y,
                Math.Max(0, e.Item.Bounds.Width - 1),
                Math.Max(0, e.Item.Bounds.Height - 1));
        }
    }

    private void List_OnMouseMove(object? sender, MouseEventArgs e)
    {
        var item = this.list.GetItemAt(e.X, e.Y);
        if (item?.Tag is not SkillBuildEquipmentChoice choice)
        {
            this.ClearHoveredToolTip();
            return;
        }

        if (this.hoveredItemTemplateId == choice.ItemTemplateId)
        {
            return;
        }

        this.hoveredItemTemplateId = choice.ItemTemplateId;
        ClientRuntimeItemTemplateObservation? template = null;
        if (ClientItemTemplateCatalog.TryGetRuntimeObservation(
                choice.ItemTemplateId,
                out var observed))
        {
            template = observed;
        }
        var icon = this.GetItemIcon(
            choice.ItemTemplateId,
            new Size(36, 36));
        var content = PilotArchiveItemToolTipBuilder.BuildCatalogItem(
            choice.ItemTemplateId,
            choice.Name,
            template,
            icon);
        this.itemToolTip.SetHoveredToolTip(
            this.list,
            content,
            SystemInformation.MouseHoverTime);
    }

    private void ClearHoveredToolTip()
    {
        this.hoveredItemTemplateId = 0;
        this.itemToolTip.Remove(this.list);
    }

    private Image? GetItemIcon(
        int itemTemplateId,
        Size size)
    {
        var key = new ItemIconKey(
            itemTemplateId,
            Math.Max(1, size.Width),
            Math.Max(1, size.Height));
        if (this.itemIcons.TryGetValue(key, out var cached))
        {
            return cached;
        }

        Image? icon = null;
        try
        {
            icon = this.resolveItemIcon(
                itemTemplateId,
                new Size(key.Width, key.Height));
        }
        catch (Exception exception)
        {
            Debug.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"[Builds] Could not load picker icon {itemTemplateId}: {exception}"));
        }
        this.itemIcons[key] = icon;
        return icon;
    }

    private void SelectCurrent(
        SkillBuildEquipmentPickerAction action)
    {
        if (this.list.SelectedItems.Count != 1 ||
            this.list.SelectedItems[0].Tag is not SkillBuildEquipmentChoice choice)
        {
            return;
        }

        this.SelectedChoice = choice;
        this.SelectedAction = action;
        this.DialogResult = DialogResult.OK;
        this.Close();
    }

    private void RunPickerCommand(
        string operation,
        Action command)
    {
        try
        {
            command();
        }
        catch (Exception exception)
        {
            Debug.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"[Builds] Could not {operation} in the equipment picker: {exception}"));

            if (this.IsDisposed || this.Disposing)
            {
                return;
            }

            var owner = this.Owner;
            try
            {
                this.DialogResult = DialogResult.Cancel;
                this.Close();
            }
            catch (Exception closeException)
            {
                Debug.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"[Builds] Could not close the failed equipment picker: {closeException}"));
            }

            try
            {
                MessageBox.Show(
                    owner,
                    string.Create(
                        CultureInfo.CurrentCulture,
                        $"Builds could not {operation}. The game client is still running.\r\n\r\n{exception.Message}"),
                    "Builds",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            catch (Exception messageException)
            {
                Debug.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"[Builds] Could not show the picker error: {messageException}"));
            }
        }
    }

    private static string FormatKind(SkillBuildEquipmentKind kind) =>
        kind switch
        {
            SkillBuildEquipmentKind.Weapon => "weapon",
            SkillBuildEquipmentKind.Shield => "shield",
            SkillBuildEquipmentKind.Reactor => "reactor",
            SkillBuildEquipmentKind.Engine => "engine",
            SkillBuildEquipmentKind.Device => "device",
            _ => "equipment",
        };

    private readonly record struct ItemIconKey(
        int ItemTemplateId,
        int Width,
        int Height);
}
