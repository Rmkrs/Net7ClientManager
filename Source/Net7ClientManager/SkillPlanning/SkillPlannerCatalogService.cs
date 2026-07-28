namespace Net7ClientManager.SkillPlanning;

using System.Text.Json;

internal sealed class SkillPlannerCatalogService
{
    private const string ResourceName =
        "Net7ClientManager.SkillPlanning.SkillPlannerCatalog.json";

    private readonly Lazy<SkillPlannerCatalog> catalog;

    public SkillPlannerCatalogService()
    {
        this.catalog = new Lazy<SkillPlannerCatalog>(
            LoadCatalog,
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public SkillPlannerCatalog GetCatalog() =>
        this.catalog.Value;

    private static SkillPlannerCatalog LoadCatalog()
    {
        var assembly = typeof(SkillPlannerCatalogService).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName) ??
            throw new InvalidOperationException(
                $"Embedded skill-planner catalog '{ResourceName}' was not found.");

        var document = JsonSerializer.Deserialize<SkillPlannerCatalogDocument>(
            stream,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            }) ?? throw new InvalidOperationException(
                "Embedded skill-planner catalog is empty.");

        return new SkillPlannerCatalog(document);
    }
}
