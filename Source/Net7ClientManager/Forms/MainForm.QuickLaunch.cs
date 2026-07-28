// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

public sealed partial class MainForm
{
    private void ReloadQuickLaunchControls()
    {
        this.isRefreshingQuickLaunchControls = true;

        try
        {
            PopulateQuickLaunchResolutionCombo(
                this.quickLaunchHostResolutionComboBox);
            PopulateQuickLaunchResolutionCombo(
                this.quickLaunchGameResolutionComboBox);

            var settings = this.clientManager.QuickLaunchSettings;
            var selectedHostItem = this.quickLaunchHostResolutionComboBox.Items
                .OfType<QuickLaunchResolutionItem>()
                .FirstOrDefault(item => string.Equals(
                    item.Name,
                    settings.HostResolutionPresetName,
                    StringComparison.Ordinal))
                ?? this.quickLaunchHostResolutionComboBox.Items
                    .OfType<QuickLaunchResolutionItem>()
                    .First();

            this.quickLaunchHostResolutionComboBox.SelectedItem =
                selectedHostItem;

            this.independentQuickLaunchGameResolution =
                FindQuickLaunchResolutionItem(
                    this.quickLaunchGameResolutionComboBox,
                    settings.GameResolutionWidth,
                    settings.GameResolutionHeight)
                ?? FindQuickLaunchResolutionItem(
                    this.quickLaunchGameResolutionComboBox,
                    selectedHostItem.Width,
                    selectedHostItem.Height)
                ?? this.quickLaunchGameResolutionComboBox.Items
                    .OfType<QuickLaunchResolutionItem>()
                    .First();

            this.quickLaunchMatchGameResolutionCheckBox.Checked =
                settings.MatchGameResolutionToHost;

            this.quickLaunchGameResolutionComboBox.SelectedItem =
                settings.MatchGameResolutionToHost
                    ? FindQuickLaunchResolutionItem(
                        this.quickLaunchGameResolutionComboBox,
                        selectedHostItem.Width,
                        selectedHostItem.Height)
                    : this.independentQuickLaunchGameResolution;
        }
        finally
        {
            this.isRefreshingQuickLaunchControls = false;
        }

        this.UpdateQuickLaunchControlState();
    }

    private void QuickLaunchHostResolutionComboBox_OnSelectedIndexChanged(
        object? sender,
        EventArgs e)
    {
        if (this.isRefreshingQuickLaunchControls)
        {
            return;
        }

        if (this.quickLaunchMatchGameResolutionCheckBox.Checked &&
            this.quickLaunchHostResolutionComboBox.SelectedItem is
                QuickLaunchResolutionItem hostResolution)
        {
            this.SelectQuickLaunchGameResolution(
                hostResolution.Width,
                hostResolution.Height);
        }

        this.PersistQuickLaunchSettings();
    }

    private void QuickLaunchMatchGameResolutionCheckBox_OnCheckedChanged(
        object? sender,
        EventArgs e)
    {
        if (this.isRefreshingQuickLaunchControls)
        {
            return;
        }

        if (this.quickLaunchMatchGameResolutionCheckBox.Checked)
        {
            if (this.quickLaunchGameResolutionComboBox.SelectedItem is
                QuickLaunchResolutionItem currentGameResolution)
            {
                this.independentQuickLaunchGameResolution =
                    currentGameResolution;
            }

            if (this.quickLaunchHostResolutionComboBox.SelectedItem is
                QuickLaunchResolutionItem hostResolution)
            {
                this.SelectQuickLaunchGameResolution(
                    hostResolution.Width,
                    hostResolution.Height);
            }
        }
        else if (this.independentQuickLaunchGameResolution != null)
        {
            this.SelectQuickLaunchGameResolution(
                this.independentQuickLaunchGameResolution.Width,
                this.independentQuickLaunchGameResolution.Height);
        }

        this.UpdateQuickLaunchControlState();
        this.PersistQuickLaunchSettings();
    }

    private void QuickLaunchGameResolutionComboBox_OnSelectedIndexChanged(
        object? sender,
        EventArgs e)
    {
        if (this.isRefreshingQuickLaunchControls ||
            this.quickLaunchMatchGameResolutionCheckBox.Checked ||
            this.quickLaunchGameResolutionComboBox.SelectedItem is not
                QuickLaunchResolutionItem gameResolution)
        {
            return;
        }

        this.independentQuickLaunchGameResolution = gameResolution;
        this.PersistQuickLaunchSettings();
    }

    private void PersistQuickLaunchSettings()
    {
        if (this.isRefreshingQuickLaunchControls ||
            this.quickLaunchHostResolutionComboBox.SelectedItem is not
                QuickLaunchResolutionItem hostResolution)
        {
            return;
        }

        var settings = this.clientManager.QuickLaunchSettings;
        settings.HostResolutionPresetName = hostResolution.Name;
        settings.MatchGameResolutionToHost =
            this.quickLaunchMatchGameResolutionCheckBox.Checked;

        var independentResolution =
            this.quickLaunchMatchGameResolutionCheckBox.Checked
                ? this.independentQuickLaunchGameResolution
                : this.quickLaunchGameResolutionComboBox.SelectedItem as
                    QuickLaunchResolutionItem;

        independentResolution ??= hostResolution;

        settings.GameResolutionWidth = independentResolution.Width;
        settings.GameResolutionHeight = independentResolution.Height;

        this.clientManager.SaveQuickLaunchSettings();
    }

    private void UpdateQuickLaunchControlState()
    {
        var launchInProgress =
            this.clientManager.IsManagedClientLaunchInProgress;

        this.quickLaunchHostResolutionComboBox.Enabled =
            !launchInProgress;
        this.quickLaunchMatchGameResolutionCheckBox.Enabled =
            !launchInProgress;
        this.quickLaunchGameResolutionComboBox.Enabled =
            !launchInProgress &&
            !this.quickLaunchMatchGameResolutionCheckBox.Checked;
        this.startClientButton.Enabled = !launchInProgress;
    }

    private void SelectQuickLaunchGameResolution(int width, int height)
    {
        var item = FindQuickLaunchResolutionItem(
            this.quickLaunchGameResolutionComboBox,
            width,
            height);

        if (item == null)
        {
            return;
        }

        var wasRefreshing = this.isRefreshingQuickLaunchControls;
        this.isRefreshingQuickLaunchControls = true;

        try
        {
            this.quickLaunchGameResolutionComboBox.SelectedItem = item;
        }
        finally
        {
            this.isRefreshingQuickLaunchControls = wasRefreshing;
        }
    }

    private void PopulateQuickLaunchResolutionCombo(ComboBox comboBox)
    {
        comboBox.BeginUpdate();

        try
        {
            comboBox.Items.Clear();

            foreach (var preset in this.clientManager.SlotResolutionPresets)
            {
                comboBox.Items.Add(
                    new QuickLaunchResolutionItem(
                        preset.Name,
                        preset.Width,
                        preset.Height));
            }
        }
        finally
        {
            comboBox.EndUpdate();
        }
    }

    private static QuickLaunchResolutionItem?
        FindQuickLaunchResolutionItem(
            ComboBox comboBox,
            int width,
            int height)
    {
        return comboBox.Items
            .OfType<QuickLaunchResolutionItem>()
            .FirstOrDefault(item =>
                item.Width == width &&
                item.Height == height);
    }

    private sealed record QuickLaunchResolutionItem(
        string Name,
        int Width,
        int Height)
    {
        public override string ToString()
        {
            return this.Name;
        }
    }
}
