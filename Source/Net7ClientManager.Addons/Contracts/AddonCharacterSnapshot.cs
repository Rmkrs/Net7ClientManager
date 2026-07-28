namespace Net7ClientManager.Addons.Contracts;

public sealed record AddonCharacterSnapshot
{
    public bool IsAvailable { get; init; }

    public AddonCharacterIdentitySnapshot? Identity { get; init; }
}
