// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

public sealed partial class MainForm
{
    private void RefreshProfileComboBox()
    {
        var selectedProfileId = this.clientManager.CurrentProfile.Id;

        this.isRefreshingProfileComboBox = true;

        try
        {
            this.profileComboBox.BeginUpdate();
            this.profileComboBox.Items.Clear();

            foreach (var profile in this.clientManager.Profiles)
            {
                this.profileComboBox.Items.Add(new ProfileComboBoxItem(profile.Id, profile.Name));
            }

            for (var index = 0; index < this.profileComboBox.Items.Count; index++)
            {
                if (this.profileComboBox.Items[index] is ProfileComboBoxItem item
                    && item.ProfileId == selectedProfileId)
                {
                    this.profileComboBox.SelectedIndex = index;
                    break;
                }
            }
        }
        finally
        {
            this.profileComboBox.EndUpdate();
            this.isRefreshingProfileComboBox = false;
        }

        this.deleteProfileButton.Enabled = this.clientManager.Profiles.Count > 1;
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

        if (selectedProfile.ProfileId == this.clientManager.CurrentProfile.Id)
        {
            return;
        }

        this.clientManager.SwitchProfile(selectedProfile.ProfileId);
        this.layoutDesignerControl.SelectSlot(slot: null);
        this.RefreshAll();
    }

    private void AddProfileButton_OnClick(object? sender, EventArgs e)
    {
        using var form = new ProfileNameForm("Create Profile", "Profile name:", "New Profile");

        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        this.clientManager.CreateProfile(form.ProfileName);
        this.layoutDesignerControl.SelectSlot(slot: null);
        this.RefreshAll();
    }

    private void RenameProfileButton_OnClick(object? sender, EventArgs e)
    {
        var currentProfile = this.clientManager.CurrentProfile;

        using var form = new ProfileNameForm("Rename Profile", "Profile name:", currentProfile.Name);

        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        this.clientManager.RenameProfile(currentProfile.Id, form.ProfileName);
        this.RefreshAll();
    }

    private void DuplicateProfileButton_OnClick(object? sender, EventArgs e)
    {
        var currentProfile = this.clientManager.CurrentProfile;

        this.clientManager.DuplicateProfile(currentProfile.Id);
        this.layoutDesignerControl.SelectSlot(slot: null);
        this.RefreshAll();
    }

    private void DeleteProfileButton_OnClick(object? sender, EventArgs e)
    {
        var currentProfile = this.clientManager.CurrentProfile;

        if (this.clientManager.Profiles.Count <= 1)
        {
            return;
        }

        var result = MessageBox.Show(
            this,
            $"Delete profile '{currentProfile.Name}'?",
            "Delete Profile",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (result != DialogResult.Yes)
        {
            return;
        }

        this.clientManager.DeleteProfile(currentProfile.Id);
        this.layoutDesignerControl.SelectSlot(slot: null);
        this.RefreshAll();
    }
}
