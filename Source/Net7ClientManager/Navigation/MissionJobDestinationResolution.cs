namespace Net7ClientManager.Navigation;

using System.Globalization;

public sealed record MissionJobDestinationResolution
{
    public required string SectorKey { get; init; }

    public required string SectorName { get; init; }

    public required string SystemName { get; init; }

    public NavigationDestination? ExactDestination { get; init; }

    public required NavigationDestination RouteDestination { get; init; }

    public string Fingerprint => string.Create(
        CultureInfo.InvariantCulture,
        $"{this.SectorKey}|{this.SectorName}|{this.SystemName}|{this.RouteDestination.Kind}|{this.RouteDestination.TargetKey ?? ""}");
}
