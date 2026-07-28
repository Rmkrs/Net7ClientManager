namespace Net7ClientManager.Navigation;

public sealed record NavigationRoutePlanResult
{
    public bool Succeeded { get; init; }

    public string Error { get; init; } = "";

    public NavigationRoutePlan? Plan { get; init; }

    public static NavigationRoutePlanResult Success(
        NavigationRoutePlan plan)
    {
        return new NavigationRoutePlanResult
        {
            Succeeded = true,
            Plan = plan,
        };
    }

    public static NavigationRoutePlanResult Failure(string error)
    {
        return new NavigationRoutePlanResult
        {
            Error = error,
        };
    }
}
