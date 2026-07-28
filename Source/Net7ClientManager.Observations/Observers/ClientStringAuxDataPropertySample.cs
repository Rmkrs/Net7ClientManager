namespace Net7ClientManager.Observations.Observers;

internal readonly record struct ClientStringAuxDataPropertySample(
    uint PropertyAddress,
    bool IsValid,
    uint StringAddress,
    string Value);
