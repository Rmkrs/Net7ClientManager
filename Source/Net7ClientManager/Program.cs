namespace Net7ClientManager;

using Net7ClientManager.Core;
using Net7ClientManager.Forms;
using Net7ClientManager.Services;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        UiObfuscationMode.Initialize(args);
        ApplicationConfiguration.Initialize();

        using var clientManager = new ClientManager();
        clientManager.Start();

        Application.Run(new MainForm(clientManager));
    }
}
