namespace Net7ClientManager.Models;

public sealed class ForgeContributionSettings
{
    public const int CurrentCategoryConsentVersion = 1;

    public bool Enabled { get; set; }

    public ForgeContributionAttribution Attribution { get; set; } =
        ForgeContributionAttribution.PubliclyAnonymous;

    public int CategoryConsentVersion { get; set; } =
        CurrentCategoryConsentVersion;

    public ForgeContributionCategorySettings Categories { get; set; } = new();

    public string? ContributorId { get; set; }

    public string? PublicKey { get; set; }

    public string? ProtectedPrivateKey { get; set; }

    public DateTimeOffset? RegisteredAtUtc { get; set; }

    public string? RecoveryRequestId { get; set; }

    public string? RecoveryCode { get; set; }

    public string? RecoveryStatus { get; set; }

    public string? RecoveryPilotName { get; set; }

    public DateTimeOffset? RecoveryCreatedAtUtc { get; set; }

    public DateTimeOffset? RecoveryExpiresAtUtc { get; set; }

    public DateTimeOffset? RecoveryResolvedAtUtc { get; set; }

    public List<string> PendingMobLootRelationshipIds { get; set; } = [];

    public List<string> PendingHarvestableResourceIds { get; set; } = [];

    public ForgeContributionLifetimeStatistics Lifetime { get; set; } = new();

    public void EnsureDefaults()
    {
        this.Categories ??= new ForgeContributionCategorySettings();
        this.PendingMobLootRelationshipIds ??= [];
        this.PendingHarvestableResourceIds ??= [];
        this.PendingMobLootRelationshipIds =
        [
            .. this.PendingMobLootRelationshipIds
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
        this.PendingHarvestableResourceIds =
        [
            .. this.PendingHarvestableResourceIds
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
        this.RecoveryRequestId = Normalize(this.RecoveryRequestId);
        this.RecoveryCode = Normalize(this.RecoveryCode);
        this.RecoveryStatus = Normalize(this.RecoveryStatus);
        this.RecoveryPilotName = Normalize(this.RecoveryPilotName);
        this.Lifetime ??= new ForgeContributionLifetimeStatistics();
        this.Lifetime.EnsureDefaults();

        if (!Enum.IsDefined(this.Attribution))
        {
            this.Attribution = ForgeContributionAttribution.PubliclyAnonymous;
        }

        if (this.CategoryConsentVersion <= 0)
        {
            this.CategoryConsentVersion = CurrentCategoryConsentVersion;
        }
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
