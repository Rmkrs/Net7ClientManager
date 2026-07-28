// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

public sealed partial class MainForm
{
    private void RefreshProfileComboBox()
    {
        var selectedProfileId = this.clientManager.ActiveProfile?.Id;
        var signature = string.Concat(
            selectedProfileId?.ToString() ?? "none",
            "|",
            string.Join(
                separator: ";",
                this.clientManager.Profiles.Select(profile => string.Concat(
                    profile.Id,
                    ':',
                    profile.Name))));

        this.RefreshProfileControls();

        if (string.Equals(
                this.profileComboBoxSignature,
                signature,
                StringComparison.Ordinal))
        {
            return;
        }

        this.isRefreshingProfileComboBox = true;

        try
        {
            this.profileComboBox.BeginUpdate();
            this.profileComboBox.Items.Clear();
            this.profileComboBox.Items.Add(
                new ProfileComboBoxItem(null, "No Profile"));

            foreach (var profile in this.clientManager.Profiles)
            {
                this.profileComboBox.Items.Add(
                    new ProfileComboBoxItem(profile.Id, profile.Name));
            }

            for (var index = 0; index < this.profileComboBox.Items.Count; index++)
            {
                if (this.profileComboBox.Items[index] is ProfileComboBoxItem item &&
                    item.ProfileId == selectedProfileId)
                {
                    this.profileComboBox.SelectedIndex = index;
                    break;
                }
            }

            this.profileComboBoxSignature = signature;
        }
        finally
        {
            this.profileComboBox.EndUpdate();
            this.isRefreshingProfileComboBox = false;
        }
    }

    private void RefreshProfileControls()
    {
        var profile = this.clientManager.ActiveProfile;
        var hasProfile = profile != null;
        var hasSlots = profile?.Slots.Count > 0;

        this.renameProfileButton.Enabled = hasProfile;
        this.duplicateProfileButton.Enabled = hasProfile;
        this.deleteProfileButton.Enabled = hasProfile;
        this.addSlotButton.Enabled = hasProfile;
        this.editLayoutButton.Enabled = hasProfile;
        this.createMissingClientsButton.Enabled =
            hasProfile &&
            hasSlots &&
            !this.clientManager.IsManagedClientLaunchInProgress;
        this.keepClientsAliveCheckBox.Enabled = hasProfile;
        this.UpdateQuickLaunchControlState();

        this.isRefreshingProfileControls = true;

        try
        {
            this.keepClientsAliveCheckBox.Checked =
                profile?.KeepClientsAlive == true;
        }
        finally
        {
            this.isRefreshingProfileControls = false;
        }
    }

    private void ProfileComboBox_OnSelectedIndexChanged(object? sender, EventArgs e)
    {
        if (this.isRefreshingProfileComboBox)
        {
            return;
        }

        if (this.profileComboBox.SelectedItem is not ProfileComboBoxItem selectedProfile)
        {
            return;
        }

        if (selectedProfile.ProfileId == this.clientManager.ActiveProfile?.Id)
        {
            return;
        }

        this.clientManager.SwitchProfile(selectedProfile.ProfileId);

        if (this.clientManager.KeepClientsAlive)
        {
            this.clientManager.CreateMissingClients(this);
        }

        this.RefreshAll();
    }

    private void AddProfileButton_OnClick(object? sender, EventArgs e)
    {
        using var form = new ProfileNameForm(
            "Create Profile",
            "Profile name:",
            "New Profile");

        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        this.clientManager.CreateProfile(form.ProfileName);
        this.RefreshAll();
    }

    private void RenameProfileButton_OnClick(object? sender, EventArgs e)
    {
        var currentProfile = this.clientManager.ActiveProfile;

        if (currentProfile == null)
        {
            return;
        }

        using var form = new ProfileNameForm(
            "Rename Profile",
            "Profile name:",
            currentProfile.Name);

        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        this.clientManager.RenameProfile(
            currentProfile.Id,
            form.ProfileName);
        this.RefreshAll();
    }

    private void DuplicateProfileButton_OnClick(object? sender, EventArgs e)
    {
        var currentProfile = this.clientManager.ActiveProfile;

        if (currentProfile == null)
        {
            return;
        }

        this.clientManager.DuplicateProfile(currentProfile.Id);
        this.RefreshAll();
    }

    private void DeleteProfileButton_OnClick(object? sender, EventArgs e)
    {
        var currentProfile = this.clientManager.ActiveProfile;

        if (currentProfile == null)
        {
            return;
        }

        if (!ThemedMessageDialog.Confirm(
                this,
                "Delete profile",
                $"Delete profile '{currentProfile.Name}'?",
                confirmButtonText: "Delete"))
        {
            return;
        }

        this.clientManager.DeleteProfile(currentProfile.Id);
        this.RefreshAll();
    }
}
