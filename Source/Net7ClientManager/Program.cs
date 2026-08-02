namespace Net7ClientManager;

using Net7ClientManager.ControlPlane;
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

        using var mainForm = new MainForm(clientManager);
        using var controlPlaneServer =
            new ClientManagerControlPlaneServer(
                new ClientManagerControlPlaneService(
                    clientManager,
                    mainForm));
        mainForm.Shown += (_, _) => controlPlaneServer.Start();

        Application.Run(mainForm);
    }
}
