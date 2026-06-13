// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using Net7ClientManager.Core;
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
    private TextBox accountNameTextBox = null!;
    private ComboBox resolutionComboBox = null!;
    private CheckBox autoLoginCheckBox = null!;
    private Label inputRiskWarningLabel = null!;

    private NumericUpDown leftNumeric = null!;
    private NumericUpDown topNumeric = null!;

    private Button saveSlotButton = null!;
    private FlowLayoutPanel runningClientsFlowPanel = null!;

    private bool isUpdatingEditor;
    private bool isRefreshingProfileComboBox;

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

        this.LoadSelectedSlotIntoEditor(slot: null);
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
        this.saveSlotButton.Click -= this.SaveSlotButton_OnClick;

        base.OnFormClosed(e);
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
}
