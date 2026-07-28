namespace Net7ClientManager.GalaxyKnowledge;

using System.Globalization;

internal static class GalaxyFinderItemFacts
{
    public static string GetAttributeText(
        GalaxyItemKnowledge item,
        uint itemInfoId,
        string suffix = "")
    {
        ArgumentNullException.ThrowIfNull(item);

        var attribute = item.Attributes.FirstOrDefault(candidate =>
            candidate.ItemInfoId == itemInfoId);

        if (attribute == null)
        {
            return "—";
        }

        if (!string.IsNullOrWhiteSpace(attribute.StringValue))
        {
            return attribute.StringValue.Trim();
        }

        if (attribute.FloatValue is { } floatValue &&
            float.IsFinite(floatValue))
        {
            return string.Concat(
                FormatNumber(floatValue),
                suffix);
        }

        if (attribute.Int32Value is { } integerValue)
        {
            return string.Concat(
                integerValue.ToString(
                    "N0",
                    CultureInfo.CurrentCulture),
                suffix);
        }

        return "—";
    }

    public static double? GetAttributeNumber(
        GalaxyItemKnowledge item,
        uint itemInfoId)
    {
        ArgumentNullException.ThrowIfNull(item);

        var attribute = item.Attributes.FirstOrDefault(candidate =>
            candidate.ItemInfoId == itemInfoId);

        if (attribute == null)
        {
            return null;
        }

        if (attribute.FloatValue is { } floatValue &&
            float.IsFinite(floatValue))
        {
            return floatValue;
        }

        if (attribute.Int32Value is { } integerValue)
        {
            return integerValue;
        }

        return null;
    }

    public static string GetLevelText(GalaxyItemKnowledge item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.TechLevel > 0
            ? item.TechLevel.ToString(CultureInfo.InvariantCulture)
            : "—";
    }

    public static string GetManufacturerText(
        GalaxyItemKnowledge item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return string.IsNullOrWhiteSpace(item.Manufacturer)
            ? "—"
            : item.Manufacturer.Trim();
    }

    public static string GetTypeText(GalaxyItemKnowledge item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if ((item.Family is
                 GalaxyItemFamily.Weapon or
                 GalaxyItemFamily.Other) &&
            !string.IsNullOrWhiteSpace(item.TypeDisplayName))
        {
            return item.TypeDisplayName.Trim();
        }

        return GalaxyFinderItemSearchIndex.GetFamilyDisplayName(
            item.Family);
    }

    public static string GetMaximumStackText(
        GalaxyItemKnowledge item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.MaximumStack > 0
            ? item.MaximumStack.ToString(
                "N0",
                CultureInfo.CurrentCulture)
            : "—";
    }

    public static string GetRecipeUseText(
        GalaxyKnowledgeSnapshot snapshot,
        GalaxyItemKnowledge item)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(item);

        var outputs = item.UsedByRecipes
            .Select(recipe =>
                snapshot.ItemsByTemplateId.TryGetValue(
                    recipe.OutputItemTemplateId,
                    out var output)
                    ? output.Name
                    : string.Create(
                        CultureInfo.InvariantCulture,
                        $"Item {recipe.OutputItemTemplateId}"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return SummarizeNames(outputs, "");
    }

    public static string GetRefinesToText(
        GalaxyKnowledgeSnapshot snapshot,
        GalaxyItemKnowledge item)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(item);

        var outputs = item.RefinesTo
            .Select(relationship =>
                snapshot.ItemsByTemplateId.TryGetValue(
                    relationship.OutputItemTemplateId,
                    out var output)
                    ? output.Name
                    : string.Create(
                        CultureInfo.InvariantCulture,
                        $"Item {relationship.OutputItemTemplateId}"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return SummarizeNames(outputs, "");
    }

    private static string SummarizeNames(
        IReadOnlyList<string> names,
        string emptyText)
    {
        if (names.Count == 0)
        {
            return emptyText;
        }

        if (names.Count <= 2)
        {
            return string.Join(" · ", names);
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{names[0]} · {names[1]} · +{names.Count - 2}");
    }

    private static string FormatNumber(double value)
    {
        if (Math.Abs(value - Math.Round(value)) < 0.0001d)
        {
            return Math.Round(value).ToString(
                "N0",
                CultureInfo.CurrentCulture);
        }

        return value.ToString(
            "0.##",
            CultureInfo.CurrentCulture);
    }
}
