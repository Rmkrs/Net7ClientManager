namespace Net7ClientManager.Services;

using Net7ClientManager.Models;

public sealed class FleetCommandCatalog(BuiltInFleetCommandProvider builtInFleetCommandProvider)
{
    public IReadOnlyList<FleetCommandDefinition> GetCommands()
    {
        return builtInFleetCommandProvider.GetCommands();
    }

    public IReadOnlyList<FleetCommandDefinition> GetOverlayCommands()
    {
        return [.. this.GetCommands()
            .Where(command => command.ShowInOverlay)];
    }
}
