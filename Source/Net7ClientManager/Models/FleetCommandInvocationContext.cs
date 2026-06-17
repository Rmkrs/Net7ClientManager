namespace Net7ClientManager.Models;

public sealed class FleetCommandInvocationContext
{
    public required ClientInstance ActiveClient { get; init; }

    public Point? ActiveClientMousePosition { get; init; }
}
