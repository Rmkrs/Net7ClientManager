// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using System.Globalization;
using Net7ClientManager.Shopping;

internal sealed class VendorShoppingCompanionForm : Form
{
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const int WmMouseActivate = 0x0021;
    private const int MaNoActivate = 3;
    private const int WmNcHitTest = 0x0084;
    private static readonly IntPtr HtTransparent = new(-1);

    private const int HeaderHeight = 24;
    private const int SummaryHeight = 20;
    private const int PurchaseRowHeight = 23;
    private const int MoreRowHeight = 18;
    private const int MaximumVisiblePurchases = 4;

    private readonly Panel content = new();
    private readonly Label heading = new();
    private readonly Label summary = new();
    private readonly BufferedTableLayoutPanel purchases = new();
    private VendorShoppingCompanionPresentation presentation =
        VendorShoppingCompanionPresentation.Hidden;
    private string fingerprint = "";
    private string layoutFingerprint = "";

    public VendorShoppingCompanionForm()
    {
        this.FormBorderStyle = FormBorderStyle.None;
        this.ShowInTaskbar = false;
        this.StartPosition = FormStartPosition.Manual;
        this.AutoScaleMode = AutoScaleMode.None;
        this.BackColor = MainWindowTheme.AccentBorder;
        this.Padding = new Padding(1);
        this.DoubleBuffered = true;

        this.content.Dock = DockStyle.Fill;
        this.content.Margin = Padding.Empty;
        this.content.Padding = Padding.Empty;
        this.content.BackColor = MainWindowTheme.Background;

        this.heading.Dock = DockStyle.Top;
        this.heading.Height = HeaderHeight;
        this.heading.Margin = Padding.Empty;
        this.heading.Padding = new Padding(2, 0, 2, 0);
        this.heading.TextAlign = ContentAlignment.MiddleLeft;
        this.heading.Font = MainWindowTheme.CreateHeadingFont(9.0f);
        this.heading.ForeColor = MainWindowTheme.Accent;
        this.heading.BackColor = MainWindowTheme.Panel;
        this.heading.AutoEllipsis = true;

        this.summary.Dock = DockStyle.Top;
        this.summary.Height = SummaryHeight;
        this.summary.Margin = Padding.Empty;
        this.summary.Padding = new Padding(7, 0, 7, 0);
        this.summary.TextAlign = ContentAlignment.MiddleLeft;
        this.summary.Font = MainWindowTheme.CreateBodyFont(8.0f);
        this.summary.ForeColor = MainWindowTheme.MutedText;
        this.summary.BackColor = MainWindowTheme.Background;
        this.summary.AutoEllipsis = true;

        this.purchases.Dock = DockStyle.Fill;
        this.purchases.Margin = Padding.Empty;
        this.purchases.Padding = new Padding(6, 0, 6, 4);
        this.purchases.ColumnCount = 1;
        this.purchases.RowCount = 1;
        this.purchases.BackColor = MainWindowTheme.Background;
        this.purchases.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 100f));

        this.content.Controls.Add(this.purchases);
        this.content.Controls.Add(this.summary);
        this.content.Controls.Add(this.heading);
        this.Controls.Add(this.content);
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= WsExToolWindow | WsExNoActivate;
            return parameters;
        }
    }

    public void SetPresentation(
        VendorShoppingCompanionPresentation nextPresentation)
    {
        ArgumentNullException.ThrowIfNull(nextPresentation);

        if (string.Equals(
                this.fingerprint,
                nextPresentation.Fingerprint,
                StringComparison.Ordinal))
        {
            return;
        }

        this.presentation = nextPresentation;
        this.fingerprint = nextPresentation.Fingerprint;
        var nextLayoutFingerprint =
            BuildLayoutFingerprint(nextPresentation);

        if (string.Equals(
                this.layoutFingerprint,
                nextLayoutFingerprint,
                StringComparison.Ordinal))
        {
            this.UpdateContent();
            return;
        }

        this.layoutFingerprint = nextLayoutFingerprint;
        this.RebuildContent();
    }

    public int GetPreferredHeight(int maximumHeight)
    {
        maximumHeight = Math.Max(1, maximumHeight);
        var visibleCount = Math.Min(
            MaximumVisiblePurchases,
            this.presentation.Lines.Count);
        var hiddenCount = Math.Max(
            0,
            this.presentation.Lines.Count - visibleCount);
        var preferredHeight =
            this.Padding.Vertical +
            this.purchases.Padding.Vertical +
            HeaderHeight +
            SummaryHeight +
            (visibleCount * PurchaseRowHeight) +
            (hiddenCount > 0 ? MoreRowHeight : 0);

        return Math.Clamp(
            preferredHeight,
            Math.Min(76, maximumHeight),
            maximumHeight);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmNcHitTest)
        {
            message.Result = HtTransparent;
            return;
        }

        if (message.Msg == WmMouseActivate)
        {
            message.Result = (IntPtr)MaNoActivate;
            return;
        }

        base.WndProc(ref message);
    }

    private void RebuildContent()
    {
        this.SuspendLayout();
        this.purchases.SuspendLayout();

        try
        {
            this.UpdateHeader();

            this.DisposePurchaseControls();
            this.purchases.RowStyles.Clear();
            var visibleLines = this.presentation.Lines
                .Take(MaximumVisiblePurchases)
                .ToArray();
            var hiddenCount = Math.Max(
                0,
                this.presentation.Lines.Count - visibleLines.Length);
            this.purchases.RowCount = Math.Max(
                1,
                visibleLines.Length + (hiddenCount > 0 ? 1 : 0));

            for (var index = 0; index < visibleLines.Length; index++)
            {
                this.purchases.RowStyles.Add(
                    new RowStyle(
                        SizeType.Absolute,
                        PurchaseRowHeight));
                this.purchases.Controls.Add(
                    new PurchaseRowControl(visibleLines[index]),
                    0,
                    index);
            }

            if (hiddenCount > 0)
            {
                var rowIndex = visibleLines.Length;
                this.purchases.RowStyles.Add(
                    new RowStyle(
                        SizeType.Absolute,
                        MoreRowHeight));
                this.purchases.Controls.Add(
                    new Label
                    {
                        Dock = DockStyle.Fill,
                        Margin = Padding.Empty,
                        Padding = new Padding(2, 0, 2, 0),
                        Text = string.Create(
                            CultureInfo.InvariantCulture,
                            $"+{hiddenCount} more {Pluralize(hiddenCount, "item", "items")}"),
                        TextAlign = ContentAlignment.MiddleLeft,
                        ForeColor = MainWindowTheme.MutedText,
                        Font = MainWindowTheme.CreateBodyFont(8.0f),
                        AutoEllipsis = true,
                    },
                    0,
                    rowIndex);
            }

            if (visibleLines.Length == 0)
            {
                this.purchases.RowStyles.Add(
                    new RowStyle(SizeType.Percent, 100f));
            }
        }
        finally
        {
            this.purchases.ResumeLayout(performLayout: true);
            this.ResumeLayout(performLayout: true);
        }
    }

    private void UpdateContent()
    {
        this.SuspendLayout();
        this.purchases.SuspendLayout();

        try
        {
            this.UpdateHeader();
            var visibleLines = this.presentation.Lines
                .Take(MaximumVisiblePurchases)
                .ToArray();

            for (var index = 0; index < visibleLines.Length; index++)
            {
                if (this.purchases.GetControlFromPosition(0, index) is
                    PurchaseRowControl row)
                {
                    row.Update(visibleLines[index]);
                }
            }
        }
        finally
        {
            this.purchases.ResumeLayout(performLayout: false);
            this.ResumeLayout(performLayout: false);
        }
    }

    private void UpdateHeader()
    {
        this.heading.Text = string.Concat(
            "SHOPPING · ",
            this.presentation.ShoppingListName);
        var itemCount = this.presentation.Lines.Count;
        this.summary.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"{this.presentation.VendorName} · {itemCount} {Pluralize(itemCount, "item", "items")} needed here");
    }

    private void DisposePurchaseControls()
    {
        var controls = this.purchases.Controls
            .Cast<Control>()
            .ToArray();
        this.purchases.Controls.Clear();

        foreach (var control in controls)
        {
            control.Dispose();
        }
    }

    private static string BuildLayoutFingerprint(
        VendorShoppingCompanionPresentation presentation)
    {
        var visibleItemIds = presentation.Lines
            .Take(MaximumVisiblePurchases)
            .Select(line => line.ItemTemplateId.ToString(
                CultureInfo.InvariantCulture));
        var hiddenCount = Math.Max(
            0,
            presentation.Lines.Count - MaximumVisiblePurchases);

        return string.Concat(
            string.Join(",", visibleItemIds),
            "|",
            hiddenCount.ToString(CultureInfo.InvariantCulture));
    }

    private sealed class PurchaseRowControl : BufferedTableLayoutPanel
    {
        private readonly Label name = new();
        private readonly Label quantity = new();

        public PurchaseRowControl(
            VendorShoppingCompanionLine line)
        {
            this.Dock = DockStyle.Fill;
            this.ColumnCount = 2;
            this.RowCount = 1;
            this.Margin = Padding.Empty;
            this.Padding = Padding.Empty;
            this.BackColor = MainWindowTheme.ElevatedPanel;
            this.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, 100f));
            this.ColumnStyles.Add(
                new ColumnStyle(SizeType.AutoSize));

            this.name.Dock = DockStyle.Fill;
            this.name.Margin = Padding.Empty;
            this.name.Padding = new Padding(7, 0, 6, 0);
            this.name.TextAlign = ContentAlignment.MiddleLeft;
            this.name.ForeColor = MainWindowTheme.Text;
            this.name.Font = MainWindowTheme.CreateBodyFont(8.0f);
            this.name.AutoEllipsis = true;

            this.quantity.AutoSize = true;
            this.quantity.Dock = DockStyle.Fill;
            this.quantity.Margin = Padding.Empty;
            this.quantity.Padding = new Padding(4, 0, 7, 0);
            this.quantity.TextAlign = ContentAlignment.MiddleRight;
            this.quantity.ForeColor = MainWindowTheme.Success;
            this.quantity.Font = MainWindowTheme.CreateBodyFont(8.0f);

            this.Controls.Add(this.name, 0, 0);
            this.Controls.Add(this.quantity, 1, 0);
            this.Update(line);
        }

        public void Update(VendorShoppingCompanionLine line)
        {
            this.name.Text = line.ItemName;
            this.quantity.Text = BuildQuantityText(line);
        }
    }

    private class BufferedTableLayoutPanel : TableLayoutPanel
    {
        public BufferedTableLayoutPanel()
        {
            this.SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer,
                true);
            this.UpdateStyles();
        }
    }

    private static string BuildQuantityText(
        VendorShoppingCompanionLine line)
    {
        if (line.PackageQuantity <= 1)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"Need {line.RemainingQuantity:N0}");
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"Need {line.RemainingQuantity:N0} · {line.PurchaseCount:N0} {Pluralize(line.PurchaseCount, "stack", "stacks")}");
    }

    private static string Pluralize(
        long count,
        string singular,
        string plural) =>
        count == 1 ? singular : plural;
}
