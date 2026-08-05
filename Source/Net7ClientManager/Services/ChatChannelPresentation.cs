namespace Net7ClientManager.Services;

using Net7ClientManager.ChatJournal;

internal enum ChatTellDirection
{
    None,
    Incoming,
    Outgoing,
}

internal readonly record struct ChatMessagePresentation(
    string ChannelName,
    string PlayerName,
    string? ConversationPlayerName,
    ChatTellDirection TellDirection,
    string DisplayText,
    string ChannelToolTip,
    string PlayerToolTip,
    string? ColorOptionName);

internal static class ChatChannelPresentation
{
    private static readonly string[] filterChannelNames =
    [
        "Computer",
        "Out of Context",
        "Broadcast",
        "Tell",
        "Group",
        "NPC",
        "Tip",
        "Local",
        "Guild",
        "General",
        "New Players",
        "Market",
        "Private Channel",
        "Race",
        "Class",
        "Terran",
        "Jenquai",
        "Progen",
        "Explorers",
        "Warriors",
        "Tradesmen",
        "Sentinels",
        "Defenders",
        "Enforcers",
        "In-context Error",
        "Out-of-context Error",
        "System",
        "Combat",
        "Warning",
        "Attention",
        "Debug",
        "Unknown",
    ];

    private static readonly IReadOnlyDictionary<int, ChatMessageTypeDefinition>
        messageTypes = BuildMessageTypes();

    public static IReadOnlyList<string> FilterChannelNames =>
        filterChannelNames;

    public static string GetFilterChannelName(string displayedChannelName)
    {
        return displayedChannelName.StartsWith(
            "#",
            StringComparison.Ordinal)
            ? "Unknown"
            : displayedChannelName;
    }

    public static IReadOnlyDictionary<int, string> BuildObservedChannelNames(
        IEnumerable<ChatJournalEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        Dictionary<int, string> names = [];

        foreach (var entry in entries)
        {
            if (TryExtractPrefixedChannel(
                    entry.Text,
                    out var channelName,
                    out _))
            {
                names[entry.Channel] = channelName;
            }
        }

        return names;
    }

    public static ChatMessagePresentation Present(
        ChatJournalEntry entry,
        IReadOnlyDictionary<int, string> observedChannelNames,
        string localPilotName)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(observedChannelNames);

        localPilotName = string.IsNullOrWhiteSpace(localPilotName)
            ? entry.PilotName
            : localPilotName.Trim();

        var rawDefinition = messageTypes.TryGetValue(
            entry.Channel,
            out var foundRawDefinition)
            ? foundRawDefinition
            : null;
        var rawChannelName = rawDefinition?.DefaultChannelName ??
            string.Concat("#", entry.Channel);
        var rawCategoryName = rawDefinition?.RawCategoryName ??
            rawChannelName;
        var channelName = rawChannelName;
        var displayText = entry.Text;
        var playerName = "";
        string? conversationPlayerName = null;
        var tellDirection = ChatTellDirection.None;
        var playerToolTip = "";
        var derivedReason = "";

        if (TryExtractPrefixedChannel(
                entry.Text,
                out var prefixedChannelName,
                out displayText))
        {
            channelName = prefixedChannelName;
            derivedReason = "identified from the channel prefix in the game text";
        }
        else if (TryClassifyStandaloneMessage(
                entry.Text,
                localPilotName,
                out var classifiedChannelName,
                out displayText,
                out playerName,
                out conversationPlayerName,
                out tellDirection,
                out playerToolTip,
                out derivedReason))
        {
            channelName = classifiedChannelName;
        }
        else if (observedChannelNames.TryGetValue(
                     entry.Channel,
                     out var observedChannelName))
        {
            channelName = observedChannelName;
            derivedReason =
                "identified from another message with the same raw game type";
        }

