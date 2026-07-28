// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using System.Globalization;
using System.Text;
using Net7ClientManager.Core;
using Net7ClientManager.Models;

public sealed class FleetLootWindowForm : ThemedForm
{
    private static readonly Size LootIconSize = new(20, 20);

    private readonly ClientManager clientManager;
    private readonly int leaderProcessId;
    private readonly TableLayoutPanel layout = new();
    private readonly BufferedFlowLayoutPanel looterList = new();
    private readonly BufferedFlowLayoutPanel lootList = new();
    private readonly Label lootHeader = new();
    private readonly System.Windows.Forms.Timer refreshTimer = new();

    private uint? initialTargetObjectId;
    private string? lastSnapshotSignature;
    private string? lastActionMessage;
    private bool lastActionFailed;
    private bool itemInvocationRunning;
    private bool tractorActive;

    private bool IsInteractionBlocked =>
        this.itemInvocationRunning ||
        this.tractorActive;

    public FleetLootWindowForm(
        ClientManager clientManager,
        int leaderProcessId)
    {
        this.clientManager = clientManager;
        this.leaderProcessId = leaderProcessId;

        this.Text = "Fleet Loot";
        this.Icon = ResourceLoader.Net7ClientManagerIcon;
        this.StartPosition = FormStartPosition.Manual;
        this.MinimumSize = new Size(680, 340);
        this.Size = new Size(720, 380);
        this.BackColor = MainWindowTheme.Background;
        this.ForeColor = MainWindowTheme.Text;
        this.Font = MainWindowTheme.CreateBodyFont(8.75f);
        this.DoubleBuffered = true;
        this.ShowInTaskbar = false;
        this.ShowIcon = false;
        this.ConfigureWindowChrome(
            allowResize: false,
            showMinimizeButton: false,
            showMaximizeButton: false,
            showIcon: false);
        this.PositionNearCursor();

        this.layout.Dock = DockStyle.Fill;
        this.layout.ColumnCount = 2;
        this.layout.RowCount = 1;
        this.layout.Padding = new Padding(10);
        this.layout.Margin = Padding.Empty;
        this.layout.BackColor = this.BackColor;
        this.layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));
        this.layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        this.layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        this.looterList.Dock = DockStyle.Fill;
        this.looterList.FlowDirection = FlowDirection.TopDown;
        this.looterList.WrapContents = false;
        this.looterList.AutoScroll = true;
        this.looterList.BackColor = MainWindowTheme.Panel;
        this.looterList.Padding = new Padding(6);
        this.looterList.Margin = new Padding(0, 0, 10, 0);

        var rightPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            BackColor = this.BackColor,
            Padding = Padding.Empty,
            Margin = Padding.Empty,
        };

        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        this.lootHeader.Dock = DockStyle.Fill;
        this.lootHeader.Font = MainWindowTheme.CreateHeadingFont(9.5f);
        this.lootHeader.TextAlign = ContentAlignment.MiddleLeft;
        this.lootHeader.ForeColor = MainWindowTheme.Accent;
        this.lootHeader.AutoEllipsis = true;

        this.lootList.Dock = DockStyle.Fill;
        this.lootList.FlowDirection = FlowDirection.TopDown;
        this.lootList.WrapContents = false;
        this.lootList.AutoScroll = true;
        this.lootList.BackColor = MainWindowTheme.Panel;
        this.lootList.Padding = new Padding(6);
        this.lootList.Margin = Padding.Empty;

        rightPanel.Controls.Add(this.lootHeader, 0, 0);
        rightPanel.Controls.Add(this.lootList, 0, 1);

        this.layout.Controls.Add(this.looterList, 0, 0);
        this.layout.Controls.Add(rightPanel, 1, 0);

        this.Controls.Add(this.layout);

        this.refreshTimer.Interval = 100;
        this.refreshTimer.Tick += this.RefreshTimer_OnTick;

        this.RefreshView(force: true);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        this.refreshTimer.Start();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        this.refreshTimer.Stop();
        this.refreshTimer.Tick -= this.RefreshTimer_OnTick;
        var restorePoint = Cursor.Position;
        _ = this.clientManager.CloseFleetLootGameWindowAsync(
            this.leaderProcessId,
            restorePoint,
            CancellationToken.None);
        base.OnFormClosed(e);
    }

    private void RefreshTimer_OnTick(object? sender, EventArgs e)
    {
        this.RefreshView(force: false);
    }

    private void RefreshView(bool force)
    {
        var snapshot = this.clientManager.BuildFleetLootWindowSnapshot(this.leaderProcessId);

        if (this.initialTargetObjectId is null or 0)
        {
            // A just-killed target can publish its corpse payload one cycle
            // before the replacement object's identity has fully settled.
            // Do not pin the window to that transitional object id.
            if (snapshot.TargetObjectId != 0 && snapshot.Items.Count > 0)
            {
                this.initialTargetObjectId = snapshot.TargetObjectId;
            }
        }
        else if (snapshot.TargetObjectId != 0 &&
                 snapshot.TargetObjectId != this.initialTargetObjectId.Value)
        {
            this.Close();
            return;
        }

        this.Text = string.IsNullOrWhiteSpace(snapshot.TargetName)
            ? "Fleet Loot"
            : string.Concat("Fleet Loot - ", snapshot.TargetName);

        this.tractorActive = snapshot.IsBusy;
        var signature = CreateSnapshotSignature(snapshot);

        if (!force &&
            string.Equals(
                this.lastSnapshotSignature,
                signature,
                StringComparison.Ordinal))
        {
            return;
        }

        this.lastSnapshotSignature = signature;

        this.layout.SuspendLayout();
        this.looterList.SuspendLayout();
        this.lootList.SuspendLayout();

        try
        {
            this.looterList.Controls.Clear();
            this.lootList.Controls.Clear();

            this.looterList.Controls.Add(
                this.CreateRoundRobinSelector(snapshot.RoundRobinEnabled));

            foreach (var looter in snapshot.Looters)
            {
                this.looterList.Controls.Add(this.CreateLooterRow(looter));
            }

            this.lootHeader.Text = this.lastActionMessage ?? snapshot.Header;
            this.lootHeader.ForeColor = this.lastActionMessage == null
                ? MainWindowTheme.Accent
                : this.lastActionFailed
                    ? MainWindowTheme.Danger
                    : MainWindowTheme.Success;

            if (snapshot.Items.Count == 0)
            {
                this.lootList.Controls.Add(
                    new Label
                    {
                        AutoSize = true,
                        ForeColor = MainWindowTheme.MutedText,
                        BackColor = MainWindowTheme.Panel,
                        Text = "No observed loot items.",
                        Padding = new Padding(4),
                    });
            }
            else
            {
                if (snapshot.Items.Count > 1)
                {
                    this.lootList.Controls.Add(this.CreateLootAllRow());
                }

                foreach (var item in snapshot.Items)
                {
                    this.lootList.Controls.Add(this.CreateLootRow(item));
                }
            }
        }
        finally
        {
            this.lootList.ResumeLayout(performLayout: true);
            this.looterList.ResumeLayout(performLayout: true);
            this.layout.ResumeLayout(performLayout: true);
        }
    }

    private Control CreateRoundRobinSelector(bool isChecked)
    {
        var checkBox = new CheckBox
        {
            Width = 204,
            Height = 26,
            Text = "Round robin",
            Checked = isChecked,
            AutoSize = false,
            FlatStyle = FlatStyle.Flat,
            BackColor = MainWindowTheme.Panel,
            ForeColor = isChecked
                ? MainWindowTheme.Accent
                : MainWindowTheme.Text,
            Cursor = Cursors.Hand,
            Margin = new Padding(2, 0, 0, 6),
            Padding = new Padding(4, 0, 0, 0),
            TextAlign = ContentAlignment.MiddleLeft,
        };

        checkBox.CheckedChanged += (_, _) =>
        {
            this.lastActionMessage = null;
            this.clientManager.SetSessionLootRoundRobinEnabled(
                this.leaderProcessId,
                checkBox.Checked);
            this.RefreshView(force: true);
        };

        return checkBox;
    }

    private Control CreateLooterRow(FleetLootLooterOption looter)
    {
        var selectedBackColor = MainWindowTheme.ButtonHover;
        var normalBackColor = MainWindowTheme.Button;
        var border = new Panel
        {
            Width = 204,
            Height = 26,
            BackColor = looter.IsSelected
                ? MainWindowTheme.AccentBorder
                : MainWindowTheme.Border,
            Padding = new Padding(1),
            Margin = new Padding(0, 0, 0, 4),
            Cursor = Cursors.Hand,
        };

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = looter.IsSelected
                ? selectedBackColor
                : normalBackColor,
            Cursor = Cursors.Hand,
        };

        content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 18));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var marker = new Label
        {
            Dock = DockStyle.Fill,
            Text = looter.IsSelected ? "✓" : "",
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = MainWindowTheme.Accent,
            BackColor = Color.Transparent,
            Cursor = Cursors.Hand,
        };

        var capacityText = looter.CargoCapacity.HasValue
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"[{looter.UsedCargoSlots}/{looter.CargoCapacity.Value}]")
            : string.Create(
                CultureInfo.InvariantCulture,
                $"[{looter.UsedCargoSlots}/--]");

        var capacityColor = MainWindowTheme.MutedText;

        if (looter.CargoCapacity is > 0)
        {
            var ratio = looter.UsedCargoSlots / (double)looter.CargoCapacity.Value;
            capacityColor = looter.UsedCargoSlots >= looter.CargoCapacity.Value
                ? MainWindowTheme.Danger
                : ratio >= 0.8d
                    ? MainWindowTheme.Warning
                    : looter.IsSelected
                        ? MainWindowTheme.Accent
                        : MainWindowTheme.MutedText;
        }

        var capacity = new Label
        {
            Dock = DockStyle.Fill,
            Text = capacityText,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = false,
            ForeColor = capacityColor,
            BackColor = Color.Transparent,
            Font = MainWindowTheme.CreateBodyFont(8.25f),
            Cursor = Cursors.Hand,
        };

        var name = new Label
        {
            Dock = DockStyle.Fill,
            Text = looter.Name,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            ForeColor = looter.IsSelected
                ? MainWindowTheme.Accent
                : MainWindowTheme.Text,
            BackColor = Color.Transparent,
            Padding = new Padding(2, 0, 4, 0),
            Cursor = Cursors.Hand,
        };

        content.Controls.Add(marker, 0, 0);
        content.Controls.Add(capacity, 1, 0);
        content.Controls.Add(name, 2, 0);
        border.Controls.Add(content);

        void SelectLooter(object? _, EventArgs __)
        {
            this.lastActionMessage = null;
            this.clientManager.SetSessionLootOwner(
                this.leaderProcessId,
                looter.ProcessId);
            this.RefreshView(force: true);
        }

        void HoverOn(object? _, EventArgs __)
        {
            content.BackColor = MainWindowTheme.ButtonHover;
        }

        void HoverOff(object? _, EventArgs __)
        {
            content.BackColor = looter.IsSelected
                ? selectedBackColor
                : normalBackColor;
        }

        foreach (var control in new Control[] { border, content, marker, capacity, name })
        {
            control.Click += SelectLooter;
            control.MouseEnter += HoverOn;
            control.MouseLeave += HoverOff;
        }

        return border;
    }

    private Control CreateLootAllRow()
    {
        var button = new Button
        {
            Width = 430,
            Height = 28,
            Text = "Loot All",
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 0, 0, 8),
            Padding = new Padding(6, 0, 6, 0),
        };

        MainWindowTheme.StyleButton(button, primary: true);
        this.ApplyTractorSoftDisable(button);

        button.Click += async (_, _) =>
        {
            if (this.IsInteractionBlocked)
            {
                return;
            }

            this.itemInvocationRunning = true;
            this.lastActionMessage = null;
            this.lastActionFailed = false;

            try
            {
                while (!this.IsDisposed)
                {
                    var snapshot = this.clientManager
                        .BuildFleetLootWindowSnapshot(this.leaderProcessId);

                    if (snapshot.Items.Count == 0)
                    {
                        this.Close();
                        return;
                    }

                    var result = await this.clientManager
                        .LootFleetLootItemAsync(
                            this.leaderProcessId,
                            snapshot.Items[0],
                            Cursor.Position,
                            CancellationToken.None)
                        .ConfigureAwait(true);

                    if (!result.Succeeded)
                    {
                        this.lastActionMessage = result.Message;
                        this.lastActionFailed = true;
                        return;
                    }

                    if (result.CloseWindow)
                    {
                        this.Close();
                        return;
                    }
                }
            }
            finally
            {
                this.itemInvocationRunning = false;

                if (!this.IsDisposed)
                {
                    this.RefreshView(force: true);
                }
            }
        };

        return button;
    }

    private Control CreateLootRow(FleetLootItemRow item)
    {
        var stack = item.StackCount is > 1
            ? string.Create(
                CultureInfo.InvariantCulture,
                $" x{item.StackCount.Value}")
            : "";

        var quality = item.QualityPercent.HasValue
            ? string.Create(
                CultureInfo.InvariantCulture,
                $" ({item.QualityPercent.Value:0}%)")
            : "";

        var button = new Button
        {
            Width = 430,
            Height = 28,
            Text = string.Create(
                CultureInfo.InvariantCulture,
                $"{item.Slot + 1:00}. {item.Name}{stack}{quality}"),
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 0, 0, 4),
            Padding = new Padding(6, 0, 6, 0),
        };

        MainWindowTheme.StyleButton(button);
        button.Image = this.clientManager.GetGameItemIcon(
            this.leaderProcessId,
            item.ItemTemplateId,
            LootIconSize);
        button.ImageAlign = ContentAlignment.MiddleLeft;
        button.TextImageRelation = TextImageRelation.ImageBeforeText;
        this.ApplyTractorSoftDisable(button);

        button.Click += async (_, _) =>
        {
            if (this.IsInteractionBlocked)
            {
                return;
            }

            this.itemInvocationRunning = true;
            this.lastActionMessage = null;
            this.lastActionFailed = false;

            try
            {
                var result = await this.clientManager
                    .LootFleetLootItemAsync(
                        this.leaderProcessId,
                        item,
                        Cursor.Position,
                        CancellationToken.None)
                    .ConfigureAwait(true);

                if (!result.Succeeded)
                {
                    this.lastActionMessage = result.Message;
                    this.lastActionFailed = true;
                }

                if (result.CloseWindow)
                {
                    this.Close();
                    return;
                }
            }
            finally
            {
                this.itemInvocationRunning = false;

                if (!this.IsDisposed)
                {
                    this.RefreshView(force: true);
                }
            }
        };

        return button;
    }

    private void ApplyTractorSoftDisable(Button button)
    {
        if (!this.tractorActive)
        {
            return;
        }

        button.BackColor = MainWindowTheme.DisabledButton;
        button.ForeColor = MainWindowTheme.DisabledText;
        button.FlatAppearance.MouseOverBackColor = MainWindowTheme.DisabledButton;
        button.FlatAppearance.MouseDownBackColor = MainWindowTheme.DisabledButton;
    }

    private static string CreateSnapshotSignature(
        FleetLootWindowSnapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.Append(snapshot.TargetObjectId.ToString(CultureInfo.InvariantCulture));
        builder.Append('|');
        builder.Append(snapshot.TargetName);
        builder.Append('|');
        builder.Append(snapshot.Header);
        builder.Append('|');
        builder.Append(snapshot.RoundRobinEnabled ? '1' : '0');
        builder.Append('|');
        builder.Append(snapshot.IsBusy ? '1' : '0');

        foreach (var looter in snapshot.Looters)
        {
            builder.Append("|L:");
            builder.Append(looter.ProcessId.ToString(CultureInfo.InvariantCulture));
            builder.Append(':');
            builder.Append(looter.Name);
            builder.Append(':');
            builder.Append(looter.UsedCargoSlots.ToString(CultureInfo.InvariantCulture));
            builder.Append('/');
            builder.Append(looter.CargoCapacity?.ToString(CultureInfo.InvariantCulture) ?? "--");
            builder.Append(':');
            builder.Append(looter.IsSelected ? '1' : '0');
        }

        foreach (var item in snapshot.Items)
        {
            builder.Append("|I:");
            builder.Append(item.Slot.ToString(CultureInfo.InvariantCulture));
            builder.Append(':');
            builder.Append(item.ItemTemplateId?.ToString(CultureInfo.InvariantCulture) ?? "");
            builder.Append(':');
            builder.Append(item.Name);
            builder.Append(':');
            builder.Append(item.StackCount?.ToString(CultureInfo.InvariantCulture) ?? "");
            builder.Append(':');
            builder.Append(item.QualityPercent?.ToString(CultureInfo.InvariantCulture) ?? "");
        }

        return builder.ToString();
    }

    private sealed class BufferedFlowLayoutPanel : FlowLayoutPanel
    {
        public BufferedFlowLayoutPanel()
        {
            this.DoubleBuffered = true;
            this.ResizeRedraw = true;
        }
    }
}
