namespace Net7ClientManager.Forms;

internal enum AutoLoginReadinessIssueKind
{
    Ready,
    NoProfile,
    NoAccounts,
    NoSlots,
    SlotAccountNotSelected,
    SlotAccountMissing,
    AccountLoginMissing,
    AccountPasswordMissing,
    AccountPasswordUnavailable,
    AccountHasNoCharacters,
    SlotCharacterNotSelected,
    SlotCharacterMissing,
    AutoLoginDisabled,
    AutoEnterGameDisabled,
}

internal sealed record AutoLoginReadinessIssue(
    AutoLoginReadinessIssueKind Kind,
    string Title,
    string Message,
    Guid? SlotId = null,
    Guid? AccountId = null);

internal enum SlotEditorGuidanceTarget
{
    None,
    Account,
    Character,
    AutoLogin,
    AutoEnterGame,
}

internal enum AccountEditorGuidanceTarget
{
    None,
    LoginName,
    Password,
}

internal enum AccountsGuidanceAction
{
    None,
    AddAccount,
    EditAccountLogin,
    EditAccountPassword,
    AddCharacter,
}

internal sealed record AccountsGuidanceRequest(
    AccountsGuidanceAction Action,
    Guid? AccountId = null);
