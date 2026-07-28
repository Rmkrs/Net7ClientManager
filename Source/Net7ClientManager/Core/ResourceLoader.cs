namespace Net7ClientManager.Core;

public static class ResourceLoader
{
    private const string EarthAndBeyondIconResourceName =
        "Net7ClientManager.Resources.EB.ICO";

    public static Icon Net7ClientManagerIcon { get; } =
        LoadApplicationIcon();

    public static Icon EarthAndBeyondIcon { get; } =
        LoadEmbeddedIcon(EarthAndBeyondIconResourceName);

    private static Icon LoadApplicationIcon()
    {
        return Icon.ExtractAssociatedIcon(Application.ExecutablePath)
               ?? SystemIcons.Application;
    }

    private static Icon LoadEmbeddedIcon(string resourceName)
    {
        var assembly = typeof(ResourceLoader).Assembly;

        using var stream = assembly.GetManifestResourceStream(resourceName)
                           ?? throw new InvalidOperationException(
                               $"Embedded icon resource '{resourceName}' was not found.");

        // Icon may retain a dependency on its source stream.
        // Clone it before disposing the stream.
        using var sourceIcon = new Icon(stream);
        return (Icon)sourceIcon.Clone();
    }
}
