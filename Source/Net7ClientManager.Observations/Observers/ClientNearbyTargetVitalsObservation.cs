namespace Net7ClientManager.Observations.Observers;

using Net7ClientManager.Observations.Models;

internal readonly record struct ClientNearbyTargetVitalsObservation(
    ClientTargetHullObservation Hull,
    ClientTargetShieldObservation Shield,
    ClientNearbyTargetVitalsBinding? Binding,
    string Status)
{
    public static ClientNearbyTargetVitalsObservation Unavailable(
        string status,
        ClientNearbyTargetVitalsBinding? binding = null)
    {
        return new ClientNearbyTargetVitalsObservation(
            ClientTargetHullObservation.Unavailable(status),
            ClientTargetShieldObservation.Unavailable(status),
            binding,
            status);
    }
}
