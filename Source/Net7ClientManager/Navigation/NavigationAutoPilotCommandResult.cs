namespace Net7ClientManager.Navigation;

public sealed record NavigationAutoPilotCommandResult
{
    public bool Succeeded { get; init; }

    public string Error { get; init; } = "";

    public NavigationAutoPilotSnapshot? Snapshot { get; init; }

    public static NavigationAutoPilotCommandResult Success(
        NavigationAutoPilotSnapshot snapshot)
    {
        return new NavigationAutoPilotCommandResult
        {
            Succeeded = true,
            Snapshot = snapshot,
        };
    }

    public static NavigationAutoPilotCommandResult Failure(string error)
    {
        return new NavigationAutoPilotCommandResult
        {
            Error = error,
        };
    }
}
