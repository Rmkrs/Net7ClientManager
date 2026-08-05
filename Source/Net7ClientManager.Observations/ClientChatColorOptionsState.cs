namespace Net7ClientManager.Observations;

public readonly record struct ClientObservedColor(
    float Red,
    float Green,
    float Blue);

public sealed record ClientChatColorOptionsState(
    bool IsAvailable,
    IReadOnlyDictionary<string, ClientObservedColor> Colors,
    string DiagnosticStatus)
{
    public static ClientChatColorOptionsState Unavailable(string status) =>
        new(
            false,
            new Dictionary<string, ClientObservedColor>(
                StringComparer.OrdinalIgnoreCase),
            status);

    public bool TryGetColor(
        string optionName,
        out ClientObservedColor color)
    {
        return this.Colors.TryGetValue(optionName, out color);
    }
}
