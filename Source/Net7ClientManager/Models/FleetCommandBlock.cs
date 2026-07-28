namespace Net7ClientManager.Models;

public sealed class FleetCommandBlock
{
    public FleetCommandScope Scope { get; set; }

    public List<FleetCommandStep> Steps { get; set; } = [];

    public static FleetCommandBlock For(
        FleetCommandScope scope,
        params FleetCommandStep[] steps)
    {
        return new FleetCommandBlock
        {
            Scope = scope,
            Steps = [.. steps],
        };
    }
}
