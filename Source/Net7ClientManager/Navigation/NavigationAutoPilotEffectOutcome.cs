namespace Net7ClientManager.Navigation;

internal readonly record struct NavigationAutoPilotEffectOutcome
{
    public NavigationAutoPilotEffectOutcome()
    {
        this.StatusText = "";
    }

    public bool Succeeded { get; init; }

    public bool ActivateVerbInstead { get; init; }

    public NavigationAutoPilotStopReason StopReason { get; init; }

    public string StatusText { get; init; } = "";

    public static NavigationAutoPilotEffectOutcome Success()
    {
        return new NavigationAutoPilotEffectOutcome
        {
            Succeeded = true,
            StatusText = "",
        };
    }

    public static NavigationAutoPilotEffectOutcome VerbReady()
    {
        return new NavigationAutoPilotEffectOutcome
        {
            ActivateVerbInstead = true,
            StatusText = "",
        };
    }

    public static NavigationAutoPilotEffectOutcome Failure(
        NavigationAutoPilotStopReason stopReason,
        string statusText)
    {
        return new NavigationAutoPilotEffectOutcome
        {
            StopReason = stopReason,
            StatusText = statusText,
        };
    }
}
