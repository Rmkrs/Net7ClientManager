// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using System.Globalization;
using Net7ClientManager.Core;
using Net7ClientManager.Models;
using Net7ClientManager.Win32;

public sealed partial class MainForm : Form
{
    private readonly ClientManager clientManager;
    private readonly LayoutDesignerControl layoutDesignerControl;
    private readonly System.Windows.Forms.Timer refreshTimer;

    private ComboBox profileComboBox = null!;
    private Button addProfileButton = null!;
    private Button renameProfileButton = null!;
    private Button duplicateProfileButton = null!;
    private Button deleteProfileButton = null!;

    private Button addSlotButton = null!;
    private Button removeSlotButton = null!;

    private TextBox slotNameTextBox = null!;
    private ComboBox resolutionComboBox = null!;
    private CheckBox autoLoginCheckBox = null!;
    private Label inputRiskWarningLabel = null!;

    private NumericUpDown leftNumeric = null!;
    private NumericUpDown topNumeric = null!;

    private FlowLayoutPanel runningClientsFlowPanel = null!;

    private Button startClientButton = null!;

    private bool isUpdatingEditor;
    private bool isRefreshingProfileComboBox;

    private Button accountsButton = null!;
    private Button inputLabButton = null!;
    private InputLabForm? inputLabForm;

    private ComboBox accountComboBox = null!;
    private ComboBox characterComboBox = null!;
    private CheckBox autoEnterGameCheckBox = null!;

    private Button createMissingClientsButton = null!;
    private CheckBox keepClientsAliveCheckBox = null!;

    private const int CommandMenuHotKeyId = 0x4E38;
    private bool commandMenuHotKeyRegistered;
    private CommandOverlayForm? commandOverlayForm;

    private readonly System.Windows.Forms.Timer commandMenuHotKeyReleaseTimer = new();
    private bool commandMenuHotKeySuppressedUntilReleased;

    public MainForm(ClientManager clientManager)
    {
        this.clientManager = clientManager;

        this.Text = "Net7 Client Manager";
        this.Icon = ResourceLoader.EarthAndBeyondIcon;
        this.StartPosition = FormStartPosition.CenterScreen;
        this.Size = new Size(width: 1280, height: 860);
        this.MinimumSize = new Size(width: 1100, height: 760);

        this.layoutDesignerControl = new LayoutDesignerControl
        {
            Dock = DockStyle.Fill,
        };

        this.layoutDesignerControl.SelectedSlotChanged += this.LayoutDesignerControl_OnSelectedSlotChanged;
        this.layoutDesignerControl.SlotBoundsChanged += this.LayoutDesignerControl_OnSlotBoundsChanged;

        var topPanel = this.CreateTopPanel();
        var canvasPanel = this.CreateCanvasPanel();
        var editorPanel = this.CreateEditorPanel();

        this.Controls.Add(canvasPanel);
        this.Controls.Add(editorPanel);
        this.Controls.Add(topPanel);

        this.refreshTimer = new System.Windows.Forms.Timer
        {
            Interval = 1000,
        };

        this.refreshTimer.Tick += this.RefreshTimer_OnTick;
        this.refreshTimer.Start();

        this.commandMenuHotKeyReleaseTimer.Interval = 25;
        this.commandMenuHotKeyReleaseTimer.Tick += this.CommandMenuHotKeyReleaseTimer_OnTick;

        this.SelectDefaultSlot();
        this.RefreshAll();

        this.RegisterCommandMenuHotKey();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WmHotKey
            && m.WParam.ToInt32() == CommandMenuHotKeyId)
        {
            if (this.commandMenuHotKeySuppressedUntilReleased)
            {
                return;
            }

            this.ShowCommandOverlay();
            return;
        }

        base.WndProc(ref m);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        this.refreshTimer.Stop();
        this.refreshTimer.Tick -= this.RefreshTimer_OnTick;
        this.refreshTimer.Dispose();

        this.commandMenuHotKeyReleaseTimer.Stop();
        this.commandMenuHotKeyReleaseTimer.Tick -= this.CommandMenuHotKeyReleaseTimer_OnTick;
        this.commandMenuHotKeyReleaseTimer.Dispose();

        this.layoutDesignerControl.SelectedSlotChanged -= this.LayoutDesignerControl_OnSelectedSlotChanged;
        this.layoutDesignerControl.SlotBoundsChanged -= this.LayoutDesignerControl_OnSlotBoundsChanged;

        this.profileComboBox.SelectedIndexChanged -= this.ProfileComboBox_OnSelectedIndexChanged;
        this.addProfileButton.Click -= this.AddProfileButton_OnClick;
        this.renameProfileButton.Click -= this.RenameProfileButton_OnClick;
        this.duplicateProfileButton.Click -= this.DuplicateProfileButton_OnClick;
        this.deleteProfileButton.Click -= this.DeleteProfileButton_OnClick;

        this.addSlotButton.Click -= this.AddSlotButton_OnClick;
        this.removeSlotButton.Click -= this.RemoveSlotButton_OnClick;

