namespace Net7ClientManager.PilotArchive;

using System.Security.Cryptography;
using System.Text.Json;
using Net7ClientManager.Addons.Contracts;
using Net7ClientManager.Addons.Projection;
using Net7ClientManager.Observations;
using Net7ClientManager.Observations.Models;

internal static class PilotArchiveProjector
{
    private static readonly JsonSerializerOptions fingerprintOptions = new()
    {
        NumberHandling =
            System.Text.Json.Serialization.JsonNumberHandling
                .AllowNamedFloatingPointLiterals,
    };

    public static PilotArchiveCapture? Project(
        ClientObservationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.LifecycleState != ClientLifecycleState.InGame)
        {
            return null;
        }

        var identity = ClientLiveCharacterIdentityResolver.Resolve(snapshot);

        if (!identity.IsAvailable ||
            identity.CharacterObjectId is not { } characterId ||
            string.IsNullOrWhiteSpace(identity.Name))
        {
            return null;
        }

        var player = snapshot.LocalPlayer;
        List<PilotArchiveSectionCapture> sections = [];

        if (identity.Status == ClientLiveCharacterIdentityStatus.Available &&
            player.Operational.IsAvailable &&
            player.CharacterProgression.IsAvailable &&
            player.Reputation.IsAvailable)
        {
            var identityPresentation =
                PilotArchiveIdentityPresentation.Resolve(
                    identity.Race,
                    identity.Profession);
            var overview = new PilotArchiveOverviewCapture
            {
                Race = identityPresentation.Race,
                Profession = identityPresentation.Profession,
                ProfessionCode = Normalize(identity.ProfessionCode),
                Affiliation = Normalize(identity.FactionAffiliation),
                GuildName = Normalize(identity.GuildName),
                GuildRank = Normalize(player.Operational.Identity.GuildRankName),
            };
            sections.Add(CreateSection(PilotArchiveSections.Overview, overview));
        }

        if (snapshot.World.IsAvailable &&
            player.CharacterDetails.RegistrationStarbaseValidState != 0 &&
            player.CharacterDetails.RegistrationStarbaseSectorValidState != 0)
        {
            sections.Add(CreateSection(
                PilotArchiveSections.Location,
                new PilotArchiveLocationCapture
                {
                    CurrentSystem = Normalize(snapshot.World.CurrentSystemName),
                    CurrentSector = Normalize(snapshot.World.CurrentSectorName),
                    CurrentStarbase = Normalize(snapshot.World.CurrentStarbaseName),
                    RegistrationStarbase = Normalize(
                        player.CharacterDetails.RegistrationStarbase),
                    RegistrationSector = Normalize(
                        player.CharacterDetails.RegistrationStarbaseSector),
                }));
        }

        if (player.CharacterProgression.IsAvailable &&
            player.CharacterProgression.SkillPoints.HasValue &&
            player.CharacterProgression.HullTier.HasValue)
        {
            sections.Add(CreateSection(
                PilotArchiveSections.Progression,
                new PilotArchiveProgressionCapture
                {
                    CombatLevel = identity.CombatLevel,
                    ExploreLevel = identity.ExploreLevel,
                    TradeLevel = identity.TradeLevel,
                    OverallLevel = identity.OverallLevel,
                    AvailableSkillPoints =
                        player.CharacterProgression.SkillPoints.Value,
                    HullTier =
                        player.CharacterProgression.HullTier.Value,
                    Credits = player.CharacterDetails.MoneyValidState != 0
                        ? player.CharacterDetails.Credits
                        : null,
                }));
        }

        if (player.CharacterProgression.Skills.IsAvailable)
        {
            var skills = player.CharacterProgression.Skills.Skills
                .Where(skill =>
                    skill.CurrentRank != 0 ||
                    skill.MaximumRank != 0)
                .OrderBy(skill => skill.Index)
                .Select(skill => new PilotArchiveSkill
                {
                    Index = skill.Index,
                    Name = skill.Name,
                    Category = skill.Category,
                    IsActive = skill.IsActiveAbility,
                    CurrentRank = skill.CurrentRank,
                    MaximumRank = skill.MaximumRank,
                    QuestOnlyLevels = skill.QuestOnlyLevels,
                    SpentSkillPoints = skill.SpentSkillPoints,
                })
                .ToArray();

            sections.Add(CreateSection(PilotArchiveSections.Skills, skills));
        }

        var inventory = player.Inventory;

        if (inventory.IsAvailable &&
            IsCompleteSlots(inventory.CargoSlots, expectedCount: 40))
        {
            var cargo = inventory.CargoSlots
                .OrderBy(slot => slot.Slot)
                .Select(slot => ProjectArchiveSlot(inventory, slot))
                .ToArray();
            sections.Add(CreateSection(PilotArchiveSections.Cargo, cargo));
        }

