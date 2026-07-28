namespace Net7ClientManager.Navigation;

internal sealed record NavigationDataStateDocument
{
    public const int CurrentSchemaVersion = 3;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public NavigationDataSnapshotReference? Active { get; init; }

    public NavigationDataSnapshotReference? Previous { get; init; }

    public NavigationDataSnapshotReference? Pending { get; init; }

    public NavigationDataSnapshotReference? Bundled { get; init; }

    public DateTimeOffset? LastSuccessfulUpdateCheck { get; init; }

    public DateTimeOffset? LastFailedUpdateCheck { get; init; }

    public string? LastUpdateError { get; init; }
}
