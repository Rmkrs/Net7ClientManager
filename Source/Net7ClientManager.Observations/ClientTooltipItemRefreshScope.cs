namespace Net7ClientManager.Observations;

[Flags]
internal enum ClientTooltipItemRefreshScope
{
    None = 0,
    LocalPlayer = 1,
    SecureInventory = 2,
    Target = 4,
    Shortcuts = 8,
}
