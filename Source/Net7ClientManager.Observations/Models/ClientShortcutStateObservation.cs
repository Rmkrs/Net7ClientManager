namespace Net7ClientManager.Observations.Models;

public sealed record ClientShortcutStateObservation
{
    public bool IsAvailable { get; init; }

    public string Status { get; init; } = "";

    public ulong? ShortcutIdentity { get; init; }

    public string? SkillsSectionName { get; init; }

    public string? EquipmentSectionName { get; init; }

    public string? CargoSectionName { get; init; }

    public uint MainViewAddress { get; init; }

    public uint CockpitControllerAddress { get; init; }

    public uint ShortcutBankAddress { get; init; }

    public bool SkillCatalogIsAvailable { get; init; }

    public string SkillCatalogStatus { get; init; } = "";

    public int SkillDefinitionCount { get; init; }

    public bool AbilityCatalogIsAvailable { get; init; }

    public string AbilityCatalogStatus { get; init; } = "";

    public int AbilityDefinitionCount { get; init; }

    public IReadOnlyList<ClientShortcutBarObservation> Bars { get; init; } = [];

    public ClientShortcutBarObservation? GetBar(int bar)
    {
        return this.Bars.FirstOrDefault(candidate => candidate.Bar == bar);
    }

    public static ClientShortcutStateObservation Unavailable(
        string status,
        ulong? shortcutIdentity = null)
    {
        return new ClientShortcutStateObservation
        {
            Status = status,
            ShortcutIdentity = shortcutIdentity,
            SkillsSectionName = shortcutIdentity.HasValue
                ? string.Concat(shortcutIdentity.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), "_Skills")
                : null,
            EquipmentSectionName = shortcutIdentity.HasValue
                ? string.Concat(shortcutIdentity.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), "_PDA_EQUIP")
                : null,
            CargoSectionName = shortcutIdentity.HasValue
                ? string.Concat(shortcutIdentity.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), "_PDA_CARGO")
                : null,
        };
    }
}
