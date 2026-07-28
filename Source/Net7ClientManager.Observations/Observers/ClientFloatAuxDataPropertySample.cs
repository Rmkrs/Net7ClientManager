namespace Net7ClientManager.Observations.Observers;

internal readonly record struct ClientFloatAuxDataPropertySample(
    uint PropertyAddress,
    bool IsValid,
    float Value);
