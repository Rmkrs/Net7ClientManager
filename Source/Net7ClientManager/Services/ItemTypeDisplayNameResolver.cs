namespace Net7ClientManager.Services;

using Net7ClientManager.Observations.Models;

internal static class ItemTypeDisplayNameResolver
{
    public static string Resolve(
        ClientRuntimeItemTemplateObservation? template,
        string fallbackTypeName,
        bool isKnownAmmo = false)
    {
        if (isKnownAmmo || IsAmmunition(template))
        {
            return "Ammo";
        }

        return string.IsNullOrWhiteSpace(template?.TypeDisplayName)
            ? fallbackTypeName
            : template.TypeDisplayName.Trim();
    }

    private static bool IsAmmunition(
        ClientRuntimeItemTemplateObservation? template)
    {
        const int weaponCategory = 10;

        // Weapons and ammunition share the weapon category. Ammunition is
        // the stackable branch; equipped weapons themselves have a max stack
        // of one.
        return template is
        {
            Category: weaponCategory,
            MaxStack: > 1,
        };
    }
}
