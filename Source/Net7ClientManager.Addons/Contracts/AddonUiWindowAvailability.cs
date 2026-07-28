namespace Net7ClientManager.Addons.Contracts;

public sealed record AddonUiWindowAvailability
{
    public bool IsAvailable { get; init; } = true;

    public string Reason { get; init; } = "";
}
