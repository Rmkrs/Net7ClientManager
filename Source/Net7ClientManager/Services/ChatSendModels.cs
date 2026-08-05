namespace Net7ClientManager.Services;

internal enum ChatSendDestination
{
    Broadcast,
    Local,
    Guild,
    Group,
    General,
    OutOfContext,
    Market,
    NewPlayers,
    Terran,
    Jenquai,
    Progen,
    Explorers,
    Warriors,
    Tradesmen,
    Sentinels,
    Defenders,
    Enforcers,
    Tell,
}

internal sealed record ChatSendDestinationDefinition(
    ChatSendDestination Destination,
    string DisplayName,
    string? ChannelOptionName,
    int? RawSelectedChannel,
    string? SelectedChannelName,
    bool RequiresGroupMembership = false,
    bool RequiresGuildMembership = false)
{
    public bool IsTell => this.Destination == ChatSendDestination.Tell;

    public bool IsSelectableChannel => this.RawSelectedChannel.HasValue;
}

internal static class ChatSendDestinationCatalog
{
    private static readonly ChatSendDestinationDefinition[] ordered =
    [
        new(
            ChatSendDestination.Broadcast,
            "Broadcast",
            "Channel_Broadcast",
            0,
            "Broadcast"),
        new(
            ChatSendDestination.Local,
            "Local",
            "Channel_Local",
            1,
            "Local"),
        new(
            ChatSendDestination.Guild,
            "Guild",
            "Channel_Guild",
            2,
            "Guild",
            RequiresGuildMembership: true),
        new(
            ChatSendDestination.Group,
            "Group",
            "Channel_Group",
            3,
            "Group",
            RequiresGroupMembership: true),
        new(
            ChatSendDestination.General,
            "General",
            "Channel_General",
            5,
            "General"),
        new(
            ChatSendDestination.OutOfContext,
            "Out of Context",
            "Channel_OOC",
            5,
            "Out of Context"),
        new(
            ChatSendDestination.Market,
            "Market",
            "Channel_Market",
            5,
            "Market"),
        new(
            ChatSendDestination.NewPlayers,
            "New Players",
            "Channel_Newbie",
            5,
            "New Players"),
        new(
            ChatSendDestination.Terran,
            "Terran",
            "Channel_Terran",
            5,
            "Terran"),
        new(
            ChatSendDestination.Jenquai,
            "Jenquai",
            "Channel_Jenquai",
            5,
            "Jenquai"),
        new(
            ChatSendDestination.Progen,
            "Progen",
            "Channel_Progen",
            5,
            "Progen"),
        new(
            ChatSendDestination.Explorers,
            "Explorers",
            "Channel_Explorers",
            5,
            "Explorers"),
        new(
            ChatSendDestination.Warriors,
            "Warriors",
            "Channel_Warriors",
            5,
            "Warriors"),
        new(
            ChatSendDestination.Tradesmen,
            "Tradesmen",
            "Channel_Traders",
            5,
            "Tradesmen"),
        new(
            ChatSendDestination.Sentinels,
            "Sentinels",
            "Channel_Sentinels",
            5,
            "Sentinels"),
        new(
            ChatSendDestination.Defenders,
            "Defenders",
            "Channel_Defenders",
            5,
            "Defenders"),
        new(
            ChatSendDestination.Enforcers,
            "Enforcers",
            "Channel_Enforcers",
            5,
            "Enforcers"),
        // Channel_Private only enables monitoring. Raw selector 4 is usable
        // only when the pilot is subscribed to a concrete named private
        // channel, so the toggle alone is not treated as a send target.
        new(
            ChatSendDestination.Tell,
            "Tell",
            null,
            null,
            null),
    ];

    private static readonly IReadOnlyDictionary<ChatSendDestination,
        ChatSendDestinationDefinition> byDestination = ordered
        .ToDictionary(definition => definition.Destination);

    private static readonly IReadOnlyDictionary<string, string>
        canonicalObservedNames = BuildCanonicalObservedNames();

    public static IReadOnlyList<ChatSendDestinationDefinition> Ordered =>
        ordered;

    public static bool TryGet(
        ChatSendDestination destination,
        out ChatSendDestinationDefinition definition)
    {
        return byDestination.TryGetValue(destination, out definition!);
    }

    public static ChatSendDestinationDefinition Get(
        ChatSendDestination destination)
    {
        return byDestination.TryGetValue(destination, out var definition)
            ? definition
            : throw new ArgumentOutOfRangeException(
                nameof(destination),
                destination,
                "Unknown chat destination");
    }

    public static string GetDisplayName(ChatSendDestination destination) =>
        Get(destination).DisplayName;

    public static bool TryCanonicalizeObservedName(
        string? observedName,
        out string canonicalName)
    {
        canonicalName = "";

        if (string.IsNullOrWhiteSpace(observedName))
        {
            return false;
        }

        return canonicalObservedNames.TryGetValue(
            NormalizeChannelName(observedName),
            out canonicalName!);
    }

    public static bool ChannelNamesEqual(
        string? first,
        string? second)
    {
        if (string.IsNullOrWhiteSpace(first) ||
            string.IsNullOrWhiteSpace(second))
        {
            return false;
        }

        var firstNormalized = NormalizeChannelName(first);
        var secondNormalized = NormalizeChannelName(second);

        if (canonicalObservedNames.TryGetValue(
                firstNormalized,
                out var firstCanonical))
        {
            firstNormalized = NormalizeChannelName(firstCanonical);
        }

        if (canonicalObservedNames.TryGetValue(
                secondNormalized,
                out var secondCanonical))
        {
            secondNormalized = NormalizeChannelName(secondCanonical);
        }

        return string.Equals(
            firstNormalized,
            secondNormalized,
            StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, string>
        BuildCanonicalObservedNames()
    {
        Dictionary<string, string> names =
            new(StringComparer.OrdinalIgnoreCase);

        foreach (var definition in ordered)
        {
            if (!string.IsNullOrWhiteSpace(
                    definition.SelectedChannelName))
            {
                names[NormalizeChannelName(
                    definition.SelectedChannelName!)] =
                    definition.SelectedChannelName;
            }
        }

        AddAlias(names, "General Chat", "General");
        AddAlias(names, "OOC", "Out of Context");
        AddAlias(names, "Newbie", "New Players");
        AddAlias(names, "New Player", "New Players");
        AddAlias(names, "Traders", "Tradesmen");
        AddAlias(names, "Tradesman", "Tradesmen");

        return names;
    }

    private static void AddAlias(
        IDictionary<string, string> names,
        string alias,
        string canonicalName)
    {
        names[NormalizeChannelName(alias)] = canonicalName;
    }

    public static string NormalizeChannelName(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length);
        var previousWasWhitespace = false;

        foreach (var character in value.Trim().TrimEnd(':'))
        {
            if (char.IsWhiteSpace(character))
            {
                if (!previousWasWhitespace && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                previousWasWhitespace = true;
                continue;
            }

            builder.Append(character);
            previousWasWhitespace = false;
        }

        return builder.ToString();
    }
}

internal sealed record ChatSendResult(
    bool Succeeded,
    int SegmentCount,
    string Status)
{
    public static ChatSendResult Failure(string status) =>
        new(false, 0, status);

    public static ChatSendResult Success(
        int segmentCount,
        string status) =>
        new(true, segmentCount, status);
}