        this.resolutionComboBox.SelectedIndexChanged -= this.ResolutionComboBox_OnSelectedIndexChanged;
        this.topNumeric.ValueChanged -= this.TopNumeric_OnValueChanged;
        this.leftNumeric.ValueChanged -= this.LeftNumeric_OnValueChanged;

        this.startClientButton.Click -= this.StartClientButton_OnClick;

        this.accountsButton.Click -= this.AccountsButton_OnClick;
        this.accountComboBox.SelectedIndexChanged -= this.AccountComboBox_SelectedIndexChanged;
        this.characterComboBox.SelectedIndexChanged -= this.CharacterComboBox_SelectedIndexChanged;

        this.inputLabButton.Click -= this.InputLabButton_OnClick;

        this.createMissingClientsButton.Click -= this.CreateMissingClientsButton_OnClick;
        this.keepClientsAliveCheckBox.CheckedChanged -= this.KeepClientsAliveCheckBox_OnCheckedChanged;

        this.commandOverlayForm?.FormClosed -= this.CommandOverlayForm_OnFormClosed;
        this.commandOverlayForm = null;

        this.UnregisterCommandMenuHotKey();

        base.OnFormClosed(e);
    }

    private void CommandMenuHotKeyReleaseTimer_OnTick(object? sender, EventArgs e)
    {
        if (this.IsAnyCommandMenuHotKeyPartDown())
        {
            return;
        }

        this.commandMenuHotKeyReleaseTimer.Stop();
        this.commandMenuHotKeySuppressedUntilReleased = false;
    }

    private bool IsAnyCommandMenuHotKeyPartDown()
    {
        var commandMenuHotKey = this.clientManager.FleetCommandSettings.CommandMenuHotKey;
        var keyCode = commandMenuHotKey & Keys.KeyCode;

        if (keyCode != Keys.None && NativeMethods.IsKeyDown(keyCode))
        {
            return true;
        }

        var requiredModifiers = commandMenuHotKey & Keys.Modifiers;

        if ((requiredModifiers & Keys.Control) == Keys.Control &&
            (
                NativeMethods.IsKeyDown(Keys.ControlKey) ||
                NativeMethods.IsKeyDown(Keys.LControlKey) ||
                NativeMethods.IsKeyDown(Keys.RControlKey)
            ))
        {
            return true;
        }

        if ((requiredModifiers & Keys.Shift) == Keys.Shift &&
            (
                NativeMethods.IsKeyDown(Keys.ShiftKey) ||
                NativeMethods.IsKeyDown(Keys.LShiftKey) ||
                NativeMethods.IsKeyDown(Keys.RShiftKey)
            ))
        {
            return true;
        }

        if ((requiredModifiers & Keys.Alt) == Keys.Alt &&
            (
                NativeMethods.IsKeyDown(Keys.Menu) ||
                NativeMethods.IsKeyDown(Keys.LMenu) ||
                NativeMethods.IsKeyDown(Keys.RMenu)
            ))
        {
            return true;
        }

        return false;
    }

    private void CommandOverlayForm_OnFormClosed(object? sender, FormClosedEventArgs e)
    {
        this.commandOverlayForm?.FormClosed -= this.CommandOverlayForm_OnFormClosed;
        this.commandOverlayForm = null;

        this.commandMenuHotKeySuppressedUntilReleased = true;
        this.commandMenuHotKeyReleaseTimer.Start();
    }

    private void ShowCommandOverlay()
    {
        var activeClient = this.clientManager.FindForegroundHostedClient();

        if (activeClient == null)
        {
            return;
        }

        Point? activeClientMousePosition = null;

        if (NativeMethods.TryGetCursorPositionRelativeToClient(
                activeClient.GameWindowHandle,
                out var mousePosition))
        {
            activeClientMousePosition = mousePosition;
        }

        var invocationContext = new FleetCommandInvocationContext
        {
            ActiveClient = activeClient,
            ActiveClientMousePosition = activeClientMousePosition,
        };

        if (this.commandOverlayForm is { IsDisposed: false })
        {
            this.commandOverlayForm.Close();
            return;
        }

        this.commandOverlayForm = new CommandOverlayForm(
            this.clientManager,
            invocationContext,
            this.clientManager.FleetCommandSettings.CommandMenuHotKey);

        this.commandOverlayForm.FormClosed += this.CommandOverlayForm_OnFormClosed;

        var cursorPosition = Cursor.Position;
        var workingArea = Screen.FromPoint(cursorPosition).WorkingArea;

        var x = cursorPosition.X - this.commandOverlayForm.Width / 2;
        var y = cursorPosition.Y - this.commandOverlayForm.Height / 2;

        x = Math.Clamp(
            x,
            workingArea.Left,
            workingArea.Right - this.commandOverlayForm.Width);

        y = Math.Clamp(
            y,
            workingArea.Top,
            workingArea.Bottom - this.commandOverlayForm.Height);

        this.commandOverlayForm.Location = new Point(x, y);
        this.commandOverlayForm.Show();
        this.commandOverlayForm.Activate();
    }

    private void RegisterCommandMenuHotKey()
    {
        if (this.commandMenuHotKeyRegistered)
        {
            return;
        }

        this.commandMenuHotKeyRegistered = NativeMethods.RegisterGlobalHotKey(
            this.Handle,
            CommandMenuHotKeyId,
            this.clientManager.FleetCommandSettings.CommandMenuHotKey);
    }

    private void UnregisterCommandMenuHotKey()
    {
        if (!this.commandMenuHotKeyRegistered)
        {
            return;
        }

        _ = NativeMethods.UnregisterGlobalHotKey(
            this.Handle,
            CommandMenuHotKeyId);

        this.commandMenuHotKeyRegistered = false;
    }

    private void SelectDefaultSlot()
    {
        var firstSlot = this.clientManager.CurrentProfile.Slots.FirstOrDefault();

        this.layoutDesignerControl.SelectSlot(firstSlot);
        this.LoadSelectedSlotIntoEditor(firstSlot);
    }

    private void StartClientButton_OnClick(object? sender, EventArgs e)
    {
        this.clientManager.StartClientFromLauncher(this);
        this.RefreshAll();
    }

    private void CreateMissingClientsButton_OnClick(object? sender, EventArgs e)
    {
        this.clientManager.CreateMissingClients(this);
        this.RefreshAll();
    }

    private void KeepClientsAliveCheckBox_OnCheckedChanged(object? sender, EventArgs e)
    {
        this.clientManager.KeepClientsAlive = this.keepClientsAliveCheckBox.Checked;

        if (this.keepClientsAliveCheckBox.Checked)
        {
            this.clientManager.CreateMissingClients(this);
        }

        this.RefreshAll();
    }

    private void AccountsButton_OnClick(object? sender, EventArgs e)
    {
        using var form = new AccountsForm(
            this.clientManager.Accounts,
            accounts =>
            {
                this.clientManager.SaveAccounts(accounts);
                this.ReloadAccountAndCharacterCombos();
                this.RefreshAll();
            });

        form.ShowDialog(this);

        this.ReloadAccountAndCharacterCombos();
    }

    private void RefreshTimer_OnTick(object? sender, EventArgs e)
    {
        this.RefreshAll();
    }

    private void RefreshAll()
    {
        this.layoutDesignerControl.Profile = this.clientManager.CurrentProfile;
        this.layoutDesignerControl.Clients = this.clientManager.Clients;

        this.RefreshProfileComboBox();
        this.RefreshRunningClients();
    }

    private void LayoutDesignerControl_OnSelectedSlotChanged(object? sender, SelectedSlotChangedEventArgs e)
    {
        this.LoadSelectedSlotIntoEditor(e.SelectedSlot);
    }

    private void LayoutDesignerControl_OnSlotBoundsChanged(object? sender, SlotBoundsChangedEventArgs e)
    {
        var slot = e.Slot;

        this.clientManager.SaveSettings();
        this.LoadSelectedSlotIntoEditor(slot);

        var assignedClient = this.clientManager.Clients.FirstOrDefault(client => client.AssignedSlotId == slot.Id);
        assignedClient?.HostForm?.ApplySlot(slot);

        this.RefreshAll();
    }

    private void InputLabButton_OnClick(object? sender, EventArgs e)
    {
        if (this.inputLabForm is { IsDisposed: false })
        {
            this.inputLabForm.Show();
            this.inputLabForm.WindowState = FormWindowState.Normal;
            this.inputLabForm.Activate();
            return;
        }

        this.inputLabForm = new InputLabForm(
            this.clientManager,
            this.BuildInputLabClientDisplayName);

        this.inputLabForm.FormClosed += (_, _) => this.inputLabForm = null;
        this.inputLabForm.Show(this);
    }

    private string BuildInputLabClientDisplayName(ClientInstance client)
    {
        var slot = this.clientManager.CurrentProfile.Slots
            .FirstOrDefault(slot => slot.Id == client.AssignedSlotId);

        var slotName = slot?.Name ?? "Unassigned";
        var accountName = this.GetAccountDisplayName(slot);

        var account = string.IsNullOrWhiteSpace(accountName)
            ? null
            : string.Concat(" - ", accountName);

        return string.Create(CultureInfo.InvariantCulture, $"{slotName}{account} - PID {client.ProcessId} - {client.State}");
    }

    private string? GetAccountDisplayName(ClientSlot? slot)
    {
        if (slot?.AccountId == null)
        {
            return null;
        }

        var account = this.clientManager.FindAccount(slot.AccountId);

        if (account == null)
        {
            return "Unknown account";
        }

        if (!string.IsNullOrWhiteSpace(account.DisplayName))
        {
            return account.DisplayName;
        }

        return string.IsNullOrWhiteSpace(account.LoginName)
            ? "Unnamed account"
            : account.LoginName;
    }
}
