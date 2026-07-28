namespace Net7ClientManager.Services;

using Net7ClientManager.Models;

public sealed class BuiltInInputActionProvider
{
    public const string TargetGroupMemberTargetName = "Target Group Member Target";
    public const string FireAllName = "Fire All";
    public const string WarpName = "Warp";
    public const string BeginChannelMessageName = "Begin Channel Message";
    public const string TargetNearestNavigationName = "Target Nearest Navigation";
    public const string PreviousTargetName = "Previous Target";
    public const string InteractWithTargetName = "Interact With Target";

    public const string LootWindowSlot1Name = "Loot Window Slot 1";
    public const string LootWindowSlot2Name = "Loot Window Slot 2";
    public const string LootWindowSlot3Name = "Loot Window Slot 3";
    public const string LootWindowSlot4Name = "Loot Window Slot 4";
    public const string LootWindowSlot5Name = "Loot Window Slot 5";
    public const string LootWindowSlot6Name = "Loot Window Slot 6";
    public const string LootWindowSlot7Name = "Loot Window Slot 7";
    public const string LootWindowSlot8Name = "Loot Window Slot 8";
    public const string CloseLootWindowName = "Close Loot Window";

    public const string ShortcutBarSlot1Name = "Shortcut Bar Slot 1";
    public const string ShortcutBarSlot2Name = "Shortcut Bar Slot 2";
    public const string ShortcutBarSlot3Name = "Shortcut Bar Slot 3";
    public const string ShortcutBarSlot4Name = "Shortcut Bar Slot 4";
    public const string ShortcutBarSlot5Name = "Shortcut Bar Slot 5";
    public const string ShortcutBarSlot6Name = "Shortcut Bar Slot 6";
    public const string LeftShortcutBankToggleName = "Left Shortcut Bank Toggle";
    public const string RightShortcutBankToggleName = "Right Shortcut Bank Toggle";

    public const string SelectGroupMember1Name = "Select Group Member 1";
    public const string SelectGroupMember2Name = "Select Group Member 2";
    public const string SelectGroupMember3Name = "Select Group Member 3";
    public const string SelectGroupMember4Name = "Select Group Member 4";
    public const string SelectGroupMember5Name = "Select Group Member 5";

    public const string TargetOfGroupMember1Name = "Target Of Group Member 1";
    public const string TargetOfGroupMember2Name = "Target Of Group Member 2";
    public const string TargetOfGroupMember3Name = "Target Of Group Member 3";
    public const string TargetOfGroupMember4Name = "Target Of Group Member 4";
    public const string TargetOfGroupMember5Name = "Target Of Group Member 5";

    public const string BuffSlot1Name = "Buff Slot 1";
    public const string BuffSlot2Name = "Buff Slot 2";
    public const string BuffSlot3Name = "Buff Slot 3";
    public const string BuffSlot4Name = "Buff Slot 4";
    public const string BuffSlot5Name = "Buff Slot 5";
    public const string BuffSlot6Name = "Buff Slot 6";
    public const string BuffSlot7Name = "Buff Slot 7";
    public const string BuffSlot8Name = "Buff Slot 8";
    public const string BuffSlot9Name = "Buff Slot 9";
    public const string BuffSlot10Name = "Buff Slot 10";
    public const string BuffSlot11Name = "Buff Slot 11";
    public const string BuffSlot12Name = "Buff Slot 12";
    public const string BuffSlot13Name = "Buff Slot 13";
    public const string BuffSlot14Name = "Buff Slot 14";
    public const string BuffSlot15Name = "Buff Slot 15";
    public const string BuffSlot16Name = "Buff Slot 16";

    public const string AcceptInviteName = "Accept Invite";
    public const string ConfirmDialogName = "Confirm Dialog";
    public const string DisbandOrLeaveGroupName = "Disband Or Leave Group";
    public const string LeaveGroupName = DisbandOrLeaveGroupName;

