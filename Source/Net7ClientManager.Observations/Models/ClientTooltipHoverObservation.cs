namespace Net7ClientManager.Observations.Models;

public enum ClientTooltipHoverViewKind
{
    None,
    MainView,
    StarbaseView,
}

public sealed record ClientTooltipHoverObservation
{
    public bool IsAvailable { get; init; }

    public string Status { get; init; } = "";

    public ClientTooltipHoverViewKind ViewKind { get; init; }

    public uint ViewAddress { get; init; }

    public uint ControllerAddress { get; init; }

    public uint ActiveGadgetAddress { get; init; }

    public uint ActiveGadgetVTableRva { get; init; }

    public bool IsDisplayed { get; init; }

    public string ControlName { get; init; } = "";

    public string NativeTooltipText { get; init; } = "";

    public bool HasActiveGadget => this.ActiveGadgetAddress != 0;

    public static ClientTooltipHoverObservation Unavailable(
        string status)
    {
        return new ClientTooltipHoverObservation
        {
            Status = status,
        };
    }
}
