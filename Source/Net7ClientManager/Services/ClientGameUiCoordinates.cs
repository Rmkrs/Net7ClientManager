namespace Net7ClientManager.Services;

internal static class ClientGameUiCoordinates
{
    // Recovered from the fixed 1280x720 game-viewport coordinate system.
    // The game scales these positions with the hosted client area.
    public static readonly ClientGameUiPoint FormationMenu =
        new(
            "formation menu",
            1280,
            720,
            1181,
            248);

    public static readonly ClientGameUiPoint FormationPipe =
        new(
            "Pipe formation",
            1280,
            720,
            1129,
            205);

    public static readonly ClientGameUiPoint FormationBlock =
        new(
            "Block formation",
            1280,
            720,
            1177,
            207);

    public static readonly ClientGameUiPoint FormationSlotBack =
        new(
            "Slot back formation",
            1280,
            720,
            1234,
            208);

    public static readonly ClientGameUiPoint FormUp =
        new(
            "Form up",
            1280,
            720,
            1126,
            168);

    public static readonly ClientGameUiPoint LeaveFormation =
        new(
            "Leave formation",
            1280,
            720,
            1180,
            169);

    public static readonly ClientGameUiPoint BreakFormation =
        new(
            "Break formation",
            1280,
            720,
            1231,
            174);

    public static readonly ClientGameUiPoint DisbandOrLeaveGroup =
        new(
            "Disband or leave group",
            1280,
            720,
            1130,
            249);

    public static readonly ClientGameUiPoint SelectGroupMember1 =
        new(
            "select group member 1",
            1280,
            720,
            1080,
            434);

    public static readonly ClientGameUiPoint SelectGroupMember2 =
        new(
            "select group member 2",
            1280,
            720,
            1080,
            396);

    public static readonly ClientGameUiPoint SelectGroupMember3 =
        new(
            "select group member 3",
            1280,
            720,
            1080,
            358);

    public static readonly ClientGameUiPoint SelectGroupMember4 =
        new(
            "select group member 4",
            1280,
            720,
            1080,
            317);

    public static readonly ClientGameUiPoint SelectGroupMember5 =
        new(
            "select group member 5",
            1280,
            720,
            1080,
            280);

    public static readonly ClientGameUiPoint TargetGroupMemberTarget =
        new(
            "target of group member 1",
            1280,
            720,
            1224,
            434);

    public static readonly ClientGameUiPoint TargetOfGroupMember1 =
        new(
            "target of group member 1",
            1280,
            720,
            1224,
            434);

    public static readonly ClientGameUiPoint TargetOfGroupMember2 =
        new(
            "target of group member 2",
            1280,
            720,
            1224,
            396);

    public static readonly ClientGameUiPoint TargetOfGroupMember3 =
        new(
            "target of group member 3",
            1280,
            720,
            1224,
            358);

    public static readonly ClientGameUiPoint TargetOfGroupMember4 =
        new(
            "target of group member 4",
            1280,
            720,
            1224,
            317);

    public static readonly ClientGameUiPoint TargetOfGroupMember5 =
        new(
            "target of group member 5",
            1280,
            720,
            1224,
            280);

    public static readonly ClientGameUiPoint PrimaryTargetVerb =
        new(
            "primary target verb",
            1280,
            720,
            1234.6666666666667,
            480.6666666666667);

    public static readonly ClientGameUiPoint LootWindowClose =
        new(
            "Loot Window Close",
            1280,
            720,
            1238,
            196);

    public static readonly ClientGameUiPoint ShortcutBarSlot1 =
        new(
            "shortcut bar slot 1",
            1280,
            720,
            338,
            678);

    public static readonly ClientGameUiPoint ShortcutBarSlot2 =
        new(
            "shortcut bar slot 2",
            1280,
            720,
            415,
            672);

    public static readonly ClientGameUiPoint ShortcutBarSlot3 =
        new(
            "shortcut bar slot 3",
            1280,
            720,
            495,
            675);

    public static readonly ClientGameUiPoint ShortcutBarSlot4 =
        new(
            "shortcut bar slot 4",
            1280,
            720,
            788,
            675);

    public static readonly ClientGameUiPoint ShortcutBarSlot5 =
        new(
            "shortcut bar slot 5",
            1280,
            720,
            866,
            674);

    public static readonly ClientGameUiPoint ShortcutBarSlot6 =
        new(
            "shortcut bar slot 6",
            1280,
            720,
            943,
            670);

    public static readonly ClientGameUiPoint LeftShortcutBankToggle =
        new(
            "left shortcut bank toggle",
            1280,
            720,
            555,
            687);

    public static readonly ClientGameUiPoint RightShortcutBankToggle =
        new(
            "right shortcut bank toggle",
            1280,
            720,
            719,
            689);

    public static bool TryGetFormationMenu(
        Size clientSize,
        out Point clientPoint)
    {
        return FormationMenu.TryScaleToClient(
            clientSize,
            out clientPoint);
    }

    public static bool TryGetFormationBlock(
        Size clientSize,
        out Point clientPoint)
    {
        return FormationBlock.TryScaleToClient(
            clientSize,
            out clientPoint);
    }

    public static bool TryGetTargetGroupMemberTarget(
        Size clientSize,
        out Point clientPoint)
    {
        return TargetGroupMemberTarget.TryScaleToClient(
            clientSize,
            out clientPoint);
    }

    public static bool TryGetPrimaryTargetVerb(
        Size clientSize,
        out Point clientPoint)
    {
        return PrimaryTargetVerb.TryScaleToClient(
            clientSize,
            out clientPoint);
    }
}
