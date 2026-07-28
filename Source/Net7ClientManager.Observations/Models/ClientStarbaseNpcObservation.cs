namespace Net7ClientManager.Observations.Models;

public sealed record ClientStarbaseNpcObservation
{
    public int Slot { get; init; }

    public uint DefinitionAddress { get; init; }

    public int DefinitionKey { get; init; }

    public int DefinitionSecondaryId { get; init; }

    public string Name { get; init; } = "";

    public ClientStarbaseNpcVendorType VendorType { get; init; }

    public ClientStarbaseNpcAmbientType AmbientType { get; init; }

    public bool IsVendor =>
        this.VendorType is not ClientStarbaseNpcVendorType.Invalid and
            not ClientStarbaseNpcVendorType.None;

    public uint WrapperAddress { get; init; }

    public uint PrimaryAvatarAddress { get; init; }

    public uint SecondaryAvatarAddress { get; init; }
}
