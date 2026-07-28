namespace Net7ClientManager.GalaxyKnowledge;

using System.Globalization;
using System.Text;

internal static class GalaxyFinderSourcePresentation
{
    public static string BuildCompactSummary(
        GalaxyItemKnowledge item,
        GalaxyFinderResolvedRoute? preferredRoute = null)
    {
        ArgumentNullException.ThrowIfNull(item);

        var sources = GetDistinctSources(item);
        List<string> parts = [];
        var primary = preferredRoute?.Source ??
                      sources.FirstOrDefault();

        if (primary != null)
        {
            parts.Add(GetSourceDisplayName(primary));
            var sameKindCount = sources.Count(source =>
                source.Kind == primary.Kind) - 1;
            if (sameKindCount > 0)
            {
                parts.Add(string.Format(
                    CultureInfo.CurrentCulture,
                    "+{0:N0} {1}",
                    sameKindCount,
                    GetPluralKind(primary.Kind)));
            }

            foreach (var group in sources
                         .Where(source => source.Kind != primary.Kind)
                         .GroupBy(source => source.Kind)
                         .OrderBy(group => group.Key))
            {
                parts.Add(group.Count() == 1
                    ? GetSourceDisplayName(group.First())
                    : string.Format(
                        CultureInfo.CurrentCulture,
                        "{0:N0} {1}",
                        group.Count(),
                        GetPluralKind(group.Key)));
            }
        }

        if (item.ProducedByRecipes.Any(recipe =>
                recipe.Kind == GalaxyRecipeKind.Manufacture))
        {
            parts.Add("Crafted");
        }

        if (item.RefinedFrom.Count != 0 ||
            item.ProducedByRecipes.Any(recipe =>
                recipe.Kind == GalaxyRecipeKind.Refine))
        {
            parts.Add("Refined");
        }

        return string.Join(" · ", parts);
    }

    public static string BuildDetails(GalaxyItemKnowledge item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var sources = GetDistinctSources(item);
        StringBuilder builder = new();

        foreach (var group in sources
                     .GroupBy(source => source.Kind)
                     .OrderBy(group => group.Key))
        {
            if (builder.Length != 0)
            {
                builder.AppendLine().AppendLine();
            }

            builder.Append(GetHeading(group.Key));
            foreach (var source in group
                         .OrderBy(
                             source => GetSourceDisplayName(source),
                             StringComparer.OrdinalIgnoreCase))
            {
                builder.AppendLine()
                    .Append("• ")
                    .Append(GetSourceDisplayName(source));

                var location = GetLocationDisplayName(source);
                if (location.Length != 0)
                {
                    builder.Append(" · ").Append(location);
                }
            }
        }

        if (item.ProducedByRecipes.Any(recipe =>
                recipe.Kind == GalaxyRecipeKind.Manufacture))
        {
            AppendSimpleSection(builder, "Manufacturing", "Crafted");
        }

        if (item.RefinedFrom.Count != 0 ||
            item.ProducedByRecipes.Any(recipe =>
                recipe.Kind == GalaxyRecipeKind.Refine))
        {
            AppendSimpleSection(builder, "Refining", "Refined");
        }

        return builder.ToString().Trim();
    }

    public static string GetSourceDisplayName(
        GalaxyItemSourceKnowledge source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (!string.IsNullOrWhiteSpace(source.SourceName))
        {
            return source.SourceName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(source.LocationName))
        {
            return source.LocationName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(source.SectorName))
        {
            return source.SectorName.Trim();
        }

        return GetSingularKind(source.Kind);
    }

    private static IReadOnlyList<GalaxyItemSourceKnowledge>
        GetDistinctSources(GalaxyItemKnowledge item)
    {
        return item.Sources
            .GroupBy(source => string.Join(
                "|",
                (int)source.Kind,
                source.SourceEntityId,
                source.SourceName,
                source.LocationName,
                source.SectorKey))
            .Select(group => group.First())
            .OrderBy(source => source.Kind)
            .ThenBy(
                source => GetSourceDisplayName(source),
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string GetLocationDisplayName(
        GalaxyItemSourceKnowledge source)
    {
        if (!string.IsNullOrWhiteSpace(source.LocationName))
        {
            return source.LocationName.Trim();
        }

        return !string.IsNullOrWhiteSpace(source.SectorName)
            ? source.SectorName.Trim()
            : "";
    }

    private static string GetHeading(GalaxyItemSourceKind kind)
    {
        return kind switch
        {
            GalaxyItemSourceKind.Vendor => "Vendors",
            GalaxyItemSourceKind.MobLoot => "Loot",
            GalaxyItemSourceKind.Harvesting => "Harvesting",
            GalaxyItemSourceKind.MissionReward => "Mission rewards",
            _ => "Sources",
        };
    }

    private static string GetSingularKind(GalaxyItemSourceKind kind)
    {
        return kind switch
        {
            GalaxyItemSourceKind.Vendor => "Vendor",
            GalaxyItemSourceKind.MobLoot => "Mob",
            GalaxyItemSourceKind.Harvesting => "Resource field",
            GalaxyItemSourceKind.MissionReward => "Mission",
            _ => "Source",
        };
    }

    private static string GetPluralKind(GalaxyItemSourceKind kind)
    {
        return kind switch
        {
            GalaxyItemSourceKind.Vendor => "vendors",
            GalaxyItemSourceKind.MobLoot => "mobs",
            GalaxyItemSourceKind.Harvesting => "fields",
            GalaxyItemSourceKind.MissionReward => "missions",
            _ => "sources",
        };
    }

    private static void AppendSimpleSection(
        StringBuilder builder,
        string heading,
        string value)
    {
        if (builder.Length != 0)
        {
            builder.AppendLine().AppendLine();
        }

        builder.Append(heading)
            .AppendLine()
            .Append("• ")
            .Append(value);
    }
}
