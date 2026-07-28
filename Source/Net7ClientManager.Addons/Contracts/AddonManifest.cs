namespace Net7ClientManager.Addons.Contracts;

public sealed record AddonManifest
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Version { get; init; }

    public required int ApiVersion { get; init; }

    public required string EntryPoint { get; init; }

    public string? Description { get; init; }

    public string? Author { get; init; }

    /// <summary>
    /// Optional lifecycle activation policy. Addons that omit this block are
    /// active only while the owner client is in_game.
    /// </summary>
    public AddonActivationOptions? Activation { get; init; }
}
