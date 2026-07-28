namespace Net7ClientManager.Navigation;

internal sealed record NavigationTargetResolutionResult
{
    public NavigationTargetResolutionStatus Status { get; init; }

    public string Detail { get; init; } = "";

    public string TargetName { get; init; } = "";

    public string TargetKind { get; init; } = "";

    public string Source { get; init; } = "";

    public uint ObjectId { get; init; }

    public uint ActiveSectorNumber { get; init; }

    public GalaxyNavigationTargetSelectionContext SelectionContext
    { get; init; } = GalaxyNavigationTargetSelectionContext.Navigation;

    public bool IsReady =>
        this.Status == NavigationTargetResolutionStatus.Ready &&
        this.ObjectId != 0;

    public static NavigationTargetResolutionResult Waiting(
        string detail,
        string? targetName = null,
        string? source = null)
    {
        return new NavigationTargetResolutionResult
        {
            Status = NavigationTargetResolutionStatus.Waiting,
            Detail = detail,
            TargetName = targetName ?? "",
            Source = source ?? "",
        };
    }

    public static NavigationTargetResolutionResult Ready(
        string targetName,
        string targetKind,
        uint objectId,
        uint activeSectorNumber,
        string? source = null,
        GalaxyNavigationTargetSelectionContext selectionContext =
            GalaxyNavigationTargetSelectionContext.Navigation)
    {
        return new NavigationTargetResolutionResult
        {
            Status = NavigationTargetResolutionStatus.Ready,
            TargetName = targetName,
            TargetKind = targetKind,
            Source = source ?? "",
            ObjectId = objectId,
            ActiveSectorNumber = activeSectorNumber,
            SelectionContext = selectionContext,
        };
    }

    public static NavigationTargetResolutionResult Invalid(
        string detail,
        string? targetName = null)
    {
        return new NavigationTargetResolutionResult
        {
            Status = NavigationTargetResolutionStatus.Invalid,
            Detail = detail,
            TargetName = targetName ?? "",
        };
    }
}
