namespace Net7ClientManager.Observations;

public sealed record ClientChatChannelOptionsState(
    bool IsAvailable,
    IReadOnlyDictionary<string, bool> Options,
    string DiagnosticStatus)
{
    public static ClientChatChannelOptionsState Unavailable(string status) =>
        new(
            false,
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            status);

    public bool TryGetEnabled(
        string optionName,
        out bool enabled)
    {
        return this.Options.TryGetValue(optionName, out enabled);
    }
}
