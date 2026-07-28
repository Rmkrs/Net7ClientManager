namespace Net7ClientManager.Observations.Observers;

internal readonly record struct ClientDeltaFloatAuxDataPropertySample(
    uint PropertyAddress,
    bool IsValid,
    float Value);
