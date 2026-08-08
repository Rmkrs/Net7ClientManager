namespace Net7ClientManager.RecipeMapping;

using Net7ClientManager.Observations.Models;

internal static class RecipeMappingManufacturabilityResolver
{
    // Item template flag 0x80 is the native/server `no_manu` bit. Unlike
    // Forge recipe observations, this is an authoritative negative signal.
    private const uint NotManufacturableFlag = 0x00000080;

    public static bool? Resolve(
        ClientRuntimeItemTemplateObservation? template,
        bool hasObservedRecipe)
    {
        if (template is { IsAvailable: true })
        {
            return (template.Flags & NotManufacturableFlag) == 0;
        }

        return hasObservedRecipe ? true : null;
    }
}