    public const string ToggleFormationDialogName = "Toggle Formation Dialog";
    public const string BeginPipeFormationName = "Begin Pipe Formation";
    public const string BeginBlockFormationName = "Begin Block Formation";
    public const string BeginSlotBackFormationName = "Begin Slot Back Formation";
    public const string FormUpName = "Form Up";
    public const string LeaveFormationName = "Leave Formation";
    public const string BreakFormationName = "Break Formation";

    private static readonly InputActionDefinition[] actions = BuildActions();

    public IReadOnlyList<InputActionDefinition> GetActions()
    {
        return actions;
    }

    public InputActionDefinition? FindByName(string actionName)
    {
        return actions.FirstOrDefault(action =>
            string.Equals(
                action.Name,
                actionName,
                StringComparison.OrdinalIgnoreCase));
    }

    private static InputActionDefinition[] BuildActions()
    {
        List<InputActionDefinition> builtInActions =
        [
            MouseClickAction(
                TargetGroupMemberTargetName,
                ClientGameUiCoordinates.TargetGroupMemberTarget),
            GameCommandAction(
                FireAllName,
                GameCommand.FireAll),
            GameCommandAction(
                WarpName,
                GameCommand.Warp),
            GameCommandAction(
                BeginChannelMessageName,
                GameCommand.BeginChannelMessage),
            GameCommandAction(
                TargetNearestNavigationName,
                GameCommand.TargetNearestNavigation),
            GameCommandAction(
                PreviousTargetName,
                GameCommand.PreviousTarget),
            MouseClickAction(
                InteractWithTargetName,
                ClientGameUiCoordinates.PrimaryTargetVerb),
            MouseClickAction(
                AcceptInviteName,
                492,
                381),
            MouseClickAction(
                ConfirmDialogName,
                492,
                381),
            MouseClickAction(
                DisbandOrLeaveGroupName,
                ClientGameUiCoordinates.DisbandOrLeaveGroup),

            MouseClickAction(
                LootWindowSlot1Name,
                1142.6666666666667,
                239.33333333333334),
            MouseClickAction(
                LootWindowSlot2Name,
                1140.6666666666667,
                297.3333333333333),
            MouseClickAction(
                LootWindowSlot3Name,
                1137.3333333333333,
                356),
            MouseClickAction(
                LootWindowSlot4Name,
                1132,
                414),
            MouseClickAction(
                LootWindowSlot5Name,
                1213.3333333333333,
                238.66666666666666),
            MouseClickAction(
                LootWindowSlot6Name,
                1216,
                298),
            MouseClickAction(
                LootWindowSlot7Name,
                1210,
                355.3333333333333),
            MouseClickAction(
                LootWindowSlot8Name,
                1214.6666666666667,
                415.3333333333333),
            MouseClickAction(
                CloseLootWindowName,
                ClientGameUiCoordinates.LootWindowClose),

            MouseClickAction(
                ShortcutBarSlot1Name,
                ClientGameUiCoordinates.ShortcutBarSlot1),
            MouseClickAction(
                ShortcutBarSlot2Name,
                ClientGameUiCoordinates.ShortcutBarSlot2),
            MouseClickAction(
                ShortcutBarSlot3Name,
                ClientGameUiCoordinates.ShortcutBarSlot3),
            MouseClickAction(
                ShortcutBarSlot4Name,
                ClientGameUiCoordinates.ShortcutBarSlot4),
            MouseClickAction(
                ShortcutBarSlot5Name,
                ClientGameUiCoordinates.ShortcutBarSlot5),
            MouseClickAction(
                ShortcutBarSlot6Name,
                ClientGameUiCoordinates.ShortcutBarSlot6),
            MouseClickAction(
                LeftShortcutBankToggleName,
                ClientGameUiCoordinates.LeftShortcutBankToggle),
            MouseClickAction(
                RightShortcutBankToggleName,
                ClientGameUiCoordinates.RightShortcutBankToggle),

            MouseClickAction(
                SelectGroupMember1Name,
                ClientGameUiCoordinates.SelectGroupMember1),
            MouseClickAction(
                SelectGroupMember2Name,
                ClientGameUiCoordinates.SelectGroupMember2),
            MouseClickAction(
                SelectGroupMember3Name,
                ClientGameUiCoordinates.SelectGroupMember3),
            MouseClickAction(
                SelectGroupMember4Name,
                ClientGameUiCoordinates.SelectGroupMember4),
            MouseClickAction(
                SelectGroupMember5Name,
                ClientGameUiCoordinates.SelectGroupMember5),
            MouseClickAction(
                TargetOfGroupMember1Name,
                ClientGameUiCoordinates.TargetOfGroupMember1),
            MouseClickAction(
                TargetOfGroupMember2Name,
                ClientGameUiCoordinates.TargetOfGroupMember2),
            MouseClickAction(
                TargetOfGroupMember3Name,
                ClientGameUiCoordinates.TargetOfGroupMember3),
            MouseClickAction(
                TargetOfGroupMember4Name,
                ClientGameUiCoordinates.TargetOfGroupMember4),
            MouseClickAction(
                TargetOfGroupMember5Name,
                ClientGameUiCoordinates.TargetOfGroupMember5),

            MouseClickAction(
                ToggleFormationDialogName,
                ClientGameUiCoordinates.FormationMenu),
            MouseClickAction(
                BeginPipeFormationName,
                ClientGameUiCoordinates.FormationPipe),
            MouseClickAction(
                BeginBlockFormationName,
                ClientGameUiCoordinates.FormationBlock),
            MouseClickAction(
                BeginSlotBackFormationName,
                ClientGameUiCoordinates.FormationSlotBack),
            MouseClickAction(
                FormUpName,
                ClientGameUiCoordinates.FormUp),
            MouseClickAction(
                LeaveFormationName,
                ClientGameUiCoordinates.LeaveFormation),
            MouseClickAction(
                BreakFormationName,
                ClientGameUiCoordinates.BreakFormation),
        ];

        builtInActions.AddRange(CreateBuffActions());

        return [.. builtInActions];
    }

