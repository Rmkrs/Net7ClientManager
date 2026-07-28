namespace Net7ClientManager.Observations.Observers;

internal readonly record struct ClientAuxDataPropertyReference(
    uint NodeAddress,
    uint NameAddress,
    string Name,
    uint PropertyAddress);
