namespace Net7ClientManager.Forms;

internal sealed record HelpLaunchRequest(
    string TopicId,
    int? ProcessId = null);

internal static class HelpCenterLauncher
{
    private static readonly object sync = new();
    private static Action<IWin32Window?, HelpLaunchRequest>? showHandler;

    public static void Configure(
        Action<IWin32Window?, HelpLaunchRequest> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (sync)
        {
            showHandler = handler;
        }
    }

    public static void Clear(
        Action<IWin32Window?, HelpLaunchRequest> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (sync)
        {
            if (showHandler == handler)
            {
                showHandler = null;
            }
        }
    }

    public static void Show(
        IWin32Window? owner,
        string? topicId = null,
        int? processId = null)
    {
        Action<IWin32Window?, HelpLaunchRequest>? handler;

        lock (sync)
        {
            handler = showHandler;
        }

        handler?.Invoke(
            owner,
            new HelpLaunchRequest(
                string.IsNullOrWhiteSpace(topicId)
                    ? HelpTopicIds.Home
                    : topicId,
                processId));
    }
}
