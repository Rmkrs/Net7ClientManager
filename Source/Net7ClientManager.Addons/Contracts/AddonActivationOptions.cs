namespace Net7ClientManager.Addons.Contracts;

public sealed record AddonActivationOptions
{
    public IReadOnlyList<string> Contexts { get; init; } = [];
}
