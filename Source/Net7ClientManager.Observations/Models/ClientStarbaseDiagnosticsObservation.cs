namespace Net7ClientManager.Observations.Models;

public sealed record ClientStarbaseDiagnosticsObservation
{
    public int CurrentInterfaceCommand { get; init; } = -1;

    public uint CurrentInterfaceAddress { get; init; }

    public ClientStarbaseInteractionKind CurrentInterfaceKind { get; init; }

    public bool CurrentInterfaceIsActive { get; init; }

    public IReadOnlyList<ClientStarbaseInteractionKind> ActiveInteractionKinds
    { get; init; } = [];

    public uint TalkTreePanelAddress { get; init; }

    public byte TalkTreePanelActiveFlag { get; init; }

    public uint VendorTradeControllerAddress { get; init; }

    public uint TalkTreeNpcNameWidgetAddress { get; init; }

    public uint PlayerTradeInterfaceAddress { get; init; }

    public byte PlayerTradeInterfaceActiveFlag { get; init; }

    public uint PlayerInteractionMenuAddress { get; init; }

    public byte PlayerInteractionMenuActiveFlag { get; init; }

    public int ReverseReferenceHitCount { get; init; }

    public IReadOnlyList<ClientStarbaseRoomControllerObservation> RoomControllers
    { get; init; } = [];

    public IReadOnlyList<ClientStarbasePanelObservation> Panels
    { get; init; } = [];

    public IReadOnlyList<string> ReadErrors
    { get; init; } = [];
}
