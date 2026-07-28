namespace Net7ClientManager.GalaxyKnowledge;

using System.Globalization;
using System.Text;
using Net7ClientManager.Services;

[Flags]
internal enum GalaxyFinderSourceFilter
{
    None = 0,
    Vendor = 1,
    Loot = 2,
    Crafted = 4,
    Refined = 8,
    Harvested = 16,
    Mission = 32,
}

internal enum GalaxyFinderItemCategory
{
    All = 0,
    Equipment = 1,
    Weapon = 2,
    Engine = 3,
    Shield = 4,
    Reactor = 5,
    Device = 6,
    Ammo = 7,
    Component = 8,
    RawResource = 9,
    RefinedMaterial = 10,
    TradeGood = 11,
    Other = 12,
    MobLoot = 13,
}

internal sealed record GalaxyFinderItemMatch(
    GalaxyItemKnowledge Item,
    int Rank,
    GalaxyFinderSourceFilter Sources,
    string SourceSummary,
    string EffectSummary,
    string MatchSummary);

internal sealed record GalaxyFinderEffectChoice(
    string Identity,
    string DisplayName,
    IReadOnlyList<string>? SourceIdentities = null)
{
    public bool MatchesIdentity(string? identity)
    {
        if (string.IsNullOrWhiteSpace(identity))
        {
            return this.Identity.Length == 0;
        }

        var candidate = identity.Trim();
        return string.Equals(
                   this.Identity,
                   candidate,
                   StringComparison.Ordinal) ||
               (this.SourceIdentities?.Contains(
                    candidate,
                    StringComparer.Ordinal) ?? false);
    }

    public override string ToString()
    {
        return this.DisplayName;
    }
}

internal sealed class GalaxyFinderItemSearchIndex
{
    private readonly IReadOnlyList<IndexedItem> items;

