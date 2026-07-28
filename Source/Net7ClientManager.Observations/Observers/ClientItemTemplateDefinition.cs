namespace Net7ClientManager.Observations.Observers;

using Net7ClientManager.Observations.Models;

internal sealed record ClientItemTemplateDefinition
{
    public int Id { get; init; }

    public int EmbeddedId { get; init; }

    public int RecordIndex { get; init; }

    public int RecordOffset { get; init; }

    public int MetadataOffset { get; init; }

    public int? ModelBassetId { get; init; }

    public int? IconBassetId { get; init; }

    public string Name { get; init; } = "";

    public string Description { get; init; } = "";

    public string Manufacturer { get; init; } = "";

    public IReadOnlyList<string> AdditionalText { get; init; } = [];

    public ClientRuntimeItemTemplateObservation RuntimeObservation { get; init; } = new();

    public bool EmbeddedIdMatchesIndexId =>
        this.EmbeddedId == this.Id;
}
