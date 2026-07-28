namespace Net7ClientManager.Navigation;

using System.Globalization;
using System.Text;
using System.Text.Json;

internal sealed class ForgeProductionRecipeCatalogStore
{
    private const int MaximumCatalogBytes = 16 * 1024 * 1024;
    private const int MaximumRecipeCount = 100000;
    private readonly NavigationDataPathProvider paths = new();

    public ForgeProductionRecipeCatalogSnapshot Load()
    {
        try
        {
            if (!File.Exists(this.paths.ProductionRecipeCatalogPath))
            {
                return ForgeProductionRecipeCatalogSnapshot.Unavailable();
            }

            var bytes = File.ReadAllBytes(
                this.paths.ProductionRecipeCatalogPath);

            if (bytes.Length is <= 0 or > MaximumCatalogBytes)
            {
                return ForgeProductionRecipeCatalogSnapshot.Unavailable(
                    "The cached Forge recipe catalogue has an invalid size.");
            }

            var response =
                JsonSerializer.Deserialize<ForgeProductionRecipeCatalogResponse>(
                    bytes,
                    ForgeNavigationDataJson.ReadOptions);

            return response == null
                ? ForgeProductionRecipeCatalogSnapshot.Unavailable(
                    "The cached Forge recipe catalogue is empty.")
                : CreateSnapshot(response);
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                JsonException or
                InvalidOperationException)
        {
            return ForgeProductionRecipeCatalogSnapshot.Unavailable(
                $"The cached Forge recipe catalogue could not be loaded: {exception.Message}");
        }
    }

