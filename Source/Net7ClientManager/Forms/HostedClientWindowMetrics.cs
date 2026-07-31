namespace Net7ClientManager.Forms;

using Net7ClientManager.Models;

internal static class HostedClientWindowMetrics
{
    public const int TitleBarHeight = 34;

    public static bool ReservesTitleBarSpace(ClientSlot slot)
    {
        return slot.EffectiveTitleBarMode == ClientTitleBarMode.Always;
    }

    public static int GetTitleBarHeight(ClientSlot slot)
    {
        return ReservesTitleBarSpace(slot)
            ? TitleBarHeight
            : 0;
    }

    public static int GetHostedWindowHeight(ClientSlot slot)
    {
        return slot.Bounds.Height + GetTitleBarHeight(slot);
    }

    public static Rectangle GetHostedWindowBounds(ClientSlot slot)
    {
        return new Rectangle(
            x: slot.Bounds.Left,
            y: slot.Bounds.Top,
            width: slot.Bounds.Width,
            height: GetHostedWindowHeight(slot));
    }

    public static string GetTitleBarModeText(ClientSlot slot)
    {
        return slot.EffectiveTitleBarMode switch
        {
            ClientTitleBarMode.Always => "title bar on",
            ClientTitleBarMode.OnHover => "title bar on hover",
            _ => "title bar off",
        };
    }
}
