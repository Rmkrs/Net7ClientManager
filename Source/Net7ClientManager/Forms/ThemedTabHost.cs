using System.ComponentModel;

namespace Net7ClientManager.Forms;

internal sealed class ThemedTabPage : Panel
{
    public ThemedTabPage(string title, bool canClose = false)
    {
        this.Title = title;
        this.CanClose = canClose;
        this.Dock = DockStyle.Fill;
        this.BackColor = MainWindowTheme.Panel;
        this.ForeColor = MainWindowTheme.Text;
        this.Padding = Padding.Empty;
        this.Visible = false;
    }

    public string Title { get; }

    public bool CanClose { get; }
}

internal sealed class ThemedTabPageEventArgs(ThemedTabPage page) : EventArgs
{
    public ThemedTabPage Page { get; } = page;
}

internal sealed class ThemedTabHost : UserControl
{
    private readonly FlowLayoutPanel tabStrip = new();
    private readonly Panel pageBorder = new();
    private readonly Dictionary<ThemedTabPage, TabButtonControls> buttons = [];
    private readonly HashSet<ThemedTabPage> hiddenPages = [];
    private ThemedTabPage? selectedPage;

    public event EventHandler? SelectedPageChanged;

    public event EventHandler<ThemedTabPageEventArgs>? PageCloseRequested;

    public ThemedTabHost()
    {
        this.BackColor = MainWindowTheme.Background;
        this.tabStrip.Dock = DockStyle.Top;
        this.tabStrip.Height = 35;
        this.tabStrip.AutoScroll = true;
        this.tabStrip.WrapContents = false;
        this.tabStrip.FlowDirection = FlowDirection.LeftToRight;
        this.tabStrip.Margin = Padding.Empty;
        this.tabStrip.Padding = Padding.Empty;
        this.tabStrip.BackColor = MainWindowTheme.Background;

        this.pageBorder.Dock = DockStyle.Fill;
        this.pageBorder.Padding = new Padding(1);
        this.pageBorder.BackColor = MainWindowTheme.Border;

        this.Controls.Add(this.pageBorder);
        this.Controls.Add(this.tabStrip);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ShowTabStrip
    {
        get => this.tabStrip.Visible;
        set => this.tabStrip.Visible = value;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ThemedTabPage? SelectedPage
    {
        get => this.selectedPage;
        set
        {
            if (value == null ||
                !this.buttons.ContainsKey(value) ||
                this.hiddenPages.Contains(value))
            {
                return;
            }

            this.SelectPage(value);
        }
    }

    public void AddPage(ThemedTabPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        var measuredWidth = TextRenderer.MeasureText(
            page.Title,
            MainWindowTheme.CreateBodyFont(9.0f)).Width;
        var closeWidth = page.CanClose ? 28 : 0;
        var container = new TableLayoutPanel
        {
            AutoSize = false,
            Height = this.tabStrip.Height,
            Width = page.CanClose
                ? Math.Clamp(
                    measuredWidth + 30 + closeWidth,
                    96,
                    280)
                : Math.Max(96, measuredWidth + 30),
            ColumnCount = page.CanClose ? 2 : 1,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = MainWindowTheme.Header,
        };
        container.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 100f));

        if (page.CanClose)
        {
            container.ColumnStyles.Add(
                new ColumnStyle(SizeType.Absolute, closeWidth));
        }
        var selectButton = new Button
        {
            Text = page.Title,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = page.CanClose
                ? new Padding(10, 0, 8, 0)
                : Padding.Empty,
            FlatStyle = FlatStyle.Flat,
            UseVisualStyleBackColor = false,
            Font = MainWindowTheme.CreateBodyFont(9.0f),
            Cursor = Cursors.Hand,
            TabStop = true,
            TextAlign = page.CanClose
                ? ContentAlignment.MiddleLeft
                : ContentAlignment.MiddleCenter,
        };
        selectButton.FlatAppearance.BorderSize = 1;
        selectButton.Click += (_, _) => this.SelectPage(page);

        Button? closeButton = null;
        if (page.CanClose)
        {
            closeButton = new Button
            {
                Text = "×",
                Dock = DockStyle.Fill,
                Width = closeWidth,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                FlatStyle = FlatStyle.Flat,
                UseVisualStyleBackColor = false,
                Font = MainWindowTheme.CreateHeadingFont(10.0f),
                Cursor = Cursors.Hand,
                TabStop = false,
            };
            closeButton.FlatAppearance.BorderSize = 1;
            closeButton.Click += (_, _) =>
                this.PageCloseRequested?.Invoke(
                    this,
                    new ThemedTabPageEventArgs(page));
            container.Controls.Add(closeButton, 1, 0);
        }

        container.Controls.Add(selectButton, 0, 0);
        var controls = new TabButtonControls(
            container,
            selectButton,
            closeButton);
        this.buttons.Add(page, controls);
        this.tabStrip.Controls.Add(container);
        this.pageBorder.Controls.Add(page);

        if (this.selectedPage == null)
        {
            this.SelectPage(page);
        }
        else
        {
            this.ApplyButtonStyle(controls, selected: false);
        }
    }

