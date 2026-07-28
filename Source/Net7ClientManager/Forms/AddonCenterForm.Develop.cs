// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using System.Diagnostics;
using Net7ClientManager.Addons.Development;
using ScintillaNET;

public sealed partial class AddonCenterForm
{
    private const int LuaDiagnosticIndicator = 8;

    private readonly ComboBox developmentWorkspaceComboBox = new();
    private readonly TreeView developmentFileTree = new();
    private readonly Scintilla developmentEditor = new();
    private readonly Button newWorkspaceButton = new();
    private readonly Button refreshWorkspacesButton = new();
    private readonly Button saveDocumentButton = new();
    private readonly Button validateDocumentButton = new();
    private readonly Button saveReloadButton = new();
    private readonly Button openWorkspaceFolderButton = new();
    private readonly Button generateApiFilesButton = new();
    private readonly Button publishAddonButton = new();
    private readonly Button developmentFontButton = new();
    private readonly Label developmentDocumentLabel = new();
    private readonly Label developmentStateLabel = new();
    private readonly Label developmentCaretLabel = new();
    private readonly ListBox developmentDiagnosticsList = new();
    private readonly TextBox developmentApiSearchTextBox = new();
    private readonly ListBox developmentApiList = new();
    private readonly Label developmentApiSignatureLabel = new();
    private readonly TextBox developmentApiDescriptionTextBox = new();
    private readonly System.Windows.Forms.Timer developmentValidationTimer = new();

    private IReadOnlyList<AddonDevelopmentWorkspace> developmentWorkspaces = [];
    private IReadOnlyList<AddonDevelopmentDocument> developmentDocuments = [];
    private IReadOnlyList<AddonDevelopmentDiagnostic> developmentDiagnostics = [];
    private AddonDevelopmentWorkspace? selectedDevelopmentWorkspace;
    private AddonDevelopmentDocument? selectedDevelopmentDocument;
    private string loadedDevelopmentText = "";
    private bool suppressDevelopmentSelection;
    private bool suppressDevelopmentTextChanged;
    private bool developmentPageInitialized;

    private bool IsDevelopmentDocumentDirty =>
        this.selectedDevelopmentDocument is { IsReadOnly: false } &&
        !string.Equals(
            this.developmentEditor.Text,
            this.loadedDevelopmentText,
            StringComparison.Ordinal);

    private void BuildDevelopPage()
    {
        this.developPage.BackColor = AddonCenterTheme.Background;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = AddonCenterTheme.Background,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            RowStyles =
            {
                new RowStyle(SizeType.Absolute, 58),
                new RowStyle(SizeType.Percent, 100),
                new RowStyle(SizeType.Absolute, 30),
            },
        };

        root.Controls.Add(this.BuildDevelopmentToolbar(), 0, 0);
        root.Controls.Add(this.BuildDevelopmentWorkspace(), 0, 1);
        root.Controls.Add(this.BuildDevelopmentStatusBar(), 0, 2);
        this.developPage.Controls.Add(root);

