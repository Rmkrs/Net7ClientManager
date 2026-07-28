namespace Net7ClientManager.Navigation;

public sealed record NavigationRouteCommandResult
{
    public bool Succeeded { get; init; }

    public string Error { get; init; } = "";

    public NavigationRouteSnapshot? Snapshot { get; init; }

    public static NavigationRouteCommandResult Success(
        NavigationRouteSnapshot snapshot)
    {
        return new NavigationRouteCommandResult
        {
            Succeeded = true,
            Snapshot = snapshot,
        };
    }

    public static NavigationRouteCommandResult Failure(string error)
    {
        return new NavigationRouteCommandResult
        {
            Error = error,
        };
    }
}
