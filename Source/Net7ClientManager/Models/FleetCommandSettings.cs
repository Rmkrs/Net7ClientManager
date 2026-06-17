namespace Net7ClientManager.Models;

public sealed class FleetCommandSettings
{
    public Keys CommandMenuHotKey { get; set; } = Keys.Control | Keys.Q;

    public string AssistMeTargetActionName { get; set; } = "Target Group Member Target";

    public string AssistMeFireActionName { get; set; } = "Fire All";

    public int DelayAfterAssistMilliseconds { get; set; } = 100;

    public int DelayBetweenClientsMilliseconds { get; set; } = 100;

    public bool ReturnFocusToMain { get; set; } = true;
}
