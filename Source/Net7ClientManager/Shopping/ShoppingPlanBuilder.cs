namespace Net7ClientManager.Shopping;

using Net7ClientManager.GalaxyKnowledge;

internal static class ShoppingPlanBuilder
{
    public static ShoppingPlanSnapshot Build(
        ShoppingListDocument list,
        GalaxyKnowledgeSnapshot knowledge,
        ShoppingOwnershipSnapshot ownership)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(knowledge);
        ArgumentNullException.ThrowIfNull(ownership);

        var selections = list.RecipeSelections
            .Where(selection =>
                selection.OutputItemTemplateId > 0 &&
                !string.IsNullOrWhiteSpace(
                    selection.RecipeIdentity))
            .GroupBy(selection =>
                selection.OutputItemTemplateId)
            .ToDictionary(
                group => group.Key,
                group => group.Last().RecipeIdentity.Trim(),
                EqualityComparer<int>.Default);
        var requested = list.RequestedOutputs
            .Where(output =>
                output.ItemTemplateId > 0 &&
                output.Quantity > 0)
            .GroupBy(output => output.ItemTemplateId)
            .Select(group =>
                new ShoppingListRequestedOutput
                {
                    ItemTemplateId = group.Key,
                    Quantity = checked(
                        group.Sum(output => output.Quantity)),
                })
            .OrderBy(output => output.ItemTemplateId)
            .ToArray();

