// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

public sealed partial class MainForm
{
    private Control CreateDashboardPanel()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(left: 18, top: 18, right: 18, bottom: 18),
            ColumnCount = 2,
            RowCount = 1,
            BackColor = MainWindowTheme.Background,
        };

        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, width: 42));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, width: 58));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, height: 100));

        this.slotsSection = this.CreateSlotsSection();
        this.runningClientsSection = this.CreateRunningClientsSection();
        root.Controls.Add(this.slotsSection, column: 0, row: 0);
        root.Controls.Add(this.runningClientsSection, column: 1, row: 0);

        return root;
    }

    private Control CreateSlotsSection()
    {
        var section = new DashboardSectionPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(left: 0, top: 0, right: 8, bottom: 0),
            Padding = new Padding(all: 1),
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(left: 14, top: 12, right: 14, bottom: 14),
            ColumnCount = 1,
            RowCount = 2,
            BackColor = MainWindowTheme.Panel,
        };

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height: 64));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, height: 100));

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
        };

        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, width: 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var textPanel = new Panel
        {
            Dock = DockStyle.Fill,
        };

        var titleLabel = new Label
        {
            Text = "Client slots",
            AutoSize = true,
            Font = MainWindowTheme.CreateHeadingFont(size: 13.0f),
            ForeColor = MainWindowTheme.Text,
            Location = new Point(x: 0, y: 0),
        };

        this.slotsSummaryLabel = new Label
        {
            Text = "No slots configured",
            AutoSize = true,
            ForeColor = MainWindowTheme.MutedText,
            Location = new Point(x: 1, y: 29),
        };

        textPanel.Controls.Add(titleLabel);
        textPanel.Controls.Add(this.slotsSummaryLabel);

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(left: 8, top: 5, right: 0, bottom: 0),
        };

        this.keepClientsAliveCheckBox = new ThemedCheckBox
        {
            Text = "Keep alive",
            AutoSize = true,
            ForeColor = MainWindowTheme.Text,
            Margin = new Padding(left: 8, top: 6, right: 4, bottom: 0),
        };

        this.createMissingClientsButton = new Button
        {
            Text = "Create missing",
            Width = 116,
            Height = 32,
            Margin = new Padding(left: 6, top: 0, right: 0, bottom: 0),
        };
        MainWindowTheme.StyleButton(this.createMissingClientsButton);

        this.editLayoutButton = new Button
        {
            Text = "Edit layout",
            Width = 96,
            Height = 32,
            Margin = new Padding(left: 6, top: 0, right: 0, bottom: 0),
        };
        MainWindowTheme.StyleButton(this.editLayoutButton);

        this.addSlotButton = new Button
        {
            Text = "+ Add slot",
            Width = 92,
            Height = 32,
            Margin = Padding.Empty,
        };
        MainWindowTheme.StyleButton(this.addSlotButton, primary: true);

        buttons.Controls.Add(this.editLayoutButton);
        buttons.Controls.Add(this.addSlotButton);
        buttons.Controls.Add(this.createMissingClientsButton);
        buttons.Controls.Add(this.keepClientsAliveCheckBox);

        header.Controls.Add(textPanel, column: 0, row: 0);
        header.Controls.Add(buttons, column: 1, row: 0);

        this.slotsFlowPanel = CreateDashboardFlowPanel();
        this.slotsFlowPanel.SizeChanged +=
            this.DashboardFlowPanel_OnSizeChanged;

        layout.Controls.Add(header, column: 0, row: 0);
        layout.Controls.Add(this.slotsFlowPanel, column: 0, row: 1);
        section.Controls.Add(layout);

        this.addSlotButton.Click += this.AddSlotButton_OnClick;
        this.editLayoutButton.Click += this.EditLayoutButton_OnClick;
        this.createMissingClientsButton.Click +=
            this.CreateMissingClientsButton_OnClick;
        this.keepClientsAliveCheckBox.CheckedChanged +=
            this.KeepClientsAliveCheckBox_OnCheckedChanged;

        return section;
    }

    private Control CreateRunningClientsSection()
    {
        var section = new DashboardSectionPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(left: 8, top: 0, right: 0, bottom: 0),
            Padding = new Padding(all: 1),
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(left: 14, top: 12, right: 14, bottom: 14),
            ColumnCount = 1,
            RowCount = 2,
            BackColor = MainWindowTheme.Panel,
        };

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height: 64));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, height: 100));

        var textPanel = new Panel
        {
            Dock = DockStyle.Fill,
        };

        var titleLabel = new Label
        {
            Text = "Running clients",
            AutoSize = true,
            Font = MainWindowTheme.CreateHeadingFont(size: 13.0f),
            ForeColor = MainWindowTheme.Text,
            Location = new Point(x: 0, y: 0),
        };

        this.runningClientsSummaryLabel = new Label
        {
            Text = "No client processes detected",
            AutoSize = true,
            ForeColor = MainWindowTheme.MutedText,
            Location = new Point(x: 1, y: 29),
        };

        textPanel.Controls.Add(titleLabel);
        textPanel.Controls.Add(this.runningClientsSummaryLabel);

        this.runningClientsFlowPanel = CreateDashboardFlowPanel();
        this.runningClientsFlowPanel.SizeChanged +=
            this.DashboardFlowPanel_OnSizeChanged;

        layout.Controls.Add(textPanel, column: 0, row: 0);
        layout.Controls.Add(this.runningClientsFlowPanel, column: 0, row: 1);
        section.Controls.Add(layout);

        return section;
    }

    private static FlowLayoutPanel CreateDashboardFlowPanel()
    {
        return new DoubleBufferedFlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = MainWindowTheme.Panel,
            Padding = new Padding(all: 0),
        };
    }

    private sealed class DashboardSectionPanel : Panel
    {
        public DashboardSectionPanel()
        {
            this.BackColor = MainWindowTheme.Border;
            this.DoubleBuffered = true;
            this.ResizeRedraw = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            using var pen = new Pen(MainWindowTheme.Border);
            e.Graphics.DrawRectangle(
                pen,
                x: 0,
                y: 0,
                width: Math.Max(0, this.ClientSize.Width - 1),
                height: Math.Max(0, this.ClientSize.Height - 1));
        }
    }

    private sealed class DoubleBufferedFlowLayoutPanel : FlowLayoutPanel
    {
        public DoubleBufferedFlowLayoutPanel()
        {
            this.DoubleBuffered = true;
            this.ResizeRedraw = true;
        }
    }
}
