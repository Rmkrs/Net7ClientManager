namespace Net7ClientManager.Models;

using System.Globalization;

public sealed class ChatCompanionSettings
{
    public bool OrderViewsByRecentMessage { get; set; }

    public bool KeepFirstViewAtTop { get; set; }

    public bool CreateTellConversationViews { get; set; }

    public bool UseGameChatColors { get; set; } = true;

    public bool ComposerAtTop { get; set; }

    public List<ChatCompanionViewSettings> Views { get; set; } = [];

    public List<string> SentMessageHistory { get; set; } = [];

    public Dictionary<string, ChatCompanionPilotSettings> Pilots
    { get; set; } = new(StringComparer.Ordinal);

    public List<Guid> OpenSlotIds { get; set; } = [];

    public void EnsureDefaults()
    {
        this.Views ??= [];
        this.SentMessageHistory ??= [];
        this.Pilots ??= new Dictionary<string, ChatCompanionPilotSettings>(
            StringComparer.Ordinal);
        this.OpenSlotIds ??= [];
        this.OpenSlotIds = [.. this.OpenSlotIds
            .Where(slotId => slotId != Guid.Empty)
            .Distinct()];

        if (this.Views.Count == 0)
        {
            this.Views.Add(new ChatCompanionViewSettings
            {
                Name = "All",
                IncludeAllMessages = true,
            });
        }

        HashSet<Guid> usedIds = [];

        foreach (var view in this.Views)
        {
            if (view.Id == Guid.Empty || !usedIds.Add(view.Id))
            {
                view.Id = Guid.NewGuid();
                usedIds.Add(view.Id);
            }

            view.Name = string.IsNullOrWhiteSpace(view.Name)
                ? "Chat"
                : view.Name.Trim();
            view.IncludedChannels ??= [];
            view.IncludedChannels = [.. view.IncludedChannels
                .Where(channel => !string.IsNullOrWhiteSpace(channel))
                .Select(channel => channel.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)];
        }

        var normalizedPilots =
            new Dictionary<string, ChatCompanionPilotSettings>(
                StringComparer.Ordinal);

        foreach (var pair in this.Pilots)
        {
            var key = pair.Key?.Trim();

            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            var pilot = pair.Value ?? new ChatCompanionPilotSettings();
            pilot.EnsureDefaults();
            normalizedPilots[key] = pilot;
        }

        this.Pilots = normalizedPilots;
        this.SentMessageHistory = [.. this.SentMessageHistory
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .Select(message => message.TrimEnd())
            .Distinct(StringComparer.Ordinal)
            .Take(100)];
    }

    public ChatCompanionPilotSettings GetOrCreatePilot(uint characterId)
    {
        this.EnsureDefaults();
        var key = characterId.ToString(CultureInfo.InvariantCulture);

        if (!this.Pilots.TryGetValue(key, out var pilot))
        {
            pilot = new ChatCompanionPilotSettings();
            this.Pilots.Add(key, pilot);
        }

        pilot.EnsureDefaults();
        return pilot;
    }

    internal static List<string> NormalizeTellPilotNames(
        IEnumerable<string> names,
        int maximumCount)
    {
        return [.. names
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(NormalizeTellPilotName)
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maximumCount)];
    }

    private static string NormalizeTellPilotName(string name)
    {
        const string privateSuffix = " (private)";
        name = name.Trim();

        return name.EndsWith(
            privateSuffix,
            StringComparison.OrdinalIgnoreCase)
            ? name[..^privateSuffix.Length].TrimEnd()
            : name;
    }
}

public sealed class ChatCompanionPilotSettings
{
    public List<string> RecentTellRecipients { get; set; } = [];

    public List<string> TellConversationPilots { get; set; } = [];

    public void EnsureDefaults()
    {
        this.RecentTellRecipients ??= [];
        this.TellConversationPilots ??= [];
        this.RecentTellRecipients =
            ChatCompanionSettings.NormalizeTellPilotNames(
                this.RecentTellRecipients,
                maximumCount: 30);
        this.TellConversationPilots =
            ChatCompanionSettings.NormalizeTellPilotNames(
                this.TellConversationPilots,
                maximumCount: 200);
    }
}

public sealed class ChatCompanionViewSettings
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "Chat";

    public bool IncludeAllMessages { get; set; }

    public bool ShowUnreadBadge { get; set; } = true;

    public List<string> IncludedChannels { get; set; } = [];
}
