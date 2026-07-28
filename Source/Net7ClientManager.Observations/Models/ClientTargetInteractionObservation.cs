namespace Net7ClientManager.Observations.Models;

public sealed record ClientTargetInteractionObservation
{
    public bool IsAvailable { get; init; }

    public string Status { get; init; } = "";

    public uint MainViewAddress { get; init; }

    public uint CockpitHudAddress { get; init; }

    public uint ScannerHudControllerAddress { get; init; }

    public uint TargetVerbControllerAddress { get; init; }

    public uint TargetVerbControllerVTableAddress { get; init; }

    public bool IsActive { get; init; }

    public bool HasTarget { get; init; }

    public uint TargetClientObjectAddress { get; init; }

    public uint TargetObjectId { get; init; }

    public uint VerbButtonVectorBeginAddress { get; init; }

    public uint VerbButtonVectorEndAddress { get; init; }

    public uint VerbButtonVectorCapacityAddress { get; init; }

    public IReadOnlyList<ClientTargetVerbActionObservation> Actions { get; init; } = [];

    public bool CanGate =>
        this.CanExecute(ClientTargetVerb.Gate);

    public bool CanExecute(ClientTargetVerb verb)
    {
        return this.IsAvailable &&
            this.IsActive &&
            this.HasTarget &&
            this.Actions.Any(action =>
                action.Verb == verb &&
                action.IsExecutable);
    }

    public ClientTargetVerbActionObservation? FindAction(
        ClientTargetVerb verb)
    {
        return this.Actions.FirstOrDefault(
            action => action.Verb == verb);
    }

    public static ClientTargetInteractionObservation Unavailable(
        string status,
        uint mainViewAddress = 0,
        uint cockpitHudAddress = 0,
        uint scannerHudControllerAddress = 0,
        uint targetVerbControllerAddress = 0,
        uint targetVerbControllerVTableAddress = 0)
    {
        return new ClientTargetInteractionObservation
        {
            Status = status,
            MainViewAddress = mainViewAddress,
            CockpitHudAddress = cockpitHudAddress,
            ScannerHudControllerAddress =
                scannerHudControllerAddress,
            TargetVerbControllerAddress =
                targetVerbControllerAddress,
            TargetVerbControllerVTableAddress =
                targetVerbControllerVTableAddress,
        };
    }
}