        if (inventory.IsAvailable &&
            IsCompleteSlots(inventory.EquippedSlots, expectedCount: 20) &&
            IsCompleteSlots(inventory.AmmoSlots, expectedCount: 20))
        {
            var equipment = new PilotArchiveEquipmentCapture
            {
                EquipmentSlots = InventorySlotProjector
                    .ProjectEquipmentSlots(inventory)
                    .Select(ProjectArchiveSlot)
                    .ToArray(),
                AmmoSlots = InventorySlotProjector
                    .ProjectAmmoSlots(inventory)
                    .Select(ProjectArchiveSlot)
                    .ToArray(),
            };
            sections.Add(CreateSection(PilotArchiveSections.Equipment, equipment));
        }

        var secure = player.SecureInventory;

        if (secure.IsAvailable &&
            secure.ReadErrorCount == 0 &&
            IsCompleteSlots(
                secure.Slots,
                ClientSecureInventoryObservation.ExpectedSlotCount))
        {
            var vault = secure.Slots
                .OrderBy(slot => slot.Slot)
                .Select(ProjectArchiveSlot)
                .ToArray();
            sections.Add(CreateSection(PilotArchiveSections.Vault, vault));
        }

        if (player.Missions.IsAvailable)
        {
            var missions = player.Missions.Missions
                .OrderBy(mission => mission.Slot)
                .Select(mission => new PilotArchiveMission
                {
                    Slot = mission.Slot,
                    RawId = mission.RawId,
                    Name = mission.Name,
                    Summary = mission.Summary,
                    Reward = mission.Reward,
                    FailureConsequence = mission.FailureConsequence,
                    IssuingFaction = mission.IssuingFaction,
                    Stage = mission.Stage,
                    StageCount = mission.StageCount,
                    IsTimed = mission.IsTimed,
                    IsForfeitable = mission.IsForfeitable,
                    IsComplete = mission.IsComplete,
                    IsFailed = mission.IsFailed,
                    IsExpired = mission.IsExpired,
                    CurrentStageText = mission.CurrentStageText,
                    Stages = mission.Stages
                        .OrderBy(stage => stage.Index)
                        .Select(stage => stage.Text)
                        .ToArray(),
                })
                .ToArray();
            sections.Add(CreateSection(PilotArchiveSections.Missions, missions));
        }

        if (player.Reputation.IsAvailable)
        {
            var reputations = player.Reputation.Factions
                .OrderBy(faction => faction.Slot)
                .Select(faction => new PilotArchiveReputation
                {
                    Slot = faction.Slot,
                    FactionKey = faction.FactionKey,
                    DisplayName = faction.DisplayName,
                    Description = faction.Description,
                    Reaction = faction.Reaction,
                    NormalizedReaction = faction.NormalizedReaction,
                    Order = faction.Order,
                })
                .ToArray();
            sections.Add(CreateSection(
                PilotArchiveSections.Reputations,
                reputations));
        }

        return new PilotArchiveCapture
        {
            CharacterId = characterId,
            Name = identity.Name.Trim(),
            ObservedAt = snapshot.ObservedAt,
            Sections = sections,
        };
    }

    private static AddonInventorySlotSnapshot
        ProjectArchiveSlot(
            ClientInventoryObservation inventory,
            ClientInventoryItemObservation slot)
    {
        return InventorySlotProjector.Project(inventory, slot) with
        {
            // Cooldown countdowns and busy tails are useful live Lua data,
            // but they are not inventory identity and would force constant
            // archive rewrites while equipment remains unchanged.
            Operational = new Dictionary<string, object?>(StringComparer.Ordinal),
        };
    }

    private static AddonInventorySlotSnapshot
        ProjectArchiveSlot(ClientInventoryItemObservation slot)
    {
        return InventorySlotProjector.Project(slot) with
        {
            Operational = new Dictionary<string, object?>(StringComparer.Ordinal),
        };
    }

    private static AddonInventorySlotSnapshot
        ProjectArchiveSlot(AddonInventorySlotSnapshot slot)
    {
        return slot with
        {
            Operational = new Dictionary<string, object?>(StringComparer.Ordinal),
        };
    }

    private static PilotArchiveSectionCapture CreateSection(
        string section,
        object value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            value,
            value.GetType(),
            fingerprintOptions);

        return new PilotArchiveSectionCapture
        {
            Section = section,
            Fingerprint = Convert.ToHexString(SHA256.HashData(bytes)),
            Value = value,
        };
    }

    private static bool IsCompleteSlots(
        IReadOnlyList<ClientInventoryItemObservation> slots,
        int expectedCount)
    {
        return slots.Count == expectedCount &&
               slots.All(slot =>
                   slot.IsOccupied ||
                   slot.IsUsableEmpty ||
                   slot.IsUnavailable);
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }
}