        var activeStock = ownership.ItemsByTemplateId.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.AvailableNow);
        var globalStock = ownership.ItemsByTemplateId.ToDictionary(
            pair => pair.Key,
            pair => checked(
                pair.Value.AvailableNow + pair.Value.OwnedElsewhere));

        var active = Run(
            requested,
            knowledge,
            selections,
            activeStock,
            "active");
        var global = Run(
            requested,
            knowledge,
            selections,
            globalStock,
            "global");

        var requestedByItem = requested.ToDictionary(
            output => output.ItemTemplateId,
            output => output.Quantity);
        var itemIds = active.Lines.Keys
            .Concat(global.Lines.Keys)
            .Concat(requestedByItem.Keys)
            .Distinct()
            .OrderBy(itemTemplateId =>
                knowledge.TryGetItem(itemTemplateId, out var item)
                    ? item.Name
                    : itemTemplateId.ToString())
            .ThenBy(itemTemplateId => itemTemplateId)
            .ToArray();

        List<ShoppingPlanLine> lines = [];
        foreach (var itemTemplateId in itemIds)
        {
            active.Lines.TryGetValue(itemTemplateId, out var activeLine);
            global.Lines.TryGetValue(itemTemplateId, out var globalLine);
            knowledge.TryGetItem(itemTemplateId, out var item);
            var itemOwnership = ownership.GetItem(itemTemplateId);
            var selectedRecipe = activeLine?.SelectedRecipe ??
                                 globalLine?.SelectedRecipe ??
                                 ResolveSelectedRecipe(
                                     item,
                                     selections);
            var alternatives = item?.ProducedByRecipes
                .OrderBy(recipe => recipe.Kind)
                .ThenBy(recipe => recipe.Identity, StringComparer.Ordinal)
                .Select(recipe => recipe.Identity)
                .ToArray() ?? [];
            long selectedRecipeOutputQuantity = 0;
            var selectedRecipeOutputQuantityKnown =
                selectedRecipe != null &&
                TryResolveRecipeOutputQuantity(
                    item,
                    out selectedRecipeOutputQuantity);

            lines.Add(new ShoppingPlanLine
            {
                ItemTemplateId = itemTemplateId,
                ItemName = item?.Name ?? $"Unknown item {itemTemplateId}",
                Family = item?.Family ?? GalaxyItemFamily.Other,
                IsRequestedOutput = requestedByItem.ContainsKey(itemTemplateId),
                RequestedQuantity = requestedByItem.GetValueOrDefault(itemTemplateId),
                OwnedAvailableNow = itemOwnership.AvailableNow,
                OwnedElsewhere = itemOwnership.OwnedElsewhere,
                ActiveDemandQuantity = activeLine?.Demand ?? 0,
                ActiveOwnedApplied = activeLine?.OwnedApplied ?? 0,
                ActiveShortfallQuantity = activeLine?.Shortfall ?? 0,
                ActiveProduceQuantity = activeLine?.Produce ?? 0,
                ActiveAcquireQuantity = activeLine?.Acquire ?? 0,
                ActiveUnresolvedRecipeQuantity =
                    activeLine?.UnresolvedRecipe ?? 0,
                GlobalDemandQuantity = globalLine?.Demand ?? 0,
                GlobalOwnedApplied = globalLine?.OwnedApplied ?? 0,
                GlobalShortfallQuantity = globalLine?.Shortfall ?? 0,
                GlobalProduceQuantity = globalLine?.Produce ?? 0,
                GlobalAcquireQuantity = globalLine?.Acquire ?? 0,
                GlobalUnresolvedRecipeQuantity =
                    globalLine?.UnresolvedRecipe ?? 0,
                SelectedRecipeIdentity = selectedRecipe?.Identity ?? "",
                SelectedRecipeKind = selectedRecipe?.Kind,
                SelectedRecipeOutputQuantityKnown =
                    selectedRecipeOutputQuantityKnown,
                SelectedRecipeOutputQuantity =
                    selectedRecipeOutputQuantityKnown
                        ? selectedRecipeOutputQuantity
                        : null,
                AlternativeRecipeIdentities = alternatives,
                HasKnownAcquisition = item != null &&
                    (item.Sources.Count != 0 ||
                     item.ProducedByRecipes.Count != 0 ||
                     item.RefinedFrom.Count != 0),
            });
        }

        var edgeKeys = active.Edges.Keys
            .Concat(global.Edges.Keys)
            .Distinct()
            .OrderBy(key => key.ParentItemTemplateId)
            .ThenBy(key => key.IngredientItemTemplateId)
            .ThenBy(key => key.RecipeIdentity, StringComparer.Ordinal)
            .ToArray();
        List<ShoppingPlanEdge> edges = [];
        foreach (var key in edgeKeys)
        {
            active.Edges.TryGetValue(key, out var activeQuantity);
            global.Edges.TryGetValue(key, out var globalQuantity);
            edges.Add(new ShoppingPlanEdge
            {
                ParentItemTemplateId = key.ParentItemTemplateId,
                IngredientItemTemplateId = key.IngredientItemTemplateId,
                RecipeIdentity = key.RecipeIdentity,
                QuantityPerRecipe = key.QuantityPerRecipe,
                ActiveRequiredQuantity = activeQuantity,
                GlobalRequiredQuantity = globalQuantity,
            });
        }

        var issues = active.Issues
            .Concat(global.Issues)
            .GroupBy(issue => new
            {
                issue.Kind,
                issue.ItemTemplateId,
                issue.RecipeIdentity,
                issue.Message,
                Path = string.Join(",", issue.ItemPath),
            })
            .Select(group => group.First())
            .OrderBy(issue => issue.Kind)
            .ThenBy(issue => issue.ItemTemplateId)
            .ThenBy(issue => issue.RecipeIdentity, StringComparer.Ordinal)
            .ToArray();

        return new ShoppingPlanSnapshot
        {
            ShoppingList = list,
            Ownership = ownership,
            BuiltAtUtc = DateTimeOffset.UtcNow,
            GalaxyKnowledgeIdentity = string.Concat(
                knowledge.Provenance.CdataSha256,
                ":",
                knowledge.Provenance.ForgeRevision,
                ":",
                knowledge.Provenance.RecipeCatalogRevision,
                ":",
                knowledge.Provenance.MissionCatalogRevision),
            Lines = lines,
            Edges = edges,
            Issues = issues,
        };
    }

    private static GalaxyRecipeKnowledge? ResolveSelectedRecipe(
        GalaxyItemKnowledge? item,
        IReadOnlyDictionary<int, string> selections)
    {
        if (item == null || item.ProducedByRecipes.Count == 0)
        {
            return null;
        }

        var recipes = item.ProducedByRecipes
            .OrderBy(recipe => recipe.Kind)
            .ThenBy(recipe => recipe.Identity, StringComparer.Ordinal)
            .ToArray();
        if (selections.TryGetValue(
                item.ItemTemplateId,
                out var selectedIdentity))
        {
            var selected = recipes.FirstOrDefault(recipe =>
                string.Equals(
                    recipe.Identity,
                    selectedIdentity,
                    StringComparison.Ordinal));
            if (selected != null)
            {
                return selected;
            }
        }

        return recipes[0];
    }

    private static PlanRun Run(
        IReadOnlyList<ShoppingListRequestedOutput> requested,
        GalaxyKnowledgeSnapshot knowledge,
        IReadOnlyDictionary<int, string> selections,
        Dictionary<int, long> stock,
        string runName)
    {
        PlanRun run = new();
        foreach (var output in requested)
        {
            var requestedDemand = ResolveRequestedDemandQuantity(
                output,
                knowledge,
                run);
            Expand(
                output.ItemTemplateId,
                requestedDemand,
                knowledge,
                selections,
                stock,
                run,
                [],
                runName,
                false);
        }

        return run;
    }

    private static void Expand(
        int itemTemplateId,
        long quantity,
        GalaxyKnowledgeSnapshot knowledge,
        IReadOnlyDictionary<int, string> selections,
        Dictionary<int, long> stock,
        PlanRun run,
        IReadOnlyList<int> path,
        string runName,
        bool consumeStock)
    {
        if (quantity <= 0)
        {
            return;
        }

        var line = run.GetLine(itemTemplateId);
        line.Demand = CheckedAdd(
            line.Demand,
            quantity,
            itemTemplateId,
            run,
            runName);
        var applied = 0L;
        if (consumeStock)
        {
            var owned = stock.GetValueOrDefault(itemTemplateId);
            applied = Math.Min(owned, quantity);
            if (applied > 0)
            {
                stock[itemTemplateId] = owned - applied;
                line.OwnedApplied = CheckedAdd(
                    line.OwnedApplied,
                    applied,
                    itemTemplateId,
                    run,
                    runName);
            }
        }

        var shortfall = quantity - applied;
        line.Shortfall = CheckedAdd(
            line.Shortfall,
            shortfall,
            itemTemplateId,
            run,
            runName);
        if (shortfall == 0)
        {
            return;
        }

        if (!knowledge.TryGetItem(itemTemplateId, out var item))
        {
            line.Acquire = CheckedAdd(
                line.Acquire,
                shortfall,
                itemTemplateId,
                run,
                runName);
            run.Issues.Add(new ShoppingPlanIssue
            {
                Kind = ShoppingPlanIssueKind.UnknownItem,
                ItemTemplateId = itemTemplateId,
                Message = $"Item template {itemTemplateId} is not present in the current Galaxy knowledge snapshot.",
                ItemPath = path.Append(itemTemplateId).ToArray(),
            });
            return;
        }

        var recipe = SelectRecipe(
            item,
            selections,
            run,
            path);
        if (recipe == null)
        {
            line.Acquire = CheckedAdd(
                line.Acquire,
                shortfall,
                itemTemplateId,
                run,
                runName);
            return;
        }

        if (path.Contains(itemTemplateId))
        {
            line.Acquire = CheckedAdd(
                line.Acquire,
                shortfall,
                itemTemplateId,
                run,
                runName);
            run.Issues.Add(new ShoppingPlanIssue
            {
                Kind = ShoppingPlanIssueKind.RecipeCycle,
                ItemTemplateId = itemTemplateId,
                RecipeIdentity = recipe.Identity,
                Message = $"Recipe expansion for {item.Name} contains a cycle.",
                ItemPath = path.Append(itemTemplateId).ToArray(),
            });
            return;
        }

        line.SelectedRecipe = recipe;
        var nextPath = path.Append(itemTemplateId).ToArray();

        if (!TryResolveRecipeOutputQuantity(
                item,
                out var recipeOutputQuantity))
        {
            line.UnresolvedRecipe = CheckedAdd(
                line.UnresolvedRecipe,
                shortfall,
                itemTemplateId,
                run,
                runName);
            run.Issues.Add(new ShoppingPlanIssue
            {
                Kind =
                    ShoppingPlanIssueKind.UnknownRecipeOutputQuantity,
                ItemTemplateId = itemTemplateId,
                RecipeIdentity = recipe.Identity,
                Message =
                    $"The current Forge recipe for {item.Name} does not report how many units one production run creates.",
                ItemPath = nextPath,
            });
            return;
        }

        var recipeRuns = DivideRoundUp(
            shortfall,
            recipeOutputQuantity);
        var producedQuantity = CheckedMultiply(
            recipeRuns,
            recipeOutputQuantity,
            itemTemplateId,
            recipe.Identity,
            run,
            nextPath);
        line.Produce = CheckedAdd(
            line.Produce,
            producedQuantity,
            itemTemplateId,
            run,
            runName);

        foreach (var ingredient in recipe.Ingredients
                     .OrderBy(ingredient => ingredient.ItemTemplateId))
        {
            if (ingredient.Quantity <= 0)
            {
                run.Issues.Add(new ShoppingPlanIssue
                {
                    Kind = ShoppingPlanIssueKind.InvalidIngredientQuantity,
                    ItemTemplateId = ingredient.ItemTemplateId,
                    RecipeIdentity = recipe.Identity,
                    Message = $"Recipe {recipe.Identity} contains a non-positive ingredient quantity.",
                    ItemPath = nextPath,
                });
                continue;
            }

            var required = CheckedMultiply(
                recipeRuns,
                ingredient.Quantity,
                ingredient.ItemTemplateId,
                recipe.Identity,
                run,
                nextPath);
            if (required == long.MaxValue)
            {
                continue;
            }

            var key = new EdgeKey(
                itemTemplateId,
                ingredient.ItemTemplateId,
                recipe.Identity,
                ingredient.Quantity);
            run.Edges[key] = CheckedAdd(
                run.Edges.GetValueOrDefault(key),
                required,
                ingredient.ItemTemplateId,
                run,
                runName);
            Expand(
                ingredient.ItemTemplateId,
                required,
                knowledge,
                selections,
                stock,
                run,
                nextPath,
                runName,
                consumeStock: true);
        }
    }

    private static GalaxyRecipeKnowledge? SelectRecipe(
        GalaxyItemKnowledge item,
        IReadOnlyDictionary<int, string> selections,
        PlanRun run,
        IReadOnlyList<int> path)
    {
        var recipes = item.ProducedByRecipes
            .OrderBy(recipe => recipe.Kind)
            .ThenBy(recipe => recipe.Identity, StringComparer.Ordinal)
            .ToArray();
        if (recipes.Length == 0)
        {
            return null;
        }

        if (!selections.TryGetValue(
                item.ItemTemplateId,
                out var selectedIdentity))
        {
            return recipes[0];
        }

        var selected = recipes.FirstOrDefault(recipe =>
            string.Equals(
                recipe.Identity,
                selectedIdentity,
                StringComparison.Ordinal));
        if (selected != null)
        {
            return selected;
        }

        run.Issues.Add(new ShoppingPlanIssue
        {
            Kind = ShoppingPlanIssueKind.InvalidRecipeSelection,
            ItemTemplateId = item.ItemTemplateId,
            RecipeIdentity = selectedIdentity,
            Message = $"The selected recipe for {item.Name} is no longer available; the deterministic default was used.",
            ItemPath = path.Append(item.ItemTemplateId).ToArray(),
        });
        return recipes[0];
    }

    private static bool TryResolveRecipeOutputQuantity(
        GalaxyItemKnowledge? item,
        out long outputQuantity)
    {
        outputQuantity = 0;

        if (item == null)
        {
            return false;
        }

        // Runtime manufacturing proof established that one ammunition run
        // creates one complete stack. CDATA's maximum stack is therefore
        // the authoritative output quantity for ammo recipes.
        if (item.Family == GalaxyItemFamily.Ammo)
        {
            if (item.MaximumStack == 0)
            {
                return false;
            }

            outputQuantity = item.MaximumStack;
            return true;
        }

        outputQuantity = 1;
        return true;
    }

    private static long ResolveRequestedDemandQuantity(
        ShoppingListRequestedOutput output,
        GalaxyKnowledgeSnapshot knowledge,
        PlanRun run)
    {
        if (!knowledge.TryGetItem(
                output.ItemTemplateId,
                out var item) ||
            item.Family != GalaxyItemFamily.Ammo ||
            item.MaximumStack == 0)
        {
            return output.Quantity;
        }

        return CheckedMultiply(
            output.Quantity,
            item.MaximumStack,
            output.ItemTemplateId,
            "",
            run,
            [output.ItemTemplateId]);
    }

    private static long DivideRoundUp(
        long quantity,
        long divisor)
    {
        return checked(
            (quantity / divisor) +
            (quantity % divisor == 0 ? 0 : 1));
    }

    private static long CheckedMultiply(
        long left,
        long right,
        int itemTemplateId,
        string recipeIdentity,
        PlanRun run,
        IReadOnlyList<int> path)
    {
        try
        {
            return checked(left * right);
        }
        catch (OverflowException)
        {
            run.Issues.Add(new ShoppingPlanIssue
            {
                Kind = ShoppingPlanIssueKind.QuantityOverflow,
                ItemTemplateId = itemTemplateId,
                RecipeIdentity = recipeIdentity,
                Message =
                    $"Recipe {recipeIdentity} exceeds the supported shopping-list quantity range.",
                ItemPath = path,
            });
            return long.MaxValue;
        }
    }

    private static long CheckedAdd(
        long left,
        long right,
        int itemTemplateId,
        PlanRun run,
        string runName)
    {
        try
        {
            return checked(left + right);
        }
        catch (OverflowException)
        {
            run.Issues.Add(new ShoppingPlanIssue
            {
                Kind = ShoppingPlanIssueKind.QuantityOverflow,
                ItemTemplateId = itemTemplateId,
                Message = $"The {runName} shopping plan exceeds the supported quantity range.",
            });
            return long.MaxValue;
        }
    }

    private sealed class PlanRun
    {
        public Dictionary<int, MutableLine> Lines { get; } = [];

        public Dictionary<EdgeKey, long> Edges { get; } = [];

        public List<ShoppingPlanIssue> Issues { get; } = [];

        public MutableLine GetLine(int itemTemplateId)
        {
            if (!this.Lines.TryGetValue(itemTemplateId, out var line))
            {
                line = new MutableLine();
                this.Lines.Add(itemTemplateId, line);
            }

            return line;
        }
    }

    private sealed class MutableLine
    {
        public long Demand { get; set; }

        public long OwnedApplied { get; set; }

        public long Shortfall { get; set; }

        public long Produce { get; set; }

        public long Acquire { get; set; }

        public long UnresolvedRecipe { get; set; }

        public GalaxyRecipeKnowledge? SelectedRecipe { get; set; }
    }

    private readonly record struct EdgeKey(
        int ParentItemTemplateId,
        int IngredientItemTemplateId,
        string RecipeIdentity,
        long QuantityPerRecipe);
}
