namespace Net7ClientManager.SkillPlanning;

using System.Globalization;
using System.Text;
using Net7ClientManager.Observations;
using Net7ClientManager.Observations.Models;
using Net7ClientManager.PilotArchive;

internal sealed class SkillBuildBoardPresentationBuilder
{
    private readonly SkillPlannerCatalog catalog;

    public SkillBuildBoardPresentationBuilder(
        SkillPlannerCatalog catalog)
    {
        this.catalog = catalog ??
            throw new ArgumentNullException(nameof(catalog));
    }

    public SkillBuildBoardPresentation Build(
        ClientObservationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.LifecycleState != ClientLifecycleState.InGame)
        {
            return SkillBuildBoardPresentation.Hidden;
        }

        var identity = ClientLiveCharacterIdentityResolver.Resolve(snapshot);
        var progression = snapshot.LocalPlayer.CharacterProgression;
        var pilotName = string.IsNullOrWhiteSpace(identity.Name)
            ? "Current pilot"
            : identity.Name.Trim();
        var characterId = identity.CharacterObjectId.GetValueOrDefault();

        if (!this.TryResolveProfession(
                identity,
                progression,
                out var profession))
        {
            return CreateUnavailable(
                snapshot,
                characterId,
                pilotName,
                identity.Profession,
                "The live profession could not be matched to the skill catalog.");
        }

        List<string> missing = [];

        var combatLevel = progression.CombatLevel ?? identity.CombatLevel;
        var exploreLevel = progression.ExploreLevel ?? identity.ExploreLevel;
        var tradeLevel = progression.TradeLevel ?? identity.TradeLevel;

        if (characterId == 0)
        {
            missing.Add("character identity");
        }

        if (!combatLevel.HasValue)
        {
            missing.Add("combat level");
        }

        if (!exploreLevel.HasValue)
        {
            missing.Add("explore level");
        }

        if (!tradeLevel.HasValue)
        {
            missing.Add("trade level");
        }

        if (!progression.SkillPoints.HasValue)
        {
            missing.Add("available skill points");
        }

        if (!progression.Skills.IsAvailable)
        {
            missing.Add("live skills");
        }

        if (missing.Count > 0)
        {
            return CreateUnavailable(
                snapshot,
                characterId,
                pilotName,
                profession.DisplayName,
                string.Concat(
                    "Waiting for ",
                    string.Join(", ", missing),
                    "."));
        }

        Dictionary<int, SkillPlannerOwnedSkill> ownedSkills = [];

        foreach (var observedSkill in progression.Skills.Skills)
        {
            if (!this.catalog.TryGetSkill(observedSkill.Index, out _))
            {
                continue;
            }

            ownedSkills[observedSkill.Index] =
                new SkillPlannerOwnedSkill
                {
                    SkillId = observedSkill.Index,
                    Name = observedSkill.Name,
                    CurrentRank = observedSkill.CurrentRank,
                    MaximumRank = observedSkill.MaximumRank,
                    QuestOnlyLevels = observedSkill.QuestOnlyLevels,
                    AvailabilityCode = observedSkill.AvailabilityCode,
                };
        }

        var baseline = new SkillPlannerCharacterBaseline
        {
            ProfessionIndex = profession.Index,
            CombatLevel = combatLevel!.Value,
            ExploreLevel = exploreLevel!.Value,
            TradeLevel = tradeLevel!.Value,
            // RPGInfo.SkillPoints is authoritative. It already includes bonus
            // points and must never be reconstructed from levels.
            AvailableSkillPoints = progression.SkillPoints!.Value,
            HullTier = progression.HullTier,
            Skills = ownedSkills,
        };

        var skills = this.BuildSkillRows(baseline);
        var equipmentBaseline = SkillBuildEquipmentBaselineFactory.Create(
            snapshot.LocalPlayer.Inventory,
            snapshot.LocalPlayer.SecureInventory);
        var equipment = BuildEquipmentRows(equipmentBaseline);

