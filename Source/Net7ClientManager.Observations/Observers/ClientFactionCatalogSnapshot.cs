namespace Net7ClientManager.Observations.Observers;

internal sealed record ClientFactionCatalogSnapshot(
    string? SourcePath,
    IReadOnlyDictionary<string, ClientFactionDefinition>
        Definitions,
    string Status)
{
    public bool IsAvailable =>
        this.Definitions.Count > 0;

    public static ClientFactionCatalogSnapshot NotLoaded()
    {
        return Unavailable(
            "Faction catalog has not been loaded yet");
    }

    public static ClientFactionCatalogSnapshot Unavailable(
        string status,
        string? sourcePath = null)
    {
        return new ClientFactionCatalogSnapshot(
            sourcePath,
            new Dictionary<string, ClientFactionDefinition>(
                StringComparer.Ordinal),
            status);
    }
}
