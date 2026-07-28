namespace Net7ClientManager.Navigation;

using System.Text.Json;

internal static class NavigationDistributionRegression
{
    public static void Validate()
    {
        ValidateStaticWorldTargetProjection();
        var source = new ForgeNavigationEntitySnapshotDocument
        {
            ContractVersion = 1,
            Revision = 1,
            GeneratedAt = new DateTimeOffset(
                2026,
                1,
                1,
                0,
                0,
                0,
                TimeSpan.Zero),
            Entities =
            [
                Create(
                    "future-anomaly",
                    "future:1",
                    3,
                    """{"id":"future:1","strength":1,"futureFlag":true}"""),
            ],
        };
        var target = new ForgeNavigationEntitySnapshotDocument
        {
            ContractVersion = 1,
            Revision = 2,
            GeneratedAt = new DateTimeOffset(
                2026,
                1,
                2,
                0,
                0,
                0,
                TimeSpan.Zero),
            Entities =
            [
                Create(
                    "future-anomaly",
                    "future:1",
                    3,
                    """{"id":"future:1","strength":2,"futureFlag":true,"newField":"preserved"}"""),
                Create(
                    "future-overlay",
                    "future:2",
                    1,
                    """{"id":"future:2","labels":["x","y"]}"""),
            ],
        };
        var targetBytes = ForgeNavigationDataJson.SerializeCanonical(target);
        var delta = new ForgeNavigationEntityDeltaDocument
        {
            ContractVersion = 1,
            BaseRevision = 1,
            TargetRevision = 2,
            GeneratedAt = target.GeneratedAt,
            Changes =
            [
                new ForgeNavigationEntityChangeDocument
                {
                    Kind = "future-anomaly",
                    Id = "future:1",
                    Version = 3,
                    Operation = "upsert",
                    Document = target.Entities[0].Document.Clone(),
                },
                new ForgeNavigationEntityChangeDocument
                {
                    Kind = "future-overlay",
                    Id = "future:2",
                    Version = 1,
                    Operation = "upsert",
                    Document = target.Entities[1].Document.Clone(),
                },
            ],
            ResultSnapshotSha256 =
                ForgeNavigationHash.ComputeSha256(targetBytes),
        };

        ForgeNavigationDataPackageLoader.ValidateSnapshot(source);
        ForgeNavigationDataPackageLoader.ValidateDelta(delta);
        var applied = ForgeNavigationDeltaApplier.Apply(source, delta);
        var appliedBytes = ForgeNavigationDataJson.SerializeCanonical(applied);

        if (!appliedBytes.AsSpan().SequenceEqual(targetBytes))
        {
            throw new InvalidOperationException(
                "Navigation distribution regression discarded unknown data.");
        }

        var restarted = JsonSerializer.Deserialize<
                ForgeNavigationEntitySnapshotDocument>(
                appliedBytes,
                ForgeNavigationDataJson.DistributionReadOptions) ??
            throw new InvalidOperationException(
                "Navigation distribution regression could not reload its snapshot.");
        var restartedBytes = ForgeNavigationDataJson.SerializeCanonical(restarted);

        if (!restartedBytes.AsSpan().SequenceEqual(targetBytes))
        {
            throw new InvalidOperationException(
                "Navigation distribution regression changed unknown data during restart.");
        }
    }

    private static void ValidateStaticWorldTargetProjection()
    {
        var snapshot = new ForgeNavigationEntitySnapshotDocument
        {
            ContractVersion = 1,
            Revision = 1,
            GeneratedAt = new DateTimeOffset(
                2026,
                1,
                1,
                0,
                0,
                0,
                TimeSpan.Zero),
            Entities =
            [
                Create(
                    ForgeNavigationEntityKinds.Sector,
                    "sector:known",
                    1,
                    """{"id":"sector:known","key":"known","name":"Known","systemName":"Regression","requiredProfession":null,"requiredFaction":null,"minimumFactionStanding":null,"aliases":[],"connections":[],"activeSectorNumber":1}"""),
                Create(
                    ForgeNavigationEntityKinds.Target,
                    "target:adrastea",
                    1,
                    """{"sectorId":"sector:known","target":{"id":"target:adrastea","ordinal":0,"name":"Adrastea","mapDisplayName":"Adrastea","rawObjectType":3,"selectionContext":"Object","hasPosition":true,"x":177270,"y":-236410,"z":0}}"""),
            ],
        };

        ForgeNavigationDataPackageLoader.ValidateSnapshot(snapshot);
        var projection =
            ForgeNavigationEntityCatalogCodec.ToDataDocument(snapshot);
        ForgeNavigationDataPackageLoader.ValidateDocument(projection);
        var target = projection.Sectors.Single().Targets.Single();

        if (!string.Equals(
                target.SelectionContext,
                "Object",
                StringComparison.Ordinal) ||
            target.Signature.HasValue ||
            target.NavType.HasValue ||
            target.IsHuge.HasValue)
        {
            throw new InvalidOperationException(
                "Navigation distribution regression lost static world-target semantics.");
        }
    }

    private static ForgeNavigationEntityRecordDocument Create(
        string kind,
        string id,
        int version,
        string json)
    {
        using var document = JsonDocument.Parse(json);

        return new ForgeNavigationEntityRecordDocument
        {
            Kind = kind,
            Id = id,
            Version = version,
            Document = document.RootElement.Clone(),
        };
    }
}
