namespace Net7ClientManager.Observations.Models;

internal static class ClientNearbyTargetKindCatalog
{
    public static ClientNearbyTargetKind FromRawObjectType(
        byte rawObjectType)
    {
        return rawObjectType switch
        {
            0 => ClientNearbyTargetKind.NonPlayerShip,
            1 => ClientNearbyTargetKind.Player,
            2 => ClientNearbyTargetKind.CapitalShip,
            3 => ClientNearbyTargetKind.Planet,
            4 => ClientNearbyTargetKind.Item,
            9 => ClientNearbyTargetKind.Missile,
            10 => ClientNearbyTargetKind.Nebula,
            11 => ClientNearbyTargetKind.SectorGate,
            12 => ClientNearbyTargetKind.Station,
            24 => ClientNearbyTargetKind.Building,
            25 => ClientNearbyTargetKind.Corpse,
            27 => ClientNearbyTargetKind.Mine,
            34 => ClientNearbyTargetKind.Resource,
            35 => ClientNearbyTargetKind.Drone,
            37 => ClientNearbyTargetKind.NavigationPoint,
            38 => ClientNearbyTargetKind.Asteroid,
            _ => ClientNearbyTargetKind.Unknown,
        };
    }
}
