namespace Net7ClientManager.Observations.Models;

public readonly record struct ClientNavigationStateGeneration(
    uint ModuleBaseAddress,
    uint ClientContextAddress,
    uint ClientObjectAddress,
    uint AuxDataAddress,
    uint AuxDataLookupAddress)
{
    public bool HasClientContext =>
        this.ClientContextAddress != 0;

    public bool HasClientObject =>
        this.ClientObjectAddress != 0;

    public bool HasAuxData =>
        this.AuxDataAddress != 0;

    public bool HasAuxDataLookup =>
        this.AuxDataLookupAddress != 0;
}
