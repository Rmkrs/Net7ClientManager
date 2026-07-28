namespace Net7ClientManager.Services;

using Microsoft.Web.WebView2.Core;

internal static class MissionWikiBrowserEnvironment
{
    private static readonly Lazy<Task<CoreWebView2Environment>>
        sharedEnvironment =
            new(
                CreateEnvironmentAsync,
                LazyThreadSafetyMode.ExecutionAndPublication);

    public static Task<CoreWebView2Environment> GetAsync()
    {
        return sharedEnvironment.Value;
    }

    private static Task<CoreWebView2Environment>
        CreateEnvironmentAsync()
    {
        var userDataFolder = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "Net7ClientManager",
            "WebView2",
            "MissionWiki");

        Directory.CreateDirectory(userDataFolder);

        return CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder: userDataFolder,
            options: null);
    }
}
