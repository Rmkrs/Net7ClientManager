namespace Net7ClientManager.Addons.Contracts;

public sealed record AddonOwnerRegistration
{
    public required int ProcessId { get; init; }

    public required string OwnerKey { get; init; }

    public required string DisplayName { get; init; }

    public IReadOnlySet<string> EnabledAddonIds { get; init; } =
        new HashSet<string>(StringComparer.Ordinal);
}

