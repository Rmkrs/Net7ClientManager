namespace Net7ClientManager.Observations.Observers;

internal readonly record struct ClientInt32AuxDataPropertySample(
    uint PropertyAddress,
    bool IsValid,
    int Value);
