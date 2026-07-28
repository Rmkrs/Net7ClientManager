namespace Net7ClientManager.Forms;

using System.Collections.Concurrent;

internal static class MainWindowTheme
{
    private static readonly ConcurrentDictionary<FontKey, Font> fonts = new();

    public static readonly Color Background = Color.FromArgb(10, 15, 22);

    public static readonly Color Header = Color.FromArgb(14, 22, 31);

    public static readonly Color Panel = Color.FromArgb(18, 28, 39);

    public static readonly Color ElevatedPanel = Color.FromArgb(23, 37, 50);

    public static readonly Color Border = Color.FromArgb(45, 87, 108);

    public static readonly Color AccentBorder = Color.FromArgb(51, 166, 199);

    public static readonly Color Text = Color.FromArgb(235, 242, 247);

    public static readonly Color MutedText = Color.FromArgb(153, 174, 190);

    public static readonly Color Accent = Color.FromArgb(77, 211, 239);

    public static readonly Color Success = Color.FromArgb(77, 213, 142);

    public static readonly Color Warning = Color.FromArgb(242, 188, 73);

    public static readonly Color Danger = Color.FromArgb(236, 94, 94);

    public static readonly Color Button = Color.FromArgb(20, 34, 46);

    public static readonly Color ButtonHover = Color.FromArgb(30, 73, 94);

    public static readonly Color DisabledButton = Color.FromArgb(16, 28, 38);

    public static readonly Color DisabledText = Color.FromArgb(126, 149, 166);

    private static readonly ToolStripRenderer contextMenuRenderer =
        new DarkToolStripRenderer();

    public static Font CreateHeadingFont(float size = 12.0f)
    {
        return GetFont(
            "Segoe UI Semibold",
            size,
            FontStyle.Bold);
    }

    public static Font CreateBodyFont(float size = 9.0f)
    {
        return GetFont(
            "Segoe UI",
            size,
            FontStyle.Regular);
    }

    private static Font GetFont(
        string familyName,
        float size,
        FontStyle style)
    {
        return fonts.GetOrAdd(
            new FontKey(familyName, size, style),
            static key => new Font(
                key.FamilyName,
                key.Size,
                key.Style));
    }

    private sealed record FontKey(
        string FamilyName,
        float Size,
        FontStyle Style);

    public static void StyleButton(
        Button button,
        bool primary = false,
        bool danger = false)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.UseVisualStyleBackColor = false;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = primary
            ? AccentBorder
            : danger
                ? Danger
                : Border;
        button.FlatAppearance.MouseOverBackColor = ButtonHover;
        button.FlatAppearance.MouseDownBackColor = ElevatedPanel;
        button.BackColor = primary
            ? Color.FromArgb(24, 64, 82)
            : danger
                ? Color.FromArgb(55, 29, 36)
                : Button;
        button.ForeColor = primary
            ? Accent
            : danger
                ? Danger
                : Text;
        button.Font = CreateBodyFont();
        button.Cursor = button.Enabled
            ? Cursors.Hand
            : Cursors.Default;

