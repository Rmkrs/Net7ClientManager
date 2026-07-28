namespace Net7ClientManager.Addons.Contracts;

public sealed record AddonUiMenuToggle
{
    public required string AddonName { get; init; }

    public required string Text { get; init; }

    public string Tooltip { get; init; } = "";

    public int Order { get; init; }

    public bool IsChecked { get; init; }
}
