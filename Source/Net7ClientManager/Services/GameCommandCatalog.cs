namespace Net7ClientManager.Services;

using Net7ClientManager.Models;

/// <summary>
/// Canonical bridge between host semantics and the stable section/key names
/// written by the Earth &amp; Beyond client.
/// </summary>
internal static class GameCommandCatalog
{
    private static readonly IReadOnlyDictionary<GameCommand, IReadOnlyList<string>> definitionNames =
        new Dictionary<GameCommand, IReadOnlyList<string>>
        {
            [GameCommand.FireAll] = ["Fire All Weapons"],
            [GameCommand.BeginChannelMessage] = ["Channel Message"],
            [GameCommand.TargetNearestNavigation] = ["Target Near Navigation"],
            [GameCommand.TargetNearestObject] =
            [
                "Target Nearest Object",
                "Target Near Object",
            ],
            [GameCommand.NextContextTarget] = ["Next Context Target"],
            [GameCommand.PreviousContextTarget] = ["Prev Context Target"],
            [GameCommand.NextTarget] = ["Next Target"],
            [GameCommand.PreviousTarget] = ["Prev Target"],
            [GameCommand.Warp] = ["Warp"],
            [GameCommand.Formation] = ["Formation"],
            [GameCommand.FireActivateSlot1] = ["Use Slot 1", "Fire/Activate slot 1"],
            [GameCommand.FireActivateSlot2] = ["Use Slot 2", "Fire/Activate slot 2"],
            [GameCommand.FireActivateSlot3] = ["Use Slot 3", "Fire/Activate slot 3"],
            [GameCommand.FireActivateSlot4] = ["Use Slot 4", "Fire/Activate slot 4"],
            [GameCommand.FireActivateSlot5] = ["Use Slot 5", "Fire/Activate slot 5"],
            [GameCommand.FireActivateSlot6] = ["Use Slot 6", "Fire/Activate slot 6"],
            [GameCommand.SwapShortcutBanks] = ["Shift Shortcuts", "Swap to other shortcut banks"],
            [GameCommand.TargetSelf] =
            [
                "Target Self",
                "Target Myself",
                "Self Target",
            ],
            [GameCommand.TargetGroupMember1] =
            [
                "Target Group 1",
                "Target Group Member 1",
                "Group Member 1",
                "Select Group Member 1",
            ],
            [GameCommand.TargetGroupMember2] =
            [
                "Target Group 2",
                "Target Group Member 2",
                "Group Member 2",
                "Select Group Member 2",
            ],
            [GameCommand.TargetGroupMember3] =
            [
                "Target Group 3",
                "Target Group Member 3",
                "Group Member 3",
                "Select Group Member 3",
            ],
            [GameCommand.TargetGroupMember4] =
            [
                "Target Group 4",
                "Target Group Member 4",
                "Group Member 4",
                "Select Group Member 4",
            ],
            [GameCommand.TargetGroupMember5] =
            [
                "Target Group 5",
                "Target Group Member 5",
                "Group Member 5",
                "Select Group Member 5",
            ],
        };

    public static string GetDefinitionName(GameCommand command)
    {
        return GetDefinitionNames(command)[0];
    }

    public static IReadOnlyList<string> GetDefinitionNames(GameCommand command)
    {
        return definitionNames.TryGetValue(command, out var names)
            ? names
            : throw new ArgumentOutOfRangeException(
                nameof(command),
                command,
                "Unknown semantic game command.");
    }
}
