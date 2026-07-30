namespace Net7ClientManager.Forms;

internal sealed record GuidedTourStep(
    Func<Control?> ResolveTarget,
    string Title,
    string Body,
    Action? Prepare = null);
