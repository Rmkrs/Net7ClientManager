namespace Net7ClientManager.Models;

/// <summary>
/// Stable host-owned names for configurable in-game actions. Callers request
/// meaning, never a default input binding.
/// </summary>
public enum GameCommand
{
    FireAll = 0,
    TargetNearestNavigation = 1,
    NextContextTarget = 2,
    PreviousContextTarget = 3,
    NextTarget = 4,
    PreviousTarget = 5,
    BeginChannelMessage = 6,
    Warp = 7,
    Formation = 8,
    FireActivateSlot1 = 9,
    FireActivateSlot2 = 10,
    FireActivateSlot3 = 11,
    FireActivateSlot4 = 12,
    FireActivateSlot5 = 13,
    FireActivateSlot6 = 14,
    SwapShortcutBanks = 15,
    TargetSelf = 16,
    TargetGroupMember1 = 17,
    TargetGroupMember2 = 18,
    TargetGroupMember3 = 19,
    TargetGroupMember4 = 20,
    TargetGroupMember5 = 21,
    TargetNearestObject = 22,
}
