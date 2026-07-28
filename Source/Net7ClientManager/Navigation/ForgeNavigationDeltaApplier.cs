namespace Net7ClientManager.Navigation;

internal static class ForgeNavigationDeltaApplier
{
    public static ForgeNavigationEntitySnapshotDocument Apply(
        ForgeNavigationEntitySnapshotDocument source,
        ForgeNavigationEntityDeltaDocument delta)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(delta);

        if (source.ContractVersion != delta.ContractVersion ||
            source.Revision != delta.BaseRevision ||
            delta.TargetRevision <= delta.BaseRevision)
        {
            throw new InvalidOperationException(
                "Navigation delta does not apply to the active snapshot.");
        }

        var entities = source.Entities.ToDictionary(
            entity => EntityKey(entity.Kind, entity.Id),
            StringComparer.Ordinal);

        foreach (var change in delta.Changes)
        {
            var key = EntityKey(change.Kind, change.Id);

            if (string.Equals(change.Operation, "delete", StringComparison.Ordinal))
            {
                entities.Remove(key);
                continue;
            }

            if (!string.Equals(change.Operation, "upsert", StringComparison.Ordinal) ||
                change.Document is null)
            {
                throw new InvalidOperationException(
                    $"Unsupported navigation delta operation '{change.Operation}'.");
            }

            entities[key] = new ForgeNavigationEntityRecordDocument
            {
                Kind = change.Kind,
                Id = change.Id,
                Version = change.Version,
                Document = change.Document.Value.Clone(),
            };
        }

        return new ForgeNavigationEntitySnapshotDocument
        {
            ContractVersion = delta.ContractVersion,
            Revision = delta.TargetRevision,
            GeneratedAt = delta.GeneratedAt,
            Entities =
            [
                .. entities.Values
                    .OrderBy(entity => entity.Kind, StringComparer.Ordinal)
                    .ThenBy(entity => entity.Id, StringComparer.Ordinal),
            ],
        };
    }

    private static string EntityKey(string kind, string id)
    {
        return string.Concat(kind, "\u001f", id);
    }
}
