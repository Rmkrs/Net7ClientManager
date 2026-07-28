namespace Net7ClientManager.Observations.Observers;

internal readonly record struct ClientBooleanAuxDataPropertySample(
    uint PropertyAddress,
    bool IsValid,
    bool Value);
