namespace Net7ClientManager.Addons.Contracts;

public enum AddonActionKind
{
    SelectNearbyTarget = 0,
    OpenNavigationPlanner = 1,
    SelectNextNavigationTarget = 2,
    ClearNavigationRoute = 3,
    FireAllWeapons = 4,
    TargetNearestNavigation = 5,
    PreviousTarget = 6,
    StartNavigationAutoPilot = 7,
    StopNavigationAutoPilot = 8,
    PlanNavigationReturnTrip = 9,
}
