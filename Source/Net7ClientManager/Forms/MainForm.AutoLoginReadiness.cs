// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using System.Globalization;
using Net7ClientManager.Models;
using Net7ClientManager.Services;

public sealed partial class MainForm
{
    private void AutoLoginReadinessButton_OnClick(
        object? sender,
        EventArgs e)
    {
        var issue = this.EvaluateAutoLoginReadiness();

        using var dialog = new AutoLoginReadinessDialog(issue);

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        this.ShowAutoLoginReadinessIssue(issue);
    }

    private AutoLoginReadinessIssue EvaluateAutoLoginReadiness()
    {
        var profile = this.clientManager.ActiveProfile;

        if (profile == null)
        {
            return new AutoLoginReadinessIssue(
                AutoLoginReadinessIssueKind.NoProfile,
                "Choose or create a profile",
                "Automatic login is configured through a profile. Create a profile first, then add the client slots you want it to manage.");
        }

        if (this.clientManager.ConfiguredAccounts.Count == 0)
        {
            return new AutoLoginReadinessIssue(
                AutoLoginReadinessIssueKind.NoAccounts,
                "Add your game account",
                "No Earth & Beyond account has been added yet. Add the account you use to sign in.");
        }

        var accountIssue = this.EvaluateAccountSetupReadiness(profile);

        if (accountIssue != null)
        {
            return accountIssue;
        }

        if (profile.Slots.Count == 0)
        {
            return new AutoLoginReadinessIssue(
                AutoLoginReadinessIssueKind.NoSlots,
                "Add a client slot",
                string.Concat(
                    "Your account is ready. Now add a client slot so profile '",
                    profile.Name,
                    "' knows where and how to launch it."));
        }

        foreach (var slot in profile.Slots)
        {
            var issue = this.EvaluateSlotAutoLoginReadiness(slot);

            if (issue != null)
            {
                return issue;
            }
        }

        var slotCountText = profile.Slots.Count == 1
            ? "The client slot is"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"All {profile.Slots.Count} client slots are");

