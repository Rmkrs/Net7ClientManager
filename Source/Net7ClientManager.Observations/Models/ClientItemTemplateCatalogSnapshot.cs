namespace Net7ClientManager.Observations.Models;

public sealed record ClientItemTemplateCatalogSnapshot
{
    public bool IsAvailable { get; init; }

    public string Status { get; init; } = "";

    public string? SourcePath { get; init; }

    public long FileLength { get; init; }

    public DateTimeOffset? LastWriteTimeUtc { get; init; }

    public string Sha256 { get; init; } = "";

    public DateTimeOffset LoadedAtUtc { get; init; }

    public int IndexEntryCount { get; init; }

    public IReadOnlyList<int> FailedItemTemplateIds { get; init; } = [];

    public IReadOnlyList<ClientItemTemplateCatalogEntry> Templates
    { get; init; } = [];
}

public sealed record ClientItemTemplateCatalogEntry
{
    public int ItemTemplateId { get; init; }

    public int? ModelBassetId { get; init; }

    public int? IconBassetId { get; init; }

    public IReadOnlyList<string> AdditionalText { get; init; } = [];

    public required ClientRuntimeItemTemplateObservation Template { get; init; }
}

public sealed record ClientItemTemplateCatalogRefreshResult
{
    public bool InputChanged { get; init; }

    public bool Applied { get; init; }

    public string Status { get; init; } = "";

    public required ClientItemTemplateCatalogSnapshot Snapshot { get; init; }
}
