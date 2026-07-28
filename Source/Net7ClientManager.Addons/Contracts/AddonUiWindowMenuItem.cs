namespace Net7ClientManager.Addons.Contracts;

public sealed record AddonUiWindowMenuItem
{
    public required string AddonName { get; init; }

    public required string Text { get; init; }

    public string Tooltip { get; init; } = "";

    public int Order { get; init; }
}