    public GalaxyFinderItemSearchIndex(
        GalaxyKnowledgeSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        this.items = snapshot.ItemsByTemplateId.Values
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.ItemTemplateId)
            .Select(item => CreateIndexedItem(snapshot, item))
            .ToArray();
    }

    public int Count => this.items.Count;

    public IReadOnlyList<GalaxyFinderItemMatch> Search(
        string? query,
        GalaxyFinderItemCategory category,
        GalaxyFinderSourceFilter sourceFilters,
        string? effectIdentity,
        int maximumResults)
    {
        if (maximumResults <= 0)
        {
            return [];
        }

        var normalizedQuery = Normalize(query);
        var browseWithoutText = string.IsNullOrWhiteSpace(query);
        var normalizedEffectIdentity =
            string.IsNullOrWhiteSpace(effectIdentity)
                ? ""
                : effectIdentity.Trim();
        List<IndexedItemMatch> candidates = [];

        foreach (var item in this.items)
        {
            if (!MatchesCategory(item.Item, category) ||
                !MatchesSources(item.Sources, sourceFilters) ||
                (normalizedEffectIdentity.Length != 0 &&
                 !item.EffectIdentities.Contains(
                     normalizedEffectIdentity)))
            {
                continue;
            }

            var score = FindMatch(
                item,
                normalizedQuery,
                browseWithoutText);
            if (score == null)
            {
                continue;
            }

            candidates.Add(
                new IndexedItemMatch(
                    item,
                    score.Rank,
                    score.MatchSummary));
        }

        return
        [
            .. candidates
                .OrderBy(candidate => candidate.Rank)
                .ThenBy(
                    candidate => candidate.Item.Item.Name,
                    StringComparer.OrdinalIgnoreCase)
                .ThenBy(candidate =>
                    candidate.Item.Item.ItemTemplateId)
                .Take(maximumResults)
                .Select(candidate => new GalaxyFinderItemMatch(
                    candidate.Item.Item,
                    candidate.Rank,
                    candidate.Item.Sources,
                    candidate.Item.SourceSummary,
                    candidate.Item.EffectSummary,
                    candidate.MatchSummary)),
        ];
    }

    public static IReadOnlyList<GalaxyFinderEffectChoice>
        BuildEffectChoices(
            GalaxyKnowledgeSnapshot snapshot,
            GalaxyFinderItemCategory category)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return
        [
            .. snapshot.EffectsByIdentity.Values
                .Where(effect => effect.Providers.Any(provider =>
                    snapshot.ItemsByTemplateId.TryGetValue(
                        provider.ItemTemplateId,
                        out var item) &&
                    MatchesCategory(item, category)))
                .Select(effect => new EffectChoiceCandidate(
                    effect.Identity,
                    BuildEffectFilterIdentity(effect),
                    GetEffectDisplayName(effect)))
                .GroupBy(
                    candidate => candidate.FilterIdentity,
                    StringComparer.Ordinal)
                .Select(group => new GalaxyFinderEffectChoice(
                    group.Key,
                    group
                        .Select(candidate => candidate.DisplayName)
                        .OrderBy(
                            displayName => displayName,
                            StringComparer.OrdinalIgnoreCase)
                        .ThenBy(
                            displayName => displayName,
                            StringComparer.Ordinal)
                        .First(),
                    group
                        .Select(candidate => candidate.SourceIdentity)
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(
                            identity => identity,
                            StringComparer.Ordinal)
                        .ToArray()))
                .OrderBy(
                    choice => choice.DisplayName,
                    StringComparer.OrdinalIgnoreCase)
                .ThenBy(
                    choice => choice.Identity,
                    StringComparer.Ordinal),
        ];
    }

    public static bool MatchesCategory(
        GalaxyItemKnowledge item,
        GalaxyFinderItemCategory category)
    {
        ArgumentNullException.ThrowIfNull(item);

        return category switch
        {
            GalaxyFinderItemCategory.All => true,
            GalaxyFinderItemCategory.Equipment =>
                IsEquipment(item.Family),
            GalaxyFinderItemCategory.Weapon =>
                item.Family == GalaxyItemFamily.Weapon,
            GalaxyFinderItemCategory.Engine =>
                item.Family == GalaxyItemFamily.Engine,
            GalaxyFinderItemCategory.Shield =>
                item.Family == GalaxyItemFamily.Shield,
            GalaxyFinderItemCategory.Reactor =>
                item.Family == GalaxyItemFamily.Reactor,
            GalaxyFinderItemCategory.Device =>
                item.Family == GalaxyItemFamily.Device,
            GalaxyFinderItemCategory.Ammo =>
                item.Family == GalaxyItemFamily.Ammo,
            GalaxyFinderItemCategory.Component =>
                item.Family == GalaxyItemFamily.Component,
            GalaxyFinderItemCategory.RawResource =>
                item.Family == GalaxyItemFamily.RawResource,
            GalaxyFinderItemCategory.RefinedMaterial =>
                item.Family == GalaxyItemFamily.RefinedMaterial,
            GalaxyFinderItemCategory.TradeGood =>
                item.Family == GalaxyItemFamily.TradeGood,
            GalaxyFinderItemCategory.Other =>
                item.Family == GalaxyItemFamily.Other,
            GalaxyFinderItemCategory.MobLoot =>
                item.Sources.Any(source =>
                    source.Kind == GalaxyItemSourceKind.MobLoot),
            _ => false,
        };
    }

    public static bool IsEquipment(GalaxyItemFamily family)
    {
        return family is
            GalaxyItemFamily.Weapon or
            GalaxyItemFamily.Engine or
            GalaxyItemFamily.Shield or
            GalaxyItemFamily.Reactor or
            GalaxyItemFamily.Device;
    }

    public static string GetFamilyDisplayName(
        GalaxyItemFamily family)
    {
        return family switch
        {
            GalaxyItemFamily.Weapon => "Weapon",
            GalaxyItemFamily.Ammo => "Ammo",
            GalaxyItemFamily.Device => "Device",
            GalaxyItemFamily.Engine => "Engine",
            GalaxyItemFamily.Shield => "Shield",
            GalaxyItemFamily.Reactor => "Reactor",
            GalaxyItemFamily.Component => "Component",
            GalaxyItemFamily.RawResource => "Raw resource",
            GalaxyItemFamily.RefinedMaterial => "Refined material",
            GalaxyItemFamily.TradeGood => "Trade good",
            _ => "Other",
        };
    }

    private static IndexedItem CreateIndexedItem(
        GalaxyKnowledgeSnapshot snapshot,
        GalaxyItemKnowledge item)
    {
        var sourceFilters = GetSourceFilters(item);
        var effectBlocks = item.Effects
            .OrderBy(effect => effect.Trigger)
            .ThenBy(effect => effect.Index)
            .Select(BuildItemEffectDisplayBlock)
            .Where(block => block.Length != 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var effectIdentities = item.Effects
            .SelectMany(effect => GetEffectFilterIdentities(
                snapshot,
                effect))
            .ToHashSet(StringComparer.Ordinal);
        List<IndexedSearchTerm> terms = [];

        foreach (var effect in item.Effects)
        {
            var resolved = ItemEffectTextFormatter.Resolve(
                effect.NameFormat,
                effect.NameValues,
                effect.DescriptionFormat,
                effect.DescriptionValues);
            var effectBlock = BuildItemEffectDisplayBlock(
                effect,
                resolved);
            AddSearchTerm(
                terms,
                resolved.Name,
                effectBlock,
                priority: 0);
            AddSearchTerm(
                terms,
                effect.Identity,
                effectBlock,
                priority: 0);
            AddSearchTerm(
                terms,
                resolved.Description,
                effectBlock,
                priority: 0);
        }

        foreach (var source in item.Sources)
        {
            var sourceSummary = BuildSourceMatchSummary(source);
            AddSearchTerm(
                terms,
                source.SourceName,
                sourceSummary,
                priority: 10);
            AddSearchTerm(
                terms,
                source.LocationName,
                sourceSummary,
                priority: 10);
            AddSearchTerm(
                terms,
                source.SectorName,
                sourceSummary,
                priority: 10);
            AddSearchTerm(
                terms,
                source.Summary,
                sourceSummary,
                priority: 10);

            foreach (var location in source.Locations)
            {
                AddSearchTerm(
                    terms,
                    location.LocationName,
                    sourceSummary,
                    priority: 10);
                AddSearchTerm(
                    terms,
                    location.SectorName,
                    sourceSummary,
                    priority: 10);
            }
        }

        foreach (var recipe in item.ProducedByRecipes)
        {
            AddSearchTerm(
                terms,
                recipe.Identity,
                recipe.Kind == GalaxyRecipeKind.Refine
                    ? "Refining recipe"
                    : "Manufacturing recipe",
                priority: 20);
        }

        foreach (var recipe in item.UsedByRecipes)
        {
            var recipeSummary = "Recipe ingredient";
            if (snapshot.ItemsByTemplateId.TryGetValue(
                    recipe.OutputItemTemplateId,
                    out var output))
            {
                recipeSummary = BuildMatchSummary(
                    "Used to make",
                    output.Name);
                AddSearchTerm(
                    terms,
                    output.Name,
                    recipeSummary,
                    priority: 20);
            }

            AddSearchTerm(
                terms,
                recipe.Identity,
                recipeSummary,
                priority: 20);
        }

        foreach (var outgoingRelationship in item.RefinesTo)
        {
            if (snapshot.ItemsByTemplateId.TryGetValue(
                    outgoingRelationship.OutputItemTemplateId,
                    out var output))
            {
                AddSearchTerm(
                    terms,
                    output.Name,
                    BuildMatchSummary(
                        "Refines to",
                        output.Name),
                    priority: 20);
            }
        }

        foreach (var incomingRelationship in item.RefinedFrom)
        {
            if (snapshot.ItemsByTemplateId.TryGetValue(
                    incomingRelationship.InputItemTemplateId,
                    out var input))
            {
                AddSearchTerm(
                    terms,
                    input.Name,
                    BuildMatchSummary(
                        "Refined from",
                        input.Name),
                    priority: 20);
            }
        }

        AddSearchTerm(
            terms,
            item.Manufacturer,
            "",
            priority: 30);
        AddSearchTerm(
            terms,
            item.TypeDisplayName,
            "",
            priority: 35);

        var familyName = GetFamilyDisplayName(item.Family);
        AddSearchTerm(
            terms,
            familyName,
            "",
            priority: 35);

        foreach (var additionalText in item.AdditionalText)
        {
            AddSearchTerm(
                terms,
                additionalText,
                CollapseWhitespace(additionalText),
                priority: 60);
        }

        AddSearchTerm(
            terms,
            item.Description,
            CollapseWhitespace(item.Description),
            priority: 70);

        return new IndexedItem(
            item,
            Normalize(item.Name),
            Normalize(item.Manufacturer),
            terms
                .DistinctBy(term => new
                {
                    term.NormalizedValue,
                    term.MatchSummary,
                    term.Priority,
                })
                .ToArray(),
            effectIdentities,
            sourceFilters,
            BuildSourceSummary(item),
            BuildEffectSummary(effectBlocks));
    }

    private static void AddSearchTerm(
        ICollection<IndexedSearchTerm> terms,
        string? value,
        string matchSummary,
        int priority)
    {
        var normalizedValue = Normalize(value);
        if (normalizedValue.Length == 0)
        {
            return;
        }

        terms.Add(
            new IndexedSearchTerm(
                normalizedValue,
                matchSummary.Trim(),
                priority));
    }

    private static string BuildMatchSummary(
        string label,
        string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? label
            : $"{label}: {value.Trim()}";
    }

    private static string BuildSourceMatchSummary(
        GalaxyItemSourceKnowledge source)
    {
        var label = source.Kind switch
        {
            GalaxyItemSourceKind.Vendor => "Vendor",
            GalaxyItemSourceKind.MobLoot => "Loot source",
            GalaxyItemSourceKind.Harvesting =>
                "Harvest location",
            GalaxyItemSourceKind.MissionReward =>
                "Mission reward",
            _ => "Known source",
        };

        if (!string.IsNullOrWhiteSpace(source.SourceName))
        {
            return BuildMatchSummary(label, source.SourceName);
        }

        if (!string.IsNullOrWhiteSpace(source.LocationName))
        {
            return BuildMatchSummary(label, source.LocationName);
        }

        return BuildMatchSummary(label, source.SectorName);
    }

    private static GalaxyFinderSourceFilter GetSourceFilters(
        GalaxyItemKnowledge item)
    {
        var filters = GalaxyFinderSourceFilter.None;

        foreach (var source in item.Sources)
        {
            filters |= source.Kind switch
            {
                GalaxyItemSourceKind.Vendor =>
                    GalaxyFinderSourceFilter.Vendor,
                GalaxyItemSourceKind.MobLoot =>
                    GalaxyFinderSourceFilter.Loot,
                GalaxyItemSourceKind.Harvesting =>
                    GalaxyFinderSourceFilter.Harvested,
                GalaxyItemSourceKind.MissionReward =>
                    GalaxyFinderSourceFilter.Mission,
                _ => GalaxyFinderSourceFilter.None,
            };
        }

        if (item.ProducedByRecipes.Any(recipe =>
                recipe.Kind == GalaxyRecipeKind.Manufacture))
        {
            filters |= GalaxyFinderSourceFilter.Crafted;
        }

        if (item.RefinedFrom.Count != 0 ||
            item.ProducedByRecipes.Any(recipe =>
                recipe.Kind == GalaxyRecipeKind.Refine))
        {
            filters |= GalaxyFinderSourceFilter.Refined;
        }

        return filters;
    }

    private static bool MatchesSources(
        GalaxyFinderSourceFilter itemSources,
        GalaxyFinderSourceFilter requestedSources)
    {
        return requestedSources == GalaxyFinderSourceFilter.None ||
               (itemSources & requestedSources) != 0;
    }

    private static ItemSearchScore? FindMatch(
        IndexedItem item,
        string normalizedQuery,
        bool browseWithoutText)
    {
        if (normalizedQuery.Length == 0)
        {
            return browseWithoutText
                ? new ItemSearchScore(500, "")
                : null;
        }

        if (string.Equals(
                item.NormalizedName,
                normalizedQuery,
                StringComparison.Ordinal))
        {
            return new ItemSearchScore(0, "");
        }

        if (item.NormalizedName.StartsWith(
                normalizedQuery,
                StringComparison.Ordinal))
        {
            return new ItemSearchScore(10, "");
        }

        if (item.NormalizedName.Contains(
                normalizedQuery,
                StringComparison.Ordinal))
        {
            return new ItemSearchScore(25, "");
        }

        var queryWords = normalizedQuery.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries |
            StringSplitOptions.TrimEntries);

        if (queryWords.Length > 1 &&
            queryWords.All(word =>
                item.NormalizedName.Contains(
                    word,
                    StringComparison.Ordinal)))
        {
            return new ItemSearchScore(35, "");
        }

        if (item.NormalizedManufacturer.StartsWith(
                normalizedQuery,
                StringComparison.Ordinal))
        {
            return new ItemSearchScore(55, "");
        }

        var matchingTerm = FindBestMatchingTerm(
            item.Terms,
            normalizedQuery,
            startsWith: true);
        if (matchingTerm != null)
        {
            return new ItemSearchScore(
                70 + matchingTerm.Priority,
                matchingTerm.MatchSummary);
        }

        matchingTerm = FindBestMatchingTerm(
            item.Terms,
            normalizedQuery,
            startsWith: false);
        if (matchingTerm != null)
        {
            return new ItemSearchScore(
                100 + matchingTerm.Priority,
                matchingTerm.MatchSummary);
        }

        if (queryWords.Length <= 1)
        {
            return null;
        }

        var wordMatches = queryWords
            .Select(word => FindBestMatchingTerm(
                item.Terms,
                word,
                startsWith: false))
            .ToArray();
        if (wordMatches.Any(match => match == null))
        {
            return null;
        }

        var summaries = wordMatches
            .Select(match => match!.MatchSummary)
            .Where(summary => summary.Length != 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ItemSearchScore(
            125,
            BuildMultipleFieldSummary(summaries));
    }

    private static IndexedSearchTerm? FindBestMatchingTerm(
        IEnumerable<IndexedSearchTerm> terms,
        string normalizedQuery,
        bool startsWith)
    {
        return terms
            .Where(term => startsWith
                ? term.NormalizedValue.StartsWith(
                    normalizedQuery,
                    StringComparison.Ordinal)
                : term.NormalizedValue.Contains(
                    normalizedQuery,
                    StringComparison.Ordinal))
            .OrderBy(term => term.Priority)
            .ThenBy(term => term.NormalizedValue.Length)
            .FirstOrDefault();
    }

    private static string BuildMultipleFieldSummary(
        IReadOnlyList<string> summaries)
    {
        if (summaries.Count == 0)
        {
            return "";
        }

        if (summaries.Count == 1)
        {
            return summaries[0];
        }

        if (summaries.Count == 2)
        {
            return string.Join(" · ", summaries);
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{summaries[0]} · {summaries[1]} · +{summaries.Count - 2}");
    }

    private static string BuildSourceSummary(
        GalaxyItemKnowledge item)
    {
        ArgumentNullException.ThrowIfNull(item);
        List<string> labels = [];

        AddCountedSourceLabel(
            labels,
            CountDistinctSources(
                item,
                GalaxyItemSourceKind.Vendor),
            "Vendor",
            "vendors");
        AddCountedSourceLabel(
            labels,
            CountDistinctSources(
                item,
                GalaxyItemSourceKind.MobLoot),
            "Loot",
            "mobs");
        AddSourceLabel(
            labels,
            item.ProducedByRecipes.Any(recipe =>
                recipe.Kind == GalaxyRecipeKind.Manufacture),
            "Crafted");
        AddSourceLabel(
            labels,
            item.RefinedFrom.Count != 0 ||
            item.ProducedByRecipes.Any(recipe =>
                recipe.Kind == GalaxyRecipeKind.Refine),
            "Refined");
        AddCountedSourceLabel(
            labels,
            CountDistinctSources(
                item,
                GalaxyItemSourceKind.Harvesting),
            "Harvested",
            "fields");
        AddCountedSourceLabel(
            labels,
            CountDistinctSources(
                item,
                GalaxyItemSourceKind.MissionReward),
            "Mission",
            "missions");

        return string.Join(" · ", labels);
    }

    private static void AddSourceLabel(
        ICollection<string> labels,
        bool isPresent,
        string label)
    {
        if (isPresent)
        {
            labels.Add(label);
        }
    }

    private static int CountDistinctSources(
        GalaxyItemKnowledge item,
        GalaxyItemSourceKind kind)
    {
        return item.Sources
            .Where(source => source.Kind == kind)
            .Select(source =>
                !string.IsNullOrWhiteSpace(source.SourceEntityId)
                    ? source.SourceEntityId
                    : !string.IsNullOrWhiteSpace(source.RelationshipId)
                        ? source.RelationshipId
                        : string.Join(
                            "|",
                            source.SourceName,
                            source.LocationName,
                            source.SectorKey))
            .Distinct(StringComparer.Ordinal)
            .Count();
    }

    private static void AddCountedSourceLabel(
        ICollection<string> labels,
        int count,
        string singularLabel,
        string pluralLabel)
    {
        if (count <= 0)
        {
            return;
        }

        labels.Add(count == 1
            ? singularLabel
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{count:N0} {pluralLabel}"));
    }

    private static string BuildEffectSummary(
        IReadOnlyList<string> effectBlocks)
    {
        return effectBlocks.Count == 0
            ? ""
            : string.Join(
                string.Concat(
                    Environment.NewLine,
                    Environment.NewLine),
                effectBlocks);
    }

    private static IEnumerable<string> GetEffectFilterIdentities(
        GalaxyKnowledgeSnapshot snapshot,
        GalaxyItemEffectKnowledge effect)
    {
        if (!string.IsNullOrWhiteSpace(effect.Identity))
        {
            yield return effect.Identity.Trim();
        }

        if (snapshot.EffectsByIdentity.TryGetValue(
                effect.Identity,
                out var aggregate))
        {
            yield return BuildEffectFilterIdentity(aggregate);
        }
    }

    private static string BuildEffectFilterIdentity(
        GalaxyEffectKnowledge effect)
    {
        var displayName = CollapseWhitespace(
            GetEffectDisplayName(effect));
        return string.Concat(
            "display:",
            displayName.ToUpperInvariant());
    }

    private static string GetEffectDisplayName(
        GalaxyEffectKnowledge effect)
    {
        var name = effect.Formats
            .Select(format => CleanEffectName(format.NameFormat))
            .FirstOrDefault(candidate => candidate.Length != 0) ??
            effect.Identity;
        var triggers = effect.Providers
            .Select(provider => provider.Trigger)
            .Distinct()
            .OrderBy(trigger => trigger)
            .ToArray();
        var triggerText = triggers.Length switch
        {
            1 when triggers[0] == GalaxyItemEffectTrigger.Activated =>
                "Activated",
            1 => "Equipped",
            > 1 => "Activated & equipped",
            _ => "Effect",
        };

        return $"{name} · {triggerText}";
    }

    private static string BuildItemEffectDisplayBlock(
        GalaxyItemEffectKnowledge effect)
    {
        var resolved = ItemEffectTextFormatter.Resolve(
            effect.NameFormat,
            effect.NameValues,
            effect.DescriptionFormat,
            effect.DescriptionValues);
        return BuildItemEffectDisplayBlock(
            effect,
            resolved);
    }

    private static string BuildItemEffectDisplayBlock(
        GalaxyItemEffectKnowledge effect,
        ItemEffectDisplayText resolved)
    {
        var name = CleanEffectName(resolved.Name);
        var resolvedName = name.Length == 0
            ? effect.Identity
            : name;
        var trigger = effect.Trigger == GalaxyItemEffectTrigger.Activated
            ? "Activated"
            : "Equipped";
        var heading = string.IsNullOrWhiteSpace(resolvedName)
            ? trigger
            : $"{resolvedName} ({trigger})";
        var description =
            ItemEffectTextFormatter.Normalize(resolved.Description);

        if (description.Length == 0 ||
            string.Equals(
                description,
                resolvedName,
                StringComparison.OrdinalIgnoreCase))
        {
            return heading;
        }

        return string.Concat(
            heading,
            Environment.NewLine,
            description);
    }

    private static string CleanEffectName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var result = value.Trim();
        result = result.Replace("(Activated)", "", StringComparison.OrdinalIgnoreCase);
        result = result.Replace("(Equip)", "", StringComparison.OrdinalIgnoreCase);
        result = result.Replace("(Equipped)", "", StringComparison.OrdinalIgnoreCase);
        return result.Trim();
    }

    private static string CollapseWhitespace(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var builder = new StringBuilder(value.Length);
        var previousWasWhitespace = false;

        foreach (var character in value.Trim())
        {
            if (char.IsWhiteSpace(character))
            {
                if (!previousWasWhitespace)
                {
                    builder.Append(' ');
                    previousWasWhitespace = true;
                }

                continue;
            }

            builder.Append(character);
            previousWasWhitespace = false;
        }

        return builder.ToString();
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var builder = new StringBuilder(value.Length);
        var previousWasSeparator = false;

        foreach (var character in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                previousWasSeparator = false;
            }
            else if (!previousWasSeparator)
            {
                builder.Append(' ');
                previousWasSeparator = true;
            }
        }

        return builder.ToString().Trim();
    }

    private sealed record EffectChoiceCandidate(
        string SourceIdentity,
        string FilterIdentity,
        string DisplayName);

    private sealed record IndexedItem(
        GalaxyItemKnowledge Item,
        string NormalizedName,
        string NormalizedManufacturer,
        IReadOnlyList<IndexedSearchTerm> Terms,
        IReadOnlySet<string> EffectIdentities,
        GalaxyFinderSourceFilter Sources,
        string SourceSummary,
        string EffectSummary);

    private sealed record IndexedSearchTerm(
        string NormalizedValue,
        string MatchSummary,
        int Priority);

    private sealed record ItemSearchScore(
        int Rank,
        string MatchSummary);

    private sealed record IndexedItemMatch(
        IndexedItem Item,
        int Rank,
        string MatchSummary);
}
