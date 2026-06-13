namespace Net7ClientManager.Core;

public static class ResourceLoader
{
    public static Icon EarthAndBeyondIcon { get; } = LoadIcon();

    private static Icon LoadIcon()
    {
        var executablePath = Application.ExecutablePath;
        return Icon.ExtractAssociatedIcon(executablePath)
               ?? SystemIcons.Application;
    }
}