        if (string.IsNullOrWhiteSpace(playerName) &&
            string.Equals(
                channelName,
                "Tell",
                StringComparison.OrdinalIgnoreCase) &&
            TrySplitDirectedTell(
                displayText,
                "To ",
                out var prefixedOutgoingRecipient,
                out var prefixedOutgoingMessage))
        {
            var conversationPlayer = NormalizeTellPilotName(
                prefixedOutgoingRecipient);
            conversationPlayerName = conversationPlayer;
            tellDirection = ChatTellDirection.Outgoing;
            playerName = localPilotName;
            displayText = prefixedOutgoingMessage;
            playerToolTip = string.Concat(
                "Outgoing Tell from ",
                localPilotName,
                " to ",
                conversationPlayer);
        }
        else if (string.IsNullOrWhiteSpace(playerName) &&
                 string.Equals(
                     channelName,
                     "Tell",
                     StringComparison.OrdinalIgnoreCase) &&
                 TrySplitDirectedTell(
                     displayText,
                     "From ",
                     out var prefixedIncomingSender,
                     out var prefixedIncomingMessage))
        {
            var conversationPlayer = NormalizeTellPilotName(
                prefixedIncomingSender);
            conversationPlayerName = conversationPlayer;
            tellDirection = ChatTellDirection.Incoming;
            playerName = conversationPlayer;
            displayText = prefixedIncomingMessage;
            playerToolTip = string.Concat(
                "Incoming Tell from ",
                conversationPlayer,
                " to ",
                localPilotName);
        }
        else if (string.IsNullOrWhiteSpace(playerName) &&
                 CanContainPlayerSpeech(entry.Channel, channelName) &&
                 TrySplitPlayerPrefix(
                displayText,
                out var parsedPlayerName,
                out var parsedMessage))
        {
            displayText = parsedMessage;

            if (string.Equals(
                    channelName,
                    "Tell",
                    StringComparison.OrdinalIgnoreCase))
            {
                var conversationPlayer = NormalizeTellPilotName(
                    parsedPlayerName);
                conversationPlayerName = conversationPlayer;

                if (entry.Channel == 16)
                {
                    tellDirection = ChatTellDirection.Outgoing;
                    playerName = localPilotName;
                    playerToolTip = string.Concat(
                        "Outgoing Tell from ",
                        localPilotName,
                        " to ",
                        conversationPlayer);
                }
                else
                {
                    tellDirection = ChatTellDirection.Incoming;
                    playerName = conversationPlayer;
                    playerToolTip = string.Concat(
                        "Incoming Tell from ",
                        conversationPlayer,
                        " to ",
                        localPilotName);
                }
            }
            else
            {
                playerName = parsedPlayerName;
                playerToolTip = "Message author";
            }
        }

