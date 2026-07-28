namespace Net7ClientManager.Addons.Runtime;

using Net7ClientManager.Addons.Contracts;
using Net7ClientManager.Addons.Loading;

internal sealed class AddonRuntimeInstance
{
    public required int OwnerProcessId { get; init; }

    public required string OwnerKey { get; init; }

    public required AddonPackage Package { get; init; }

    public ILuaRuntime? Runtime { get; set; }

    public AddonRuntimeState State { get; set; } =
        AddonRuntimeState.Loading;

    public string Error { get; set; } = "";

    public DateTimeOffset? LoadedAt { get; set; }

    public DateTimeOffset? LastActivityAt { get; set; }
}

