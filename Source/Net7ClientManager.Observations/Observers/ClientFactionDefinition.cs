namespace Net7ClientManager.Observations.Observers;

internal sealed record ClientFactionDefinition
{
    public string Key { get; init; } = "";

    public string DisplayName { get; init; } = "";

    public string Description { get; init; } = "";
}
