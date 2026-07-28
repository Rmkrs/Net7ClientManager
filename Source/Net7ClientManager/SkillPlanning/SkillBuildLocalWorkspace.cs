namespace Net7ClientManager.SkillPlanning;

internal sealed record SkillBuildForgeLink
{
    public string LocalBuildId { get; init; } = "";

    public string ForgeBuildId { get; init; } = "";

    public int Version { get; init; }

    public string ContentSha256 { get; init; } = "";

    public string PublisherPilotName { get; init; } = "";

    public bool IsPublisherSource { get; init; }

    public int LatestKnownVersion { get; init; }

    public int StarCount { get; init; }

    public bool IsStarredByMe { get; init; }

    public bool IsOwnedByMe { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }

    public bool HasNewerVersion => this.LatestKnownVersion > this.Version;
}

internal sealed record SkillBuildLocalLibrary
{
    public IReadOnlyList<SkillBuildDocument> Builds { get; init; } = [];

    public string? ActiveBuildId { get; init; }

    public IReadOnlyDictionary<string, SkillBuildForgeLink> ForgeLinks { get; init; } =
        new Dictionary<string, SkillBuildForgeLink>(StringComparer.OrdinalIgnoreCase);

    public SkillBuildDocument? ActiveBuild =>
        this.ActiveBuildId == null
            ? null
            : this.Builds.FirstOrDefault(value => string.Equals(
                value.BuildId,
                this.ActiveBuildId,
                StringComparison.OrdinalIgnoreCase));

    public SkillBuildForgeLink? GetForgeLink(string localBuildId) =>
        this.ForgeLinks.TryGetValue(localBuildId, out var link)
            ? link
            : null;
}

/// <summary>
/// Shared build workspace used by live clients and Pilot Archive. It joins
/// local persistence, Forge lineage, catalog access, derived requirements,
/// and comparison against the selected pilot.
/// </summary>
internal sealed class SkillBuildLocalWorkspace
{
    private readonly SkillBuildLocalStore store;

    public SkillBuildLocalWorkspace(
        SkillPlannerCatalog catalog,
        SkillBuildHullCatalog hullCatalog,
        SkillBuildLocalStore store)
    {
        this.Catalog = catalog ??
            throw new ArgumentNullException(nameof(catalog));
        this.HullCatalog = hullCatalog ??
            throw new ArgumentNullException(nameof(hullCatalog));
        this.store = store ??
            throw new ArgumentNullException(nameof(store));
        this.EquipmentCatalog = new SkillBuildEquipmentCatalog(this.Catalog);
        this.Board = new SkillBuildBoardEngine(
            this.Catalog,
            this.HullCatalog,
            this.EquipmentCatalog);
    }

    public event EventHandler? Changed;

    public SkillPlannerCatalog Catalog { get; }

    public SkillBuildHullCatalog HullCatalog { get; }

    public SkillBuildEquipmentCatalog EquipmentCatalog { get; }

    public SkillBuildBoardEngine Board { get; }

    public SkillBuildLocalLibrary GetLibrary(
        uint characterId,
        int professionIndex)
    {
        var builds = this.store.GetBuildsForCharacter(characterId, professionIndex);
        return new SkillBuildLocalLibrary
        {
            Builds = builds,
            ActiveBuildId = this.store.GetActiveBuildId(characterId),
            ForgeLinks = this.store.GetForgeLinks(
                builds.Select(value => value.BuildId)),
        };
    }

    public string? GetActiveBuildDisplayName(uint characterId)
    {
        var activeBuildId = this.store.GetActiveBuildId(characterId);
        if (string.IsNullOrWhiteSpace(activeBuildId))
        {
            return null;
        }

        var build = this.store.GetBuild(activeBuildId);
        if (build == null)
        {
            return null;
        }

        var link = this.store.GetForgeLink(activeBuildId);
        return link == null
            ? build.Title
            : link.IsPublisherSource
                ? string.Concat(
                    build.Title,
                    " \u00b7 published v",
                    link.Version)
                : string.Concat(
                    build.Title,
                    " \u00b7 Forge v",
                    link.Version);
    }

    public void SaveBuild(
        uint characterId,
        SkillBuildDocument build)
    {
        ArgumentNullException.ThrowIfNull(build);
        this.store.SaveBuild(characterId, build, DateTimeOffset.UtcNow);
        this.Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetActiveBuild(
        uint characterId,
        string buildId)
    {
        this.store.SetActiveBuild(characterId, buildId);
        this.Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SaveAndActivateForgeVersion(
        uint characterId,
        SkillBuildDocument build,
        SkillBuildForgeLink link)
    {
        ArgumentNullException.ThrowIfNull(build);
        ArgumentNullException.ThrowIfNull(link);
        this.store.SaveAndActivateForgeVersion(
            characterId,
            build,
            link,
            DateTimeOffset.UtcNow);
        this.Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SaveForgeLink(SkillBuildForgeLink link)
    {
        this.store.SaveForgeLink(link);
        this.Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool DeleteBuild(
        uint characterId,
        string buildId)
    {
        var deleted = this.store.DeleteBuild(characterId, buildId);
        if (deleted)
        {
            this.Changed?.Invoke(this, EventArgs.Empty);
        }

        return deleted;
    }

    public SkillBuildForgeLink? GetForgeLink(string localBuildId) =>
        this.store.GetForgeLink(localBuildId);

    public void UpdateForgeKnowledge(
        string forgeBuildId,
        int latestKnownVersion,
        int starCount,
        bool isStarredByMe,
        bool isOwnedByMe)
    {
        this.store.UpdateForgeKnowledge(
            forgeBuildId,
            latestKnownVersion,
            starCount,
            isStarredByMe,
            isOwnedByMe);
        this.Changed?.Invoke(this, EventArgs.Empty);
    }
}
