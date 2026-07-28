namespace Net7ClientManager.Addons.Contracts;

public sealed record AddonUiInteraction
{
    public required AddonUiInteractionKind Kind { get; init; }

    public required int OwnerProcessId { get; init; }

    public required string AddonId { get; init; }

    public required string WidgetId { get; init; }

    public bool? IsChecked { get; init; }

    public DateTimeOffset ObservedAt { get; init; } =
        DateTimeOffset.UtcNow;
}