        button.Paint -= Button_OnPaint;
        button.Paint += Button_OnPaint;
        button.EnabledChanged -= Button_OnEnabledChanged;
        button.EnabledChanged += Button_OnEnabledChanged;
    }

    private static void Button_OnEnabledChanged(
        object? sender,
        EventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        button.Cursor = button.Enabled
            ? Cursors.Hand
            : Cursors.Default;
        button.Invalidate();
    }

    private static void Button_OnPaint(
        object? sender,
        PaintEventArgs e)
    {
        if (sender is not Button { Enabled: false } button)
        {
            return;
        }

        using var backgroundBrush = new SolidBrush(DisabledButton);
        e.Graphics.FillRectangle(backgroundBrush, button.ClientRectangle);

        using var borderPen = new Pen(Border);
        e.Graphics.DrawRectangle(
            borderPen,
            x: 0,
            y: 0,
            width: Math.Max(0, button.ClientSize.Width - 1),
            height: Math.Max(0, button.ClientSize.Height - 1));

        var textBounds = Rectangle.FromLTRB(
            button.Padding.Left,
            button.Padding.Top,
            Math.Max(button.Padding.Left, button.ClientSize.Width - button.Padding.Right),
            Math.Max(button.Padding.Top, button.ClientSize.Height - button.Padding.Bottom));

        TextRenderer.DrawText(
            e.Graphics,
            button.Text,
            button.Font,
            textBounds,
            DisabledText,
            GetButtonTextFormatFlags(button.TextAlign));
    }

    private static TextFormatFlags GetButtonTextFormatFlags(
        ContentAlignment textAlign)
    {
        var flags = TextFormatFlags.EndEllipsis |
                    TextFormatFlags.NoPadding |
                    TextFormatFlags.SingleLine;

        flags |= textAlign switch
        {
            ContentAlignment.TopRight or
            ContentAlignment.MiddleRight or
            ContentAlignment.BottomRight => TextFormatFlags.Right,
            ContentAlignment.TopCenter or
            ContentAlignment.MiddleCenter or
            ContentAlignment.BottomCenter => TextFormatFlags.HorizontalCenter,
            _ => (TextFormatFlags)0,
        };

        flags |= textAlign switch
        {
            ContentAlignment.BottomLeft or
            ContentAlignment.BottomCenter or
            ContentAlignment.BottomRight => TextFormatFlags.Bottom,
            ContentAlignment.MiddleLeft or
            ContentAlignment.MiddleCenter or
            ContentAlignment.MiddleRight => TextFormatFlags.VerticalCenter,
            _ => (TextFormatFlags)0,
        };

        return flags;
    }

    public static void StyleComboBox(ComboBox comboBox)
    {
        comboBox.BackColor = ElevatedPanel;
        comboBox.ForeColor = Text;
        comboBox.FlatStyle = FlatStyle.Flat;
        comboBox.Font = CreateBodyFont();
    }

    public static void StyleTextBox(TextBox textBox)
    {
        textBox.BackColor = ElevatedPanel;
        textBox.ForeColor = Text;
        textBox.BorderStyle = BorderStyle.FixedSingle;
        textBox.Font = CreateBodyFont();
    }

    public static void StyleNumericUpDown(NumericUpDown numericUpDown)
    {
        numericUpDown.BackColor = ElevatedPanel;
        numericUpDown.ForeColor = Text;
        numericUpDown.BorderStyle = BorderStyle.FixedSingle;
        numericUpDown.Font = CreateBodyFont();
    }

    public static void StyleContextMenu(ContextMenuStrip menu)
    {
        ApplyContextMenuTheme(menu);
    }

    private static void ApplyContextMenuTheme(ToolStripDropDown menu)
    {
        menu.BackColor = ElevatedPanel;
        menu.ForeColor = Text;
        menu.Font = CreateBodyFont();
        menu.RenderMode = ToolStripRenderMode.Professional;
        menu.Renderer = contextMenuRenderer;
        if (menu is ContextMenuStrip contextMenu)
        {
            contextMenu.ShowImageMargin = false;
            contextMenu.ShowCheckMargin = false;
        }

        foreach (ToolStripItem item in menu.Items)
        {
            item.BackColor = ElevatedPanel;
            item.ForeColor = item.Enabled ? Text : DisabledText;
            if (item is ToolStripDropDownItem dropDownItem &&
                dropDownItem.HasDropDownItems)
            {
                ApplyContextMenuTheme(dropDownItem.DropDown);
            }
        }
    }

    private sealed class DarkToolStripRenderer : ToolStripProfessionalRenderer
    {
        public DarkToolStripRenderer()
            : base(new DarkColorTable())
        {
            this.RoundedEdges = false;
        }

        protected override void OnRenderArrow(
            ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = e.Item.Enabled ? Text : DisabledText;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemText(
            ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Text : DisabledText;
            base.OnRenderItemText(e);
        }
    }

    private sealed class DarkColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => ElevatedPanel;

        public override Color ImageMarginGradientBegin => ElevatedPanel;

        public override Color ImageMarginGradientMiddle => ElevatedPanel;

        public override Color ImageMarginGradientEnd => ElevatedPanel;

        public override Color MenuBorder => Border;

        public override Color MenuItemBorder => AccentBorder;

        public override Color MenuItemSelected => ButtonHover;

        public override Color MenuItemSelectedGradientBegin => ButtonHover;

        public override Color MenuItemSelectedGradientEnd => ButtonHover;

        public override Color MenuItemPressedGradientBegin => Panel;

        public override Color MenuItemPressedGradientMiddle => Panel;

        public override Color MenuItemPressedGradientEnd => Panel;

        public override Color SeparatorDark => Border;

        public override Color SeparatorLight => ElevatedPanel;
    }

}
