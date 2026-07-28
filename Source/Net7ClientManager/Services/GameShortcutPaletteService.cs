// ReSharper disable StringLiteralTypo
namespace Net7ClientManager.Services;

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Net7ClientManager.Models;
using Net7ClientManager.Observations;
using Net7ClientManager.Observations.Models;

internal sealed partial class GameShortcutPaletteService(
    GameKeyMapLocator keyMapLocator,
    SkillIniCatalogService skillIniCatalogService)
{
    public const string CommandIdPrefix = "shortcut:";

    private const string ShortcutArgument = "shortcut";
    private const string KindArgument = "shortcutKind";
    private const string BarArgument = "shortcutBar";
    private const string GroupArgument = "shortcutGroup";
    private const string ButtonArgument = "shortcutButton";
    private const string NameArgument = "shortcutName";

    public IReadOnlyList<FleetCommandDefinition> BuildCommands(
        ClientInstance client,
        ClientObservationSnapshot snapshot,
        ClientShortcutStateObservation shortcuts)
    {
        return
        [
            .. this.BuildEntries(
                    client,
                    snapshot,
                    shortcuts)
                .OrderBy(entry => entry.Group)
                .ThenBy(entry => entry.Bar)
                .ThenBy(entry => entry.Button)
                .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .Select(CreateCommand),
        ];
    }

    public IReadOnlyList<GameShortcutPaletteEntry> BuildEntries(
        ClientInstance client,
        ClientObservationSnapshot snapshot,
        ClientShortcutStateObservation shortcuts)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(shortcuts);

        var skillCatalog = skillIniCatalogService.GetCatalog(client);
        var diskEntries = this.ReadDiskEntries(
            client,
            snapshot,
            shortcuts);

        var runtimeSlots = shortcuts.Bars
            .Where(bar => bar.IsAvailable)
            .SelectMany(bar => bar.Slots)
            .ToList();

        // A complete runtime matrix is authoritative for placement and
        // identity. shortcut.ini remains a conservative fallback for an
        // unresolved slot and for the equipment-versus-cargo distinction,
        // which shares one native payload class.
        IReadOnlyList<ResolvedShortcutEntry> resolvedEntries =
            runtimeSlots.Count == 12
                ? ResolveRuntimeEntries(
                    runtimeSlots,
                    diskEntries,
                    snapshot)
                : diskEntries;

        return
        [
            .. resolvedEntries
                .Select(entry =>
                    CreatePaletteEntry(
                        entry,
                        shortcuts,
                        snapshot,
                        skillCatalog))
                .Where(entry =>
                    entry.SkillDetails?.IsIntrinsicallyActivatable != false),
        ];
    }

    private IReadOnlyList<ResolvedShortcutEntry> ReadDiskEntries(
        ClientInstance client,
        ClientObservationSnapshot snapshot,
        ClientShortcutStateObservation shortcuts)
    {
        if (!shortcuts.ShortcutIdentity.HasValue)
        {
            return [];
        }

        var shortcutIniPath = keyMapLocator.LocateShortcutIni(client);

        if (shortcutIniPath == null ||
            !File.Exists(shortcutIniPath) ||
            !GameShortcutIniDocument.TryRead(
                shortcutIniPath,
                out var document))
        {
            return [];
        }

        var entries = new List<ResolvedShortcutEntry>();

        if (!string.IsNullOrWhiteSpace(shortcuts.EquipmentSectionName))
        {
            entries.AddRange(
                ResolveItemShortcuts(
                    document,
                    shortcuts.EquipmentSectionName,
                    GameShortcutKind.Equipment,
                    snapshot.LocalPlayer.Inventory.EquippedSlots));
        }

        if (!string.IsNullOrWhiteSpace(shortcuts.CargoSectionName))
        {
            entries.AddRange(
                ResolveItemShortcuts(
                    document,
                    shortcuts.CargoSectionName,
                    GameShortcutKind.Cargo,
                    snapshot.LocalPlayer.Inventory.CargoSlots));
        }

        if (!string.IsNullOrWhiteSpace(shortcuts.SkillsSectionName))
        {
            entries.AddRange(
                ResolveSkillShortcuts(
                    document,
                    shortcuts.SkillsSectionName));
        }

        return
        [
            .. entries.Where(entry =>
                entry.Bar is 0 or 1 &&
                entry.Group is 0 or 1 &&
                entry.Button is >= 0 and <= 2),
        ];
    }

    private static IReadOnlyList<ResolvedShortcutEntry> ResolveRuntimeEntries(
        IReadOnlyList<ClientShortcutSlotObservation> runtimeSlots,
        IReadOnlyList<ResolvedShortcutEntry> diskEntries,
        ClientObservationSnapshot snapshot)
    {
        var diskByPlacement = diskEntries
            .GroupBy(entry => new ShortcutPlacement(
                entry.Bar,
                entry.Group,
                entry.Button))
            .ToDictionary(
                group => group.Key,
                group => group.First());

        List<ResolvedShortcutEntry> resolved = [];

        foreach (var slot in runtimeSlots.Where(slot => slot.IsOccupied))
        {
            if (TryCreateNativeEntry(
                    slot,
                    diskEntries,
                    snapshot,
                    out var nativeEntry))
            {
                resolved.Add(nativeEntry);
                continue;
            }

            var placement = new ShortcutPlacement(
                slot.Bar,
                slot.Group,
                slot.Button);

            if (diskByPlacement.TryGetValue(
                    placement,
                    out var diskEntry))
            {
                resolved.Add(diskEntry);
            }
        }

        return resolved;
    }

    private static bool TryCreateNativeEntry(
        ClientShortcutSlotObservation slot,
        IReadOnlyList<ResolvedShortcutEntry> diskEntries,
        ClientObservationSnapshot snapshot,
        out ResolvedShortcutEntry entry)
    {
        entry = default!;

        if (slot.Kind == ClientShortcutKind.Skill &&
            slot.PrimaryIdentity is >= 0)
        {
            var name = slot.ResolvedName;

            if (!slot.ResolvedNameIsExact)
            {
                var diskSkill = diskEntries.FirstOrDefault(candidate =>
                    candidate.Kind == GameShortcutKind.Skill &&
                    candidate.Bar == slot.Bar &&
                    candidate.Group == slot.Group &&
                    candidate.Button == slot.Button);

                if (diskSkill != null)
                {
                    name = diskSkill.Name;
                }
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            entry = new ResolvedShortcutEntry(
                GameShortcutKind.Skill,
                slot.Bar,
                slot.Group,
                slot.Button,
                name,
                SourceSlotIndex: null,
                PrimaryIdentity: slot.PrimaryIdentity,
                SecondaryIdentity: slot.SecondaryIdentity,
                FamilyName: slot.FamilyName,
                IconResourceName: slot.IconResourceName,
                TintRed: slot.TintRed,
                TintGreen: slot.TintGreen,
                TintBlue: slot.TintBlue,
                ItemTemplateId: null);

            return true;
        }

        if (slot.Kind is not (
                ClientShortcutKind.Equipment or
                ClientShortcutKind.Cargo) ||
            slot.PrimaryIdentity is not >= 0)
        {
            return false;
        }

        var sourceSlotIndex = slot.PrimaryIdentity.Value;
        var kind = ResolveInventoryKind(
            slot,
            diskEntries);

        if (!kind.HasValue)
        {
            return false;
        }

        var resolvedKind = kind.Value;
        var observedItem = resolvedKind == GameShortcutKind.Equipment
            ? snapshot.LocalPlayer.Inventory.EquippedSlots
                .FirstOrDefault(candidate =>
                    candidate.Slot == sourceSlotIndex &&
                    candidate.IsOccupied)
            : snapshot.LocalPlayer.Inventory.CargoSlots
                .FirstOrDefault(candidate =>
                    candidate.Slot == sourceSlotIndex &&
                    candidate.IsOccupied);

        var itemTemplateId = observedItem?.ItemTemplateId ??
                             GetCandidateTemplateId(
                                 slot,
                                 resolvedKind);
        var name2 = ClientItemTemplateNameResolver.GetKnownName(
                       itemTemplateId) ??
                   GetCandidateName(
                       slot,
                       resolvedKind);

        if (string.IsNullOrWhiteSpace(name2))
        {
            name2 = slot.ResolvedName;
        }

        if (string.IsNullOrWhiteSpace(name2))
        {
            return false;
        }

        entry = new ResolvedShortcutEntry(
            resolvedKind,
            slot.Bar,
            slot.Group,
            slot.Button,
            name2,
            SourceSlotIndex: sourceSlotIndex,
            PrimaryIdentity: slot.PrimaryIdentity,
            SecondaryIdentity: null,
            FamilyName: "",
            IconResourceName: "",
            TintRed: null,
            TintGreen: null,
            TintBlue: null,
            ItemTemplateId: itemTemplateId);

        return true;
    }

    private static GameShortcutKind? ResolveInventoryKind(
        ClientShortcutSlotObservation slot,
        IReadOnlyList<ResolvedShortcutEntry> diskEntries)
    {
        if (slot.InventoryCollection ==
            ClientInventoryCollectionKind.Cargo)
        {
            return GameShortcutKind.Cargo;
        }

        if (slot.InventoryCollection ==
            ClientInventoryCollectionKind.Equipped)
        {
            return GameShortcutKind.Equipment;
        }

        var sourceSlotIndex = slot.PrimaryIdentity;

        if (!sourceSlotIndex.HasValue)
        {
            return null;
        }

        var candidates = diskEntries
            .Where(candidate =>
                (candidate.Kind == GameShortcutKind.Equipment ||
                 candidate.Kind == GameShortcutKind.Cargo) &&
                candidate.SourceSlotIndex == sourceSlotIndex)
            .ToList();

        var exactTemplateMatches = candidates
            .Where(candidate =>
                candidate.ItemTemplateId.HasValue &&
                (candidate.Kind == GameShortcutKind.Equipment
                    ? candidate.ItemTemplateId ==
                      slot.EquippedItemTemplateIdCandidate
                    : candidate.ItemTemplateId ==
                      slot.CargoItemTemplateIdCandidate))
            .Select(candidate => candidate.Kind)
            .Distinct()
            .ToList();

        if (exactTemplateMatches.Count == 1)
        {
            return exactTemplateMatches[0];
        }

        var distinctKinds = candidates
            .Select(candidate => candidate.Kind)
            .Distinct()
            .ToList();

        if (distinctKinds.Count == 1)
        {
            return distinctKinds[0];
        }

        var placementMatch = candidates.FirstOrDefault(candidate =>
            candidate.Bar == slot.Bar &&
            candidate.Group == slot.Group &&
            candidate.Button == slot.Button);

        if (placementMatch != null)
        {
            return placementMatch.Kind;
        }

        if (slot.EquippedItemTemplateIdCandidate.HasValue &&
            !slot.CargoItemTemplateIdCandidate.HasValue)
        {
            return GameShortcutKind.Equipment;
        }

        if (slot.CargoItemTemplateIdCandidate.HasValue &&
            !slot.EquippedItemTemplateIdCandidate.HasValue)
        {
            return GameShortcutKind.Cargo;
        }

        return null;
    }

    private static int? GetCandidateTemplateId(
        ClientShortcutSlotObservation slot,
        GameShortcutKind kind)
    {
        return kind == GameShortcutKind.Equipment
            ? slot.EquippedItemTemplateIdCandidate
            : slot.CargoItemTemplateIdCandidate;
    }

    private static string GetCandidateName(
        ClientShortcutSlotObservation slot,
        GameShortcutKind kind)
    {
        return kind == GameShortcutKind.Equipment
            ? slot.EquippedItemNameCandidate
            : slot.CargoItemNameCandidate;
    }

    public static bool IsShortcutCommand(
        FleetCommandDefinition command)
    {
        return command.Arguments.TryGetValue(
                   ShortcutArgument,
                   out var value) &&
               string.Equals(
                   value,
                   "true",
                   StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryCreateInvocation(
        FleetCommandDefinition command,
        out GameShortcutInvocation invocation,
        out string error)
    {
        invocation = default;
        error = "";

        if (!IsShortcutCommand(command))
        {
            error = "The command is not a shortcut command.";
            return false;
        }

        if (!TryGetIntArgument(
                command,
                BarArgument,
                out var bar) ||
            bar is < 0 or > 1)
        {
            error = "The shortcut command has an invalid bar.";
            return false;
        }

        if (!TryGetIntArgument(
                command,
                GroupArgument,
                out var group) ||
            group is < 0 or > 1)
        {
            error = "The shortcut command has an invalid bank.";
            return false;
        }

        if (!TryGetIntArgument(
                command,
                ButtonArgument,
                out var button) ||
            button is < 0 or > 2)
        {
            error = "The shortcut command has an invalid button.";
            return false;
        }

        command.Arguments.TryGetValue(
            NameArgument,
            out var name);

        invocation = new GameShortcutInvocation(
            bar,
            group,
            button,
            string.IsNullOrWhiteSpace(name)
                ? command.Label
                : name);

        return true;
    }

    private static IEnumerable<ResolvedShortcutEntry> ResolveSkillShortcuts(
        GameShortcutIniDocument document,
        string sectionName)
    {
        foreach (var shortcut in document.GetEntries(sectionName))
        {
            var tokens = ParseShortcutKeyTokens(shortcut.Key);

            if (tokens.Count == 0)
            {
                continue;
            }

            var abilityName = tokens.Count >= 3
                ? tokens[2]
                : tokens.Count >= 2
                    ? tokens[1]
                    : string.Concat("Skill ", tokens[0]);

            if (string.IsNullOrWhiteSpace(abilityName))
            {
                continue;
            }

            int? primaryIdentity = null;

            if (int.TryParse(
                    tokens[0],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var parsedPrimaryIdentity))
            {
                primaryIdentity = parsedPrimaryIdentity;
            }

            yield return new ResolvedShortcutEntry(
                GameShortcutKind.Skill,
                shortcut.Bar,
                shortcut.Group,
                shortcut.Button,
                abilityName.Trim(),
                SourceSlotIndex: null,
                PrimaryIdentity: primaryIdentity,
                SecondaryIdentity: null,
                FamilyName: tokens.Count >= 2
                    ? tokens[1].Trim()
                    : "",
                IconResourceName: "",
                TintRed: null,
                TintGreen: null,
                TintBlue: null,
                ItemTemplateId: null);
        }
    }

    private static IEnumerable<ResolvedShortcutEntry> ResolveItemShortcuts(
        GameShortcutIniDocument document,
        string sectionName,
        GameShortcutKind kind,
        IReadOnlyList<ClientInventoryItemObservation> slots)
    {
        foreach (var shortcut in document.GetEntries(sectionName))
        {
            var tokens = ParseShortcutKeyTokens(shortcut.Key);

            if (tokens.Count == 0 ||
                !int.TryParse(
                    tokens[0],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var slotIndex))
            {
                continue;
            }

            var item = slots.FirstOrDefault(candidate =>
                candidate.Slot == slotIndex &&
                candidate.IsOccupied);

            if (item == null)
            {
                continue;
            }

            var itemName = ClientItemTemplateNameResolver.GetKnownName(
                item.ItemTemplateId);

            if (string.IsNullOrWhiteSpace(itemName))
            {
                itemName = kind == GameShortcutKind.Equipment
                    ? string.Create(
                        CultureInfo.InvariantCulture,
                        $"Equipment slot {slotIndex}")
                    : string.Create(
                        CultureInfo.InvariantCulture,
                        $"Cargo slot {slotIndex}");
            }

            yield return new ResolvedShortcutEntry(
                kind,
                shortcut.Bar,
                shortcut.Group,
                shortcut.Button,
                itemName,
                SourceSlotIndex: slotIndex,
                PrimaryIdentity: slotIndex,
                SecondaryIdentity: null,
                FamilyName: "",
                IconResourceName: "",
                TintRed: null,
                TintGreen: null,
                TintBlue: null,
                ItemTemplateId: item.ItemTemplateId);
        }
    }

    private static GameShortcutPaletteEntry CreatePaletteEntry(
        ResolvedShortcutEntry entry,
        ClientShortcutStateObservation shortcuts,
        ClientObservationSnapshot snapshot,
        SkillIniCatalog skillCatalog)
    {
        var visibleKey = entry.Bar * 3 + entry.Button + 1;
        var keyLabel = entry.Group == 0
            ? visibleKey.ToString(CultureInfo.InvariantCulture)
            : string.Concat(
                "Alt+",
                visibleKey.ToString(CultureInfo.InvariantCulture));
        var currentGroup = shortcuts.GetBar(entry.Bar)?.CurrentGroup;
        var name = entry.Name;
        var familyName = entry.FamilyName;
        var primaryIdentity = entry.PrimaryIdentity;
        var secondaryIdentity = entry.SecondaryIdentity;
        var iconResourceName = entry.IconResourceName;
        var tintRed = entry.TintRed;
        var tintGreen = entry.TintGreen;
        var tintBlue = entry.TintBlue;
        SkillShortcutDetails? skillDetails = null;
        ItemShortcutDetails? itemDetails = null;

        if (entry.Kind is
            GameShortcutKind.Equipment or
            GameShortcutKind.Cargo)
        {
            itemDetails = ItemShortcutDetails.Create(
                entry.Kind,
                FindObservedItem(
                    entry,
                    snapshot));

            if (!string.IsNullOrWhiteSpace(
                    itemDetails?.Name))
            {
                name = itemDetails.Name;
            }
        }

        if (entry.Kind == GameShortcutKind.Skill &&
            skillCatalog.TryResolveAbility(
                entry.SecondaryIdentity,
                entry.Name,
                entry.FamilyName,
                out var definition))
        {
            name = definition.Name;
            familyName = definition.SkillFamilyName;
            primaryIdentity ??= definition.SkillFamilyId;
            secondaryIdentity = definition.AbilityId;

            if (string.IsNullOrWhiteSpace(iconResourceName))
            {
                iconResourceName = definition.IconResourceName;
            }

            tintRed ??= definition.TintRed;
            tintGreen ??= definition.TintGreen;
            tintBlue ??= definition.TintBlue;

            skillDetails = new SkillShortcutDetails
            {
                AbilityId = definition.AbilityId,
                IsIntrinsicallyActivatable =
                    definition.IsIntrinsicallyActivatable,
                SkillFamilyName = definition.SkillFamilyName,
                SkillFamilyId = definition.SkillFamilyId,
                Rank = definition.MinimumSkillLevel,
                ListedEnergyCost = definition.ListedEnergyCost,
                MaximumReactorCostPercent =
                    definition.MaximumReactorCostPercent,
                Range = definition.ResolveRange(definition.MinimumSkillLevel),
                Description = FirstNonEmpty(
                    definition.Description,
                    definition.RankDescription,
                    definition.SkillFamilyDescription),
                Semantics = definition.Semantics,
            };
        }

        return new GameShortcutPaletteEntry(
            entry.Kind,
            skillDetails?.Semantics.Category ??
            ClassifyShortcut(entry.Kind, name),
            entry.Bar,
            entry.Group,
            entry.Button,
            visibleKey,
            keyLabel,
            name,
            currentGroup == entry.Group,
            entry.SourceSlotIndex)
        {
            PrimaryIdentity = primaryIdentity,
            SecondaryIdentity = secondaryIdentity,
            FamilyName = familyName,
            IconResourceName = iconResourceName,
            TintRed = tintRed,
            TintGreen = tintGreen,
            TintBlue = tintBlue,
            ItemTemplateId = entry.ItemTemplateId,
            SkillDetails = skillDetails,
            ItemDetails = itemDetails,
        };
    }

    private static ClientInventoryItemObservation? FindObservedItem(
        ResolvedShortcutEntry entry,
        ClientObservationSnapshot snapshot)
    {
        var slots = entry.Kind == GameShortcutKind.Equipment
            ? snapshot.LocalPlayer.Inventory.EquippedSlots
            : snapshot.LocalPlayer.Inventory.CargoSlots;

        if (entry.SourceSlotIndex.HasValue)
        {
            return slots.FirstOrDefault(slot =>
                slot.Slot == entry.SourceSlotIndex.Value &&
                slot.IsOccupied &&
                (!entry.ItemTemplateId.HasValue ||
                 slot.ItemTemplateId == entry.ItemTemplateId));
        }

        if (!entry.ItemTemplateId.HasValue)
        {
            return null;
        }

        var matches = slots
            .Where(slot =>
                slot.IsOccupied &&
                slot.ItemTemplateId == entry.ItemTemplateId)
            .Take(2)
            .ToArray();

        return matches.Length == 1
            ? matches[0]
            : null;
    }

    private static string FirstNonEmpty(
        params string[] values)
    {
        return values.FirstOrDefault(value =>
            !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";
    }

    private static GameShortcutActionCategory ClassifyShortcut(
        GameShortcutKind kind,
        string name)
    {
        if (ContainsAny(
                name,
                "anger",
                "enrage",
                "mass field",
                "gravity",
                "energy drain",
                "energy leech",
                "shield drain",
                "shield leech",
                "shield sap",
                "hack",
                "teleport enemy",
                "voltoi",
                "explosive",
                "plasma",
                "missile",
                "beam",
                "projectile"))
        {
            return GameShortcutActionCategory.Offensive;
        }

        if (ContainsAny(
                name,
                "recharge",
                "repair",
                "jumpstart",
                "regenerate equipment",
                "group sap",
                "shield restore",
                "hull patch",
                "reactor recharge"))
        {
            return GameShortcutActionCategory.Defensive;
        }

        if (ContainsAny(
                name,
                "cloak",
                "wormhole",
                "summon",
                "navigate",
                "fold space",
                "shield charge",
                "normandy",
                "rally",
                "boost",
                "buff"))
        {
            return GameShortcutActionCategory.Buff;
        }

        if (kind != GameShortcutKind.Skill)
        {
            return GameShortcutActionCategory.Item;
        }

        return GameShortcutActionCategory.Utility;
    }

    private static bool ContainsAny(
        string value,
        params string[] needles)
    {
        return needles.Any(needle =>
            value.Contains(
                needle,
                StringComparison.OrdinalIgnoreCase));
    }

    private static FleetCommandDefinition CreateCommand(
        GameShortcutPaletteEntry entry)
    {
        var safeName = NormalizeCommandIdPart(entry.Name);

        return new FleetCommandDefinition
        {
            Id = string.Create(
                CultureInfo.InvariantCulture,
                $"{CommandIdPrefix}{entry.Kind}:{entry.Bar}:{entry.Group}:{entry.Button}:{safeName}"),
            Label = string.Concat(entry.KeyLabel, " ", entry.Name),
            ShowInOverlay = true,
            Category = FleetCommandCategory.Combat,
            Arguments =
            {
                [ShortcutArgument] = "true",
                [KindArgument] = entry.Kind.ToString(),
                [BarArgument] = entry.Bar.ToString(CultureInfo.InvariantCulture),
                [GroupArgument] = entry.Group.ToString(CultureInfo.InvariantCulture),
                [ButtonArgument] = entry.Button.ToString(CultureInfo.InvariantCulture),
                [NameArgument] = entry.Name,
            },
        };
    }

    private static bool TryGetIntArgument(
        FleetCommandDefinition command,
        string key,
        out int value)
    {
        value = 0;

        return command.Arguments.TryGetValue(key, out var text) &&
               int.TryParse(
                   text,
                   NumberStyles.Integer,
                   CultureInfo.InvariantCulture,
                   out value);
    }

    private static IReadOnlyList<string> ParseShortcutKeyTokens(
        string value)
    {
        var matches = ShortcutKeyTokenRegex().Matches(value);

        if (matches.Count == 0)
        {
            return [];
        }

        return
        [
            .. matches
                .Select(match => match.Groups[1].Value.Trim())
                .Where(token => token.Length > 0),
        ];
    }

    private static string NormalizeCommandIdPart(string value)
    {
        var builder = new StringBuilder(value.Length);

        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                continue;
            }

            if (builder.Length == 0 ||
                builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        return builder.ToString().Trim('-');
    }

    [GeneratedRegex("<([^>]*)/>", RegexOptions.CultureInvariant)]
    private static partial Regex ShortcutKeyTokenRegex();

    private readonly record struct ShortcutPlacement(
        int Bar,
        int Group,
        int Button);

    private sealed record ResolvedShortcutEntry(
        GameShortcutKind Kind,
        int Bar,
        int Group,
        int Button,
        string Name,
        int? SourceSlotIndex,
        int? PrimaryIdentity,
        int? SecondaryIdentity,
        string FamilyName,
        string IconResourceName,
        float? TintRed,
        float? TintGreen,
        float? TintBlue,
        int? ItemTemplateId);
}

public sealed record GameShortcutPaletteEntry(
    GameShortcutKind Kind,
    GameShortcutActionCategory Category,
    int Bar,
    int Group,
    int Button,
    int VisibleKey,
    string KeyLabel,
    string Name,
    bool IsVisible,
    int? SourceSlotIndex)
{
    public int? PrimaryIdentity { get; init; }

    public int? SecondaryIdentity { get; init; }

    public string FamilyName { get; init; } = "";

    public string IconResourceName { get; init; } = "";

    public float? TintRed { get; init; }

    public float? TintGreen { get; init; }

    public float? TintBlue { get; init; }

    public int? ItemTemplateId { get; init; }

    internal SkillShortcutDetails? SkillDetails { get; init; }

    internal ItemShortcutDetails? ItemDetails { get; init; }

    public bool IsIndividualWeapon =>
        this.Kind == GameShortcutKind.Equipment &&
        this.SourceSlotIndex is >= 3 and <= 5;

    public GameShortcutInvocation ToInvocation()
    {
        return new GameShortcutInvocation(
            this.Bar,
            this.Group,
            this.Button,
            this.Name);
    }
}

public readonly record struct GameShortcutInvocation(
    int Bar,
    int Group,
    int Button,
    string Name)
{
    public int VisibleKey => this.Bar * 3 + this.Button + 1;
}

public enum GameShortcutKind
{
    Skill,
    Equipment,
    Cargo,
}

public enum GameShortcutActionCategory
{
    Offensive,
    Defensive,
    Buff,
    Utility,
    Item,
}