        return new ChatMessagePresentation(
            channelName,
            playerName,
            conversationPlayerName,
            tellDirection,
            displayText,
            BuildChannelToolTip(
                entry.Channel,
                rawCategoryName,
                rawChannelName,
                channelName,
                derivedReason),
            playerToolTip,
            rawDefinition?.ColorOptionName);
    }

    public static string NormalizeTellPilotName(string value)
    {
        const string privateSuffix = " (private)";
        value = value.Trim();

        return value.EndsWith(
            privateSuffix,
            StringComparison.OrdinalIgnoreCase)
            ? value[..^privateSuffix.Length].TrimEnd()
            : value;
    }

    private static string BuildChannelToolTip(
        int rawMessageType,
        string rawCategoryName,
        string defaultChannelName,
        string displayedChannelName,
        string derivedReason)
    {
        var tooltip = string.Concat(
            "Game message type #",
            rawMessageType,
            " · ",
            rawCategoryName);

        if (!string.Equals(
                defaultChannelName,
                displayedChannelName,
                StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(derivedReason))
        {
            tooltip = string.Concat(
                tooltip,
                ". Displayed as ",
                displayedChannelName,
                " because it was ",
                derivedReason,
                ".");
        }

        return tooltip;
    }

    private static bool TryExtractPrefixedChannel(
        string text,
        out string channelName,
        out string displayText)
    {
        channelName = "";
        displayText = text;

        if (string.IsNullOrWhiteSpace(text) ||
            text[0] != '[')
        {
            return false;
        }

        var closingBracket = text.IndexOf(']');

        if (closingBracket <= 1 ||
            closingBracket > 64)
        {
            return false;
        }

        var observedName = text[1..closingBracket];

        if (!ChatSendDestinationCatalog.TryCanonicalizeObservedName(
                observedName,
                out channelName))
        {
            return false;
        }

        displayText = text[(closingBracket + 1)..].TrimStart();
        return true;
    }

    private static bool TryClassifyStandaloneMessage(
        string text,
        string localPilotName,
        out string channelName,
        out string displayText,
        out string playerName,
        out string? conversationPlayerName,
        out ChatTellDirection tellDirection,
        out string playerToolTip,
        out string reason)
    {
        channelName = "";
        displayText = text;
        playerName = "";
        conversationPlayerName = null;
        tellDirection = ChatTellDirection.None;
        playerToolTip = "";
        reason = "";

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (TryStripPrefix(
                text,
                "COMPUTER:",
                out displayText))
        {
            channelName = "Computer";
            reason = "identified from its COMPUTER prefix";
            return true;
        }

        if (TryStripPrefix(
                text,
                "SYSTEM:",
                out displayText))
        {
            channelName = "System";
            reason = "identified from its SYSTEM prefix";
            return true;
        }

        if (TryStripPrefix(
                text,
                "TIP:",
                out displayText))
        {
            channelName = "Tip";
            reason = "identified from its TIP prefix";
            return true;
        }

        if (TryStripPrefix(
                text,
                "Guild MOTD:",
                out displayText))
        {
            channelName = "Guild";
            reason = "identified as the guild message of the day";
            return true;
        }

        if (TrySplitDirectedTell(
                text,
                "To ",
                out var outgoingRecipient,
                out displayText))
        {
            channelName = "Tell";
            var conversationPlayer = NormalizeTellPilotName(
                outgoingRecipient);
            conversationPlayerName = conversationPlayer;
            tellDirection = ChatTellDirection.Outgoing;
            playerName = localPilotName;
            playerToolTip = string.Concat(
                "Outgoing Tell from ",
                localPilotName,
                " to ",
                conversationPlayer);
            reason = "identified from its outgoing tell prefix";
            return true;
        }

        if (TrySplitDirectedTell(
                text,
                "From ",
                out var incomingSender,
                out displayText))
        {
            channelName = "Tell";
            var conversationPlayer = NormalizeTellPilotName(
                incomingSender);
            conversationPlayerName = conversationPlayer;
            tellDirection = ChatTellDirection.Incoming;
            playerName = conversationPlayer;
            playerToolTip = string.Concat(
                "Incoming Tell from ",
                conversationPlayer,
                " to ",
                localPilotName);
            reason = "identified from its incoming tell prefix";
            return true;
        }

        if (text.Contains(
                "help new players",
                StringComparison.OrdinalIgnoreCase))
        {
            channelName = "New Players";
            reason = "identified as a New Players channel notice";
            return true;
        }

        if (text.Contains(
                " has been invited to the group",
                StringComparison.OrdinalIgnoreCase) ||
            text.Contains(
                " has joined the group",
                StringComparison.OrdinalIgnoreCase) ||
            text.Contains(
                " has left the group",
                StringComparison.OrdinalIgnoreCase) ||
            text.Contains(
                " is now the group leader",
                StringComparison.OrdinalIgnoreCase))
        {
            channelName = "Group";
            reason = "identified as a group lifecycle notice";
            return true;
        }

        if (text.Contains(
                "private channel",
                StringComparison.OrdinalIgnoreCase) &&
            (text.Contains(
                 "joined",
                 StringComparison.OrdinalIgnoreCase) ||
             text.Contains(
                 "left",
                 StringComparison.OrdinalIgnoreCase) ||
             text.Contains(
                 "subscribed",
                 StringComparison.OrdinalIgnoreCase) ||
             text.Contains(
                 "unsubscribed",
                 StringComparison.OrdinalIgnoreCase)))
        {
            channelName = "Private Channel";
            reason = "identified as a private-channel lifecycle notice";
            return true;
        }

        return false;
    }

    private static bool TrySplitDirectedTell(
        string text,
        string prefix,
        out string playerName,
        out string message)
    {
        playerName = "";
        message = text;

        if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var colon = text.IndexOf(':', prefix.Length);

        if (colon <= prefix.Length ||
            colon > prefix.Length + 64)
        {
            return false;
        }

        var candidate = text[prefix.Length..colon].Trim();

        if (!IsPlausiblePlayerName(candidate))
        {
            return false;
        }

        playerName = candidate;
        message = text[(colon + 1)..].TrimStart();
        return true;
    }

    private static bool TrySplitPlayerPrefix(
        string text,
        out string playerName,
        out string message)
    {
        playerName = "";
        message = text;
        var colon = text.IndexOf(':');

        if (colon <= 0 ||
            colon > 64)
        {
            return false;
        }

        var candidate = text[..colon].Trim();

        if (!IsPlausiblePlayerName(candidate))
        {
            return false;
        }

        playerName = candidate;
        message = text[(colon + 1)..].TrimStart();
        return true;
    }

    private static bool IsPlausiblePlayerName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > 64 ||
            value.Any(character =>
                char.IsControl(character) ||
                character is '[' or ']'))
        {
            return false;
        }

        return !string.Equals(
                   value,
                   "COMPUTER",
                   StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(
                   value,
                   "SYSTEM",
                   StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(
                   value,
                   "TIP",
                   StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(
                   value,
                   "Guild MOTD",
                   StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(
                   value,
                   "ERROR",
                   StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(
                   value,
                   "WARNING",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool CanContainPlayerSpeech(
        int rawMessageType,
        string channelName)
    {
        if (rawMessageType is
            1 or 2 or 3 or 4 or 7 or 8 or 9 or 10 or 11 or 12 or 13 or
            14 or 15 or 16)
        {
            return true;
        }

        return ChatSendDestinationCatalog.TryCanonicalizeObservedName(
            channelName,
            out _);
    }

    private static bool TryStripPrefix(
        string text,
        string prefix,
        out string remainder)
    {
        if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            remainder = text;
            return false;
        }

        remainder = text[prefix.Length..].TrimStart();
        return true;
    }

    private static IReadOnlyDictionary<int, ChatMessageTypeDefinition>
        BuildMessageTypes()
    {
        ChatMessageTypeDefinition[] definitions =
        [
            new(0, "Computer", "Context / Computer", "Color_Context"),
            new(1, "Out of Context", "Out of Context", "Color_OOC"),
            new(2, "Broadcast", "Broadcast", "Color_Broadcast"),
            new(3, "Tell", "Tell", "Color_Tell"),
            new(4, "Group", "Group", "Color_Group"),
            new(5, "NPC", "NPC", "Color_NPC"),
            new(6, "Tip", "Tip", "Color_Tip"),
            new(7, "Local", "Local", "Color_Local"),
            new(8, "Guild", "Guild", "Color_Guild"),
            new(9, "General", "General", "Color_General_Channel"),
            new(
                10,
                "Out of Context",
                "Out of Context channel",
                "Color_OOC_Channel"),
            new(11, "New Players", "New Players", "Color_Newbie_Channel"),
            new(12, "Market", "Market", "Color_Market_Channel"),
            new(
                13,
                "Private Channel",
                "Private channel",
                "Color_Private_Channel"),
            new(14, "Race", "Race channel", "Color_Race_Channel"),
            new(15, "Class", "Class channel", "Color_Class_Channel"),
            new(16, "Tell", "Echo", "Color_Echo"),
            new(
                17,
                "In-context Error",
                "In-context error",
                "Color_Error_Context"),
            new(
                18,
                "Out-of-context Error",
                "Out-of-context error",
                "Color_Error_OOC"),
            new(
                19,
                "System",
                "System error / System",
                "Color_Error_System"),
            new(20, "Combat", "Combat", "Color_Combat"),
            new(21, "Warning", "Warning", "Color_Warning"),
            new(22, "Attention", "Attention", "Color_Attention"),
            new(23, "Debug", "Debug", "Color_Debug"),
        ];

        return definitions.ToDictionary(definition => definition.Id);
    }

    private sealed record ChatMessageTypeDefinition(
        int Id,
        string DefaultChannelName,
        string RawCategoryName,
        string ColorOptionName);
}
