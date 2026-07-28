namespace Net7ClientManager.Observations.Models;

public sealed record ClientShortcutBarObservation
{
    public int Bar { get; init; }

    public bool IsAvailable { get; init; }

    public string Status { get; init; } = "";

    public uint BarAddress { get; init; }

    public int? CurrentGroup { get; init; }

    public int? Group0Count { get; init; }

    public int? Group1Count { get; init; }

    public uint ToggleWidgetAddress { get; init; }

    public IReadOnlyList<ClientShortcutSlotObservation> Slots { get; init; } = [];

    public ClientShortcutSlotObservation? GetSlot(int group, int button)
    {
        return this.Slots.FirstOrDefault(candidate =>
            candidate.Group == group &&
            candidate.Button == button);
    }

    public static ClientShortcutBarObservation Unavailable(
        int bar,
        string status)
    {
        return new ClientShortcutBarObservation
        {
            Bar = bar,
            Status = status,
        };
    }
}
