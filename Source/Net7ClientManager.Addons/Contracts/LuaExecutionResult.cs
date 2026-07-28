namespace Net7ClientManager.Addons.Contracts;

public sealed record LuaExecutionResult
{
    public bool Succeeded { get; init; }

    public bool WasCancelled { get; init; }

    public IReadOnlyList<string> Values { get; init; } = [];

    public string Error { get; init; } = "";
}

