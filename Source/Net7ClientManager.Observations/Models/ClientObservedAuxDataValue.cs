namespace Net7ClientManager.Observations.Models;

public readonly record struct ClientObservedAuxDataValue<T>(
    uint PropertyAddress,
    uint StateMarker,
    T Value)
{
    public bool IsPresent =>
        this.PropertyAddress != 0;

    public bool IsAvailable =>
        this.PropertyAddress != 0 &&
        this.StateMarker != 0;

    public bool IsFresh =>
        this.StateMarker == 2;

    public bool IsSettled =>
        this.StateMarker == 1;
}
