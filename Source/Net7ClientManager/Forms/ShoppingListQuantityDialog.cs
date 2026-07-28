// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using Net7ClientManager.Core;

internal sealed class ShoppingListQuantityDialog : ThemedForm
{
    private const long MaximumQuantity = 99_999;

    private readonly NumericUpDown quantityNumeric = new();
    private readonly Button acceptButton = new();

    public ShoppingListQuantityDialog(
        string title,
        string prompt,
        long initialQuantity = 1)
    {
        this.Text = title;
        this.Icon = ResourceLoader.Net7ClientManagerIcon;
        this.StartPosition = FormStartPosition.CenterParent;
        this.ShowInTaskbar = false;
        this.MinimumSize = new Size(320, 220);
        this.BackColor = MainWindowTheme.Background;
        this.ForeColor = MainWindowTheme.Text;
        this.Font = MainWindowTheme.CreateBodyFont();
        this.ConfigureWindowChrome(
            allowResize: false,
            showMinimizeButton: false,
            showMaximizeButton: false);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(24, 20, 24, 20),
            BackColor = MainWindowTheme.Background,
        };
        root.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(
            new RowStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(
            new RowStyle(SizeType.Absolute, 46f));
        root.RowStyles.Add(
            new RowStyle(SizeType.Absolute, 58f));

        var label = new Label
        {
            Dock = DockStyle.Fill,
            Text = prompt,
            ForeColor = MainWindowTheme.Text,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(4, 0, 4, 0),
        };

        var quantityPanel = new TableLayoutPanel
        {
            Anchor = AnchorStyles.None,
            Width = 144,
            Height = 36,
            ColumnCount = 3,
            RowCount = 1,
            Margin = Padding.Empty,
            BackColor = MainWindowTheme.Background,
        };
        quantityPanel.ColumnStyles.Add(
            new ColumnStyle(SizeType.Absolute, 24f));
        quantityPanel.ColumnStyles.Add(
            new ColumnStyle(SizeType.Absolute, 96f));
        quantityPanel.ColumnStyles.Add(
            new ColumnStyle(SizeType.Absolute, 24f));

        var minusButton = CreateNudgeButton("-");
        var plusButton = CreateNudgeButton("+");
        minusButton.Click += (_, _) =>
            this.quantityNumeric.Value = Math.Max(
                this.quantityNumeric.Minimum,
                this.quantityNumeric.Value - 1);
        plusButton.Click += (_, _) =>
            this.quantityNumeric.Value = Math.Min(
                this.quantityNumeric.Maximum,
                this.quantityNumeric.Value + 1);

        this.quantityNumeric.Anchor = AnchorStyles.None;
        this.quantityNumeric.Width = 92;
        this.quantityNumeric.Margin = Padding.Empty;
        this.quantityNumeric.Minimum = 1;
        this.quantityNumeric.Maximum = MaximumQuantity;
        this.quantityNumeric.Value = Math.Clamp(
            initialQuantity,
            1,
            MaximumQuantity);
        this.quantityNumeric.ThousandsSeparator = true;
        MainWindowTheme.StyleNumericUpDown(this.quantityNumeric);
        this.quantityNumeric.KeyDown +=
            this.QuantityNumeric_OnKeyDown;

        quantityPanel.Controls.Add(minusButton, 0, 0);
        quantityPanel.Controls.Add(this.quantityNumeric, 1, 0);
        quantityPanel.Controls.Add(plusButton, 2, 0);

        var cancelButton = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Width = 104,
            Height = 36,
            Margin = Padding.Empty,
        };
        MainWindowTheme.StyleButton(cancelButton);

        this.acceptButton.Text = "Add";
        this.acceptButton.DialogResult = DialogResult.OK;
        this.acceptButton.Width = 104;
        this.acceptButton.Height = 36;
        this.acceptButton.Margin = new Padding(8, 0, 0, 0);
        MainWindowTheme.StyleButton(
            this.acceptButton,
            primary: true);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 14, 0, 0),
            BackColor = MainWindowTheme.Background,
        };
        buttons.Controls.Add(this.acceptButton);
        buttons.Controls.Add(cancelButton);

        root.Controls.Add(label, 0, 0);
        root.Controls.Add(quantityPanel, 0, 1);
        root.Controls.Add(buttons, 0, 2);
        this.Controls.Add(root);

        this.AcceptButton = this.acceptButton;
        this.CancelButton = cancelButton;
        this.ResizeToContent();
    }

    public long Quantity =>
        decimal.ToInt64(this.quantityNumeric.Value);

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        this.quantityNumeric.KeyDown -=
            this.QuantityNumeric_OnKeyDown;
        base.OnFormClosed(e);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        this.ResizeToContent();
        this.quantityNumeric.Focus();
        this.quantityNumeric.Controls
            .OfType<TextBoxBase>()
            .FirstOrDefault()?
            .SelectAll();
    }

    private void QuantityNumeric_OnKeyDown(
        object? sender,
        KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter)
        {
            return;
        }

        e.Handled = true;
        e.SuppressKeyPress = true;

        // Moving focus commits text typed directly into NumericUpDown before
        // the dialog's normal accept action reads Value.
        this.acceptButton.Focus();

        try
        {
            this.BeginInvoke((Action)this.acceptButton.PerformClick);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static Button CreateNudgeButton(string text)
    {
        var button = new Button
        {
            Text = text,
            Anchor = AnchorStyles.None,
            Width = 22,
            Height = 22,
            Margin = Padding.Empty,
        };
        MainWindowTheme.StyleButton(button);
        return button;
    }

    private void ResizeToContent()
    {
        var scale = this.DeviceDpi / 96f;
        this.ClientSize = new Size(
            Math.Max(
                300,
                (int)Math.Ceiling(300 * scale)),
            Math.Max(
                190,
                (int)Math.Ceiling(190 * scale)));
    }
}
