namespace Net7ClientManager.Observations.Models;

public sealed record ClientStarbaseFacilityObservation
{
    public int Slot { get; init; }

    public uint DefinitionAddress { get; init; }

    public int DefinitionSlot { get; init; }

    public int FacilityType { get; init; }

    public string FacilityTypeName { get; init; } = "";

    public ClientStarbaseInteractionKind InteractionKind { get; init; }

    public ClientStarbasePanelKind? PanelKind { get; init; }

    public int? CurrentInterfaceCommand { get; init; }

    public uint ReservedValue { get; init; }
}
