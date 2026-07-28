namespace Net7ClientManager.Observations.Observers;

using Net7ClientManager.Observations.Models;

internal readonly record struct ClientShipAuxDataObservation(
    ClientTargetShieldObservation Shield,
    ClientTargetHullObservation Hull,
    ClientTargetEnergyObservation Energy,
    ClientShipOperationalObservation Operational)
{
    public static ClientShipAuxDataObservation Unavailable(
        string status,
        uint auxDataLookupAddress = 0)
    {
        return new ClientShipAuxDataObservation(
            ClientTargetShieldObservation.Unavailable(
                status,
                auxDataLookupAddress),
            ClientTargetHullObservation.Unavailable(
                status,
                auxDataLookupAddress),
            ClientTargetEnergyObservation.Unavailable(
                status,
                auxDataLookupAddress),
            ClientShipOperationalObservation.Unavailable(
                status,
                auxDataLookupAddress));
    }
}
