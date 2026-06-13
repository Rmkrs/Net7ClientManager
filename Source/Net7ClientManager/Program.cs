namespace Net7ClientManager;

using Net7ClientManager.Core;
using Net7ClientManager.Forms;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        using var clientManager = new ClientManager();
        clientManager.Start();

        Application.Run(new MainForm(clientManager));
    }
}
