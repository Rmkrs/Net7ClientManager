namespace Net7ClientManager.Addons.Contracts;

public sealed record AddonUiCommand
{
    public required AddonUiCommandKind Kind { get; init; }

    public required int OwnerProcessId { get; init; }

    public required string AddonId { get; init; }

    public string WidgetId { get; init; } = "";

    public AddonUiWindow? Window { get; init; }

    public AddonUiLabel? Label { get; init; }

    public AddonUiButton? Button { get; init; }

    public AddonUiWindowAvailability? WindowAvailability { get; init; }

    public AddonUiWindowMenuItem? WindowMenuItem { get; init; }

    public AddonUiMenuToggle? MenuToggle { get; init; }
}
