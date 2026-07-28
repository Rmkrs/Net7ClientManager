namespace Net7ClientManager.SkillPlanning;

using System.Text.Json;

internal sealed class SkillBuildHullCatalogService
{
    private const string ResourceName =
        "Net7ClientManager.SkillPlanning.SkillBuildHullCatalog.json";

    private readonly Lazy<SkillBuildHullCatalog> catalog;

    public SkillBuildHullCatalogService()
    {
        this.catalog = new Lazy<SkillBuildHullCatalog>(
            LoadCatalog,
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public SkillBuildHullCatalog GetCatalog() =>
        this.catalog.Value;

    private static SkillBuildHullCatalog LoadCatalog()
    {
        var assembly = typeof(SkillBuildHullCatalogService).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName) ??
            throw new InvalidOperationException(
                $"Embedded skill-build hull catalog '{ResourceName}' was not found.");

        var document = JsonSerializer.Deserialize<SkillBuildHullCatalogDocument>(
            stream,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            }) ?? throw new InvalidOperationException(
                "Embedded skill-build hull catalog is empty.");

        return new SkillBuildHullCatalog(document);
    }
}