        return new SkillBuildBoardPresentation
        {
            IsVisible = true,
            Fingerprint = BuildFingerprint(
                characterId,
                pilotName,
                profession,
                baseline,
                skills,
                equipment,
                equipmentBaseline),
            CharacterId = characterId,
            PilotName = pilotName,
            ProfessionName = profession.DisplayName,
            ProfessionTag = profession.Tag,
            CombatLevel = baseline.CombatLevel,
            ExploreLevel = baseline.ExploreLevel,
            TradeLevel = baseline.TradeLevel,
            AvailableSkillPoints = baseline.AvailableSkillPoints,
            HasBuildContext = true,
            HasAvailableSkillPoints = true,
            HasHullTier = baseline.HullTier.HasValue,
            ContextText = "Live pilot",
            StatusText = equipmentBaseline.IsAvailable
                ? "Live character build context is ready."
                : string.Concat(
                    "Skills are ready. Equipment is unavailable: ",
                    equipmentBaseline.Status),
            Baseline = baseline,
            EquipmentBaseline = equipmentBaseline,
            Skills = skills,
            Equipment = equipment,
        };
    }

    public SkillBuildBoardPresentation Build(
        PilotArchivePilotDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);

        var pilot = details.Pilot;
        var identity = PilotArchiveIdentityPresentation.Resolve(
            pilot.Race,
            pilot.Profession);
        var pilotName = string.IsNullOrWhiteSpace(pilot.Name)
            ? "Archived pilot"
            : pilot.Name.Trim();

        if (!this.TryResolveProfession(
                pilot,
                identity,
                out var profession))
        {
            return CreateArchiveUnavailable(
                pilot,
                pilotName,
                "The archived profession could not be matched to the skill catalog.");
        }

        List<string> missing = [];

        if (pilot.CharacterId == 0)
        {
            missing.Add("character identity");
        }

        if (!pilot.CombatLevel.HasValue)
        {
            missing.Add("combat level");
        }

        if (!pilot.ExploreLevel.HasValue)
        {
            missing.Add("explore level");
        }

        if (!pilot.TradeLevel.HasValue)
        {
            missing.Add("trade level");
        }

        if (details.Skills.Count == 0)
        {
            missing.Add("stored skills");
        }

        if (missing.Count > 0)
        {
            return CreateArchiveUnavailable(
                pilot,
                pilotName,
                string.Concat(
                    "Pilot Archive does not yet contain ",
                    string.Join(", ", missing),
                    "."));
        }

        Dictionary<int, SkillPlannerOwnedSkill> ownedSkills = [];

        foreach (var archivedSkill in details.Skills)
        {
            if (!this.catalog.TryGetSkill(archivedSkill.Index, out _))
            {
                continue;
            }

            ownedSkills[archivedSkill.Index] =
                new SkillPlannerOwnedSkill
                {
                    SkillId = archivedSkill.Index,
                    Name = archivedSkill.Name,
                    CurrentRank = archivedSkill.CurrentRank,
                    MaximumRank = archivedSkill.MaximumRank,
                    QuestOnlyLevels = archivedSkill.QuestOnlyLevels,
                    AvailabilityCode = archivedSkill.IsActive ? 1 : 0,
                };
        }

        // Builds 1.4.1 persists both values for every newly observed pilot.
        // Pre-release version-2 archives can contain nulls until that pilot is
        // logged in once more; keep those local rows usable with bounded
        // temporary defaults instead of surfacing an incomplete feature.
        var availableSkillPoints = Math.Max(
            0,
            pilot.AvailableSkillPoints.GetValueOrDefault());
        var archivedHullTier = pilot.HullTier;
        var hullTier = archivedHullTier is >= 1 and <= 7
            ? archivedHullTier.Value
            : 1;

        var baseline = new SkillPlannerCharacterBaseline
        {
            ProfessionIndex = profession.Index,
            CombatLevel = pilot.CombatLevel!.Value,
            ExploreLevel = pilot.ExploreLevel!.Value,
            TradeLevel = pilot.TradeLevel!.Value,
            AvailableSkillPoints = availableSkillPoints,
            HullTier = hullTier,
            Skills = ownedSkills,
        };

        var skills = this.BuildSkillRows(baseline);
        var equipmentBaseline = SkillBuildEquipmentBaselineFactory.Create(
            details.EquipmentSlots,
            details.CargoSlots,
            details.VaultSlots);
        var equipment = BuildEquipmentRows(equipmentBaseline);
        var observedText = pilot.LastObservedAt == default
            ? "Pilot Archive"
            : string.Create(
                CultureInfo.CurrentCulture,
                $"Pilot Archive · observed {pilot.LastObservedAt.LocalDateTime:g}");

        return new SkillBuildBoardPresentation
        {
            IsVisible = true,
            Fingerprint = string.Concat(
                "archive:",
                BuildFingerprint(
                    pilot.CharacterId,
                    pilotName,
                    profession,
                    baseline,
                    skills,
                    equipment,
                    equipmentBaseline),
                "|observed:",
                pilot.LastObservedAt.ToUnixTimeMilliseconds()
                    .ToString(CultureInfo.InvariantCulture)),
            CharacterId = pilot.CharacterId,
            PilotName = pilotName,
            ProfessionName = profession.DisplayName,
            ProfessionTag = profession.Tag,
            CombatLevel = baseline.CombatLevel,
            ExploreLevel = baseline.ExploreLevel,
            TradeLevel = baseline.TradeLevel,
            AvailableSkillPoints = baseline.AvailableSkillPoints,
            HasBuildContext = true,
            IsArchivedContext = true,
            HasAvailableSkillPoints = true,
            HasHullTier = true,
            ContextText = observedText,
            StatusText = "Stored Pilot Archive build context is ready.",
            Baseline = baseline,
            EquipmentBaseline = equipmentBaseline,
            Skills = skills,
            Equipment = equipment,
        };
    }

    private bool TryResolveProfession(
        PilotArchivePilotSnapshot pilot,
        (string? Race, string? Profession) identity,
        out SkillPlannerProfessionDefinition profession)
    {
        if (!string.IsNullOrWhiteSpace(pilot.ProfessionCode) &&
            this.catalog.TryGetProfession(
                pilot.ProfessionCode.Trim(),
                out profession))
        {
            return true;
        }

        profession = this.catalog.Professions.FirstOrDefault(candidate =>
            (string.IsNullOrWhiteSpace(identity.Race) ||
             string.Equals(
                 candidate.RaceName,
                 identity.Race,
                 StringComparison.OrdinalIgnoreCase)) &&
            (string.Equals(
                 candidate.ProfessionName,
                 identity.Profession,
                 StringComparison.OrdinalIgnoreCase) ||
             string.Equals(
                 candidate.DisplayName,
                 pilot.Profession,
                 StringComparison.OrdinalIgnoreCase)))!;

        return profession != null;
    }

    private bool TryResolveProfession(
        ClientLiveCharacterIdentity identity,
        ClientCharacterProgressionObservation progression,
        out SkillPlannerProfessionDefinition profession)
    {
        if (progression.Race.HasValue &&
            progression.Profession.HasValue &&
            SkillPlannerCharacterBaselineFactory
                .TryResolveClientProfessionIndex(
                    this.catalog,
                    progression.Race.Value,
                    progression.Profession.Value,
                    out var professionIndex) &&
            this.catalog.TryGetProfession(
                professionIndex,
                out profession))
        {
            return true;
        }

        profession = this.catalog.Professions.FirstOrDefault(
            candidate => string.Equals(
                candidate.DisplayName,
                identity.Profession,
                StringComparison.OrdinalIgnoreCase))!;

        return profession != null;
    }

    private IReadOnlyList<SkillBuildBoardSkillRow> BuildSkillRows(
        SkillPlannerCharacterBaseline baseline)
    {
        List<SkillBuildBoardSkillRow> rows = [];

        foreach (var skill in this.catalog.Skills
                     .Where(skill => skill.TryGetProfessionRule(
                         baseline.ProfessionIndex,
                         out _))
                     .OrderBy(
                         skill => skill.Name,
                         StringComparer.OrdinalIgnoreCase)
                     .ThenBy(skill => skill.Id))
        {
            _ = skill.TryGetProfessionRule(
                baseline.ProfessionIndex,
                out var professionRule);

            var currentRank = baseline.Skills.TryGetValue(
                    skill.Id,
                    out var ownedSkill)
                ? Math.Max(skill.MinimumRank, ownedSkill.CurrentRank)
                : skill.MinimumRank;

            rows.Add(
                new SkillBuildBoardSkillRow(
                    skill.Id,
                    skill.GroupName,
                    skill.Name,
                    currentRank,
                    skill.MinimumRank,
                    professionRule.MaximumRank));
        }

        return rows;
    }

    private static IReadOnlyList<SkillBuildBoardEquipmentRow>
        BuildEquipmentRows(
            SkillBuildEquipmentBaseline baseline)
    {
        if (!baseline.IsAvailable)
        {
            return [];
        }

        return baseline.Items
            .OrderBy(pair => GetEquipmentSortKey(pair.Key))
            .Select(
                pair =>
                    new SkillBuildBoardEquipmentRow(
                        pair.Key,
                        pair.Value.ItemTemplateId,
                        FormatSlotName(pair.Key),
                        string.IsNullOrWhiteSpace(pair.Value.ItemName)
                            ? string.Create(
                                CultureInfo.InvariantCulture,
                                $"Item #{pair.Value.ItemTemplateId}")
                            : pair.Value.ItemName))
            .ToArray();
    }

    private static int GetEquipmentSortKey(
        SkillBuildEquipmentSlot slot)
    {
        return slot.Kind switch
        {
            SkillBuildEquipmentKind.Shield => 0,
            SkillBuildEquipmentKind.Reactor => 100,
            SkillBuildEquipmentKind.Engine => 200,
            SkillBuildEquipmentKind.Weapon => 300 + slot.Ordinal,
            SkillBuildEquipmentKind.Device => 400 + slot.Ordinal,
            _ => int.MaxValue,
        };
    }

    private static string FormatSlotName(
        SkillBuildEquipmentSlot slot)
    {
        return slot.Kind switch
        {
            SkillBuildEquipmentKind.Shield => "Shield",
            SkillBuildEquipmentKind.Reactor => "Reactor",
            SkillBuildEquipmentKind.Engine => "Engine",
            SkillBuildEquipmentKind.Weapon => string.Create(
                CultureInfo.InvariantCulture,
                $"Weapon {slot.Ordinal}"),
            SkillBuildEquipmentKind.Device => string.Create(
                CultureInfo.InvariantCulture,
                $"Device {slot.Ordinal}"),
            _ => "Equipment",
        };
    }

    private static SkillBuildBoardPresentation CreateUnavailable(
        ClientObservationSnapshot snapshot,
        uint characterId,
        string pilotName,
        string? professionName,
        string status)
    {
        return new SkillBuildBoardPresentation
        {
            IsVisible = true,
            Fingerprint = string.Create(
                CultureInfo.InvariantCulture,
                $"unavailable:{snapshot.LocalPlayer.ObjectId}:{pilotName}:{professionName}:{status}"),
            CharacterId = characterId,
            PilotName = pilotName,
            ProfessionName = string.IsNullOrWhiteSpace(professionName)
                ? "Current profession"
                : professionName,
            StatusText = status,
        };
    }

    private static SkillBuildBoardPresentation CreateArchiveUnavailable(
        PilotArchivePilotSnapshot pilot,
        string pilotName,
        string status)
    {
        return new SkillBuildBoardPresentation
        {
            IsVisible = true,
            Fingerprint = string.Create(
                CultureInfo.InvariantCulture,
                $"archive-unavailable:{pilot.CharacterId}:{pilotName}:{pilot.Profession}:{status}"),
            CharacterId = pilot.CharacterId,
            PilotName = pilotName,
            ProfessionName = string.IsNullOrWhiteSpace(pilot.Profession)
                ? "Archived profession"
                : pilot.Profession.Trim(),
            IsArchivedContext = true,
            ContextText = "Pilot Archive",
            StatusText = status,
        };
    }

    private static string BuildFingerprint(
        uint characterId,
        string pilotName,
        SkillPlannerProfessionDefinition profession,
        SkillPlannerCharacterBaseline baseline,
        IReadOnlyList<SkillBuildBoardSkillRow> skills,
        IReadOnlyList<SkillBuildBoardEquipmentRow> equipment,
        SkillBuildEquipmentBaseline equipmentBaseline)
    {
        var builder = new StringBuilder(512);
        builder.Append(characterId)
            .Append('|')
            .Append(pilotName)
            .Append('|')
            .Append(profession.Index)
            .Append('|')
            .Append(baseline.CombatLevel)
            .Append('|')
            .Append(baseline.ExploreLevel)
            .Append('|')
            .Append(baseline.TradeLevel)
            .Append('|')
            .Append(baseline.AvailableSkillPoints)
            .Append('|')
            .Append(baseline.HullTier?.ToString(CultureInfo.InvariantCulture) ?? "-")
            .Append('|')
            .Append(equipmentBaseline.Status);


        foreach (var pair in equipmentBaseline.InventoryItemCounts
                     .OrderBy(value => value.Key))
        {
            builder.Append("|cargo:")
                .Append(pair.Key)
                .Append(':')
                .Append(pair.Value);
        }

        foreach (var pair in equipmentBaseline.VaultItemCounts
                     .OrderBy(value => value.Key))
        {
            builder.Append("|vault:")
                .Append(pair.Key)
                .Append(':')
                .Append(pair.Value);
        }

        foreach (var skill in skills)
        {
            builder.Append('|')
                .Append(skill.SkillId)
                .Append(':')
                .Append(skill.CurrentRank)
                .Append('/')
                .Append(skill.MaximumRank);
        }

        foreach (var item in equipment)
        {
            builder.Append('|')
                .Append(item.Slot.Kind)
                .Append(':')
                .Append(item.Slot.Ordinal)
                .Append(':')
                .Append(item.ItemTemplateId);
        }

        return builder.ToString();
    }
}
