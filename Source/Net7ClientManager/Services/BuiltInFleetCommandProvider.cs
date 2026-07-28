namespace Net7ClientManager.Services;

using Net7ClientManager.Models;

public sealed class BuiltInFleetCommandProvider
{
    public const string InteractCommandId = "interact";
    public const string AssistMeCommandId = "assist-me";
    public const string ComeToMeCommandId = "come-to-me";
    public const string FormationModeCommandId = "formation:mode";
    public const string FormationToggleCommandId = "formation:toggle";
    public const string TargetNearestNavigationCommandId = "target-nearest-navigation";
    public const string PreviousTargetCommandId = "previous-target";

    public const string LootWindowSlot1CommandId = "loot-window-slot-1";
    public const string LootWindowSlot2CommandId = "loot-window-slot-2";
    public const string LootWindowSlot3CommandId = "loot-window-slot-3";
    public const string LootWindowSlot4CommandId = "loot-window-slot-4";
    public const string LootWindowSlot5CommandId = "loot-window-slot-5";
    public const string LootWindowSlot6CommandId = "loot-window-slot-6";
    public const string LootWindowSlot7CommandId = "loot-window-slot-7";
    public const string LootWindowSlot8CommandId = "loot-window-slot-8";

    private static readonly FleetCommandDefinition[] commands =
    [
        new()
        {
            Id = InteractCommandId,
            Label = "Interact",
            ShowInOverlay = true,
            Category = FleetCommandCategory.Interact,
            Blocks =
            [
                FleetCommandBlock.For(
                    FleetCommandScope.Followers,
                    FleetCommandStep.SetTitle("Interacting with {pilot}", durationMilliseconds: 5000),
                    FleetCommandStep.TargetInvokingPilotTarget(),
                    FleetCommandStep.Delay(100),
                    FleetCommandStep.Action(BuiltInInputActionProvider.InteractWithTargetName),
                    FleetCommandStep.Delay(100)),
                FleetCommandBlock.For(
                    FleetCommandScope.Pilot,
                    FleetCommandStep.Action(BuiltInInputActionProvider.InteractWithTargetName)),
                FleetCommandBlock.For(
                    FleetCommandScope.System,
                    FleetCommandStep.RestorePilotFocus()),
            ],
        },
        new()
        {
            Id = AssistMeCommandId,
            Label = "Assist Me",
            ShowInOverlay = false,
            Category = FleetCommandCategory.Combat,
            Blocks =
            [
                FleetCommandBlock.For(
                    FleetCommandScope.Followers,
                    FleetCommandStep.SetTitle("Assisting {pilot}", durationMilliseconds: 5000),
                    FleetCommandStep.TargetInvokingPilotTarget(),
                    FleetCommandStep.Delay(100),
                    FleetCommandStep.Action(BuiltInInputActionProvider.FireAllName),
                    FleetCommandStep.Delay(100)),
                FleetCommandBlock.For(
                    FleetCommandScope.System,
                    FleetCommandStep.RestorePilotFocus()),
            ],
        },
        new()
        {
            Id = TargetNearestNavigationCommandId,
            Label = "Nearest Nav",
            ShowInOverlay = false,
            Category = FleetCommandCategory.Move,
            Blocks =
            [
                FleetCommandBlock.For(
                    FleetCommandScope.Pilot,
                    FleetCommandStep.Action(
                        BuiltInInputActionProvider.TargetNearestNavigationName)),
            ],
        },
        new()
        {
            Id = PreviousTargetCommandId,
            Label = "Previous Target",
            ShowInOverlay = false,
            Category = FleetCommandCategory.Combat,
            Blocks =
            [
                FleetCommandBlock.For(
                    FleetCommandScope.Pilot,
                    FleetCommandStep.Action(
                        BuiltInInputActionProvider.PreviousTargetName)),
            ],
        },
        CreateLootWindowSlotCommand(
            LootWindowSlot1CommandId,
            "Loot Slot 1",
            BuiltInInputActionProvider.LootWindowSlot1Name),
        CreateLootWindowSlotCommand(
            LootWindowSlot5CommandId,
            "Loot Slot 5",
            BuiltInInputActionProvider.LootWindowSlot5Name),
        CreateLootWindowSlotCommand(
            LootWindowSlot2CommandId,
            "Loot Slot 2",
            BuiltInInputActionProvider.LootWindowSlot2Name),
        CreateLootWindowSlotCommand(
            LootWindowSlot6CommandId,
            "Loot Slot 6",
            BuiltInInputActionProvider.LootWindowSlot6Name),
        CreateLootWindowSlotCommand(
            LootWindowSlot3CommandId,
            "Loot Slot 3",
            BuiltInInputActionProvider.LootWindowSlot3Name),
        CreateLootWindowSlotCommand(
            LootWindowSlot7CommandId,
            "Loot Slot 7",
            BuiltInInputActionProvider.LootWindowSlot7Name),
        CreateLootWindowSlotCommand(
            LootWindowSlot4CommandId,
            "Loot Slot 4",
            BuiltInInputActionProvider.LootWindowSlot4Name),
        CreateLootWindowSlotCommand(
            LootWindowSlot8CommandId,
            "Loot Slot 8",
            BuiltInInputActionProvider.LootWindowSlot8Name),
    ];

    public IReadOnlyList<FleetCommandDefinition> GetCommands()
    {
        return commands;
    }

    public IReadOnlyList<FleetCommandDefinition> GetOverlayCommands()
    {
        return [.. commands
            .Where(command => command.ShowInOverlay)
            .OrderBy(command => command.OverlayOrder)
            .ThenBy(command => command.OverlayRow)
            .ThenBy(command => command.OverlayColumn)
            .ThenBy(command => command.Label, StringComparer.OrdinalIgnoreCase)];
    }

    public FleetCommandDefinition? FindById(string commandId)
    {
        return commands.FirstOrDefault(command =>
            string.Equals(
                command.Id,
                commandId,
                StringComparison.OrdinalIgnoreCase));
    }

    private static FleetCommandDefinition CreateLootWindowSlotCommand(
        string commandId,
        string label,
        string actionName)
    {
        return new FleetCommandDefinition
        {
            Id = commandId,
            Label = label,
            ShowInOverlay = false,
            Category = FleetCommandCategory.Combat,
            Blocks =
            [
                FleetCommandBlock.For(
                    FleetCommandScope.All,
                    FleetCommandStep.Action(actionName)),
                FleetCommandBlock.For(
                    FleetCommandScope.System,
                    FleetCommandStep.RestorePilotFocus()),
            ],
        };
    }
}
