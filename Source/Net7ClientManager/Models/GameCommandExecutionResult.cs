namespace Net7ClientManager.Models;

internal sealed class GameCommandExecutionResult
{
    public bool Succeeded { get; init; }

    public string Error { get; init; } = "";

    public GameCommandBindingResolution? Binding { get; init; }

    public static GameCommandExecutionResult Success(
        GameCommandBindingResolution binding)
    {
        return new GameCommandExecutionResult
        {
            Succeeded = true,
            Binding = binding,
        };
    }

    public static GameCommandExecutionResult Failure(
        string error,
        GameCommandBindingResolution? binding = null)
    {
        return new GameCommandExecutionResult
        {
            Error = error,
            Binding = binding,
        };
    }
}
