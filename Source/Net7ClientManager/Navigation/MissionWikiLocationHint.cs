namespace Net7ClientManager.Navigation;

public sealed record MissionWikiLocationHint
{
    public string PageTitle { get; init; } = "";

    public string LinkText { get; init; } = "";

    public string Context { get; init; } = "";
}
