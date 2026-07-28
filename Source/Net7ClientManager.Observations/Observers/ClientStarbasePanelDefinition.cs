namespace Net7ClientManager.Observations.Observers;

using Net7ClientManager.Observations.Models;

internal sealed record ClientStarbasePanelDefinition(
    int Slot,
    ClientStarbasePanelKind Kind,
    string DisplayName,
    uint ViewFieldOffset,
    int? CurrentInterfaceCommand,
    int? FacilityType,
    bool IsPersistent);