    public void Save(
        ForgeProductionRecipeCatalogResponse response)
    {
        var normalized = NormalizeAndValidate(response);
        this.paths.EnsureDirectories();
        var bytes =
            ForgeNavigationDataJson.SerializeCanonical(normalized);

        if (bytes.Length > MaximumCatalogBytes)
        {
            throw new InvalidOperationException(
                "The Forge recipe catalogue exceeds the supported size.");
        }

        var temporaryPath = string.Concat(
            this.paths.ProductionRecipeCatalogPath,
            ".",
            Guid.NewGuid().ToString(
                "N",
                CultureInfo.InvariantCulture),
            ".tmp");

        try
        {
            File.WriteAllBytes(temporaryPath, bytes);
            File.Move(
                temporaryPath,
                this.paths.ProductionRecipeCatalogPath,
                overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public static ForgeProductionRecipeCatalogSnapshot CreateSnapshot(
        ForgeProductionRecipeCatalogResponse response)
    {
        var normalized = NormalizeAndValidate(response);
        var canonicalBytes =
            ForgeNavigationDataJson.SerializeCanonical(normalized);

        return new ForgeProductionRecipeCatalogSnapshot
        {
            IsAvailable = true,
            Status =
                $"Forge recipe catalogue revision {normalized.Revision} is available.",
            Revision = normalized.Revision,
            GeneratedAtUtc = normalized.GeneratedAtUtc,
            Sha256 = ForgeNavigationHash.ComputeSha256(canonicalBytes),
            Recipes = normalized.Recipes,
        };
    }

    private static ForgeProductionRecipeCatalogResponse
        NormalizeAndValidate(
            ForgeProductionRecipeCatalogResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        Validate(response);

        return response with
        {
            Recipes = Array.AsReadOnly(
                response.Recipes
                    .OrderBy(recipe => recipe.Kind)
                    .ThenBy(recipe => recipe.OutputItemTemplateId)
                    .ThenBy(recipe => recipe.Id, StringComparer.Ordinal)
                    .Select(recipe =>
                        recipe with
                        {
                            Ingredients = Array.AsReadOnly(
                                recipe.Ingredients
                                    .OrderBy(ingredient =>
                                        ingredient.ItemTemplateId)
                                    .ToArray()),
                            NamedReporters = Array.AsReadOnly(
                                recipe.NamedReporters
                                    .Select(name => name.Trim())
                                    .Distinct(StringComparer.Ordinal)
                                    .OrderBy(
                                        name => name,
                                        StringComparer.Ordinal)
                                    .ToArray()),
                        })
                    .ToArray()),
        };
    }

    private static string ComputeRecipeFingerprint(
        ForgeProductionRecipeCatalogItem recipe)
    {
        var source = string.Join(
            "|",
            new[]
            {
                recipe.Kind.ToString(CultureInfo.InvariantCulture),
                recipe.OutputItemTemplateId.ToString(
                    CultureInfo.InvariantCulture),
                string.Join(
                    ",",
                    recipe.Ingredients
                        .OrderBy(ingredient =>
                            ingredient.ItemTemplateId)
                        .Select(ingredient =>
                            string.Create(
                                CultureInfo.InvariantCulture,
                                $"{ingredient.ItemTemplateId}:{ingredient.Quantity}"))),
            });

        return ForgeNavigationHash.ComputeSha256(
            Encoding.UTF8.GetBytes(source));
    }

    private static void Validate(
        ForgeProductionRecipeCatalogResponse response)
    {
        if (response.Revision < 0 ||
            response.GeneratedAtUtc == default ||
            response.Recipes == null ||
            response.Recipes.Count > MaximumRecipeCount ||
            response.Recipes.Any(recipe => recipe == null))
        {
            throw new InvalidOperationException(
                "The Forge recipe catalogue is invalid.");
        }

        HashSet<string> identities = new(StringComparer.Ordinal);
        HashSet<(int Kind, int OutputItemTemplateId)> outputs = [];

        foreach (var recipe in response.Recipes)
        {
            if (string.IsNullOrWhiteSpace(recipe.Id) ||
                recipe.Id.Length > 160 ||
                recipe.Kind is not (1 or 4) ||
                recipe.OutputItemTemplateId <= 0 ||
                recipe.Ingredients == null ||
                recipe.Ingredients.Count is < 1 or > 6 ||
                recipe.Ingredients.Any(ingredient =>
                    ingredient == null ||
                    ingredient.ItemTemplateId <= 0 ||
                    ingredient.Quantity is < 1 or > 6) ||
                recipe.Ingredients.Sum(ingredient =>
                    ingredient.Quantity) > 6 ||
                recipe.Ingredients
                    .Select(ingredient =>
                        ingredient.ItemTemplateId)
                    .Distinct()
                    .Count() != recipe.Ingredients.Count ||
                (!string.Equals(
                     recipe.Confidence,
                     "observed",
                     StringComparison.Ordinal) &&
                 !string.Equals(
                     recipe.Confidence,
                     "corroborated",
                     StringComparison.Ordinal)) ||
                recipe.NamedReporters == null ||
                recipe.NamedReporters.Any(name =>
                    string.IsNullOrWhiteSpace(name) ||
                    name.Length > 64 ||
                    name.Any(char.IsControl)))
            {
                throw new InvalidOperationException(
                    "The Forge recipe catalogue contains an invalid recipe.");
            }

            var expectedId = string.Create(
                CultureInfo.InvariantCulture,
                $"{(recipe.Kind == 1 ? "manufacture" : "refine")}:{recipe.OutputItemTemplateId}");
            var expectedFingerprint =
                ComputeRecipeFingerprint(recipe);

            if (!string.Equals(
                    recipe.Id,
                    expectedId,
                    StringComparison.Ordinal) ||
                !ForgeNavigationHash.IsSha256(
                    recipe.RecipeFingerprint) ||
                !string.Equals(
                    recipe.RecipeFingerprint,
                    expectedFingerprint,
                    StringComparison.OrdinalIgnoreCase) ||
                !identities.Add(recipe.Id) ||
                !outputs.Add((
                    recipe.Kind,
                    recipe.OutputItemTemplateId)))
            {
                throw new InvalidOperationException(
                    "The Forge recipe catalogue contains an inconsistent recipe.");
            }
        }
    }
}
