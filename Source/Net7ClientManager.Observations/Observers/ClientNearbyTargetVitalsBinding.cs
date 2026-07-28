namespace Net7ClientManager.Observations.Observers;

internal sealed record ClientNearbyTargetVitalsBinding(
    uint AuxDataAddress,
    ClientAuxDataLookupSnapshot Lookup);
