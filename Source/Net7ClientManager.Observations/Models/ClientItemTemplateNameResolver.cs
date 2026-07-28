namespace Net7ClientManager.Observations.Models;

using Net7ClientManager.Observations.Observers;

/// <summary>
/// Resolves addon-safe display information for observed item template
/// identities. The underlying cdata catalog remains an observation detail.
/// </summary>
public static class ClientItemTemplateNameResolver
{
    public static string? GetKnownName(int? itemTemplateId)
    {
        return itemTemplateId is > 0
            ? ClientItemTemplateCatalog.GetKnownName(
                itemTemplateId.Value)
            : null;
    }

    public static ClientRuntimeItemTemplateObservation? GetKnownTemplate(
        int? itemTemplateId)
    {
        return itemTemplateId is > 0 &&
               ClientItemTemplateCatalog.TryGetRuntimeObservation(
                   itemTemplateId.Value,
                   out var observation)
            ? observation
            : null;
    }

    public static ClientItemTemplateCatalogSnapshot GetCatalogSnapshot()
    {
        return ClientItemTemplateCatalog.GetSnapshot();
    }

    public static ClientItemTemplateCatalogRefreshResult
        RefreshCatalogIfChanged()
    {
        return ClientItemTemplateCatalog.RefreshIfChanged();
    }

    public static IReadOnlyList<ClientItemTemplateSummary>
        GetKnownEquipmentTemplates()
    {
        return ClientItemTemplateCatalog.GetKnownDefinitions()
            .Select(
                definition =>
                {
                    var runtime = definition.RuntimeObservation;
                    return new ClientItemTemplateSummary
                    {
                        ItemTemplateId = definition.Id,
                        Name = definition.Name,
                        TypeDisplayName = runtime.TypeDisplayName,
                        Category = runtime.Category,
                        Subcategory = runtime.Subcategory,
                        ItemType = runtime.ItemType,
                        TechLevel = checked((int)runtime.TechLevel),
                        ProfessionRestriction = GetRestrictionMask(runtime, 0x03),
                        RaceRestriction = GetRestrictionMask(runtime, 0x12),
                        LoreRestriction = GetInt32Attribute(runtime, 0x11),
                        RequiredCombatLevel = GetInt32Attribute(runtime, 0x04),
                        RequiredExploreLevel = GetInt32Attribute(runtime, 0x0c),
                        RequiredOverallLevel = GetInt32Attribute(runtime, 0x0e),
                        RequiredTradeLevel = GetInt32Attribute(runtime, 0x20),
                    };
                })
            .ToArray();
    }

    private static int GetInt32Attribute(
        ClientRuntimeItemTemplateObservation item,
        uint itemInfoId)
    {
        return item.Attributes
            .Where(value => value.ItemInfoId == itemInfoId)
            .Select(value => value.Int32Value ?? 0)
            .DefaultIfEmpty()
            .Max();
    }

    private static int GetRestrictionMask(
        ClientRuntimeItemTemplateObservation item,
        uint itemInfoId)
    {
        var result = 0;
        foreach (var value in item.Attributes.Where(value =>
                     value.ItemInfoId == itemInfoId))
        {
            result |= value.Int32Value.GetValueOrDefault();
        }

        return result;
    }
}
