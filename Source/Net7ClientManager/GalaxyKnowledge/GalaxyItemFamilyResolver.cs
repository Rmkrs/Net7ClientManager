namespace Net7ClientManager.GalaxyKnowledge;

using Net7ClientManager.Observations.Models;

internal static class GalaxyItemFamilyResolver
{
    public static GalaxyItemFamily Resolve(
        ClientRuntimeItemTemplateObservation template)
    {
        ArgumentNullException.ThrowIfNull(template);

        return (template.Category, template.Subcategory, template.ItemType)
            switch
            {
                (10, 100, 14) or
                (10, 101, 16) or
                (10, 102, 15) =>
                    GalaxyItemFamily.Weapon,
                (10, 103, 10) =>
                    GalaxyItemFamily.Ammo,
                (11, 110, 11) =>
                    GalaxyItemFamily.Device,
                (12, 120, 7) or
                (51, 120, 7) =>
                    GalaxyItemFamily.Reactor,
                (12, 121, 6) =>
                    GalaxyItemFamily.Engine,
                (12, 122, 2) =>
                    GalaxyItemFamily.Shield,
                (>= 50 and <= 54, _, _) =>
                    GalaxyItemFamily.Component,
                (80, _, _) =>
                    GalaxyItemFamily.RefinedMaterial,
                (81, _, _) =>
                    GalaxyItemFamily.RawResource,
                (90, _, _) =>
                    GalaxyItemFamily.TradeGood,
                _ =>
                    GalaxyItemFamily.Other,
            };
    }

    public static bool IsKnownCombination(
        ClientRuntimeItemTemplateObservation template)
    {
        return Resolve(template) != GalaxyItemFamily.Other;
    }
}
