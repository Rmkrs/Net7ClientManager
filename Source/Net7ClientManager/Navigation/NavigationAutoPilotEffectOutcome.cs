namespace Net7ClientManager.Navigation;

using Net7ClientManager.Observations.Models;

internal readonly record struct NavigationAutoPilotEffectOutcome
{
    public NavigationAutoPilotEffectOutcome()
    {
        this.StatusText = "";
    }

    public bool Succeeded { get; init; }

    public bool ActivateVerbInstead { get; init; }

    public ClientTargetVerb DetectedVerb { get; init; }

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

    public static NavigationAutoPilotEffectOutcome VerbReady(
        ClientTargetVerb detectedVerb =
            ClientTargetVerb.NotApplicable)
    {
        return new NavigationAutoPilotEffectOutcome
        {
            ActivateVerbInstead = true,
            DetectedVerb = detectedVerb,
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