    public void RemovePage(ThemedTabPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        if (!this.buttons.TryGetValue(page, out var controls))
        {
            return;
        }

        var wasSelected = ReferenceEquals(this.selectedPage, page);
        this.buttons.Remove(page);
        this.hiddenPages.Remove(page);
        this.tabStrip.Controls.Remove(controls.Container);
        this.pageBorder.Controls.Remove(page);

        if (wasSelected)
        {
            this.selectedPage = null;
            var replacement = this.FindFirstVisiblePage();

            if (replacement != null)
            {
                this.SelectPage(replacement);
            }
            else
            {
                this.SelectedPageChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        controls.Container.Dispose();
        page.Dispose();
    }

    public void SetPageVisible(ThemedTabPage page, bool visible)
    {
        ArgumentNullException.ThrowIfNull(page);

        if (!this.buttons.TryGetValue(page, out var controls))
        {
            throw new InvalidOperationException(
                "The page has not been added to this tab host.");
        }

        var currentlyVisible = !this.hiddenPages.Contains(page);

        if (currentlyVisible == visible)
        {
            return;
        }

        controls.Container.Visible = visible;

        if (visible)
        {
            this.hiddenPages.Remove(page);
            if (this.selectedPage == null)
            {
                this.SelectPage(page);
            }

            return;
        }

        this.hiddenPages.Add(page);
        page.Visible = false;
        this.ApplyButtonStyle(controls, selected: false);

        if (!ReferenceEquals(this.selectedPage, page))
        {
            return;
        }

        this.selectedPage = null;
        var replacement = this.FindFirstVisiblePage();

        if (replacement != null)
        {
            this.SelectPage(replacement);
        }
        else
        {
            this.SelectedPageChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private ThemedTabPage? FindFirstVisiblePage()
    {
        foreach (Control control in this.tabStrip.Controls)
        {
            foreach (var pair in this.buttons)
            {
                if (ReferenceEquals(pair.Value.Container, control) &&
                    !this.hiddenPages.Contains(pair.Key))
                {
                    return pair.Key;
                }
            }
        }

        return null;
    }

    private void SelectPage(ThemedTabPage page)
    {
        if (!this.buttons.TryGetValue(page, out var controls) ||
            this.hiddenPages.Contains(page) ||
            ReferenceEquals(this.selectedPage, page))
        {
            return;
        }

        if (this.selectedPage != null)
        {
            this.selectedPage.Visible = false;
            this.ApplyButtonStyle(
                this.buttons[this.selectedPage],
                selected: false);
        }

        this.selectedPage = page;
        page.Visible = true;
        page.BringToFront();
        this.ApplyButtonStyle(controls, selected: true);
        this.SelectedPageChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyButtonStyle(
        TabButtonControls controls,
        bool selected)
    {
        var background = selected
            ? MainWindowTheme.Panel
            : MainWindowTheme.Header;
        var foreground = selected
            ? MainWindowTheme.Accent
            : MainWindowTheme.MutedText;
        var border = selected
            ? MainWindowTheme.AccentBorder
            : MainWindowTheme.Border;

        controls.Container.BackColor = background;
        ApplyButtonStyle(
            controls.SelectButton,
            background,
            foreground,
            border);

        if (controls.CloseButton != null)
        {
            ApplyButtonStyle(
                controls.CloseButton,
                background,
                foreground,
                border);
        }
    }

    private static void ApplyButtonStyle(
        Button button,
        Color background,
        Color foreground,
        Color border)
    {
        button.BackColor = background;
        button.ForeColor = foreground;
        button.FlatAppearance.BorderColor = border;
        button.FlatAppearance.MouseOverBackColor =
            MainWindowTheme.ButtonHover;
        button.FlatAppearance.MouseDownBackColor =
            MainWindowTheme.ElevatedPanel;
        button.Invalidate();
    }

    private sealed record TabButtonControls(
        TableLayoutPanel Container,
        Button SelectButton,
        Button? CloseButton);
}
