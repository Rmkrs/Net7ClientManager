namespace Net7ClientManager.Navigation;

public static class GalaxyNavigationTargetKinds
{
    public static GalaxyNavigationTargetKind FromRawObjectType(
        byte rawObjectType)
    {
        return rawObjectType switch
        {
            3 => GalaxyNavigationTargetKind.Planet,
            11 => GalaxyNavigationTargetKind.SectorGate,
            12 => GalaxyNavigationTargetKind.Station,
            37 => GalaxyNavigationTargetKind.NavigationPoint,
            38 => GalaxyNavigationTargetKind.Asteroid,
            _ => GalaxyNavigationTargetKind.Unknown,
        };
    }

    public static string ToPublicName(
        this GalaxyNavigationTargetKind kind)
    {
        if (kind ==
            GalaxyNavigationTargetKind.Planet)
        {
            return "planet";
        }

        if (kind ==
            GalaxyNavigationTargetKind.SectorGate)
        {
            return "gate";
        }

        if (kind ==
            GalaxyNavigationTargetKind.Station)
        {
            return "station";
        }

        if (kind ==
            GalaxyNavigationTargetKind.NavigationPoint)
        {
            return "navigation_target";
        }

        if (kind ==
            GalaxyNavigationTargetKind.Asteroid)
        {
            return "asteroid";
        }

        return "unknown";
    }
}
