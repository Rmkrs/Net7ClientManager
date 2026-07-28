// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using System.Drawing.Text;

internal sealed class AddonEditorFontDialog : AddonCenterDialogForm
{
    private static readonly int[] fontSizes =
    [
        8,
        9,
        10,
        11,
        12,
        13,
        14,
        16,
        18,
        20,
        22,
        24,
        26,
        28,
    ];

    private readonly ComboBox familyComboBox = new();
    private readonly ComboBox sizeComboBox = new();
    private readonly TextBox previewTextBox = new();
    private readonly Button applyButton = new();
    private Font? previewFont;

    public AddonEditorFontDialog(
        string currentFamily,
        int currentSize)
        : base("Addon Editor Font", new Size(620, 410))
    {
        var heading = new Label
        {
            Text = "EDITOR TYPEFACE",
            Font = new Font("Segoe UI", 14.0f, FontStyle.Bold),
            ForeColor = AddonCenterTheme.Text,
            Location = new Point(24, 20),
            Size = new Size(560, 32),
        };

        var description = new Label
        {
            Text = string.Concat(
                "Choose the font family and size used by the Lua and JSON editor. ",
                "Syntax colors continue to follow the Net7 theme."),
            ForeColor = AddonCenterTheme.MutedText,
            Location = new Point(26, 58),
            Size = new Size(560, 46),
        };

        this.ContentPanel.Controls.Add(new Label
        {
            Text = "FONT FAMILY",
            ForeColor = AddonCenterTheme.MutedText,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            Location = new Point(26, 116),
            Size = new Size(116, 26),
            TextAlign = ContentAlignment.MiddleLeft,
        });

        this.familyComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        this.familyComboBox.BackColor = AddonCenterTheme.Button;
        this.familyComboBox.ForeColor = AddonCenterTheme.Text;
        this.familyComboBox.FlatStyle = FlatStyle.Flat;
        this.familyComboBox.Location = new Point(152, 116);
        this.familyComboBox.Size = new Size(326, 28);

        using (var fonts = new InstalledFontCollection())
        {
            foreach (var family in fonts.Families
                         .Select(item => item.Name)
                         .Where(name => !name.StartsWith("@", StringComparison.Ordinal))
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
            {
                this.familyComboBox.Items.Add(family);
            }
        }

        var selectedFamily = this.familyComboBox.Items
            .Cast<string>()
            .FirstOrDefault(name => string.Equals(
                name,
                currentFamily,
                StringComparison.OrdinalIgnoreCase)) ??
            this.familyComboBox.Items
                .Cast<string>()
                .FirstOrDefault(name => string.Equals(
                    name,
                    "Consolas",
                    StringComparison.OrdinalIgnoreCase)) ??
            this.familyComboBox.Items.Cast<string>().FirstOrDefault();

        if (selectedFamily != null)
        {
            this.familyComboBox.SelectedItem = selectedFamily;
        }

        this.ContentPanel.Controls.Add(new Label
        {
            Text = "FONT SIZE",
            ForeColor = AddonCenterTheme.MutedText,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            Location = new Point(26, 156),
            Size = new Size(116, 26),
            TextAlign = ContentAlignment.MiddleLeft,
        });

        this.sizeComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        this.sizeComboBox.BackColor = AddonCenterTheme.Button;
        this.sizeComboBox.ForeColor = AddonCenterTheme.Text;
        this.sizeComboBox.FlatStyle = FlatStyle.Flat;
        this.sizeComboBox.Location = new Point(152, 156);
        this.sizeComboBox.Size = new Size(100, 28);

        foreach (var size in fontSizes)
        {
            this.sizeComboBox.Items.Add(size);
        }

        var normalizedSize = fontSizes.Contains(currentSize)
            ? currentSize
            : fontSizes.OrderBy(size => Math.Abs(size - currentSize)).First();
        this.sizeComboBox.SelectedItem = normalizedSize;

        var previewLabel = new Label
        {
            Text = "PREVIEW",
            ForeColor = AddonCenterTheme.MutedText,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            Location = new Point(26, 202),
            Size = new Size(116, 24),
            TextAlign = ContentAlignment.MiddleLeft,
        };

        this.previewTextBox.Multiline = true;
        this.previewTextBox.ReadOnly = true;
        this.previewTextBox.WordWrap = false;
        this.previewTextBox.ScrollBars = ScrollBars.Both;
        this.previewTextBox.BackColor = AddonCenterTheme.Background;
        this.previewTextBox.ForeColor = AddonCenterTheme.Text;
        this.previewTextBox.BorderStyle = BorderStyle.FixedSingle;
        this.previewTextBox.Location = new Point(26, 228);
        this.previewTextBox.Size = new Size(568, 112);
        this.previewTextBox.Text =
            "addon.on_load(function()\r\n" +
            "    addon.log.info(\"Hello, Net7!\")\r\n" +
            "end)";

        var cancelButton = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new Point(394, 356),
            Size = new Size(96, 36),
        };
        AddonCenterTheme.StyleButton(cancelButton);

        this.applyButton.Text = "Apply";
        this.applyButton.DialogResult = DialogResult.OK;
        this.applyButton.Location = new Point(498, 356);
        this.applyButton.Size = new Size(96, 36);
        AddonCenterTheme.StyleButton(
            this.applyButton,
            AddonCenterTheme.Success);

        this.familyComboBox.SelectedIndexChanged +=
            this.FontSelection_OnChanged;
        this.sizeComboBox.SelectedIndexChanged +=
            this.FontSelection_OnChanged;

        this.AcceptButton = this.applyButton;
        this.CancelButton = cancelButton;
        this.ContentPanel.Controls.Add(heading);
        this.ContentPanel.Controls.Add(description);
        this.ContentPanel.Controls.Add(this.familyComboBox);
        this.ContentPanel.Controls.Add(this.sizeComboBox);
        this.ContentPanel.Controls.Add(previewLabel);
        this.ContentPanel.Controls.Add(this.previewTextBox);
        this.ContentPanel.Controls.Add(cancelButton);
        this.ContentPanel.Controls.Add(this.applyButton);
        this.UpdatePreview();
    }

    public string SelectedFontFamily =>
        this.familyComboBox.SelectedItem as string ?? "Consolas";

    public int SelectedFontSize =>
        this.sizeComboBox.SelectedItem is int size
            ? size
            : 10;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.familyComboBox.SelectedIndexChanged -=
                this.FontSelection_OnChanged;
            this.sizeComboBox.SelectedIndexChanged -=
                this.FontSelection_OnChanged;
            this.previewFont?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void FontSelection_OnChanged(object? sender, EventArgs e)
    {
        this.UpdatePreview();
    }

    private void UpdatePreview()
    {
        var family = this.SelectedFontFamily;
        var size = this.SelectedFontSize;

        try
        {
            var font = new Font(
                family,
                size,
                FontStyle.Regular,
                GraphicsUnit.Point);
            var oldFont = this.previewFont;
            this.previewFont = font;
            this.previewTextBox.Font = font;
            oldFont?.Dispose();
            this.applyButton.Enabled = true;
        }
        catch (ArgumentException)
        {
            this.applyButton.Enabled = false;
        }
    }
}