        this.ConfigureDevelopmentEditor();
        this.RefreshApiSymbolList();
        this.developmentValidationTimer.Interval = 550;
    }

    private Control BuildDevelopmentToolbar()
    {
        var toolbar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 9,
            RowCount = 1,
            BackColor = AddonCenterTheme.Background,
            Padding = new Padding(0, 8, 0, 8),
            Margin = Padding.Empty,
            ColumnStyles =
            {
                new ColumnStyle(SizeType.Percent, 100),
                new ColumnStyle(SizeType.Absolute, 112),
                new ColumnStyle(SizeType.Absolute, 92),
                new ColumnStyle(SizeType.Absolute, 86),
                new ColumnStyle(SizeType.Absolute, 86),
                new ColumnStyle(SizeType.Absolute, 126),
                new ColumnStyle(SizeType.Absolute, 108),
                new ColumnStyle(SizeType.Absolute, 112),
                new ColumnStyle(SizeType.Absolute, 102),
            },
        };

        this.developmentWorkspaceComboBox.DropDownStyle =
            ComboBoxStyle.DropDownList;
        this.developmentWorkspaceComboBox.BackColor = AddonCenterTheme.Button;
        this.developmentWorkspaceComboBox.ForeColor = AddonCenterTheme.Text;
        this.developmentWorkspaceComboBox.FlatStyle = FlatStyle.Flat;
        this.developmentWorkspaceComboBox.Dock = DockStyle.Fill;
        this.developmentWorkspaceComboBox.Margin = new Padding(0, 0, 10, 0);

        ConfigureDevelopmentButton(this.newWorkspaceButton, "New addon");
        ConfigureDevelopmentButton(this.refreshWorkspacesButton, "Refresh");
        ConfigureDevelopmentButton(this.saveDocumentButton, "Save");
        ConfigureDevelopmentButton(this.validateDocumentButton, "Validate");
        ConfigureDevelopmentButton(this.saveReloadButton, "Save + reload");
        ConfigureDevelopmentButton(this.openWorkspaceFolderButton, "Open folder");
        ConfigureDevelopmentButton(this.generateApiFilesButton, "API files");
        ConfigureDevelopmentButton(this.publishAddonButton, "Publish");

        toolbar.Controls.Add(this.developmentWorkspaceComboBox, 0, 0);
        toolbar.Controls.Add(this.newWorkspaceButton, 1, 0);
        toolbar.Controls.Add(this.refreshWorkspacesButton, 2, 0);
        toolbar.Controls.Add(this.saveDocumentButton, 3, 0);
        toolbar.Controls.Add(this.validateDocumentButton, 4, 0);
        toolbar.Controls.Add(this.saveReloadButton, 5, 0);
        toolbar.Controls.Add(this.openWorkspaceFolderButton, 6, 0);
        toolbar.Controls.Add(this.generateApiFilesButton, 7, 0);
        toolbar.Controls.Add(this.publishAddonButton, 8, 0);
        return toolbar;
    }

    private Control BuildDevelopmentWorkspace()
    {
        var outerSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            BackColor = AddonCenterTheme.SoftBorder,
            Size = new Size(1008, 600),
            SplitterWidth = 5,
            Panel1MinSize = 170,
            Panel2MinSize = 490,
            SplitterDistance = 218,
        };

        outerSplit.Panel1.BackColor = AddonCenterTheme.Panel;
        outerSplit.Panel2.BackColor = AddonCenterTheme.Background;
        outerSplit.Panel1.Padding = new Padding(1);
        outerSplit.Panel2.Padding = new Padding(0);
        outerSplit.Panel1.Controls.Add(this.BuildDevelopmentFilePanel());

        var editorApiSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            BackColor = AddonCenterTheme.SoftBorder,
            Size = new Size(785, 600),
            SplitterWidth = 5,
            Panel1MinSize = 300,
            Panel2MinSize = 180,
            SplitterDistance = 550,
        };

        editorApiSplit.Panel1.BackColor = AddonCenterTheme.Panel;
        editorApiSplit.Panel2.BackColor = AddonCenterTheme.Panel;
        editorApiSplit.Panel1.Controls.Add(this.BuildDevelopmentEditorPanel());
        editorApiSplit.Panel2.Controls.Add(this.BuildDevelopmentApiPanel());
        outerSplit.Panel2.Controls.Add(editorApiSplit);
        return outerSplit;
    }

    private Control BuildDevelopmentFilePanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = AddonCenterTheme.Panel,
            Padding = new Padding(10),
            RowStyles =
            {
                new RowStyle(SizeType.Absolute, 34),
                new RowStyle(SizeType.Percent, 100),
            },
        };

        var heading = new Label
        {
            Text = "WORKSPACE FILES",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9.0f, FontStyle.Bold),
            ForeColor = AddonCenterTheme.Text,
            TextAlign = ContentAlignment.MiddleLeft,
        };

        this.developmentFileTree.Dock = DockStyle.Fill;
        this.developmentFileTree.BackColor = AddonCenterTheme.Background;
        this.developmentFileTree.ForeColor = AddonCenterTheme.Text;
        this.developmentFileTree.BorderStyle = (System.Windows.Forms.BorderStyle)BorderStyle.FixedSingle;
        this.developmentFileTree.HideSelection = false;
        this.developmentFileTree.FullRowSelect = true;
        this.developmentFileTree.ShowLines = false;
        this.developmentFileTree.ShowPlusMinus = true;
        this.developmentFileTree.ShowRootLines = false;

        panel.Controls.Add(heading, 0, 0);
        panel.Controls.Add(this.developmentFileTree, 0, 1);
        return panel;
    }

    private Control BuildDevelopmentEditorPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = AddonCenterTheme.Panel,
            Padding = new Padding(1),
            RowStyles =
            {
                new RowStyle(SizeType.Absolute, 40),
                new RowStyle(SizeType.Percent, 100),
                new RowStyle(SizeType.Absolute, 122),
            },
        };

        var editorHeader = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = AddonCenterTheme.ElevatedPanel,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            ColumnStyles =
            {
                new ColumnStyle(SizeType.Percent, 100),
                new ColumnStyle(SizeType.Absolute, 176),
            },
        };

        this.developmentDocumentLabel.Dock = DockStyle.Fill;
        this.developmentDocumentLabel.BackColor = AddonCenterTheme.ElevatedPanel;
        this.developmentDocumentLabel.ForeColor = AddonCenterTheme.Text;
        this.developmentDocumentLabel.Font =
            new Font("Segoe UI", 9.0f, FontStyle.Bold);
        this.developmentDocumentLabel.Text = "NO DOCUMENT OPEN";
        this.developmentDocumentLabel.Padding = new Padding(12, 0, 0, 0);
        this.developmentDocumentLabel.TextAlign = ContentAlignment.MiddleLeft;

        AddonCenterTheme.StyleButton(this.developmentFontButton);
        this.developmentFontButton.Dock = DockStyle.Fill;
        this.developmentFontButton.Margin = new Padding(8, 5, 8, 5);
        this.developmentFontButton.AccessibleName = "Change addon editor font";
        this.developmentFontButton.AutoEllipsis = true;
        editorHeader.Controls.Add(this.developmentDocumentLabel, 0, 0);
        editorHeader.Controls.Add(this.developmentFontButton, 1, 0);

        this.developmentEditor.Dock = DockStyle.Fill;
        this.developmentEditor.BorderStyle = BorderStyle.None;

        var diagnosticPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = AddonCenterTheme.ElevatedPanel,
            Padding = new Padding(8, 6, 8, 8),
            RowStyles =
            {
                new RowStyle(SizeType.Absolute, 24),
                new RowStyle(SizeType.Percent, 100),
            },
        };

        diagnosticPanel.Controls.Add(new Label
        {
            Text = "DIAGNOSTICS",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            ForeColor = AddonCenterTheme.MutedText,
            TextAlign = ContentAlignment.MiddleLeft,
        }, 0, 0);

        this.developmentDiagnosticsList.Dock = DockStyle.Fill;
        this.developmentDiagnosticsList.BackColor = AddonCenterTheme.Background;
        this.developmentDiagnosticsList.ForeColor = AddonCenterTheme.Text;
        this.developmentDiagnosticsList.BorderStyle = (System.Windows.Forms.BorderStyle)BorderStyle.FixedSingle;
        this.developmentDiagnosticsList.IntegralHeight = false;
        this.developmentDiagnosticsList.Font =
            new Font(FontFamily.GenericMonospace, 8.5f);
        diagnosticPanel.Controls.Add(this.developmentDiagnosticsList, 0, 1);

        panel.Controls.Add(editorHeader, 0, 0);
        panel.Controls.Add(this.developmentEditor, 0, 1);
        panel.Controls.Add(diagnosticPanel, 0, 2);
        return panel;
    }

    private Control BuildDevelopmentApiPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            BackColor = AddonCenterTheme.Panel,
            Padding = new Padding(10),
            RowStyles =
            {
                new RowStyle(SizeType.Absolute, 32),
                new RowStyle(SizeType.Absolute, 34),
                new RowStyle(SizeType.Percent, 46),
                new RowStyle(SizeType.Absolute, 70),
                new RowStyle(SizeType.Percent, 54),
            },
        };

        panel.Controls.Add(new Label
        {
            Text = "LUA + NET7 API",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9.0f, FontStyle.Bold),
            ForeColor = AddonCenterTheme.Text,
            TextAlign = ContentAlignment.MiddleLeft,
        }, 0, 0);

        this.developmentApiSearchTextBox.PlaceholderText =
            "Filter symbols, events, functions...";
        this.developmentApiSearchTextBox.Dock = DockStyle.Fill;
        this.developmentApiSearchTextBox.BackColor = AddonCenterTheme.Button;
        this.developmentApiSearchTextBox.ForeColor = AddonCenterTheme.Text;
        this.developmentApiSearchTextBox.BorderStyle = (System.Windows.Forms.BorderStyle)BorderStyle.FixedSingle;
        panel.Controls.Add(this.developmentApiSearchTextBox, 0, 1);

        this.developmentApiList.Dock = DockStyle.Fill;
        this.developmentApiList.BackColor = AddonCenterTheme.Background;
        this.developmentApiList.ForeColor = AddonCenterTheme.Text;
        this.developmentApiList.BorderStyle = (System.Windows.Forms.BorderStyle)BorderStyle.FixedSingle;
        this.developmentApiList.IntegralHeight = false;
        this.developmentApiList.Font =
            new Font(FontFamily.GenericMonospace, 8.5f);
        panel.Controls.Add(this.developmentApiList, 0, 2);

        this.developmentApiSignatureLabel.Dock = DockStyle.Fill;
        this.developmentApiSignatureLabel.BackColor =
            AddonCenterTheme.ElevatedPanel;
        this.developmentApiSignatureLabel.ForeColor = AddonCenterTheme.Accent;
        this.developmentApiSignatureLabel.Font =
            new Font(FontFamily.GenericMonospace, 8.5f, FontStyle.Bold);
        this.developmentApiSignatureLabel.Padding = new Padding(8);
        this.developmentApiSignatureLabel.AutoEllipsis = true;
        this.developmentApiSignatureLabel.Text =
            "Select a symbol for signatures and documentation.";
        panel.Controls.Add(this.developmentApiSignatureLabel, 0, 3);

        this.developmentApiDescriptionTextBox.Dock = DockStyle.Fill;
        this.developmentApiDescriptionTextBox.Multiline = true;
        this.developmentApiDescriptionTextBox.ReadOnly = true;
        this.developmentApiDescriptionTextBox.ScrollBars = ScrollBars.Vertical;
        this.developmentApiDescriptionTextBox.BackColor =
            AddonCenterTheme.Background;
        this.developmentApiDescriptionTextBox.ForeColor =
            AddonCenterTheme.MutedText;
        this.developmentApiDescriptionTextBox.BorderStyle =
            (System.Windows.Forms.BorderStyle)BorderStyle.FixedSingle;
        panel.Controls.Add(this.developmentApiDescriptionTextBox, 0, 4);
        return panel;
    }

    private Control BuildDevelopmentStatusBar()
    {
        var status = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = AddonCenterTheme.ElevatedPanel,
            Padding = new Padding(10, 0, 10, 0),
            ColumnStyles =
            {
                new ColumnStyle(SizeType.Percent, 50),
                new ColumnStyle(SizeType.Percent, 50),
                new ColumnStyle(SizeType.Absolute, 150),
            },
        };

        ConfigureStatusLabel(this.developmentStateLabel, ContentAlignment.MiddleLeft);
        ConfigureStatusLabel(this.developmentCaretLabel, ContentAlignment.MiddleRight);
        this.developmentStateLabel.Text =
            "Create or select a development workspace.";
        this.developmentCaretLabel.Text = "Ln 1, Col 1";

        status.Controls.Add(this.developmentStateLabel, 0, 0);
        status.SetColumnSpan(this.developmentStateLabel, 2);
        status.Controls.Add(this.developmentCaretLabel, 2, 0);
        return status;
    }

    private void ConfigureDevelopmentEditor()
    {
        this.ApplyDevelopmentEditorAppearance("lua");

        this.developmentEditor.Margins[0].Type = MarginType.Number;
        this.developmentEditor.TabWidth = 4;
        this.developmentEditor.UseTabs = false;
        this.developmentEditor.CaretForeColor = AddonCenterTheme.Accent;
        this.developmentEditor.SelectionBackColor =
            AddonCenterTheme.ButtonHover;
        this.developmentEditor.AutoCIgnoreCase = false;
        this.developmentEditor.AutoCAutoHide = true;
        this.developmentEditor.AutoCOrder = Order.PerformSort;
        this.developmentEditor.AutoCSetFillUps("(");
        this.developmentEditor.Indicators[LuaDiagnosticIndicator].Style =
            IndicatorStyle.Squiggle;
        this.developmentEditor.Indicators[LuaDiagnosticIndicator].ForeColor =
            AddonCenterTheme.Danger;
        this.developmentEditor.ReadOnly = true;
    }

    private void ApplyDevelopmentEditorAppearance(string? language = null)
    {
        var fontFamily = this.clientManager.AddonEditorFontFamily;
        var fontSize = this.clientManager.AddonEditorFontSize;

        this.developmentEditor.StyleResetDefault();
        this.developmentEditor.Styles[Style.Default].Font = fontFamily;
        this.developmentEditor.Styles[Style.Default].Size = fontSize;
        this.developmentEditor.Styles[Style.Default].ForeColor =
            AddonCenterTheme.Text;
        this.developmentEditor.Styles[Style.Default].BackColor =
            AddonCenterTheme.Background;
        this.developmentEditor.StyleClearAll();

        this.developmentEditor.Styles[Style.LineNumber].ForeColor =
            AddonCenterTheme.MutedText;
        this.developmentEditor.Styles[Style.LineNumber].BackColor =
            AddonCenterTheme.Panel;
        this.developmentEditor.Styles[Style.LineNumber].Size =
            Math.Max(8, fontSize - 1);
        this.developmentEditor.Margins[0].Width =
            Math.Max(48, (fontSize * 4) + 8);
        this.developmentEditor.Styles[Style.CallTip].ForeColor =
            AddonCenterTheme.Text;
        this.developmentEditor.Styles[Style.CallTip].BackColor =
            AddonCenterTheme.ElevatedPanel;

        this.ConfigureDevelopmentLexer(
            language ?? this.selectedDevelopmentDocument?.Language ?? "lua");
        this.developmentFontButton.Text = string.Concat(
            fontFamily,
            "  ·  ",
            fontSize,
            " pt");
    }

    private void ConfigureDevelopmentLexer(string language)
    {
        this.developmentEditor.LexerName = language is "lua" or "json"
            ? language
            : "null";

        if (string.Equals(language, "json", StringComparison.Ordinal))
        {
            this.developmentEditor.Styles[Style.Json.Number].ForeColor =
                Color.FromArgb(205, 164, 108);
            this.developmentEditor.Styles[Style.Json.String].ForeColor =
                Color.FromArgb(205, 145, 132);
            this.developmentEditor.Styles[Style.Json.PropertyName].ForeColor =
                AddonCenterTheme.Accent;
            this.developmentEditor.Styles[Style.Json.LineComment].ForeColor =
                Color.FromArgb(104, 151, 116);
            this.developmentEditor.Styles[Style.Json.BlockComment].ForeColor =
                Color.FromArgb(104, 151, 116);
            this.developmentEditor.Styles[Style.Json.Operator].ForeColor =
                Color.FromArgb(191, 201, 214);
            this.developmentEditor.Styles[Style.Json.Keyword].ForeColor =
                Color.FromArgb(97, 186, 229);
            return;
        }

        if (!string.Equals(language, "lua", StringComparison.Ordinal))
        {
            return;
        }

        this.developmentEditor.SetKeywords(
            0,
            string.Join(' ', AddonApiCatalog.LuaKeywords));
        this.developmentEditor.SetKeywords(
            1,
            "addon ui actions game");

        this.developmentEditor.Styles[Style.Lua.Comment].ForeColor =
            Color.FromArgb(104, 151, 116);
        this.developmentEditor.Styles[Style.Lua.CommentLine].ForeColor =
            Color.FromArgb(104, 151, 116);
        this.developmentEditor.Styles[Style.Lua.CommentDoc].ForeColor =
            Color.FromArgb(104, 151, 116);
        this.developmentEditor.Styles[Style.Lua.Number].ForeColor =
            Color.FromArgb(205, 164, 108);
        this.developmentEditor.Styles[Style.Lua.Word].ForeColor =
            Color.FromArgb(97, 186, 229);
        this.developmentEditor.Styles[Style.Lua.Word].Bold = true;
        this.developmentEditor.Styles[Style.Lua.Word2].ForeColor =
            AddonCenterTheme.Accent;
        this.developmentEditor.Styles[Style.Lua.String].ForeColor =
            Color.FromArgb(205, 145, 132);
        this.developmentEditor.Styles[Style.Lua.Character].ForeColor =
            Color.FromArgb(205, 145, 132);
        this.developmentEditor.Styles[Style.Lua.LiteralString].ForeColor =
            Color.FromArgb(205, 145, 132);
        this.developmentEditor.Styles[Style.Lua.Operator].ForeColor =
            Color.FromArgb(191, 201, 214);
    }

    private void WireDevelopEvents()
    {
        this.developmentWorkspaceComboBox.SelectedIndexChanged +=
            this.DevelopmentWorkspaceComboBox_OnSelectedIndexChanged;
        this.developmentFileTree.AfterSelect +=
            this.DevelopmentFileTree_OnAfterSelect;
        this.newWorkspaceButton.Click += this.NewWorkspaceButton_OnClick;
        this.refreshWorkspacesButton.Click +=
            this.RefreshWorkspacesButton_OnClick;
        this.saveDocumentButton.Click += this.SaveDocumentButton_OnClick;
        this.validateDocumentButton.Click +=
            this.ValidateDocumentButton_OnClick;
        this.saveReloadButton.Click += this.SaveReloadButton_OnClick;
        this.openWorkspaceFolderButton.Click +=
            this.OpenWorkspaceFolderButton_OnClick;
        this.generateApiFilesButton.Click +=
            this.GenerateApiFilesButton_OnClick;
        this.publishAddonButton.Click +=
            this.PublishAddonButton_OnClick;
        this.developmentFontButton.Click +=
            this.DevelopmentFontButton_OnClick;
        this.developmentEditor.TextChanged += this.DevelopmentEditor_OnTextChanged;
        this.developmentEditor.CharAdded += this.DevelopmentEditor_OnCharAdded;
        this.developmentEditor.UpdateUI += this.DevelopmentEditor_OnUpdateUi;
        this.developmentEditor.KeyDown += this.DevelopmentEditor_OnKeyDown;
        this.developmentDiagnosticsList.DoubleClick +=
            this.DevelopmentDiagnosticsList_OnDoubleClick;
        this.developmentApiSearchTextBox.TextChanged +=
            this.DevelopmentApiSearchTextBox_OnTextChanged;
        this.developmentApiList.SelectedIndexChanged +=
            this.DevelopmentApiList_OnSelectedIndexChanged;
        this.developmentApiList.DoubleClick +=
            this.DevelopmentApiList_OnDoubleClick;
        this.developmentValidationTimer.Tick +=
            this.DevelopmentValidationTimer_OnTick;
        this.FormClosing += this.AddonCenterForm_OnFormClosingForDevelopment;
    }

    private void UnwireDevelopEvents()
    {
        this.developmentWorkspaceComboBox.SelectedIndexChanged -=
            this.DevelopmentWorkspaceComboBox_OnSelectedIndexChanged;
        this.developmentFileTree.AfterSelect -=
            this.DevelopmentFileTree_OnAfterSelect;
        this.newWorkspaceButton.Click -= this.NewWorkspaceButton_OnClick;
        this.refreshWorkspacesButton.Click -=
            this.RefreshWorkspacesButton_OnClick;
        this.saveDocumentButton.Click -= this.SaveDocumentButton_OnClick;
        this.validateDocumentButton.Click -=
            this.ValidateDocumentButton_OnClick;
        this.saveReloadButton.Click -= this.SaveReloadButton_OnClick;
        this.openWorkspaceFolderButton.Click -=
            this.OpenWorkspaceFolderButton_OnClick;
        this.generateApiFilesButton.Click -=
            this.GenerateApiFilesButton_OnClick;
        this.publishAddonButton.Click -=
            this.PublishAddonButton_OnClick;
        this.developmentFontButton.Click -=
            this.DevelopmentFontButton_OnClick;
        this.developmentEditor.TextChanged -= this.DevelopmentEditor_OnTextChanged;
        this.developmentEditor.CharAdded -= this.DevelopmentEditor_OnCharAdded;
        this.developmentEditor.UpdateUI -= this.DevelopmentEditor_OnUpdateUi;
        this.developmentEditor.KeyDown -= this.DevelopmentEditor_OnKeyDown;
        this.developmentDiagnosticsList.DoubleClick -=
            this.DevelopmentDiagnosticsList_OnDoubleClick;
        this.developmentApiSearchTextBox.TextChanged -=
            this.DevelopmentApiSearchTextBox_OnTextChanged;
        this.developmentApiList.SelectedIndexChanged -=
            this.DevelopmentApiList_OnSelectedIndexChanged;
        this.developmentApiList.DoubleClick -=
            this.DevelopmentApiList_OnDoubleClick;
        this.developmentValidationTimer.Tick -=
            this.DevelopmentValidationTimer_OnTick;
        this.FormClosing -= this.AddonCenterForm_OnFormClosingForDevelopment;
        this.developmentValidationTimer.Stop();
    }

    private void EnsureDevelopmentPageInitialized()
    {
        if (this.developmentPageInitialized)
        {
            return;
        }

        this.developmentPageInitialized = true;
        this.RefreshDevelopmentWorkspaces();
    }

    private bool ConfirmDevelopmentWorkspaceSwitch(
        string addonId,
        out bool discardCurrentChanges)
    {
        discardCurrentChanges = this.IsDevelopmentDocumentDirty &&
                                !string.Equals(
                                    this.selectedDevelopmentWorkspace?.Id,
                                    addonId,
                                    StringComparison.Ordinal);
        return !discardCurrentChanges ||
               this.ConfirmDiscardDevelopmentChanges();
    }

    private void OpenDevelopmentWorkspace(
        string addonId,
        bool discardCurrentChanges = false)
    {
        this.ShowPage(AddonCenterPage.Develop);

        if (string.Equals(
                this.selectedDevelopmentWorkspace?.Id,
                addonId,
                StringComparison.Ordinal))
        {
            return;
        }

        if (discardCurrentChanges)
        {
            this.loadedDevelopmentText = this.developmentEditor.Text;
        }

        this.RefreshDevelopmentWorkspaces(addonId);
    }

    private bool RefreshDevelopmentWorkspaces(
        string? selectWorkspaceId = null,
        string? selectDocumentPath = null)
    {
        if (this.IsDevelopmentDocumentDirty)
        {
            if (!this.ConfirmDiscardDevelopmentChanges())
            {
                return false;
            }

            this.loadedDevelopmentText = this.developmentEditor.Text;
        }

        try
        {
            this.developmentWorkspaces =
                this.clientManager.GetAddonDevelopmentWorkspaces();
            var previousId = selectWorkspaceId ??
                             this.selectedDevelopmentWorkspace?.Id;

            this.suppressDevelopmentSelection = true;
            this.developmentWorkspaceComboBox.Items.Clear();

            foreach (var workspace in this.developmentWorkspaces)
            {
                this.developmentWorkspaceComboBox.Items.Add(
                    new DevelopmentWorkspaceComboItem(workspace));
            }

            var selectedIndex = this.developmentWorkspaces
                .Select((workspace, index) => (workspace, index))
                .Where(item => string.Equals(
                    item.workspace.Id,
                    previousId,
                    StringComparison.Ordinal))
                .Select(item => item.index)
                .DefaultIfEmpty(this.developmentWorkspaces.Count > 0 ? 0 : -1)
                .First();

            this.developmentWorkspaceComboBox.SelectedIndex = selectedIndex;
            this.suppressDevelopmentSelection = false;

            if (selectedIndex >= 0)
            {
                this.SelectDevelopmentWorkspace(
                    this.developmentWorkspaces[selectedIndex],
                    selectDocumentPath);
            }
            else
            {
                this.ClearDevelopmentEditor(
                    "No local addon workspaces yet. Create one and the forge lights will come on.",
                    clearWorkspace: true,
                    clearTree: true);
            }

            return true;
        }
        catch (Exception ex)
        {
            this.suppressDevelopmentSelection = false;
            ThemedMessageDialog.ShowWarning(
                this,
                "Development Workspaces",
                ex.Message);
            return false;
        }
    }

    private void SelectDevelopmentWorkspace(
        AddonDevelopmentWorkspace workspace,
        string? preferredDocumentPath = null)
    {
        this.selectedDevelopmentWorkspace = workspace;
        this.developmentDocuments =
            this.clientManager.GetAddonDevelopmentDocuments(workspace.Id);
        this.developmentFileTree.BeginUpdate();

        try
        {
            var root = this.PopulateDevelopmentFileTree(workspace);
            var preferred = !string.IsNullOrWhiteSpace(preferredDocumentPath)
                ? this.developmentDocuments.FirstOrDefault(document =>
                    string.Equals(
                        document.RelativePath,
                        preferredDocumentPath,
                        StringComparison.OrdinalIgnoreCase))
                : null;
            preferred ??= this.developmentDocuments.FirstOrDefault(
                              document => document.IsEntryPoint) ??
                          this.developmentDocuments.FirstOrDefault();

            if (preferred != null)
            {
                var preferredNode = FindDocumentNode(
                    root,
                    preferred.RelativePath);
                this.developmentFileTree.SelectedNode = preferredNode;
                preferredNode?.EnsureVisible();
            }
            else
            {
                this.ClearDevelopmentEditor(
                    "This workspace contains no editable Lua or JSON files.",
                    clearWorkspace: false,
                    clearTree: false);
            }
        }
        finally
        {
            this.developmentFileTree.EndUpdate();
        }

        this.developmentStateLabel.Text = workspace.IsValid
            ? string.Concat(workspace.AddonId, " · development package is loadable")
            : string.Concat(workspace.AddonId, " · ", workspace.ValidationMessage);
        this.UpdateDevelopmentButtons();
    }

    private TreeNode PopulateDevelopmentFileTree(
        AddonDevelopmentWorkspace workspace)
    {
        this.developmentFileTree.Nodes.Clear();
        var root = new TreeNode(workspace.Name)
        {
            Tag = workspace,
            ForeColor = workspace.IsValid
                ? AddonCenterTheme.Text
                : AddonCenterTheme.Warning,
        };
        this.developmentFileTree.Nodes.Add(root);

        foreach (var document in this.developmentDocuments)
        {
            AddDevelopmentDocumentNode(root, document);
        }

        root.Expand();
        return root;
    }

    private void RefreshDevelopmentDocumentTreePreservingEditor()
    {
        var workspace = this.selectedDevelopmentWorkspace;
        var selectedPath = this.selectedDevelopmentDocument?.RelativePath;

        if (workspace == null)
        {
            return;
        }

        this.developmentDocuments =
            this.clientManager.GetAddonDevelopmentDocuments(workspace.Id);
        var refreshedDocument = string.IsNullOrWhiteSpace(selectedPath)
            ? null
            : this.developmentDocuments.FirstOrDefault(document =>
                string.Equals(
                    document.RelativePath,
                    selectedPath,
                    StringComparison.OrdinalIgnoreCase));

        this.developmentFileTree.BeginUpdate();
        this.suppressDevelopmentSelection = true;

        try
        {
            var root = this.PopulateDevelopmentFileTree(workspace);
            this.selectedDevelopmentDocument = refreshedDocument;

            if (refreshedDocument != null)
            {
                var selectedNode = FindDocumentNode(
                    root,
                    refreshedDocument.RelativePath);
                this.developmentFileTree.SelectedNode = selectedNode;
                selectedNode?.EnsureVisible();
            }
        }
        finally
        {
            this.suppressDevelopmentSelection = false;
            this.developmentFileTree.EndUpdate();
        }

        this.UpdateDevelopmentDocumentCaption();
        this.UpdateDevelopmentButtons();
    }

    private static void AddDevelopmentDocumentNode(
        TreeNode root,
        AddonDevelopmentDocument document)
    {
        var parts = document.IsGenerated
            ? new[] { "Generated API", document.FileName }
            : document.RelativePath.Split('/');
        var parent = root;

        for (var index = 0; index < parts.Length - 1; index++)
        {
            var part = parts[index];
            var folder = parent.Nodes.Cast<TreeNode>().FirstOrDefault(node =>
                node.Tag == null &&
                string.Equals(node.Text, part, StringComparison.OrdinalIgnoreCase));

            if (folder == null)
            {
                folder = new TreeNode(part)
                {
                    ForeColor = document.IsGenerated
                        ? AddonCenterTheme.Accent
                        : AddonCenterTheme.MutedText,
                };
                parent.Nodes.Add(folder);
            }

            parent = folder;
        }

        parent.Nodes.Add(new TreeNode(
            document.IsEntryPoint
                ? string.Concat(parts[^1], "  ★")
                : parts[^1])
        {
            Tag = document,
            ForeColor = document.IsGenerated
                ? AddonCenterTheme.Accent
                : document.IsEntryPoint
                    ? AddonCenterTheme.Accent
                    : AddonCenterTheme.Text,
        });
    }

    private static TreeNode? FindDocumentNode(
        TreeNode parent,
        string relativePath)
    {
        foreach (TreeNode node in parent.Nodes)
        {
            if (node.Tag is AddonDevelopmentDocument document &&
                string.Equals(
                    document.RelativePath,
                    relativePath,
                    StringComparison.OrdinalIgnoreCase))
            {
                return node;
            }

            var nested = FindDocumentNode(node, relativePath);

            if (nested != null)
            {
                return nested;
            }
        }

        return null;
    }

    private void OpenDevelopmentDocument(AddonDevelopmentDocument document)
    {
        if (this.selectedDevelopmentWorkspace == null)
        {
            return;
        }

        if (this.IsDevelopmentDocumentDirty &&
            !this.ConfirmDiscardDevelopmentChanges())
        {
            this.ReselectCurrentDevelopmentDocument();
            return;
        }

        try
        {
            var content = this.clientManager.ReadAddonDevelopmentDocument(
                this.selectedDevelopmentWorkspace.Id,
                document.RelativePath);
            this.selectedDevelopmentDocument = document;
            this.loadedDevelopmentText = content.Text;
            this.suppressDevelopmentTextChanged = true;
            this.developmentEditor.ReadOnly = false;
            this.ConfigureDevelopmentLexer(document.Language);
            this.developmentEditor.Text = content.Text;
            this.developmentEditor.EmptyUndoBuffer();
            this.developmentEditor.SetSavePoint();
            this.developmentEditor.GotoPosition(0);
            this.developmentEditor.ReadOnly = document.IsReadOnly;
            this.suppressDevelopmentTextChanged = false;
            this.UpdateDevelopmentDocumentCaption();
            this.ValidateCurrentDevelopmentDocument();
            this.UpdateDevelopmentButtons();
            this.UpdateDevelopmentCaretStatus();
        }
        catch (Exception ex)
        {
            this.suppressDevelopmentTextChanged = false;
            ThemedMessageDialog.ShowWarning(
                this,
                "Open Addon Source",
                ex.Message);
        }
    }

    private void ClearDevelopmentEditor(
        string status,
        bool clearWorkspace,
        bool clearTree)
    {
        if (clearWorkspace)
        {
            this.selectedDevelopmentWorkspace = null;
        }

        this.selectedDevelopmentDocument = null;
        this.loadedDevelopmentText = "";
        this.suppressDevelopmentTextChanged = true;
        this.developmentEditor.ReadOnly = false;
        this.developmentEditor.Text = "";
        this.developmentEditor.ReadOnly = true;
        this.suppressDevelopmentTextChanged = false;
        if (clearTree)
        {
            this.developmentFileTree.Nodes.Clear();
        }

        this.developmentDocumentLabel.Text = "NO DOCUMENT OPEN";
        this.developmentDiagnosticsList.Items.Clear();
        this.developmentStateLabel.Text = status;
        this.UpdateDevelopmentButtons();
    }

    private void ValidateCurrentDevelopmentDocument()
    {
        this.developmentValidationTimer.Stop();

        if (this.selectedDevelopmentWorkspace == null ||
            this.selectedDevelopmentDocument == null)
        {
            return;
        }

        if (this.selectedDevelopmentDocument.IsReadOnly)
        {
            this.ShowDevelopmentDiagnostics(
                new AddonDevelopmentValidationResult
                {
                    Diagnostics =
                    [
                        new AddonDevelopmentDiagnostic
                        {
                            Severity =
                                AddonDevelopmentDiagnosticSeverity.Information,
                            Message =
                                "Generated from the canonical Net7 API catalog. This document is read-only.",
                        },
                    ],
                });
            this.developmentStateLabel.Text =
                "Generated API file · read-only";
            this.developmentStateLabel.ForeColor = AddonCenterTheme.Accent;
            return;
        }

        try
        {
            var validation =
                this.clientManager.ValidateAddonDevelopmentDocument(
                    this.selectedDevelopmentWorkspace.Id,
                    this.selectedDevelopmentDocument.RelativePath,
                    this.developmentEditor.Text);
            this.ShowDevelopmentDiagnostics(validation);
        }
        catch (Exception ex)
        {
            this.ShowDevelopmentDiagnostics(
                new AddonDevelopmentValidationResult
                {
                    Diagnostics =
                    [
                        new AddonDevelopmentDiagnostic
                        {
                            Severity = AddonDevelopmentDiagnosticSeverity.Error,
                            Message = ex.Message,
                        },
                    ],
                });
        }
    }

    private void ShowDevelopmentDiagnostics(
        AddonDevelopmentValidationResult validation)
    {
        this.developmentDiagnostics = validation.Diagnostics;
        this.developmentDiagnosticsList.BeginUpdate();
        this.developmentDiagnosticsList.Items.Clear();

        try
        {
            foreach (var diagnostic in validation.Diagnostics)
            {
                this.developmentDiagnosticsList.Items.Add(
                    new DevelopmentDiagnosticListItem(diagnostic));
            }
        }
        finally
        {
            this.developmentDiagnosticsList.EndUpdate();
        }

        this.developmentEditor.IndicatorCurrent = LuaDiagnosticIndicator;
        this.developmentEditor.IndicatorClearRange(
            0,
            this.developmentEditor.TextLength);

        foreach (var diagnostic in validation.Diagnostics.Where(diagnostic =>
                     diagnostic.Severity ==
                     AddonDevelopmentDiagnosticSeverity.Error &&
                     diagnostic.Line > 0 &&
                     diagnostic.Line <= this.developmentEditor.Lines.Count))
        {
            var line = this.developmentEditor.Lines[diagnostic.Line - 1];
            var length = Math.Max(1, line.EndPosition - line.Position);
            this.developmentEditor.IndicatorFillRange(line.Position, length);
        }

        var errorCount = validation.Diagnostics.Count(diagnostic =>
            diagnostic.Severity == AddonDevelopmentDiagnosticSeverity.Error);
        this.developmentStateLabel.Text = errorCount == 0
            ? string.Concat(
                this.IsDevelopmentDocumentDirty ? "Modified · " : "",
                "validation clean")
            : string.Concat(
                this.IsDevelopmentDocumentDirty ? "Modified · " : "",
                errorCount,
                errorCount == 1 ? " validation error" : " validation errors");
        this.developmentStateLabel.ForeColor = errorCount == 0
            ? AddonCenterTheme.Success
            : AddonCenterTheme.Warning;
    }

    private AddonDevelopmentSaveResult? SaveCurrentDevelopmentDocument()
    {
        if (this.selectedDevelopmentWorkspace == null ||
            this.selectedDevelopmentDocument == null ||
            this.selectedDevelopmentDocument.IsReadOnly)
        {
            return null;
        }

        try
        {
            var result = this.clientManager.SaveAddonDevelopmentDocument(
                this.selectedDevelopmentWorkspace.Id,
                this.selectedDevelopmentDocument.RelativePath,
                this.developmentEditor.Text);
            this.ShowDevelopmentDiagnostics(result.Validation);

            if (!result.Saved)
            {
                ThemedMessageDialog.ShowWarning(
                    this,
                    "Save Addon Source",
                    result.Error);
                return result;
            }

            this.loadedDevelopmentText = this.developmentEditor.Text;
            this.developmentEditor.SetSavePoint();
            this.UpdateDevelopmentDocumentCaption();
            this.developmentStateLabel.Text = result.Validation.Succeeded
                ? "Saved · validation clean"
                : "Saved · validation errors remain, reload blocked";
            this.developmentStateLabel.ForeColor = result.Validation.Succeeded
                ? AddonCenterTheme.Success
                : AddonCenterTheme.Warning;
            return result;
        }
        catch (Exception ex)
        {
            ThemedMessageDialog.ShowWarning(
                this,
                "Save Addon Source",
                ex.Message);
            return null;
        }
    }

    private void ShowCompletion(bool explicitRequest = false)
    {
        if (this.selectedDevelopmentDocument?.Language != "lua")
        {
            return;
        }

        var caret = this.developmentEditor.CurrentPosition;
        var text = this.developmentEditor.Text;
        caret = Math.Clamp(caret, 0, text.Length);
        var start = caret;

        while (start > 0 && IsCompletionCharacter(text[start - 1]))
        {
            start--;
        }

        var token = text[start..caret];
        var eventPrefix = TryGetEventNamePrefix(text, caret);

        if (eventPrefix != null)
        {
            var eventNames = AddonApiCatalog.EventNames
                .Where(name => name.StartsWith(
                    eventPrefix,
                    StringComparison.OrdinalIgnoreCase))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            this.developmentEditor.AutoCShow(
                eventPrefix.Length,
                string.Join(' ', eventNames));
            return;
        }

        var dotIndex = token.LastIndexOf('.');
        var parent = dotIndex >= 0 ? token[..dotIndex] : "";
        var prefix = dotIndex >= 0 ? token[(dotIndex + 1)..] : token;
        var candidates = AddonApiCatalog.GetChildren(parent)
            .Where(symbol => symbol.Kind != AddonApiSymbolKind.Event)
            .Where(symbol => symbol.Name.StartsWith(
                prefix,
                StringComparison.OrdinalIgnoreCase))
            .Select(symbol => symbol.Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        if (candidates.Length == 0 ||
            (!explicitRequest && prefix.Length < 2 && dotIndex < 0))
        {
            return;
        }

        this.developmentEditor.AutoCShow(
            prefix.Length,
            string.Join(' ', candidates));
    }

    private void ShowCallTip()
    {
        var caret = this.developmentEditor.CurrentPosition;
        var text = this.developmentEditor.Text;
        var end = Math.Clamp(caret - 1, 0, text.Length);
        var start = end;

        while (start > 0 && IsCompletionCharacter(text[start - 1]))
        {
            start--;
        }

        var path = text[start..end];
        var symbol = AddonApiCatalog.Find(path);

        if (symbol?.Kind != AddonApiSymbolKind.Function)
        {
            return;
        }

        this.developmentEditor.CallTipShow(
            start,
            string.Concat(
                symbol.Signature,
                "\n",
                symbol.Description,
                string.IsNullOrWhiteSpace(symbol.ReturnType)
                    ? ""
                    : string.Concat("\nReturns: ", symbol.ReturnType)));
    }

    private void RefreshApiSymbolList()
    {
        var query = this.developmentApiSearchTextBox.Text.Trim();
        var symbols = AddonApiCatalog.Symbols
            .Where(symbol => symbol.Kind != AddonApiSymbolKind.Keyword)
            .Where(symbol => string.IsNullOrWhiteSpace(query) ||
                             symbol.Path.Contains(
                                 query,
                                 StringComparison.OrdinalIgnoreCase) ||
                             symbol.Description.Contains(
                                 query,
                                 StringComparison.OrdinalIgnoreCase))
            .OrderBy(symbol => symbol.Path, StringComparer.Ordinal)
            .ToArray();

        this.developmentApiList.BeginUpdate();
        this.developmentApiList.Items.Clear();

        foreach (var symbol in symbols)
        {
            this.developmentApiList.Items.Add(
                new DevelopmentApiListItem(symbol));
        }

        this.developmentApiList.EndUpdate();

        if (this.developmentApiList.Items.Count > 0)
        {
            this.developmentApiList.SelectedIndex = 0;
        }
    }

    private void UpdateDevelopmentButtons()
    {
        var hasWorkspace = this.selectedDevelopmentWorkspace != null;
        var hasDocument = this.selectedDevelopmentDocument != null;
        var hasEditableDocument = this.selectedDevelopmentDocument is
        {
            IsReadOnly: false,
        };
        var enabled = !this.operationInProgress;

        this.newWorkspaceButton.Enabled = enabled;
        this.refreshWorkspacesButton.Enabled = enabled;
        this.developmentWorkspaceComboBox.Enabled = enabled &&
                                                    this.developmentWorkspaces.Count > 0;
        this.saveDocumentButton.Enabled = enabled && hasEditableDocument;
        this.validateDocumentButton.Enabled = enabled && hasEditableDocument;
        this.saveReloadButton.Enabled = enabled && hasEditableDocument;
        this.openWorkspaceFolderButton.Enabled = enabled && hasWorkspace;
        this.generateApiFilesButton.Enabled = enabled && hasWorkspace;
        this.publishAddonButton.Enabled = enabled && hasWorkspace;
        this.developmentFontButton.Enabled = enabled;
        this.developmentFileTree.Enabled = enabled && hasWorkspace;
        this.developmentEditor.ReadOnly = !enabled ||
                                          !hasDocument ||
                                          !hasEditableDocument;
    }

    private void SetDevelopOperationsEnabled(bool enabled)
    {
        this.UpdateDevelopmentButtons();

        if (!enabled)
        {
            this.newWorkspaceButton.Enabled = false;
            this.refreshWorkspacesButton.Enabled = false;
            this.saveDocumentButton.Enabled = false;
            this.validateDocumentButton.Enabled = false;
            this.saveReloadButton.Enabled = false;
            this.openWorkspaceFolderButton.Enabled = false;
            this.generateApiFilesButton.Enabled = false;
            this.publishAddonButton.Enabled = false;
            this.developmentFontButton.Enabled = false;
            this.developmentWorkspaceComboBox.Enabled = false;
            this.developmentFileTree.Enabled = false;
            this.developmentEditor.ReadOnly = true;
        }
    }

    private void UpdateDevelopmentDocumentCaption()
    {
        if (this.selectedDevelopmentDocument == null)
        {
            return;
        }

        this.developmentDocumentLabel.Text = string.Concat(
            this.selectedDevelopmentDocument.IsGenerated
                ? this.selectedDevelopmentDocument.FileName.ToUpperInvariant()
                : this.selectedDevelopmentDocument.RelativePath.ToUpperInvariant(),
            this.selectedDevelopmentDocument.IsEntryPoint
                ? "  ·  ENTRY POINT"
                : "",
            this.selectedDevelopmentDocument.IsGenerated
                ? "  ·  GENERATED · READ ONLY"
                : "",
            this.IsDevelopmentDocumentDirty ? "  ●" : "");
        this.UpdateDevelopmentButtons();
    }

    private void UpdateDevelopmentCaretStatus()
    {
        var position = this.developmentEditor.CurrentPosition;
        var lineIndex = this.developmentEditor.LineFromPosition(position);
        var lineStart = lineIndex >= 0 &&
                        lineIndex < this.developmentEditor.Lines.Count
            ? this.developmentEditor.Lines[lineIndex].Position
            : 0;
        this.developmentCaretLabel.Text = string.Concat(
            "Ln ",
            lineIndex + 1,
            ", Col ",
            Math.Max(0, position - lineStart) + 1);
    }

    private bool ConfirmDiscardDevelopmentChanges()
    {
        return !this.IsDevelopmentDocumentDirty ||
               ThemedMessageDialog.Confirm(
                   this,
                   "Discard Unsaved Addon Changes",
                   "This source file has unsaved changes. Discard them and continue?",
                   "Discard");
    }

    private void ReselectCurrentDevelopmentDocument()
    {
        if (this.selectedDevelopmentDocument == null ||
            this.developmentFileTree.Nodes.Count == 0)
        {
            return;
        }

        this.suppressDevelopmentSelection = true;
        this.developmentFileTree.SelectedNode = FindDocumentNode(
            this.developmentFileTree.Nodes[0],
            this.selectedDevelopmentDocument.RelativePath);
        this.suppressDevelopmentSelection = false;
    }

    private void DevelopmentWorkspaceComboBox_OnSelectedIndexChanged(
        object? sender,
        EventArgs e)
    {
        if (this.suppressDevelopmentSelection ||
            this.developmentWorkspaceComboBox.SelectedItem is not
                DevelopmentWorkspaceComboItem item)
        {
            return;
        }

        if (this.IsDevelopmentDocumentDirty &&
            !this.ConfirmDiscardDevelopmentChanges())
        {
            this.suppressDevelopmentSelection = true;
            this.developmentWorkspaceComboBox.SelectedItem =
                this.developmentWorkspaceComboBox.Items
                    .Cast<DevelopmentWorkspaceComboItem>()
                    .FirstOrDefault(candidate => string.Equals(
                        candidate.Workspace.Id,
                        this.selectedDevelopmentWorkspace?.Id,
                        StringComparison.Ordinal));
            this.suppressDevelopmentSelection = false;
            return;
        }

        this.SelectDevelopmentWorkspace(item.Workspace);
    }

    private void DevelopmentFileTree_OnAfterSelect(
        object? sender,
        TreeViewEventArgs e)
    {
        if (!this.suppressDevelopmentSelection &&
            e.Node.Tag is AddonDevelopmentDocument document)
        {
            this.OpenDevelopmentDocument(document);
        }
    }

    private void NewWorkspaceButton_OnClick(object? sender, EventArgs e)
    {
        using var dialog = new NewAddonWorkspaceDialog();

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            var workspace = this.clientManager.CreateAddonDevelopmentWorkspace(
                dialog.AddonId,
                dialog.AddonName,
                dialog.Author);
            this.RefreshDevelopmentWorkspaces(workspace.Id);
            this.RefreshView(force: true);
        }
        catch (Exception ex)
        {
            ThemedMessageDialog.ShowWarning(
                this,
                "Create Addon Workspace",
                ex.Message);
        }
    }

    private void RefreshWorkspacesButton_OnClick(object? sender, EventArgs e)
    {
        this.RefreshDevelopmentWorkspaces();
    }

    private void SaveDocumentButton_OnClick(object? sender, EventArgs e)
    {
        _ = this.SaveCurrentDevelopmentDocument();
    }

    private void ValidateDocumentButton_OnClick(object? sender, EventArgs e)
    {
        this.ValidateCurrentDevelopmentDocument();
    }

    private async void SaveReloadButton_OnClick(object? sender, EventArgs e)
    {
        var result = this.SaveCurrentDevelopmentDocument();
        var workspace = this.selectedDevelopmentWorkspace;

        if (result is not { Saved: true } ||
            !result.Validation.Succeeded ||
            workspace == null)
        {
            return;
        }

        var workspaceValidation =
            this.clientManager.ValidateAddonDevelopmentWorkspace(workspace.Id);

        if (!workspaceValidation.Succeeded)
        {
            var messages = workspaceValidation.Diagnostics
                .Where(diagnostic =>
                    diagnostic.Severity ==
                    AddonDevelopmentDiagnosticSeverity.Error)
                .Select(diagnostic => diagnostic.Message)
                .Distinct(StringComparer.Ordinal)
                .Take(8)
                .ToArray();

            ThemedMessageDialog.ShowWarning(
                this,
                "Reload Addon",
                string.Concat(
                    "The workspace still contains validation errors. Reload was blocked.",
                    Environment.NewLine,
                    Environment.NewLine,
                    string.Join(Environment.NewLine, messages)));
            return;
        }

        var freshWorkspace = this.clientManager
            .GetAddonDevelopmentWorkspaces()
            .FirstOrDefault(candidate => string.Equals(
                candidate.Id,
                workspace.Id,
                StringComparison.Ordinal));

        if (freshWorkspace is not { IsValid: true })
        {
            ThemedMessageDialog.ShowWarning(
                this,
                "Reload Addon",
                freshWorkspace?.ValidationMessage ??
                "The development package is not currently loadable.");
            return;
        }

        await this.RunCommandAsync(
            string.Concat(
                "Reloading ",
                freshWorkspace.Name,
                "..."),
            () => this.clientManager.ReloadAddonAsync(
                this.ownerProcessId,
                freshWorkspace.AddonId));
        this.RefreshDevelopmentWorkspaces(freshWorkspace.Id);
    }

    private async void PublishAddonButton_OnClick(
        object? sender,
        EventArgs e)
    {
        var workspace = this.selectedDevelopmentWorkspace;

        if (workspace == null)
        {
            return;
        }

        if (this.IsDevelopmentDocumentDirty)
        {
            var saveResult = this.SaveCurrentDevelopmentDocument();

            if (saveResult is not { Saved: true } ||
                !saveResult.Validation.Succeeded)
            {
                return;
            }
        }

        var validation = this.clientManager
            .ValidateAddonDevelopmentWorkspace(workspace.Id);

        if (!validation.Succeeded)
        {
            var messages = validation.Diagnostics
                .Where(diagnostic =>
                    diagnostic.Severity ==
                    AddonDevelopmentDiagnosticSeverity.Error)
                .Select(diagnostic => diagnostic.Message)
                .Distinct(StringComparer.Ordinal)
                .Take(8);
            ThemedMessageDialog.ShowWarning(
                this,
                "Publish Addon",
                string.Concat(
                    "The workspace contains validation errors.",
                    Environment.NewLine,
                    Environment.NewLine,
                    string.Join(Environment.NewLine, messages)));
            return;
        }

        var freshWorkspace = this.clientManager
            .GetAddonDevelopmentWorkspaces()
            .FirstOrDefault(candidate => string.Equals(
                candidate.Id,
                workspace.Id,
                StringComparison.Ordinal));

        if (freshWorkspace is not { IsValid: true, Manifest: not null })
        {
            ThemedMessageDialog.ShowWarning(
                this,
                "Publish Addon",
                freshWorkspace?.ValidationMessage ??
                "The addon workspace is not currently publishable.");
            return;
        }

        var pilotName = this.clientManager.GetAddonPublicationPilotName(
            this.ownerProcessId);

        if (pilotName.Length == 0)
        {
            ThemedMessageDialog.ShowWarning(
                this,
                "Publish Addon",
                "Log a pilot into the game before publishing an addon.");
            return;
        }

        using var dialog = new PublishAddonDialog(
            freshWorkspace.Manifest,
            pilotName);

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        this.SetOperationState(
            inProgress: true,
            string.Concat(
                "Publishing ",
                freshWorkspace.Name,
                " ",
                freshWorkspace.Manifest.Version,
                "..."));

        try
        {
            var result = await this.clientManager
                .PublishAddonDevelopmentWorkspaceAsync(
                    this.ownerProcessId,
                    freshWorkspace.Id,
                    dialog.Summary);
            this.RefreshDevelopmentWorkspaces(freshWorkspace.Id);
            this.RefreshView(force: true);

            var status = result.AlreadyPublished
                ? string.Concat(
                    result.Release.Name,
                    " ",
                    result.Release.Version,
                    " was already published with identical content.")
                : string.Concat(
                    "Published ",
                    result.Release.Name,
                    " ",
                    result.Release.Version,
                    " by ",
                    result.Release.PublisherName,
                    ".");
            this.developmentStateLabel.Text = status;
            this.developmentStateLabel.ForeColor = AddonCenterTheme.Success;
            this.summaryLabel.Text = status;
            this.summaryLabel.ForeColor = AddonCenterTheme.Success;
        }
        catch (Exception ex)
        {
            this.developmentStateLabel.Text = ex.Message;
            this.developmentStateLabel.ForeColor = AddonCenterTheme.Danger;
            ThemedMessageDialog.ShowWarning(
                this,
                "Publish Addon",
                ex.Message);
        }
        finally
        {
            this.SetOperationState(inProgress: false);
        }
    }

    private void OpenWorkspaceFolderButton_OnClick(object? sender, EventArgs e)
    {
        if (this.selectedDevelopmentWorkspace == null)
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = this.selectedDevelopmentWorkspace.DirectoryPath,
            UseShellExecute = true,
        });
    }

    private void GenerateApiFilesButton_OnClick(object? sender, EventArgs e)
    {
        if (this.selectedDevelopmentWorkspace == null)
        {
            return;
        }

        try
        {
            var preserveCurrentSource = this.IsDevelopmentDocumentDirty;
            this.clientManager.WriteAddonDevelopmentApiStub(
                this.selectedDevelopmentWorkspace.Id);
            this.clientManager.WriteAddonDevelopmentApiReference(
                this.selectedDevelopmentWorkspace.Id);

            if (preserveCurrentSource)
            {
                this.RefreshDevelopmentDocumentTreePreservingEditor();
                this.developmentStateLabel.Text =
                    "Generated the API files under Generated API. Your unsaved source remains open.";
            }
            else
            {
                this.RefreshDevelopmentWorkspaces(
                    this.selectedDevelopmentWorkspace.Id,
                    AddonDevelopmentWorkspaceService
                        .GeneratedApiReferenceRelativePath);
                this.developmentStateLabel.Text =
                    "Generated the API annotations and reference, then opened NET7-API.md.";
            }

            this.developmentStateLabel.ForeColor = AddonCenterTheme.Success;
        }
        catch (Exception ex)
        {
            ThemedMessageDialog.ShowWarning(
                this,
                "Generate API Files",
                ex.Message);
        }
    }

    private void DevelopmentFontButton_OnClick(
        object? sender,
        EventArgs e)
    {
        using var dialog = new AddonEditorFontDialog(
            this.clientManager.AddonEditorFontFamily,
            this.clientManager.AddonEditorFontSize);

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        this.clientManager.SetAddonEditorFont(
            dialog.SelectedFontFamily,
            dialog.SelectedFontSize);
        this.ApplyDevelopmentEditorAppearance();
        this.developmentStateLabel.Text = string.Concat(
            "Editor font changed to ",
            dialog.SelectedFontFamily,
            " ",
            dialog.SelectedFontSize,
            " pt.");
        this.developmentStateLabel.ForeColor = AddonCenterTheme.Success;
    }

    private void DevelopmentEditor_OnTextChanged(object? sender, EventArgs e)
    {
        if (this.suppressDevelopmentTextChanged)
        {
            return;
        }

        this.UpdateDevelopmentDocumentCaption();
        this.developmentStateLabel.Text = "Modified · validation pending";
        this.developmentStateLabel.ForeColor = AddonCenterTheme.Accent;
        this.developmentValidationTimer.Stop();
        this.developmentValidationTimer.Start();
    }

    private void DevelopmentEditor_OnCharAdded(
        object? sender,
        CharAddedEventArgs e)
    {
        var character = (char)e.Char;

        if (character == '(')
        {
            this.ShowCallTip();
        }
        else if (character == '.' ||
                 character == '_' ||
                 char.IsLetterOrDigit(character) ||
                 character is '"' or '\'')
        {
            this.ShowCompletion();
        }
    }

    private void DevelopmentEditor_OnUpdateUi(
        object? sender,
        UpdateUIEventArgs e)
    {
        this.UpdateDevelopmentCaretStatus();
    }

    private void DevelopmentEditor_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.Space)
        {
            this.ShowCompletion(explicitRequest: true);
            e.SuppressKeyPress = true;
        }
        else if (e.Control && e.Shift && e.KeyCode == Keys.S)
        {
            this.SaveReloadButton_OnClick(sender, EventArgs.Empty);
            e.SuppressKeyPress = true;
        }
        else if (e.Control && e.KeyCode == Keys.S)
        {
            _ = this.SaveCurrentDevelopmentDocument();
            e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.F8)
        {
            this.ValidateCurrentDevelopmentDocument();
            e.SuppressKeyPress = true;
        }
    }

    private void DevelopmentDiagnosticsList_OnDoubleClick(
        object? sender,
        EventArgs e)
    {
        if (this.developmentDiagnosticsList.SelectedIndex < 0 ||
            this.developmentDiagnosticsList.SelectedIndex >=
            this.developmentDiagnostics.Count)
        {
            return;
        }

        var diagnostic = this.developmentDiagnostics[
            this.developmentDiagnosticsList.SelectedIndex];

        if (diagnostic.Line <= 0 ||
            diagnostic.Line > this.developmentEditor.Lines.Count)
        {
            return;
        }

        var line = this.developmentEditor.Lines[diagnostic.Line - 1];
        var position = Math.Min(
            line.EndPosition,
            line.Position + Math.Max(0, diagnostic.Column - 1));
        this.developmentEditor.GotoPosition(position);
        this.developmentEditor.Focus();
    }

    private void DevelopmentApiSearchTextBox_OnTextChanged(
        object? sender,
        EventArgs e)
    {
        this.RefreshApiSymbolList();
    }

    private void DevelopmentApiList_OnSelectedIndexChanged(
        object? sender,
        EventArgs e)
    {
        if (this.developmentApiList.SelectedItem is not
            DevelopmentApiListItem item)
        {
            return;
        }

        this.developmentApiSignatureLabel.Text =
            string.IsNullOrWhiteSpace(item.Symbol.Signature)
                ? item.Symbol.Path
                : item.Symbol.Signature;
        this.developmentApiDescriptionTextBox.Text = string.Concat(
            item.Symbol.Description,
            string.IsNullOrWhiteSpace(item.Symbol.ReturnType)
                ? ""
                : string.Concat(
                    Environment.NewLine,
                    Environment.NewLine,
                    "Returns: ",
                    item.Symbol.ReturnType));
    }

    private void DevelopmentApiList_OnDoubleClick(object? sender, EventArgs e)
    {
        if (this.developmentApiList.SelectedItem is not
                DevelopmentApiListItem item ||
            this.selectedDevelopmentDocument?.Language != "lua")
        {
            return;
        }

        var insertText = item.Symbol.Kind == AddonApiSymbolKind.Event
            ? string.Concat("\"", item.Symbol.Path, "\"")
            : item.Symbol.Path;
        this.developmentEditor.ReplaceSelection(insertText);
        this.developmentEditor.Focus();
    }

    private void DevelopmentValidationTimer_OnTick(object? sender, EventArgs e)
    {
        this.developmentValidationTimer.Stop();
        this.ValidateCurrentDevelopmentDocument();
    }

    private void AddonCenterForm_OnFormClosingForDevelopment(
        object? sender,
        FormClosingEventArgs e)
    {
        if (this.IsDevelopmentDocumentDirty &&
            !this.ConfirmDiscardDevelopmentChanges())
        {
            e.Cancel = true;
        }
    }

    private static bool IsCompletionCharacter(char character)
    {
        return char.IsLetterOrDigit(character) ||
               character is '_' or '.';
    }

    private static string? TryGetEventNamePrefix(string text, int caret)
    {
        var lineStart = text.LastIndexOf('\n', Math.Max(0, caret - 1));
        lineStart = lineStart < 0 ? 0 : lineStart + 1;
        var line = text[lineStart..caret];
        var marker = "game.events.on";
        var markerIndex = line.LastIndexOf(marker, StringComparison.Ordinal);

        if (markerIndex < 0)
        {
            return null;
        }

        var index = markerIndex + marker.Length;

        while (index < line.Length && char.IsWhiteSpace(line[index]))
        {
            index++;
        }

        if (index >= line.Length || line[index] != '(')
        {
            return null;
        }

        index++;

        while (index < line.Length && char.IsWhiteSpace(line[index]))
        {
            index++;
        }

        if (index >= line.Length || line[index] is not ('"' or '\''))
        {
            return null;
        }

        var quote = line[index++];
        var prefix = line[index..];
        return prefix.Contains(quote) ? null : prefix;
    }

    private static void ConfigureDevelopmentButton(Button button, string text)
    {
        AddonCenterTheme.StyleButton(button);
        button.Text = text;
        button.Dock = DockStyle.Fill;
        button.Margin = new Padding(4, 0, 0, 0);
    }

    private static void ConfigureStatusLabel(
        Label label,
        ContentAlignment alignment)
    {
        label.Dock = DockStyle.Fill;
        label.ForeColor = AddonCenterTheme.MutedText;
        label.Font = new Font("Segoe UI", 8.25f);
        label.TextAlign = alignment;
        label.AutoEllipsis = true;
    }

    private sealed record DevelopmentWorkspaceComboItem(
        AddonDevelopmentWorkspace Workspace)
    {
        public override string ToString()
        {
            return string.Concat(
                this.Workspace.Name,
                "  ·  ",
                this.Workspace.AddonId,
                this.Workspace.IsValid ? "" : "  ⚠");
        }
    }

    private sealed record DevelopmentDiagnosticListItem(
        AddonDevelopmentDiagnostic Diagnostic)
    {
        public override string ToString()
        {
            var location = this.Diagnostic.Line > 0
                ? string.Concat(
                    "L",
                    this.Diagnostic.Line,
                    this.Diagnostic.Column > 0
                        ? string.Concat(":", this.Diagnostic.Column)
                        : "",
                    "  ")
                : "";
            var icon = this.Diagnostic.Severity switch
            {
                AddonDevelopmentDiagnosticSeverity.Error => "ERR",
                AddonDevelopmentDiagnosticSeverity.Warning => "WARN",
                _ => "OK",
            };

            return string.Concat(
                icon,
                "  ",
                location,
                this.Diagnostic.Message);
        }
    }

    private sealed record DevelopmentApiListItem(AddonApiSymbol Symbol)
    {
        public override string ToString()
        {
            var kind = this.Symbol.Kind switch
            {
                AddonApiSymbolKind.Function => "fn",
                AddonApiSymbolKind.Table => "tbl",
                AddonApiSymbolKind.Field => "val",
                AddonApiSymbolKind.Event => "evt",
                _ => "lua",
            };

            return string.Concat(kind.PadRight(4), this.Symbol.Path);
        }
    }
}
