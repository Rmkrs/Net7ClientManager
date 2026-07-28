namespace Net7ClientManager.Navigation;

internal readonly record struct NavigationAutoPilotTargetSelectionOutcome
{
    public NavigationAutoPilotTargetSelectionOutcome()
    {
        this.TargetName = "";
        this.Error = "";
    }

    public bool Succeeded { get; init; }

    public bool IsTransientFailure { get; init; }

    public uint ObjectId { get; init; }

    public string TargetName { get; init; } = "";

    public string Error { get; init; } = "";

    public static NavigationAutoPilotTargetSelectionOutcome Success(
        uint objectId,
        string targetName)
    {
        return new NavigationAutoPilotTargetSelectionOutcome
        {
            Succeeded = true,
            ObjectId = objectId,
            TargetName = targetName,
            Error = "",
        };
    }

    public static NavigationAutoPilotTargetSelectionOutcome Failure(
        string error,
        bool isTransientFailure)
    {
        return new NavigationAutoPilotTargetSelectionOutcome
        {
            IsTransientFailure = isTransientFailure,
            TargetName = "",
            Error = error,
        };
    }
}
