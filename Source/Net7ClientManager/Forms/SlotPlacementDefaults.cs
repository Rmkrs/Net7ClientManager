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
        var width = Math.Min(
            1920,
            Math.Max(640, screenBounds.Width));

        var height = Math.Min(
            1080,
            Math.Max(480, screenBounds.Height));

        return new WindowBounds
        {
            Left = screenBounds.Left,
            Top = screenBounds.Top,
            Width = width,
            Height = height,
        };
    }

    public static SlotResolutionPreset SelectBestFitResolution(
        IReadOnlyList<SlotResolutionPreset> presets,
        SlotResolutionPreset preferredPreset,
        Rectangle screenBounds)
    {
        if (FitsScreen(preferredPreset, screenBounds))
        {
            return preferredPreset;
        }

        return presets
                   .Where(preset => FitsScreen(preset, screenBounds))
                   .OrderByDescending(preset =>
                       (long)preset.Width * preset.Height)
                   .ThenByDescending(preset => preset.Width)
                   .ThenByDescending(preset => preset.Height)
                   .FirstOrDefault()
               ?? presets
                   .OrderBy(preset =>
                       (long)preset.Width * preset.Height)
                   .FirstOrDefault()
               ?? preferredPreset;
    }

    public static WindowBounds CreateForPrimaryScreen()
    {
        var screenBounds = Screen.PrimaryScreen?.Bounds
                           ?? SystemInformation.VirtualScreen;

        return CreateForScreen(screenBounds);
    }

    private static bool FitsScreen(
        SlotResolutionPreset preset,
        Rectangle screenBounds)
    {
        return preset.Width <= screenBounds.Width &&
               preset.Height <= screenBounds.Height;
    }
}
