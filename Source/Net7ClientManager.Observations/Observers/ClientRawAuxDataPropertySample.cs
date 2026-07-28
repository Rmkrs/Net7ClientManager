namespace Net7ClientManager.Observations.Observers;

internal readonly record struct ClientRawAuxDataPropertySample(
    uint PropertyAddress,
    bool IsValid,
    uint PrimaryValue,
    uint SecondaryValue,
    bool HasSecondaryValue);
