namespace Net7ClientManager.Models;

internal sealed class InputActionExecutionResult
{
    public bool Succeeded { get; init; }

    public string Error { get; init; } = "";

    public static InputActionExecutionResult Success()
    {
        return new InputActionExecutionResult
        {
            Succeeded = true,
        };
    }

    public static InputActionExecutionResult Failure(string error)
    {
        return new InputActionExecutionResult
        {
            Error = error,
        };
    }
}
