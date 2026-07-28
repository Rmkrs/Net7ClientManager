namespace Net7ClientManager.Observations.Models;

public enum ClientRelationshipRaw
{
    Kos = 0,
    Shun = 1,
    Friendly = 2,
    Adore = 3,
}

public enum ClientResolvedDisposition
{
    Unknown,
    Hostile,
    Neutral,
    Friendly,
}

public sealed record ClientObjectRelationshipObservation
{
    public int RelationshipRaw { get; init; }

    public int AggressionRaw { get; init; }

    public ClientResolvedDisposition Disposition { get; init; }

    public bool IsActivelyAggressive =>
        (this.AggressionRaw & 0xFF) != 0;

    public static ClientObjectRelationshipObservation FromRaw(
        int relationshipRaw,
        int aggressionRaw)
    {
        return new ClientObjectRelationshipObservation
        {
            RelationshipRaw = relationshipRaw,
            AggressionRaw = aggressionRaw,
            Disposition = ResolveDisposition(relationshipRaw),
        };
    }

    public static ClientObjectRelationshipObservation Unknown()
    {
        return new ClientObjectRelationshipObservation
        {
            RelationshipRaw = -1,
            AggressionRaw = 0,
            Disposition = ClientResolvedDisposition.Unknown,
        };
    }

    public static ClientResolvedDisposition ResolveDisposition(
        int relationshipRaw)
    {
        return relationshipRaw switch
        {
            (int)ClientRelationshipRaw.Kos =>
                ClientResolvedDisposition.Hostile,
            (int)ClientRelationshipRaw.Shun =>
                ClientResolvedDisposition.Neutral,
            (int)ClientRelationshipRaw.Friendly =>
                ClientResolvedDisposition.Friendly,
            (int)ClientRelationshipRaw.Adore =>
                ClientResolvedDisposition.Friendly,
            _ => ClientResolvedDisposition.Unknown,
        };
    }
}
