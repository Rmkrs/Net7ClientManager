namespace Net7ClientManager.Forms;

using Net7ClientManager.Models;

internal static class SlotPlacementDefaults
{
    public static string CreateDefaultName(
        IEnumerable<ClientSlot> slots)
    {
        var existingNames = slots
            .Select(slot => slot.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        for (var slotNumber = 1; ; slotNumber++)
        {
            var candidate = string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"Client {slotNumber}");

            if (!existingNames.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    public static WindowBounds CreateForScreen(
        Rectangle screenBounds)
    {
        var inset = Math.Min(
            40,
            Math.Max(16, screenBounds.Width / 20));

        var width = Math.Min(
            1280,
            Math.Max(640, screenBounds.Width / 2));

        var height = Math.Min(
            720,
            Math.Max(480, screenBounds.Height / 2));

        return new WindowBounds
        {
            Left = screenBounds.Left + inset,
            Top = screenBounds.Top + inset,
            Width = width,
            Height = height,
        };
    }

    public static WindowBounds CreateForPrimaryScreen()
    {
        var screenBounds = Screen.PrimaryScreen?.Bounds
                           ?? SystemInformation.VirtualScreen;

        return CreateForScreen(screenBounds);
    }
}
