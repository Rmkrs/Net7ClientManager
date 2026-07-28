namespace Net7ClientManager.Services;

using Net7ClientManager.Models;

public sealed class InputActionCatalog(BuiltInInputActionProvider builtInInputActionProvider)
{
    public IReadOnlyList<InputActionDefinition> GetActions()
    {
        // The lab-taught action store is the canonical coordinate source.
        // Built-ins provide safe defaults, but persisted actions with the same
        // name must win so calibration stays resolution/slot-safe.
        return
        [
            .. builtInInputActionProvider.GetActions()
                .Concat(new InputActionStore().LoadActions())
                .GroupBy(action => action.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .OrderBy(action => action.Name, StringComparer.OrdinalIgnoreCase),
        ];
    }

    public InputActionDefinition? FindByName(string actionName)
    {
        return this.GetActions().FirstOrDefault(action =>
            string.Equals(
                action.Name,
                actionName,
                StringComparison.OrdinalIgnoreCase));
    }
}
