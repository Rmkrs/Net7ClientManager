// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using System.Globalization;
using Net7ClientManager.Core;
using Net7ClientManager.Models;
using Net7ClientManager.Services;
using Net7ClientManager.Win32;

public sealed class InputLabForm : Form
{
    private const int CaptureMouseHotKeyId = 0x4E37;

    private readonly ClientManager clientManager;
    private readonly Func<ClientInstance, string>? clientDisplayNameFactory;
    private readonly System.Windows.Forms.Timer refreshTimer = new();
    private readonly InputActionStore inputActionStore = new();

    private readonly ComboBox clientComboBox = new();

    private readonly TextBox keyTextBox = new();
    private readonly NumericUpDown holdMillisecondsNumeric = new();

    private readonly NumericUpDown mouseXNumeric = new();
    private readonly NumericUpDown mouseYNumeric = new();

    private readonly TextBox teachNameTextBox = new();
    private readonly Button armTeachButton = new();
    private readonly ComboBox clickActionComboBox = new();
    private readonly NumericUpDown repeatCountNumeric = new();
    private readonly NumericUpDown repeatDelayMillisecondsNumeric = new();
    private readonly Button sendClickActionButton = new();

    private readonly TextBox logTextBox = new();

    private string? pendingTeachName;
    private bool captureHotKeyRegistered;
    private bool isRefreshingClients;

    public InputLabForm(
        ClientManager clientManager,
        Func<ClientInstance, string>? clientDisplayNameFactory = null)
    {
        this.clientManager = clientManager;
        this.clientDisplayNameFactory = clientDisplayNameFactory;

        this.Text = "Net7 Client Manager - Input Lab";
        this.StartPosition = FormStartPosition.CenterParent;
        this.MinimumSize = new Size(920, 620);
        this.Size = new Size(1100, 760);

        this.BuildUi();
        this.RegisterCaptureHotKey();

        this.refreshTimer.Interval = 1000;
        this.refreshTimer.Tick += this.RefreshTimer_OnTick;
        this.refreshTimer.Start();

        this.RefreshClientList();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WmHotKey
            && m.WParam.ToInt32() == CaptureMouseHotKeyId)
        {
            this.CaptureMousePosition();
            return;
        }

        base.WndProc(ref m);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        this.refreshTimer.Stop();
        this.refreshTimer.Tick -= this.RefreshTimer_OnTick;
        this.refreshTimer.Dispose();

        this.UnregisterCaptureHotKey();

        this.armTeachButton.Click -= this.ArmTeachButton_OnClick;
        this.sendClickActionButton.Click -= this.SendClickActionButton_OnClick;

