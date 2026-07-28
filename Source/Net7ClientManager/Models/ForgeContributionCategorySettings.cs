namespace Net7ClientManager.Models;

public sealed class ForgeContributionCategorySettings
{
    public bool NpcPresence { get; set; } = true;

    public bool NavigationObjects { get; set; } = true;

    public bool StationServices { get; set; } = true;

    public bool VendorInventories { get; set; } = true;

    public bool MobObservations { get; set; } = true;

    public bool LootObservations { get; set; } = true;

    public bool ResourceObservations { get; set; } = true;

    public bool ProductionRecipes { get; set; } = true;

    public bool Missions { get; set; } = true;

    public bool JobOffers { get; set; } = true;
}
