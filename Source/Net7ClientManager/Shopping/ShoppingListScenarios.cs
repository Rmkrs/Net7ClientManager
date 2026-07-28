#if DEBUG
namespace Net7ClientManager.Shopping;

using System.Collections.ObjectModel;
using Net7ClientManager.GalaxyKnowledge;

internal static class ShoppingListScenarios
{
    public static void Validate()
    {
        ValidateStore();
        ValidateRecursivePlan();
        ValidateSharedIngredientAggregation();
        ValidateAlternativeRecipeAndCycle();
        ValidateAmmoStackOutputQuantity();
    }

    private static void ValidateStore()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            string.Concat(
                "n7cm-shopping-store-",
                Guid.NewGuid().ToString("N")));
        var databasePath = Path.Combine(directory, "shopping-lists.db");

        try
        {
            var store = new ShoppingListStore(databasePath);
            store.Initialize();
            store.Initialize();
            var list = store.Create("Scenario list");
            var saved = store.Save(list with
            {
                RequestedOutputs =
                [
                    new ShoppingListRequestedOutput
                    {
                        ItemTemplateId = 100,
                        Quantity = 3,
                    },
                    new ShoppingListRequestedOutput
                    {
                        ItemTemplateId = 100,
                        Quantity = 2,
                    },
                ],
                RecipeSelections =
                [
                    new ShoppingListRecipeSelection
                    {
                        OutputItemTemplateId = 100,
                        RecipeIdentity = "recipe-a",
                    },
                ],
            });

            AssertEqual(1, saved.RequestedOutputs.Count, "merged outputs");
            AssertEqual(5L, saved.RequestedOutputs[0].Quantity, "merged quantity");
            AssertEqual(saved.ListId, store.GetActiveListId() ?? "", "active list");
            AssertEqual(1, store.GetLists().Count, "stored list count");
            AssertEqual(true, store.Delete(saved.ListId), "delete list");
            AssertEqual<string?>(null, store.GetActiveListId(), "clear active list");
        }
        finally
        {
            TryDelete(directory);
        }
    }

    private static void ValidateRecursivePlan()
    {
        var recipeA = Recipe("recipe-a", 100, (200, 2), (300, 1));
        var recipeB = Recipe("recipe-b", 200, (400, 3));
        var knowledge = Knowledge(
            Item(100, "Output", recipeA),
            Item(200, "Intermediate", recipeB),
            Item(300, "Vendor part"),
            Item(400, "Ore"));
        var ownership = Ownership(
            (200, 1L, 0L),
            (300, 0L, 1L),
            (400, 2L, 4L));
        var list = new ShoppingListDocument
        {
            ListId = "scenario",
            Name = "Scenario",
            RequestedOutputs =
            [
                new ShoppingListRequestedOutput
                {
                    ItemTemplateId = 100,
                    Quantity = 2,
                },
            ],
        };

        var plan = ShoppingPlanBuilder.Build(list, knowledge, ownership);
        var output = GetLine(plan, 100);
        var intermediate = GetLine(plan, 200);
        var vendor = GetLine(plan, 300);
        var ore = GetLine(plan, 400);

        AssertEqual(2L, output.ActiveProduceQuantity, "output production");
        AssertEqual(4L, intermediate.ActiveDemandQuantity, "intermediate demand");
        AssertEqual(1L, intermediate.ActiveOwnedApplied, "intermediate ownership");
        AssertEqual(3L, intermediate.ActiveProduceQuantity, "intermediate production");
        AssertEqual(2L, vendor.ActiveAcquireQuantity, "vendor active acquisition");
        AssertEqual(1L, vendor.GlobalOwnedApplied, "vendor global ownership");
        AssertEqual(9L, ore.ActiveDemandQuantity, "ore demand");
        AssertEqual(2L, ore.ActiveOwnedApplied, "ore active ownership");
        AssertEqual(7L, ore.ActiveAcquireQuantity, "ore active acquisition");
        AssertEqual(6L, ore.GlobalOwnedApplied, "ore global ownership");
        AssertEqual(3L, ore.GlobalAcquireQuantity, "ore global acquisition");
        AssertEqual(0, plan.Issues.Count, "recursive issues");
    }

    private static void ValidateSharedIngredientAggregation()
    {
        var firstRecipe = Recipe(
            "first-recipe",
            100,
            (400, 2));
        var secondRecipe = Recipe(
            "second-recipe",
            101,
            (400, 3));
        var knowledge = Knowledge(
            Item(100, "First output", firstRecipe),
            Item(101, "Second output", secondRecipe),
            Item(400, "Shared material"));
        var list = new ShoppingListDocument
        {
            ListId = "shared-material",
            Name = "Shared material",
            RequestedOutputs =
            [
                new ShoppingListRequestedOutput
                {
                    ItemTemplateId = 100,
                    Quantity = 2,
                },
                new ShoppingListRequestedOutput
                {
                    ItemTemplateId = 101,
                    Quantity = 1,
                },
            ],
        };

        var plan = ShoppingPlanBuilder.Build(
            list,
            knowledge,
            Ownership((400, 2L, 1L)));
        var material = GetLine(plan, 400);

        AssertEqual(
            7L,
            material.ActiveDemandQuantity,
            "shared active demand");
        AssertEqual(
            2L,
            material.ActiveOwnedApplied,
            "shared active ownership");
        AssertEqual(
            5L,
            material.ActiveAcquireQuantity,
            "shared active acquisition");
        AssertEqual(
            3L,
            material.GlobalOwnedApplied,
            "shared global ownership");
        AssertEqual(
            4L,
            material.GlobalAcquireQuantity,
            "shared global acquisition");
        AssertEqual(
            2,
            plan.Edges.Count,
            "shared recipe edges");
    }

    private static void ValidateAlternativeRecipeAndCycle()
    {
        var recipeA = Recipe("recipe-a", 100, (200, 1));
        var recipeB = Recipe("recipe-b", 100, (300, 1));
        var cycleRecipe = Recipe("recipe-cycle", 300, (100, 1));
        var knowledge = Knowledge(
            Item(100, "Output", recipeA, recipeB),
            Item(200, "Safe leaf"),
            Item(300, "Cycle", cycleRecipe));
        var list = new ShoppingListDocument
        {
            ListId = "alternatives",
            Name = "Alternatives",
            RequestedOutputs =
            [
                new ShoppingListRequestedOutput
                {
                    ItemTemplateId = 100,
                    Quantity = 1,
                },
            ],
            RecipeSelections =
            [
                new ShoppingListRecipeSelection
                {
                    OutputItemTemplateId = 100,
                    RecipeIdentity = "recipe-b",
                },
            ],
        };

        var plan = ShoppingPlanBuilder.Build(
            list,
            knowledge,
            Ownership());
        AssertEqual(
            "recipe-b",
            GetLine(plan, 100).SelectedRecipeIdentity,
            "selected recipe");
        AssertEqual(
            true,
            plan.Issues.Any(issue =>
                issue.Kind == ShoppingPlanIssueKind.RecipeCycle),
            "cycle issue");
    }

    private static void ValidateAmmoStackOutputQuantity()
    {
        var recipe = Recipe(
            "ammo-recipe",
            500,
            (600, 2));
        var knowledge = Knowledge(
            Item(
                500,
                "Scenario ammo",
                GalaxyItemFamily.Ammo,
                200,
                recipe),
            Item(600, "Scenario component"));
        var list = new ShoppingListDocument
        {
            ListId = "ammo-yield",
            Name = "Ammo yield",
            RequestedOutputs =
            [
                new ShoppingListRequestedOutput
                {
                    ItemTemplateId = 500,
                    Quantity = 2,
                },
            ],
        };

        var plan = ShoppingPlanBuilder.Build(
            list,
            knowledge,
            Ownership((500, 400L, 600L)));
        var ammo = GetLine(plan, 500);
        var component = GetLine(plan, 600);

        AssertEqual(
            400L,
            ammo.ActiveDemandQuantity,
            "two requested ammo stacks");
        AssertEqual(
            0L,
            ammo.ActiveOwnedApplied,
            "requested ammo is incremental");
        AssertEqual(
            400L,
            ammo.ActiveProduceQuantity,
            "ammo production");
        AssertEqual(
            0L,
            ammo.GlobalOwnedApplied,
            "other-pilot ammo does not satisfy the request");
        AssertEqual(
            400L,
            ammo.GlobalProduceQuantity,
            "global ammo production remains incremental");
        AssertEqual(
            4L,
            component.ActiveDemandQuantity,
            "ammo ingredients");
        AssertEqual(
            false,
            plan.Issues.Any(issue =>
                issue.Kind ==
                    ShoppingPlanIssueKind.UnknownRecipeOutputQuantity),
            "known ammo yield");
    }

    private static GalaxyRecipeKnowledge Recipe(
        string identity,
        int output,
        params (int Item, int Quantity)[] ingredients)
    {
        return new GalaxyRecipeKnowledge
        {
            Identity = identity,
            Kind = GalaxyRecipeKind.Manufacture,
            OutputItemTemplateId = output,
            Ingredients = ingredients.Select(ingredient =>
                new GalaxyRecipeIngredientKnowledge
                {
                    ItemTemplateId = ingredient.Item,
                    Quantity = ingredient.Quantity,
                }).ToArray(),
        };
    }

    private static GalaxyItemKnowledge Item(
        int id,
        string name,
        params GalaxyRecipeKnowledge[] recipes)
    {
        return Item(
            id,
            name,
            GalaxyItemFamily.Component,
            1,
            recipes);
    }

    private static GalaxyItemKnowledge Item(
        int id,
        string name,
        GalaxyItemFamily family,
        uint maximumStack,
        params GalaxyRecipeKnowledge[] recipes)
    {
        return new GalaxyItemKnowledge
        {
            ItemTemplateId = id,
            Name = name,
            Family = family,
            TypeDisplayName = family.ToString(),
            MaximumStack = maximumStack,
            ProducedByRecipes = recipes,
        };
    }

    private static GalaxyKnowledgeSnapshot Knowledge(
        params GalaxyItemKnowledge[] items)
    {
        return new GalaxyKnowledgeSnapshot
        {
            Provenance = new GalaxyKnowledgeProvenance
            {
                CdataSha256 = "scenario",
                ForgeRevision = 1,
                RecipeCatalogRevision = 1,
            },
            Diagnostics = new GalaxyKnowledgeDiagnostics(),
            ItemsByTemplateId =
                new ReadOnlyDictionary<int, GalaxyItemKnowledge>(
                    items.ToDictionary(item => item.ItemTemplateId)),
            EffectsByIdentity =
                new ReadOnlyDictionary<string, GalaxyEffectKnowledge>(
                    new Dictionary<string, GalaxyEffectKnowledge>(
                        StringComparer.Ordinal)),
            Recipes = items
                .SelectMany(item => item.ProducedByRecipes)
                .Distinct()
                .ToArray(),
        };
    }

    private static ShoppingOwnershipSnapshot Ownership(
        params (int Item, long Active, long Elsewhere)[] values)
    {
        var items = values.ToDictionary(
            value => value.Item,
            value => new ShoppingItemOwnership
            {
                ItemTemplateId = value.Item,
                AvailableNow = value.Active,
                OwnedElsewhere = value.Elsewhere,
            });
        return new ShoppingOwnershipSnapshot
        {
            BuiltAtUtc = DateTimeOffset.UtcNow,
            ItemsByTemplateId =
                new ReadOnlyDictionary<int, ShoppingItemOwnership>(items),
        };
    }

    private static ShoppingPlanLine GetLine(
        ShoppingPlanSnapshot plan,
        int itemTemplateId)
    {
        return plan.Lines.Single(line =>
            line.ItemTemplateId == itemTemplateId);
    }

    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void AssertEqual<T>(
        T expected,
        T actual,
        string scenario)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"Shopping-list scenario failed: {scenario}; expected '{expected}', got '{actual}'.");
        }
    }
}
#endif
