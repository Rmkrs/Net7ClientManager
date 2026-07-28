namespace Net7ClientManager.Navigation;

public sealed record NavigationRouteStep
{
    public required int Number { get; init; }

    public required NavigationRouteStepKind Kind { get; init; }

    public required string FromSectorKey { get; init; }

    public required string FromSectorName { get; init; }

    public required string FromSystemName { get; init; }

    public required string ToSectorKey { get; init; }

    public required string ToSectorName { get; init; }

    public required string ToSystemName { get; init; }

    public string? DepartureTargetKey { get; init; }

    public string? DepartureTargetName { get; init; }

    public string? DepartureTargetType { get; init; }

    public byte? DepartureTargetRawObjectType { get; init; }

    public GalaxyNavigationTargetSelectionContext
        DepartureTargetSelectionContext { get; init; } =
            GalaxyNavigationTargetSelectionContext.Navigation;

    public bool HasDepartureTargetPosition { get; init; }

    public float DepartureTargetX { get; init; }

    public float DepartureTargetY { get; init; }

    public float DepartureTargetZ { get; init; }

    public string? FinalTargetKey { get; init; }

    public string? FinalTargetName { get; init; }

    public string? FinalTargetType { get; init; }

    public byte? FinalTargetRawObjectType { get; init; }

    public GalaxyNavigationTargetSelectionContext
        FinalTargetSelectionContext { get; init; } =
            GalaxyNavigationTargetSelectionContext.Navigation;

    public bool HasFinalTargetPosition { get; init; }

    public float FinalTargetX { get; init; }

    public float FinalTargetY { get; init; }

    public float FinalTargetZ { get; init; }

    public string AccessRequirement { get; init; } = "";
}