        base.OnFormClosed(e);
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(12),
        };

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 116));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 124));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        this.Controls.Add(root);

        root.Controls.Add(this.BuildTargetGroup(), 0, 0);
        root.Controls.Add(this.BuildKnownGoodInputGroup(), 0, 1);
        root.Controls.Add(this.BuildTeachGroup(), 0, 2);

        this.logTextBox.Dock = DockStyle.Fill;
        this.logTextBox.Multiline = true;
        this.logTextBox.ReadOnly = true;
        this.logTextBox.ScrollBars = ScrollBars.Vertical;
        this.logTextBox.Font = new Font(FontFamily.GenericMonospace, 9);

        root.Controls.Add(this.logTextBox, 0, 3);
    }

    private GroupBox BuildTargetGroup()
    {
        var group = new GroupBox
        {
            Text = "Target client",
            Dock = DockStyle.Fill,
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            RowCount = 1,
            Padding = new Padding(8),
        };

        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));

        group.Controls.Add(layout);

        this.clientComboBox.Dock = DockStyle.Fill;
        this.clientComboBox.DropDownStyle = ComboBoxStyle.DropDownList;

        layout.Controls.Add(this.CreateLabel("Client"), 0, 0);
        layout.Controls.Add(this.clientComboBox, 1, 0);
        layout.Controls.Add(this.CreateButton("Refresh", (_, _) => this.RefreshClientList()), 2, 0);
        layout.Controls.Add(this.CreateButton("Focus", this.FocusButton_OnClick), 3, 0);
        layout.Controls.Add(this.CreateButton("Log target", this.LogTargetButton_OnClick), 4, 0);

        return group;
    }

    private GroupBox BuildKnownGoodInputGroup()
    {
        var group = new GroupBox
        {
            Text = "Known-good input",
            Dock = DockStyle.Fill,
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 6,
            RowCount = 2,
            Padding = new Padding(8),
        };

        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));

        group.Controls.Add(layout);

        this.keyTextBox.Dock = DockStyle.Fill;
        this.keyTextBox.Text = "F";

        this.holdMillisecondsNumeric.Minimum = 50;
        this.holdMillisecondsNumeric.Maximum = 10000;
        this.holdMillisecondsNumeric.Increment = 50;
        this.holdMillisecondsNumeric.Value = 500;
        this.holdMillisecondsNumeric.Dock = DockStyle.Fill;

        this.mouseXNumeric.Maximum = 10000;
        this.mouseYNumeric.Maximum = 10000;
        this.mouseXNumeric.Dock = DockStyle.Fill;
        this.mouseYNumeric.Dock = DockStyle.Fill;

        layout.Controls.Add(this.CreateLabel("Key"), 0, 0);
        layout.Controls.Add(this.keyTextBox, 1, 0);
        layout.Controls.Add(this.CreateButton("Send key", this.SendKeyButton_OnClick), 2, 0);
        layout.Controls.Add(this.CreateLabel("Hold ms"), 3, 0);
        layout.Controls.Add(this.holdMillisecondsNumeric, 4, 0);
        layout.Controls.Add(this.CreateButton("Hold key", this.HoldKeyButton_OnClick), 5, 0);

        layout.Controls.Add(this.CreateLabel("X / Y"), 0, 1);
        layout.Controls.Add(this.mouseXNumeric, 1, 1);
        layout.Controls.Add(this.mouseYNumeric, 2, 1);
        layout.Controls.Add(this.CreateButton("Click coords", this.ClickCoordinatesButton_OnClick), 3, 1);
        layout.Controls.Add(this.CreateLabel("Ctrl+Shift+C captures mouse"), 4, 1);
        layout.Controls.Add(this.CreateButton("Clear log", (_, _) => this.logTextBox.Clear()), 5, 1);

        return group;
    }

    private GroupBox BuildTeachGroup()
    {
        var group = new GroupBox
        {
            Text = "Teach and replay click targets",
            Dock = DockStyle.Fill,
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 7,
            RowCount = 2,
            Padding = new Padding(8),
        };

        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));

        group.Controls.Add(layout);

        this.teachNameTextBox.Dock = DockStyle.Fill;
        this.teachNameTextBox.PlaceholderText = "Example: Loot Window 1";

        this.armTeachButton.Text = "Arm teach";
        this.armTeachButton.Dock = DockStyle.Fill;
        this.armTeachButton.Click += this.ArmTeachButton_OnClick;

        this.clickActionComboBox.Dock = DockStyle.Fill;
        this.clickActionComboBox.DropDownStyle = ComboBoxStyle.DropDownList;

        this.repeatCountNumeric.Minimum = 1;
        this.repeatCountNumeric.Maximum = 100;
        this.repeatCountNumeric.Value = 1;
        this.repeatCountNumeric.Dock = DockStyle.Fill;

        this.repeatDelayMillisecondsNumeric.Minimum = 0;
        this.repeatDelayMillisecondsNumeric.Maximum = 10000;
        this.repeatDelayMillisecondsNumeric.Increment = 50;
        this.repeatDelayMillisecondsNumeric.Value = 300;
        this.repeatDelayMillisecondsNumeric.Dock = DockStyle.Fill;

        this.sendClickActionButton.Text = "Send selected";
        this.sendClickActionButton.Dock = DockStyle.Fill;
        this.sendClickActionButton.Click += this.SendClickActionButton_OnClick;

        layout.Controls.Add(this.CreateLabel("Teach name"), 0, 0);
        layout.Controls.Add(this.teachNameTextBox, 1, 0);
        layout.Controls.Add(this.armTeachButton, 2, 0);

        var hintLabel = this.CreateLabel("Hover in game, then press Ctrl+Shift+C");
        layout.Controls.Add(hintLabel, 3, 0);
        layout.SetColumnSpan(hintLabel, 4);

        layout.Controls.Add(this.CreateLabel("Click target"), 0, 1);
        layout.Controls.Add(this.clickActionComboBox, 1, 1);
        layout.SetColumnSpan(this.clickActionComboBox, 2);

        layout.Controls.Add(this.CreateLabel("Repeat"), 3, 1);
        layout.Controls.Add(this.repeatCountNumeric, 4, 1);
        layout.Controls.Add(this.repeatDelayMillisecondsNumeric, 5, 1);
        layout.Controls.Add(this.sendClickActionButton, 6, 1);

        this.ReloadClickActions();

        return group;
    }

    private Label CreateLabel(string text)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
        };
    }

    private Button CreateButton(string text, EventHandler onClick)
    {
        var button = new Button
        {
            Text = text,
            Dock = DockStyle.Fill,
        };

        button.Click += onClick;

        return button;
    }

    private void RefreshTimer_OnTick(object? sender, EventArgs e)
    {
        this.RefreshClientList();
    }

    private void RefreshClientList()
    {
        if (this.isRefreshingClients)
        {
            return;
        }

        this.isRefreshingClients = true;

        try
        {
            var selectedProcessId = (this.clientComboBox.SelectedItem as ClientItem)?.Client.ProcessId;

            this.clientComboBox.BeginUpdate();
            this.clientComboBox.Items.Clear();

            foreach (var client in this.clientManager.Clients.OrderBy(client => client.ProcessId))
            {
                this.clientComboBox.Items.Add(new ClientItem(
                    client,
                    this.BuildClientDisplayName(client)));
            }

            if (this.clientComboBox.Items.Count == 0)
            {
                return;
            }

            var selectedItem = this.clientComboBox.Items
                .OfType<ClientItem>()
                .FirstOrDefault(item => item.Client.ProcessId == selectedProcessId);

            this.clientComboBox.SelectedItem = selectedItem ?? this.clientComboBox.Items[0];
        }
        finally
        {
            this.clientComboBox.EndUpdate();
            this.isRefreshingClients = false;
        }
    }

    private string BuildClientDisplayName(ClientInstance client)
    {
        if (this.clientDisplayNameFactory != null)
        {
            return this.clientDisplayNameFactory(client);
        }

        var slotText = client.AssignedSlotId == null
            ? "Unassigned"
            : string.Concat("Slot ", client.AssignedSlotId.Value.ToString("N")[..8]);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{slotText} - PID {client.ProcessId} - {client.State}");
    }

    private void FocusButton_OnClick(object? sender, EventArgs e)
    {
        this.RunWithTarget("Focus", client => NativeMethods.FocusWindow(client.GameWindowHandle));
    }

    private void LogTargetButton_OnClick(object? sender, EventArgs e)
    {
        this.RunWithTarget("Log target", client =>
        {
            var clientSizeText = NativeMethods.TryGetClientSize(
                client.GameWindowHandle,
                out var clientSize)
                ? string.Create(CultureInfo.InvariantCulture, $"{clientSize.Width}x{clientSize.Height}")
                : "unknown";

            this.Log(string.Create(
                CultureInfo.InvariantCulture,
                $"Target: PID={client.ProcessId}, State={client.State}, GameHwnd=0x{client.GameWindowHandle.ToInt64():X}, ClientSize={clientSizeText}, AssignedSlot={client.AssignedSlotId}"));
        });
    }

    private async void SendKeyButton_OnClick(object? sender, EventArgs e)
    {
        await this.RunWithTargetAsync("Send key", async client => await NativeMethods.ForegroundHoldKeyAsync(
                client.GameWindowHandle,
                this.ParseKey(),
                TimeSpan.FromMilliseconds(100)));
    }

    private async void HoldKeyButton_OnClick(object? sender, EventArgs e)
    {
        await this.RunWithTargetAsync("Hold key", async client => await NativeMethods.ForegroundHoldKeyAsync(
                client.GameWindowHandle,
                this.ParseKey(),
                TimeSpan.FromMilliseconds((int)this.holdMillisecondsNumeric.Value)));
    }

    private void ClickCoordinatesButton_OnClick(object? sender, EventArgs e)
    {
        this.RunWithTarget("Click coordinates", client =>
        {
            var clicked = NativeMethods.ForegroundLeftClick(
                client.GameWindowHandle,
                (int)this.mouseXNumeric.Value,
                (int)this.mouseYNumeric.Value);

            if (!clicked)
            {
                throw new InvalidOperationException("Foreground click failed.");
            }
        });
    }

    private void ArmTeachButton_OnClick(object? sender, EventArgs e)
    {
        var name = this.teachNameTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            this.Log("Teach mode: enter a name first");
            return;
        }

        this.pendingTeachName = name;

        this.Log($"Teach mode armed for '{name}'. Hover in the selected client and press Ctrl+Shift+C.");
    }

    private async void SendClickActionButton_OnClick(object? sender, EventArgs e)
    {
        if (this.clickActionComboBox.SelectedItem is not InputActionDefinition action)
        {
            this.Log("Send selected: no click target selected");
            return;
        }

        var repeatCount = (int)this.repeatCountNumeric.Value;
        var delay = TimeSpan.FromMilliseconds((int)this.repeatDelayMillisecondsNumeric.Value);

        await this.RunWithTargetAsync($"Send selected '{action.Name}'", async client =>
        {
            for (var index = 0; index < repeatCount; index++)
            {
                this.SendClickAction(client, action);

                if (index < repeatCount - 1 && delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay).ConfigureAwait(true);
                }
            }
        });
    }

    private void SendClickAction(ClientInstance client, InputActionDefinition action)
    {
        if (action.Kind != InputActionKind.MouseClick)
        {
            throw new InvalidOperationException($"Action '{action.Name}' is not a mouse click.");
        }

        if (!NativeMethods.TryGetClientSize(client.GameWindowHandle, out var clientSize))
        {
            throw new InvalidOperationException("Could not get client size.");
        }

        var x = (int)Math.Round(action.BaseX * clientSize.Width / action.BaseWidth);
        var y = (int)Math.Round(action.BaseY * clientSize.Height / action.BaseHeight);

        var clicked = NativeMethods.ForegroundLeftClick(
            client.GameWindowHandle,
            x,
            y);

        if (!clicked)
        {
            throw new InvalidOperationException("Click failed.");
        }

        this.Log(string.Create(
                     CultureInfo.InvariantCulture,
                     $"Resolved '{action.Name}' to X={x}, Y={y}, ClientSize={clientSize.Width}x{clientSize.Height}"));
    }

    private void RunWithTarget(string actionName, Action<ClientInstance> action)
    {
        if (this.clientComboBox.SelectedItem is not ClientItem item)
        {
            this.Log($"{actionName}: no target client selected");
            return;
        }

        if (item.Client.GameWindowHandle == IntPtr.Zero)
        {
            this.Log($"{actionName}: selected client has no game window handle");
            return;
        }

        try
        {
            action(item.Client);
            this.Log(string.Create(CultureInfo.InvariantCulture, $"{actionName}: sent to PID {item.Client.ProcessId}"));
        }
        catch (Exception ex)
        {
            this.Log($"{actionName}: failed - {ex.Message}");
        }
    }

    private async Task RunWithTargetAsync(string actionName, Func<ClientInstance, Task> action)
    {
        if (this.clientComboBox.SelectedItem is not ClientItem item)
        {
            this.Log($"{actionName}: no target client selected");
            return;
        }

        if (item.Client.GameWindowHandle == IntPtr.Zero)
        {
            this.Log($"{actionName}: selected client has no game window handle");
            return;
        }

        try
        {
            await action(item.Client);
            this.Log(string.Create(CultureInfo.InvariantCulture, $"{actionName}: sent to PID {item.Client.ProcessId}"));
        }
        catch (Exception ex)
        {
            this.Log($"{actionName}: failed - {ex.Message}");
        }
    }

    private Keys ParseKey()
    {
        var text = this.keyTextBox.Text.Trim();

        if (string.IsNullOrEmpty(text))
        {
            throw new InvalidOperationException("Key cannot be empty.");
        }

        if (text.Length == 1)
        {
            return (Keys)char.ToUpperInvariant(text[0]);
        }

        if (Enum.TryParse<Keys>(text, ignoreCase: true, out var key))
        {
            return key;
        }

        throw new InvalidOperationException($"Could not parse key '{text}'. Try F, Space, Escape, Tab, D1, F2, etc.");
    }

    private void CaptureMousePosition()
    {
        if (this.clientComboBox.SelectedItem is not ClientItem item)
        {
            this.Log("Capture mouse: no target client selected");
            return;
        }

        if (!NativeMethods.TryGetCursorPositionRelativeToClient(
                item.Client.GameWindowHandle,
                out var point))
        {
            this.Log("Capture mouse: failed");
            return;
        }

        if (!NativeMethods.TryGetClientSize(
                item.Client.GameWindowHandle,
                out var clientSize))
        {
            this.Log("Capture mouse: failed to get client size");
            return;
        }

        this.mouseXNumeric.Value = Math.Clamp(
            point.X,
            (int)this.mouseXNumeric.Minimum,
            (int)this.mouseXNumeric.Maximum);

        this.mouseYNumeric.Value = Math.Clamp(
            point.Y,
            (int)this.mouseYNumeric.Minimum,
            (int)this.mouseYNumeric.Maximum);

        var baseX = point.X * 1280.0 / clientSize.Width;
        var baseY = point.Y * 720.0 / clientSize.Height;

        this.Log(string.Create(
            CultureInfo.InvariantCulture,
            $"Capture mouse: X={point.X}, Y={point.Y}, ClientSize={clientSize.Width}x{clientSize.Height}, Base={baseX:0.0},{baseY:0.0}, PID={item.Client.ProcessId}"));

        if (this.pendingTeachName == null)
        {
            return;
        }

        var action = new InputActionDefinition
        {
            Name = this.pendingTeachName,
            Kind = InputActionKind.MouseClick,
            BaseX = baseX,
            BaseY = baseY,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        this.inputActionStore.SaveOrReplaceAction(action);

        this.Log(string.Create(
            CultureInfo.InvariantCulture,
            $"Teach mode: saved '{action.Name}' as Base={action.BaseX:0.0},{action.BaseY:0.0}"));

        var savedName = action.Name;

        this.pendingTeachName = null;
        this.ReloadClickActions(savedName);
    }

    private void ReloadClickActions(string? selectName = null)
    {
        var selectedName = selectName
                           ?? (this.clickActionComboBox.SelectedItem as InputActionDefinition)?.Name;

        this.clickActionComboBox.BeginUpdate();

        try
        {
            this.clickActionComboBox.Items.Clear();

            foreach (var action in this.inputActionStore.LoadClickActions())
            {
                this.clickActionComboBox.Items.Add(action);
            }

            if (this.clickActionComboBox.Items.Count == 0)
            {
                return;
            }

            var selectedItem = this.clickActionComboBox.Items
                .OfType<InputActionDefinition>()
                .FirstOrDefault(action => string.Equals(
                    action.Name,
                    selectedName,
                    StringComparison.OrdinalIgnoreCase));

            this.clickActionComboBox.SelectedItem = selectedItem ?? this.clickActionComboBox.Items[0];
        }
        finally
        {
            this.clickActionComboBox.EndUpdate();
        }
    }

    private void RegisterCaptureHotKey()
    {
        if (this.captureHotKeyRegistered)
        {
            return;
        }

        this.captureHotKeyRegistered = NativeMethods.RegisterCaptureHotKey(
            this.Handle,
            CaptureMouseHotKeyId);

        this.Log(this.captureHotKeyRegistered
            ? "Capture hotkey registered: Ctrl+Shift+C"
            : "Capture hotkey registration failed");
    }

    private void UnregisterCaptureHotKey()
    {
        if (!this.captureHotKeyRegistered)
        {
            return;
        }

        _ = NativeMethods.UnregisterCaptureHotKey(
            this.Handle,
            CaptureMouseHotKeyId);

        this.captureHotKeyRegistered = false;
    }

    private void Log(string message)
    {
        var line = string.Create(
            CultureInfo.InvariantCulture,
            $"{DateTime.Now:HH:mm:ss} {message}");

        this.logTextBox.AppendText(line);
        this.logTextBox.AppendText(Environment.NewLine);
    }

    private sealed record ClientItem(ClientInstance Client, string DisplayName)
    {
        public override string ToString()
        {
            return this.DisplayName;
        }
    }
}