    private static IEnumerable<InputActionDefinition> CreateBuffActions()
    {
        var names = new[]
        {
            BuffSlot1Name,
            BuffSlot2Name,
            BuffSlot3Name,
            BuffSlot4Name,
            BuffSlot5Name,
            BuffSlot6Name,
            BuffSlot7Name,
            BuffSlot8Name,
            BuffSlot9Name,
            BuffSlot10Name,
            BuffSlot11Name,
            BuffSlot12Name,
            BuffSlot13Name,
            BuffSlot14Name,
            BuffSlot15Name,
            BuffSlot16Name,
        };

        for (var index = 0; index < names.Length; index++)
        {
            var row = index / 4;
            var column = index % 4;

            yield return MouseClickAction(
                names[index],
                1227 - column * 49,
                36 + row * 36);
        }
    }

    private static InputActionDefinition GameCommandAction(
        string name,
        GameCommand gameCommand)
    {
        return new InputActionDefinition
        {
            Name = name,
            Kind = InputActionKind.GameCommand,
            GameCommand = gameCommand,
            Key = Keys.None,
            BaseWidth = 1280,
            BaseHeight = 720,
        };
    }

    private static InputActionDefinition KeyTapAction(
        string name,
        Keys key)
    {
        return new InputActionDefinition
        {
            Name = name,
            Kind = InputActionKind.KeyTap,
            Key = key,
            BaseWidth = 1280,
            BaseHeight = 720,
        };
    }

    private static InputActionDefinition MouseClickAction(
        string name,
        ClientGameUiPoint point)
    {
        return MouseClickAction(
            name,
            point.BaseX,
            point.BaseY,
            point.BaseWidth,
            point.BaseHeight);
    }

    private static InputActionDefinition MouseClickAction(
        string name,
        double baseX,
        double baseY,
        int baseWidth = 1280,
        int baseHeight = 720)
    {
        return new InputActionDefinition
        {
            Name = name,
            Kind = InputActionKind.MouseClick,
            Key = Keys.None,
            BaseWidth = baseWidth,
            BaseHeight = baseHeight,
            BaseX = baseX,
            BaseY = baseY,
        };
    }
}
