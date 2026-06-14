// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using System.Globalization;
using Net7ClientManager.Core;
using Net7ClientManager.Models;

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

        this.SelectDefaultSlot();
        this.RefreshAll();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        this.refreshTimer.Stop();
        this.refreshTimer.Tick -= this.RefreshTimer_OnTick;
        this.refreshTimer.Dispose();

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
        this.leftNumeric.ValueChanged -= this.LeftNumeric_OnValueChanged;

        this.startClientButton.Click -= this.StartClientButton_OnClick;

        this.accountsButton.Click -= this.AccountsButton_OnClick;
        this.accountComboBox.SelectedIndexChanged -= this.AccountComboBox_SelectedIndexChanged;
        this.characterComboBox.SelectedIndexChanged -= this.CharacterComboBox_SelectedIndexChanged;

        this.inputLabButton.Click -= this.InputLabButton_OnClick;

        this.createMissingClientsButton.Click -= this.CreateMissingClientsButton_OnClick;
        this.keepClientsAliveCheckBox.CheckedChanged -= this.KeepClientsAliveCheckBox_OnCheckedChanged;

        base.OnFormClosed(e);
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
