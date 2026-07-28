namespace Net7ClientManager.Navigation;

public sealed record WorldSearchMatch
{
    public required WorldSearchEntry Entry { get; init; }

    public int Rank { get; init; }
}
