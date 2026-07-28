namespace Net7ClientManager.Observations.Models;

public sealed record ClientFrameRateObservation
{
    public bool IsAvailable { get; init; }

    public string Status { get; init; } = "";

    public uint MainViewAddress { get; init; }

    public uint InterfaceListHeadAddress { get; init; }

    public uint InterfaceNodeAddress { get; init; }

    public uint InterfaceAddress { get; init; }

    public uint InterfaceVTableAddress { get; init; }

    public int ScannedInterfaceCount { get; init; }

    public float SmoothedFramesPerSecond { get; init; }

    public float MinimumFramesPerSecond { get; init; }

    public float MaximumFramesPerSecond { get; init; }

    public static ClientFrameRateObservation Unavailable(
        string status,
        uint mainViewAddress = 0,
        uint interfaceListHeadAddress = 0,
        uint interfaceNodeAddress = 0,
        uint interfaceAddress = 0,
        uint interfaceVTableAddress = 0,
        int scannedInterfaceCount = 0)
    {
        return new ClientFrameRateObservation
        {
            Status = status,
            MainViewAddress = mainViewAddress,
            InterfaceListHeadAddress = interfaceListHeadAddress,
            InterfaceNodeAddress = interfaceNodeAddress,
            InterfaceAddress = interfaceAddress,
            InterfaceVTableAddress = interfaceVTableAddress,
            ScannedInterfaceCount = scannedInterfaceCount,
        };
    }
}
