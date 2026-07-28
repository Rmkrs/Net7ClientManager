namespace Net7ClientManager.Navigation;

public sealed record JobTerminalRoutePresentation
{
    public bool IsVisible { get; init; }

    public uint JobId { get; init; }

    public int? HopCount { get; init; }

    public NavigationDestination? Destination { get; init; }

    public string DestinationName { get; init; } = "";

    public string StatusText { get; init; } = "";

    public bool CanSetDestination =>
        this.IsVisible &&
        this.HopCount.HasValue &&
        this.Destination != null;

    public static JobTerminalRoutePresentation Hidden { get; } = new();
}
