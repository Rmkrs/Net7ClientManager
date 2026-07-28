namespace Net7ClientManager.Navigation;

internal readonly record struct NavigationAutoPilotMachineTransition(
    NavigationAutoPilotMachineState State,
    NavigationAutoPilotEffectKind Effect =
        NavigationAutoPilotEffectKind.None);
