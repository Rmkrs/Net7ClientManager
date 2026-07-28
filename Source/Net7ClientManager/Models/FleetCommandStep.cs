namespace Net7ClientManager.Models;

public sealed class FleetCommandStep
{
    public FleetCommandStepKind Kind { get; set; }

    public string? ActionName { get; set; }

    public Keys Key { get; set; } = Keys.None;

    public int Milliseconds { get; set; }

    public string? Text { get; set; }

    public int DurationMilliseconds { get; set; }

    public bool Blink { get; set; }

    public static FleetCommandStep Action(string actionName)
    {
        return new FleetCommandStep
        {
            Kind = FleetCommandStepKind.Action,
            ActionName = actionName,
        };
    }

    public static FleetCommandStep Delay(int milliseconds)
    {
        return new FleetCommandStep
        {
            Kind = FleetCommandStepKind.Delay,
            Milliseconds = milliseconds,
        };
    }

    public static FleetCommandStep SetTitle(
        string text,
        int durationMilliseconds,
        bool blink = false)
    {
        return new FleetCommandStep
        {
            Kind = FleetCommandStepKind.SetTitle,
            Text = text,
            DurationMilliseconds = durationMilliseconds,
            Blink = blink,
        };
    }

    public static FleetCommandStep RestorePilotFocus()
    {
        return new FleetCommandStep
        {
            Kind = FleetCommandStepKind.RestorePilotFocus,
        };
    }

    public static FleetCommandStep TapKey(Keys key)
    {
        return new FleetCommandStep
        {
            Kind = FleetCommandStepKind.TapKey,
            Key = key,
        };
    }

    public static FleetCommandStep TypeText(string text)
    {
        return new FleetCommandStep
        {
            Kind = FleetCommandStepKind.TypeText,
            Text = text,
        };
    }

    public static FleetCommandStep ChatCommand(string text)
    {
        return new FleetCommandStep
        {
            Kind = FleetCommandStepKind.ChatCommand,
            Text = text,
        };
    }

    public static FleetCommandStep TargetInvokingPilot()
    {
        return new FleetCommandStep
        {
            Kind = FleetCommandStepKind.TargetInvokingPilot,
        };
    }

    public static FleetCommandStep TargetInvokingPilotTarget()
    {
        return new FleetCommandStep
        {
            Kind = FleetCommandStepKind.TargetInvokingPilotTarget,
        };
    }
}