        return new AutoLoginReadinessIssue(
            AutoLoginReadinessIssueKind.Ready,
            "Automatic login is ready",
            string.Concat(
                slotCountText,
                " ready. Each slot has an account, stored password, selected character, and both automation options enabled."));
    }

    private AutoLoginReadinessIssue? EvaluateAccountSetupReadiness(
        LayoutProfile profile)
    {
        var accountsToCheck = new List<GameAccount>();
        var seenAccountIds = new HashSet<Guid>();

        foreach (var slot in profile.Slots)
        {
            if (slot.AccountId is not { } accountId ||
                !seenAccountIds.Add(accountId))
            {
                continue;
            }

            var account = this.clientManager.FindConfiguredAccount(accountId);

            if (account != null)
            {
                accountsToCheck.Add(account);
            }
        }

        if (accountsToCheck.Count == 0)
        {
            accountsToCheck.Add(this.clientManager.ConfiguredAccounts[0]);
        }

        foreach (var account in accountsToCheck)
        {
            var issue = EvaluateAccountAutoLoginReadiness(account);

            if (issue != null)
            {
                return issue;
            }
        }

        return null;
    }

    private static AutoLoginReadinessIssue?
        EvaluateAccountAutoLoginReadiness(GameAccount account)
    {
        var accountName = UiObfuscationMode.AccountName(account.ToString());

        if (string.IsNullOrWhiteSpace(account.LoginName))
        {
            return new AutoLoginReadinessIssue(
                AutoLoginReadinessIssueKind.AccountLoginMissing,
                "Enter the login name",
                string.Concat(
                    "Account '",
                    accountName,
                    "' has no login name. Enter the username used to sign in to Earth & Beyond."),
                AccountId: account.Id);
        }

        if (string.IsNullOrWhiteSpace(account.ProtectedPassword))
        {
            return new AutoLoginReadinessIssue(
                AutoLoginReadinessIssueKind.AccountPasswordMissing,
                "Store the account password",
                string.Concat(
                    "Account '",
                    accountName,
                    "' has no stored password. Store it before creating the client slot."),
                AccountId: account.Id);
        }

        if (string.IsNullOrEmpty(
                PasswordProtector.Unprotect(account.ProtectedPassword)))
        {
            return new AutoLoginReadinessIssue(
                AutoLoginReadinessIssueKind.AccountPasswordUnavailable,
                "Store the account password again",
                string.Concat(
                    "The stored password for account '",
                    accountName,
                    "' cannot be read by this Windows user. Enter it again."),
                AccountId: account.Id);
        }

        if (account.Characters.Count == 0)
        {
            return new AutoLoginReadinessIssue(
                AutoLoginReadinessIssueKind.AccountHasNoCharacters,
                "Add the character",
                string.Concat(
                    "Account '",
                    accountName,
                    "' has no configured characters. Add the character you want Client Manager to enter automatically."),
                AccountId: account.Id);
        }

        return null;
    }

    private AutoLoginReadinessIssue? EvaluateSlotAutoLoginReadiness(
        ClientSlot slot)
    {
        if (slot.AccountId is not { } accountId)
        {
            return new AutoLoginReadinessIssue(
                AutoLoginReadinessIssueKind.SlotAccountNotSelected,
                "Choose an account",
                string.Concat(
                    "Slot '",
                    slot.Name,
                    "' has no account assigned. Choose the account this slot should use."),
                SlotId: slot.Id);
        }

        var account = this.clientManager.FindConfiguredAccount(accountId);

        if (account == null)
        {
            return new AutoLoginReadinessIssue(
                AutoLoginReadinessIssueKind.SlotAccountMissing,
                "Choose an available account",
                string.Concat(
                    "The account assigned to slot '",
                    slot.Name,
                    "' no longer exists. Choose another account."),
                SlotId: slot.Id);
        }

        var accountIssue = EvaluateAccountAutoLoginReadiness(account);

        if (accountIssue != null)
        {
            return accountIssue;
        }

        if (slot.CharacterId is not { } characterId)
        {
            return new AutoLoginReadinessIssue(
                AutoLoginReadinessIssueKind.SlotCharacterNotSelected,
                "Choose a character",
                string.Concat(
                    "Slot '",
                    slot.Name,
                    "' has no character selected. Choose the character it should enter."),
                SlotId: slot.Id,
                AccountId: account.Id);
        }

        if (!account.Characters.Exists(character => character.Id == characterId))
        {
            return new AutoLoginReadinessIssue(
                AutoLoginReadinessIssueKind.SlotCharacterMissing,
                "Choose an available character",
                string.Concat(
                    "The character selected for slot '",
                    slot.Name,
                    "' no longer exists. Choose another character."),
                SlotId: slot.Id,
                AccountId: account.Id);
        }

        if (!slot.AutoLogin)
        {
            return new AutoLoginReadinessIssue(
                AutoLoginReadinessIssueKind.AutoLoginDisabled,
                "Enable automatic login",
                string.Concat(
                    "Automatic login is turned off for slot '",
                    slot.Name,
                    "'."),
                SlotId: slot.Id,
                AccountId: account.Id);
        }

        if (!slot.AutoEnterGame)
        {
            return new AutoLoginReadinessIssue(
                AutoLoginReadinessIssueKind.AutoEnterGameDisabled,
                "Enable automatic character entry",
                string.Concat(
                    "Automatic character entry is turned off for slot '",
                    slot.Name,
                    "'."),
                SlotId: slot.Id,
                AccountId: account.Id);
        }

        return null;
    }

    private void ShowAutoLoginReadinessIssue(
        AutoLoginReadinessIssue issue)
    {
        switch (issue.Kind)
        {
            case AutoLoginReadinessIssueKind.NoProfile:
                this.AddProfileButton_OnClick(this, EventArgs.Empty);
                break;

            case AutoLoginReadinessIssueKind.NoAccounts:
                this.ShowAccountsWithGuidance(
                    new AccountsGuidanceRequest(
                        AccountsGuidanceAction.AddAccount));
                break;

            case AutoLoginReadinessIssueKind.NoSlots:
                this.AddSlot(SlotEditorGuidanceTarget.Account);
                break;

            case AutoLoginReadinessIssueKind.SlotAccountNotSelected:
            case AutoLoginReadinessIssueKind.SlotAccountMissing:
                this.EditSlotWithGuidance(
                    issue.SlotId,
                    SlotEditorGuidanceTarget.Account);
                break;

            case AutoLoginReadinessIssueKind.AccountLoginMissing:
                this.ShowAccountsWithGuidance(
                    new AccountsGuidanceRequest(
                        AccountsGuidanceAction.EditAccountLogin,
                        issue.AccountId));
                break;

            case AutoLoginReadinessIssueKind.AccountPasswordMissing:
            case AutoLoginReadinessIssueKind.AccountPasswordUnavailable:
                this.ShowAccountsWithGuidance(
                    new AccountsGuidanceRequest(
                        AccountsGuidanceAction.EditAccountPassword,
                        issue.AccountId));
                break;

            case AutoLoginReadinessIssueKind.AccountHasNoCharacters:
                this.ShowAccountsWithGuidance(
                    new AccountsGuidanceRequest(
                        AccountsGuidanceAction.AddCharacter,
                        issue.AccountId));
                break;

            case AutoLoginReadinessIssueKind.SlotCharacterNotSelected:
            case AutoLoginReadinessIssueKind.SlotCharacterMissing:
                this.EditSlotWithGuidance(
                    issue.SlotId,
                    SlotEditorGuidanceTarget.Character);
                break;

            case AutoLoginReadinessIssueKind.AutoLoginDisabled:
                this.EditSlotWithGuidance(
                    issue.SlotId,
                    SlotEditorGuidanceTarget.AutoLogin);
                break;

            case AutoLoginReadinessIssueKind.AutoEnterGameDisabled:
                this.EditSlotWithGuidance(
                    issue.SlotId,
                    SlotEditorGuidanceTarget.AutoEnterGame);
                break;
        }
    }

    private void ShowAccountsWithGuidance(
        AccountsGuidanceRequest guidanceRequest)
    {
        using var form = new AccountsForm(
            this.clientManager.ConfiguredAccounts,
            accounts =>
            {
                this.clientManager.SaveConfiguredAccounts(accounts);
                this.RefreshAll();
            },
            guidanceRequest);
        using var windowPlacement =
            this.clientManager.BindGlobalWindowPlacement(
                form,
                WindowPlacementIds.Accounts,
                this);

        _ = form.ShowDialog(this);
        this.RefreshAll();
    }

    private void EditSlotWithGuidance(
        Guid? slotId,
        SlotEditorGuidanceTarget guidanceTarget)
    {
        var slot = this.clientManager.ActiveProfile?.Slots.FirstOrDefault(
            candidate => candidate.Id == slotId);

        if (slot == null)
        {
            return;
        }

        this.EditSlot(slot, guidanceTarget);
    }
}
