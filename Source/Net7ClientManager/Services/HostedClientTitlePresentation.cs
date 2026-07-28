namespace Net7ClientManager.Services;

public sealed record HostedClientTitlePresentation
{
    public required string WindowTitle { get; init; }

    public IReadOnlyList<HostedClientTitleSegment> Segments { get; init; } = [];
}
