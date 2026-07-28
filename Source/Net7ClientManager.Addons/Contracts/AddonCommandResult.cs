namespace Net7ClientManager.Addons.Contracts;

public sealed record AddonCommandResult
{
    public required bool Succeeded { get; init; }

    public string Error { get; init; } = "";

    public static AddonCommandResult Success()
    {
        return new AddonCommandResult
        {
            Succeeded = true,
        };
    }

    public static AddonCommandResult Failure(string error)
    {
        return new AddonCommandResult
        {
            Succeeded = false,
            Error = error,
        };
    }
}

