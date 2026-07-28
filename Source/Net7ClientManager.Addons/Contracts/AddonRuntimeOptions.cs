namespace Net7ClientManager.Addons.Contracts;

using Net7ClientManager.Addons.Runtime;

public sealed record AddonRuntimeOptions
{
    public required int OwnerProcessId { get; init; }

    public required AddonManifest Manifest { get; init; }

    public IReadOnlyDictionary<string, string> Modules { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public AddonActionDispatcher? ActionDispatcher { get; init; }

    internal string OwnerKey { get; init; } = "";

    internal AddonStorageStore? Storage { get; init; }
}
