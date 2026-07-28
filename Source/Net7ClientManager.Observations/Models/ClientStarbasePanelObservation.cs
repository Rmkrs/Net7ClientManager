namespace Net7ClientManager.Observations.Models;

using Net7ClientManager.Observations.Observers;

public sealed record ClientStarbasePanelObservation
{
    public int Slot { get; init; }

    public ClientStarbasePanelKind Kind { get; init; }

    public string Name { get; init; } = "";

    public uint ViewFieldOffset { get; init; }

    public int? CurrentInterfaceCommand { get; init; }

    public bool IsPersistent { get; init; }

    public uint Address { get; init; }

    public byte ActiveFlag { get; init; }

    public uint ChildAddress { get; init; }

    public ClientStarbaseInteractionKind ChildKind { get; init; }

    public string Status { get; init; } = "";

    public bool IsAttached =>
        this.Address != 0;

    public bool IsActive =>
        this.ActiveFlag != 0 ||
        this.ChildAddress != 0;

    public ClientStarbaseInteractionKind ActiveInteractionKind =>
        this.IsActive
            ? ClientStarbaseInteractionCatalog
                .GetPanelInteractionKind(
                    this.Kind,
                    this.ChildAddress)
            : ClientStarbaseInteractionKind.None;
}
