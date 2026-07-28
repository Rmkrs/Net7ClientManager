namespace Net7ClientManager.Navigation;

using System.Text.Json;

internal sealed record ForgeNavigationEntityRecordDocument
{
    public required string Kind { get; init; }

    public required string Id { get; init; }

    public int Version { get; init; } = ForgeNavigationEntityKinds.CurrentEntityVersion;

    public required JsonElement Document { get; init; }
}
