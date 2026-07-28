namespace Net7ClientManager.Observations.Models;

public sealed record ClientShortcutSlotObservation
{
    public int Bar { get; init; }

    public int Group { get; init; }

    public int Button { get; init; }

    public int VisibleKey => this.Bar * 3 + this.Button + 1;

    public bool IsVisible { get; init; }

    public uint GadgetAddress { get; init; }

    public uint RendererAddress { get; init; }

    public uint PayloadAddress { get; init; }

    public uint RendererVTableRva { get; init; }

    public uint PayloadVTableRva { get; init; }

    public uint VisibleButtonAddress { get; init; }

    public uint VisibleButtonActiveGadgetAddress { get; init; }

    public bool? VisibleButtonGadgetMatches { get; init; }

    public ClientShortcutKind Kind { get; init; }

    public int? PrimaryIdentity { get; init; }

    public int? SecondaryIdentity { get; init; }

    public string FamilyName { get; init; } = "";

    public string ResolvedName { get; init; } = "";

    public bool ResolvedNameIsExact { get; init; }

    public string IconResourceName { get; init; } = "";

    public float? TintRed { get; init; }

    public float? TintGreen { get; init; }

    public float? TintBlue { get; init; }

    public ClientInventoryCollectionKind? InventoryCollection { get; init; }

    public int? ItemTemplateId { get; init; }

    public int? EquippedItemTemplateIdCandidate { get; init; }

    public string EquippedItemNameCandidate { get; init; } = "";

    public int? CargoItemTemplateIdCandidate { get; init; }

    public string CargoItemNameCandidate { get; init; } = "";

    public string Status { get; init; } = "";

    public bool IsOccupied => this.GadgetAddress != 0;

    public bool IsResolved =>
        this.Kind switch
        {
            ClientShortcutKind.Skill =>
                this.PrimaryIdentity.HasValue &&
                this.ResolvedNameIsExact &&
                !string.IsNullOrWhiteSpace(this.ResolvedName),
            ClientShortcutKind.Equipment or
            ClientShortcutKind.Cargo =>
                this.PrimaryIdentity.HasValue &&
                this.ItemTemplateId is > 0 &&
                !string.IsNullOrWhiteSpace(this.ResolvedName),
            _ => false,
        };
}
