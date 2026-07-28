namespace Net7ClientManager.Observations.Models;

public sealed record ClientFactionReputationObservation
{
    public int Slot { get; init; }

    public uint Address { get; init; }

    public uint ValidState { get; init; }

    public string FactionKey { get; init; } = "";

    public string DisplayName { get; init; } = "";

    public string Description { get; init; } = "";

    public bool IsCatalogResolved { get; init; }

    public float? Reaction { get; init; }

    public int? Order { get; init; }

    public float? NormalizedReaction =>
        this.Reaction.HasValue
            ? Math.Clamp(
                this.Reaction.Value * 0.0001f,
                -1.0f,
                1.0f)
            : null;
}
