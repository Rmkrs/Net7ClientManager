// ReSharper disable StringLiteralTypo
namespace Net7ClientManager.Addons.Projection;

using System.Text.Json;
using Net7ClientManager.Addons.Contracts;
using Net7ClientManager.Observations;
using Net7ClientManager.Observations.Models;

public sealed partial class GameSnapshotProjector
{
    private static readonly JsonSerializerOptions fingerprintJsonOptions =
        new()
        {
            NumberHandling =
                System.Text.Json.Serialization.JsonNumberHandling
                    .AllowNamedFloatingPointLiterals,
        };


    private readonly System.Threading.Lock cacheLock = new();
    private readonly Dictionary<int, ProjectionCache> projectionCache = [];

    public AddonGameSnapshot Project(
        ClientObservationSnapshot snapshot,
        AddonNavigationRouteSnapshot? navigationRoute = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var identity = snapshot.LocalPlayer.Operational.Identity;
        var liveIdentity =
            ClientLiveCharacterIdentityResolver.Resolve(snapshot);
        var characterAvailable = liveIdentity.IsAvailable;

        var worldAvailable =
            snapshot.LifecycleState == ClientLifecycleState.InGame &&
            snapshot.World.IsAvailable;

        var lifecycle = new AddonLifecycleSnapshot
        {
            State = NormalizeLifecycle(snapshot.LifecycleState),
            IsInGame = snapshot.LifecycleState == ClientLifecycleState.InGame,
            IsTransitioning = snapshot.LoadingOrTransitionFlag != 0,
        };

        var world = new AddonWorldSnapshot
        {
            IsAvailable = worldAvailable,
            Environment = NormalizeEnvironment(
                snapshot.World.Environment),
            SystemName = worldAvailable
                ? Normalize(snapshot.World.CurrentSystemName)
                : null,
            SectorName = worldAvailable
                ? Normalize(snapshot.World.CurrentSectorName)
                : null,
            StarbaseName = worldAvailable
                ? Normalize(snapshot.World.CurrentStarbaseName)
                : null,
            SectorId = worldAvailable &&
                       snapshot.World.ActiveSectorNumber != 0
                ? snapshot.World.ActiveSectorNumber
                : null,
            StarbaseId = worldAvailable &&
                         snapshot.World.CurrentStarbaseId != 0
                ? snapshot.World.CurrentStarbaseId
                : null,
        };

        var character = new AddonCharacterSnapshot
        {
            IsAvailable = characterAvailable,
            Identity = characterAvailable
                ? new AddonCharacterIdentitySnapshot
                {
                    Id = liveIdentity.CharacterObjectId,
                    Name = Normalize(liveIdentity.Name),
                    OwnerName = Normalize(identity.Owner),
                    Title = Normalize(identity.Title),
                    Rank = Normalize(identity.Rank),
                    Race = Normalize(liveIdentity.Race),
                    Profession = Normalize(liveIdentity.Profession),
                    ProfessionCode = Normalize(
                        liveIdentity.ProfessionCode),
                    Affiliation = Normalize(
                        liveIdentity.FactionAffiliation),
                    ResolutionStatus =
                        NormalizeEnum(liveIdentity.Status),
                    ResolutionMessage =
                        Normalize(liveIdentity.StatusText),
                    GuildName = Normalize(liveIdentity.GuildName),
                    GuildRank = Normalize(identity.GuildRankName),
                    CombatLevel = liveIdentity.CombatLevel,
                }
                : null,
        };

        var publicProjection =
            this.GetOrCreatePublicProjection(
                snapshot,
                character,
                navigationRoute);

        return new AddonGameSnapshot
        {
            Sequence = snapshot.Sequence,
            ObservedAt = snapshot.ObservedAt,
            Lifecycle = lifecycle,
            World = world,
            Character = character,
            InternalTargetObjectId = snapshot.Target.HasTarget
                ? snapshot.Target.ObjectId
                : null,
            PublicData = publicProjection.PublicData,
            DomainFingerprints =
                publicProjection.DomainFingerprints,
            EventDomainFingerprints =
                publicProjection.EventDomainFingerprints,
            RecentCombatEvents =
                publicProjection.RecentCombatEvents,
        };
    }

    public void Forget(int processId)
    {
        lock (this.cacheLock)
        {
            this.projectionCache.Remove(processId);
        }
    }

    private CachedPublicProjection GetOrCreatePublicProjection(
        ClientObservationSnapshot snapshot,
        AddonCharacterSnapshot character,
        AddonNavigationRouteSnapshot? navigationRoute)
    {
        lock (this.cacheLock)
        {
            if (!this.projectionCache.TryGetValue(
                    snapshot.ProcessId,
                    out var cache))
            {
                cache = new ProjectionCache();
                this.projectionCache[snapshot.ProcessId] = cache;
            }

            var characterDomain = cache.GetOrCreate(
                "character",
                () => CreateCharacterDomainProjection(
                    snapshot.LocalPlayer,
                    character),
                character.IsAvailable,
                snapshot.LocalPlayer.ObjectId,
                snapshot.LocalPlayer.Operational.Identity,
                snapshot.LocalPlayer.CharacterDetails,
                snapshot.LocalPlayer.CharacterProgression,
                snapshot.LocalPlayer.Reputation.Affiliation,
                snapshot.LocalPlayer.Spatial);

            var shipDomain = cache.GetOrCreate(
                "ship",
                () => CreateDomainProjection(
                    MapShip(snapshot.LocalPlayer)),
                snapshot.LocalPlayer.IsAvailable,
                snapshot.LocalPlayer.Shield,
                snapshot.LocalPlayer.Hull,
                snapshot.LocalPlayer.Energy,
                snapshot.LocalPlayer.Operational);

            var targetDomain = cache.GetOrCreate(
                "target",
                () => CreateTargetDomainProjection(
                    snapshot.Target,
                    snapshot.TargetInteraction),
                snapshot.Target,
                snapshot.TargetInteraction);

            var nearbyTargetsDomain = cache.GetOrCreate(
                "nearby_targets",
                () => CreateNearbyTargetsDomainProjection(
                    snapshot.NearbyTargets,
                    snapshot.Target),
                snapshot.NearbyTargets,
                snapshot.Target.ObjectId,
                snapshot.Target.HasTarget);

            var groupDomain = cache.GetOrCreate(
                "group",
                () => CreateDomainProjection(
                    MapGroup(snapshot.Group)),
                snapshot.Group);

            var inventoryDomain = cache.GetOrCreate(
                "inventory",
                () => CreateDomainProjection(
                    MapInventory(snapshot.LocalPlayer)),
                snapshot.LocalPlayer.Inventory,
                snapshot.LocalPlayer.SecureInventory,
                snapshot.LocalPlayer.RewardOverflowInventory,
                snapshot.LocalPlayer.VendorInventory);

            var buffsDomain = cache.GetOrCreate(
                "buffs",
                () => CreateDomainProjection(
                    MapBuffs(snapshot.LocalPlayer.Buffs)),
                snapshot.LocalPlayer.Buffs);

            var missionsDomain = cache.GetOrCreate(
                "missions",
                () => CreateDomainProjection(
                    MapMissions(snapshot.LocalPlayer.Missions)),
                snapshot.LocalPlayer.Missions);

            var reputationsDomain = cache.GetOrCreate(
                "reputations",
                () => CreateDomainProjection(
                    MapReputations(
                        snapshot.LocalPlayer.Reputation)),
                snapshot.LocalPlayer.Reputation);

            var navigationDomain = cache.GetOrCreate(
                "navigation",
                () => CreateNavigationDomainProjection(
                    snapshot.Navigation,
                    snapshot.NavigationState,
                    navigationRoute),
                snapshot.Navigation,
                snapshot.NavigationState,
                navigationRoute);

            var starbaseDomain = cache.GetOrCreate(
                "starbase",
                () => CreateDomainProjection(
                    MapStarbase(snapshot.StarbaseContext)),
                snapshot.StarbaseContext);

            var panelsDomain = cache.GetOrCreate(
                "panels",
                () => CreateDomainProjection(
                    MapPanels(
                        snapshot.PanelPresentation,
                        snapshot.StarMapPresentation,
                        snapshot.LocalPlayer.Missions,
                        snapshot.LocalPlayer.Reputation)),
                snapshot.PanelPresentation,
                snapshot.StarMapPresentation,
                snapshot.LocalPlayer.Missions,
                snapshot.LocalPlayer.Reputation);

            var jobsDomain = cache.GetOrCreate(
                "jobs",
                () => CreateDomainProjection(
                    MapJobs(snapshot.JobTerminal)),
                snapshot.JobTerminal);

            var shortcutsDomain = cache.GetOrCreate(
                "shortcuts",
                () => CreateDomainProjection(
                    MapShortcuts(snapshot.Shortcuts)),
                snapshot.Shortcuts);

            var tooltipsDomain = cache.GetOrCreate(
                "tooltips",
                () => CreateDomainProjection(
                    MapTooltips(
                        snapshot.TooltipDelay,
                        snapshot.TooltipHover)),
                snapshot.TooltipDelay,
                snapshot.TooltipHover);

            var productionDomain = cache.GetOrCreate(
                "production",
                () => CreateDomainProjection(
                    MapProduction(snapshot.ProductionRecipe)),
                snapshot.ProductionRecipe);

            var audioDomain = cache.GetOrCreate(
                "audio",
                () => CreateDomainProjection(
                    MapAudio(snapshot.AudioCue)),
                snapshot.AudioCue);

            var lootDomain = cache.GetOrCreate(
                "loot",
                () => CreateDomainProjection(
                    MapLoot(
                        snapshot.Looting,
                        snapshot.LootTractor)),
                snapshot.Looting,
                snapshot.LootTractor);

            var statsDomain = cache.GetOrCreate(
                "stats",
                () => CreateDomainProjection(
                    MapStats(
                        snapshot.NetworkTraffic,
                        snapshot.FrameRate)),
                snapshot.NetworkTraffic,
                snapshot.FrameRate);

            var combatDomain = cache.GetOrCreate(
                "combat",
                () =>
                {
                    var recentCombatEvents =
                        snapshot.Combat.RecentEvents
                            .Select(MapCombatEvent)
                            .ToArray();

                    return CreateDomainProjection(
                        MapCombat(
                            snapshot.Combat,
                            recentCombatEvents),
                        recentCombatEvents);
                },
                snapshot.Combat);

            var chatDomain = cache.GetOrCreate(
                "chat",
                () => CreateDomainProjection(
                    new Dictionary<string, object?>(
                        StringComparer.Ordinal)
                    {
                        ["available"] = true,
                        ["event_name"] = "chat.message",
                    }));

            var domains = new[]
            {
                characterDomain,
                shipDomain,
                targetDomain,
                nearbyTargetsDomain,
                groupDomain,
                inventoryDomain,
                buffsDomain,
                missionsDomain,
                reputationsDomain,
                navigationDomain,
                starbaseDomain,
                panelsDomain,
                jobsDomain,
                shortcutsDomain,
                tooltipsDomain,
                productionDomain,
                audioDomain,
                lootDomain,
                statsDomain,
                combatDomain,
                chatDomain,
            };

            return new CachedPublicProjection
            {
                PublicData = domains.ToDictionary(
                    domain => domain.Name,
                    domain => (object?)domain.Value,
                    StringComparer.Ordinal),
                DomainFingerprints = domains.ToDictionary(
                    domain => domain.Name,
                    domain => domain.Fingerprint,
                    StringComparer.Ordinal),
                EventDomainFingerprints = domains.ToDictionary(
                    domain => domain.Name,
                    domain => domain.EventFingerprint,
                    StringComparer.Ordinal),
                RecentCombatEvents =
                    combatDomain.RecentCombatEvents,
            };
        }
    }

    private static DomainProjection CreateDomainProjection(
        IReadOnlyDictionary<string, object?> value,
        IReadOnlyList<IReadOnlyDictionary<string, object?>>?
            recentCombatEvents = null)
    {
        return new DomainProjection
        {
            Value = value,
            Fingerprint = CreateFingerprint(value),
            EventFingerprint = CreateFingerprint(value),
            RecentCombatEvents =
                recentCombatEvents ?? [],
        };
    }

    private static DomainProjection CreateDomainProjectionWithEventFingerprint(
        IReadOnlyDictionary<string, object?> value,
        object eventFingerprintValue,
        IReadOnlyList<IReadOnlyDictionary<string, object?>>?
            recentCombatEvents = null)
    {
        return new DomainProjection
        {
            Value = value,
            Fingerprint = CreateFingerprint(value),
            EventFingerprint = CreateFingerprint(eventFingerprintValue),
            RecentCombatEvents = recentCombatEvents ?? [],
        };
    }

    private static string CreateFingerprint(object? value)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(
            value,
            value?.GetType() ?? typeof(object),
            fingerprintJsonOptions);

        return Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(json));
    }

    public static AddonGameSnapshot CreateSynthetic(
        string lifecycleState,
        string characterName,
        long sequence = 1)
    {
        var lifecycle = new AddonLifecycleSnapshot
        {
            State = lifecycleState,
            IsInGame = string.Equals(
                lifecycleState,
                "in_game",
                StringComparison.Ordinal),
        };

        var world = new AddonWorldSnapshot
        {
            IsAvailable = true,
            Environment = "space",
            SystemName = "Synthetic System",
            SectorName = "Synthetic Sector",
        };

        var character = new AddonCharacterSnapshot
        {
            IsAvailable = true,
            Identity = new AddonCharacterIdentitySnapshot
            {
                Name = characterName,
                Race = "Jenquai",
                Profession = "Jenquai Explorer",
                ProfessionCode = "JE",
                Affiliation = "Sha'ha'dem Explorers",
                ResolutionStatus = "available",
                ResolutionMessage =
                    "Synthetic character identity is available",
                CombatLevel = 42,
            },
        };

        var publicData = CreateSyntheticPublicData(character);

        return new AddonGameSnapshot
        {
            Sequence = sequence,
            ObservedAt = DateTimeOffset.UtcNow,
            Lifecycle = lifecycle,
            World = world,
            Character = character,
            PublicData = publicData,
            DomainFingerprints = publicData.ToDictionary(
                item => item.Key,
                item => CreateFingerprint(item.Value),
                StringComparer.Ordinal),
            EventDomainFingerprints = publicData.ToDictionary(
                item => item.Key,
                item => CreateFingerprint(item.Value),
                StringComparer.Ordinal),
        };
    }

    private static IReadOnlyDictionary<string, object?>
        CreateSyntheticPublicData(
            AddonCharacterSnapshot character)
    {
        var identity = character.Identity;

        return new Dictionary<string, object?>(
            StringComparer.Ordinal)
        {
            ["character"] = new Dictionary<string, object?>(
                StringComparer.Ordinal)
            {
                ["available"] = character.IsAvailable,
                ["identity"] = identity == null
                    ? null
                    : new Dictionary<string, object?>(
                        StringComparer.Ordinal)
                    {
                        ["name"] = identity.Name,
                        ["title"] = identity.Title,
                        ["rank"] = identity.Rank,
                        ["race"] = identity.Race,
                        ["profession"] = identity.Profession,
                        ["affiliation"] = identity.Affiliation,
                        ["guild_name"] = identity.GuildName,
                        ["guild_rank"] = identity.GuildRank,
                        ["combat_level"] = identity.CombatLevel,
                    },
                ["details"] = new Dictionary<string, object?>(
                    StringComparer.Ordinal)
                {
                    ["available"] = true,
                    ["credits"] = 123456UL,
                    ["experience_debt"] = 0,
                    ["registration_starbase"] = "Synthetic Station",
                    ["registration_sector"] = "Synthetic Sector",
                },
                ["progression"] = new Dictionary<string, object?>(
                    StringComparer.Ordinal)
                {
                    ["available"] = true,
                    ["combat"] = MapSyntheticTrack(42),
                    ["explore"] = MapSyntheticTrack(40),
                    ["trade"] = MapSyntheticTrack(38),
                    ["overall_level"] = 120,
                    ["skill_points"] = 12,
                    ["hull_tier"] = 6,
                },
                ["skills"] = new Dictionary<string, object?>(
                    StringComparer.Ordinal)
                {
                    ["available"] = true,
                    ["learned_count"] = 1,
                    ["spent_skill_points"] = 3,
                    ["items"] = new object?[]
                    {
                        new Dictionary<string, object?>(
                            StringComparer.Ordinal)
                        {
                            ["name"] = "Synthetic Skill",
                            ["category"] = "Synthetic",
                            ["active"] = true,
                            ["current_rank"] = 3,
                            ["maximum_rank"] = 7,
                            ["learned"] = true,
                            ["maxed"] = false,
                            ["spent_skill_points"] = 3,
                        },
                    },
                },
                ["spatial"] = new Dictionary<string, object?>(
                    StringComparer.Ordinal)
                {
                    ["available"] = true,
                    ["position"] = new Dictionary<string, object?>(
                        StringComparer.Ordinal)
                    {
                        ["x"] = 100.0,
                        ["y"] = 200.0,
                        ["z"] = 300.0,
                    },
                    ["targeting_distance_radius"] = 50.0,
                },
            },
            ["ship"] = new Dictionary<string, object?>(
                StringComparer.Ordinal)
            {
                ["available"] = true,
                ["vitals"] = new Dictionary<string, object?>(
                    StringComparer.Ordinal)
                {
                    ["shield"] = MapSyntheticVital(80),
                    ["hull"] = MapSyntheticVital(100),
                    ["energy"] = MapSyntheticVital(65),
                },
            },
            ["target"] = EmptyDomain(),
            ["nearby_targets"] = new Dictionary<string, object?>(
                StringComparer.Ordinal)
            {
                ["available"] = true,
                // Compatibility-only fields used by the bundled DPS addons.
                ["active_sector_id"] = 4242u,
                ["count"] = 1,
                ["inside_viewport_count"] = 0,
                ["gutter_count"] = 1,
                ["targets"] = new object?[]
                {
                    new Dictionary<string, object?>(
                        StringComparer.Ordinal)
                    {
                        // Compatibility-only field used by the bundled DPS Meter.
                        ["id"] = 100478u,
                        ["name"] = "Synthetic Drone",
                        ["display_name"] = "Synthetic Drone",
                        ["owner_name"] = null,
                        ["title"] = null,
                        ["rank"] = null,
                        ["kind"] = "non_player_ship",
                        ["inside_viewport"] = false,
                        ["on_gutter"] = true,
                        ["hovered"] = true,
                        ["selected"] = false,
                        ["hull"] = MapSyntheticVital(72),
                        ["shield"] = MapSyntheticVital(38),
                        ["screen_position"] =
                            new Dictionary<string, object?>(
                                StringComparer.Ordinal)
                            {
                                ["x"] = 0.990625f,
                                ["y"] = 0.575484f,
                            },
                    },
                },
            },
            ["group"] = EmptyDomain(),
            ["inventory"] = EmptyDomain(),
            ["buffs"] = EmptyDomain(),
            ["missions"] = EmptyDomain(),
            ["reputations"] = EmptyDomain(),
            ["navigation"] = new Dictionary<string, object?>(
                StringComparer.Ordinal)
            {
                ["available"] = false,
                ["route"] = MapNavigationRoute(snapshot: null),
                ["control"] = EmptyDomain(),
            },
            ["starbase"] = EmptyDomain(),
            ["panels"] = EmptyDomain(),
            ["jobs"] = EmptyDomain(),
            ["shortcuts"] = EmptyDomain(),
            ["tooltips"] = EmptyDomain(),
            ["production"] = EmptyDomain(),
            ["audio"] = EmptyDomain(),
            ["loot"] = new Dictionary<string, object?>(
                StringComparer.Ordinal)
            {
                ["available"] = false,
                ["panel_displayed"] = false,
                ["has_target"] = false,
                ["active"] = false,
                ["tractor"] = MapLootTractor(
                    ClientLootTractorObservation.Unavailable(
                        "Synthetic loot tractor is unavailable")),
            },
            ["stats"] = EmptyDomain(),
            ["combat"] = EmptyDomain(),
            ["chat"] = new Dictionary<string, object?>(
                StringComparer.Ordinal)
            {
                ["available"] = true,
                ["event_name"] = "chat.message",
            },
        };
    }

    private static IReadOnlyDictionary<string, object?> MapCharacter(
        ClientLocalPlayerObservation player,
        AddonCharacterSnapshot character)
    {
        var identity = character.Identity;

        return new Dictionary<string, object?>(
            StringComparer.Ordinal)
        {
            ["available"] = character.IsAvailable,
            ["identity"] = identity == null
                ? null
                : new Dictionary<string, object?>(
                    StringComparer.Ordinal)
                {
                    ["name"] = identity.Name,
                    ["title"] = identity.Title,
                    ["rank"] = identity.Rank,
                    ["race"] = identity.Race,
                    ["profession"] = identity.Profession,
                    ["affiliation"] = identity.Affiliation,
                    ["guild_name"] = identity.GuildName,
                    ["guild_rank"] = identity.GuildRank,
                    ["combat_level"] = identity.CombatLevel,
                },
            ["details"] = MapCharacterDetails(
                player.CharacterDetails),
            ["progression"] = MapProgression(
                player.CharacterProgression),
            ["skills"] = MapSkills(
                player.CharacterProgression.Skills),
            ["spatial"] = MapSpatial(player.Spatial),
        };
    }

    private static IReadOnlyDictionary<string, object?> MapCharacterDetails(
        ClientCharacterDetailsObservation details)
    {
        return new Dictionary<string, object?>(
            StringComparer.Ordinal)
        {
            ["available"] = details.IsAvailable,
            ["credits"] = details.Credits,
            ["experience_debt"] = details.ExperienceDebt,
            ["registration_starbase"] =
                Normalize(details.RegistrationStarbase),
            ["registration_sector"] =
                Normalize(details.RegistrationStarbaseSector),
        };
    }

    private static IReadOnlyDictionary<string, object?> MapProgression(
        ClientCharacterProgressionObservation progression)
    {
        return new Dictionary<string, object?>(
            StringComparer.Ordinal)
        {
            ["available"] = progression.IsAvailable,
            ["combat"] = MapExperienceTrack(progression.Combat),
            ["explore"] = MapExperienceTrack(progression.Explore),
            ["trade"] = MapExperienceTrack(progression.Trade),
            ["skill_points"] = progression.SkillPoints,
            ["hull_upgrade_level"] =
                progression.HullUpgradeLevel,
            ["hull_tier"] = progression.HullTier,
            ["current_hull_upgrade_overall_level"] =
                progression.CurrentHullUpgradeOverallLevel,
            ["next_hull_upgrade_overall_level"] =
                progression.NextHullUpgradeOverallLevel,
            ["overall_level"] = progression.OverallLevel,
            ["overall_levels_until_next_hull_upgrade"] =
                progression.OverallLevelsUntilNextHullUpgrade,
            ["maximum_hull_tier"] =
                progression.IsMaximumHullTier,
        };
    }

    private static IReadOnlyDictionary<string, object?> MapExperienceTrack(
        ClientCharacterExperienceTrackObservation track)
    {
        return new Dictionary<string, object?>(
            StringComparer.Ordinal)
        {
            ["level"] = track.Level,
            ["progress"] = track.ProgressFraction,
            ["progress_percent"] = track.ProgressPercent,
            ["experience_required"] =
                track.ExperienceRequiredForNextLevel,
            ["experience_earned"] =
                track.ExperienceEarnedInCurrentLevel,
            ["experience_remaining"] =
                track.ExperienceRemainingToNextLevel,
            ["maximum_level"] = track.IsAtMaximumLevel,
            ["next_level"] = track.NextLevel,
            ["complete"] = track.IsComplete,
        };
    }

    private static IReadOnlyDictionary<string, object?> MapSkills(
        ClientCharacterSkillsObservation skills)
    {
        var items = skills.Skills
            .Select(
                skill =>
                    (object?)new Dictionary<string, object?>(
                        StringComparer.Ordinal)
                    {
                        ["name"] = skill.Name,
                        ["category"] = skill.Category,
                        ["active"] = skill.IsActiveAbility,
                        ["current_rank"] = skill.CurrentRank,
                        ["maximum_rank"] = skill.MaximumRank,
                        ["quest_only_levels"] =
                            skill.QuestOnlyLevels,
                        ["learned"] = skill.IsLearned,
                        ["maxed"] = skill.IsMaxed,
                        ["spent_skill_points"] =
                            skill.SpentSkillPoints,
                    })
            .ToArray();

        return new Dictionary<string, object?>(
            StringComparer.Ordinal)
        {
            ["available"] = skills.IsAvailable,
            ["learned_count"] = skills.LearnedSkillCount,
            ["spent_skill_points"] = skills.SpentSkillPoints,
            ["items"] = items,
        };
    }

    private static IReadOnlyDictionary<string, object?> MapShip(
        ClientLocalPlayerObservation player)
    {
        var operational = player.Operational;

        return new Dictionary<string, object?>(
            StringComparer.Ordinal)
        {
            ["available"] = player.IsAvailable,
            ["vitals"] = MapVitals(
                player.Shield,
                player.Hull,
                player.Energy),
            ["flags"] = MapShipFlags(operational.Flags),
            ["runtime"] = MapShipRuntime(operational.Runtime),
            ["movement"] = MapShipMovement(operational.Movement),
            ["base_stats"] = MapShipStats(operational.BaseStats),
            ["current_stats"] = MapShipStats(operational.CurrentStats),
            ["quadrants"] = MapShipQuadrants(
                operational.Quadrants),
            ["radar"] = MapShipRadar(operational.Radar),
        };
    }

    private static IReadOnlyList<object?> MapShipQuadrants(
        IReadOnlyList<ClientShipQuadrantObservation> quadrants)
    {
        return quadrants
            .Select(
                quadrant =>
                    (object?)new Dictionary<string, object?>(
                        StringComparer.Ordinal)
                    {
                        ["number"] = quadrant.Index + 1,
                        ["health"] = quadrant.HealthFraction,
                        ["health_percent"] =
                            quadrant.HealthPercent,
                        ["damage"] = quadrant.DamageFraction,
                        ["damage_percent"] =
                            quadrant.DamagePercent,
                    })
            .ToArray();
    }

    private static IReadOnlyDictionary<string, object?> MapShipRadar(
        ClientNavigationRadarObservation radar)
    {
        return new Dictionary<string, object?>(
            StringComparer.Ordinal)
        {
            ["available"] = radar.HasData,
            ["appears"] = radar.AppearsInRadar,
            ["range"] = radar.RadarRange,
        };
    }

    private static IReadOnlyDictionary<string, object?> MapVitals(
        ClientTargetShieldObservation shield,
        ClientTargetHullObservation hull,
        ClientTargetEnergyObservation energy)
    {
        return new Dictionary<string, object?>(
            StringComparer.Ordinal)
        {
            ["shield"] = MapShield(shield),
            ["hull"] = MapHull(hull),
            ["energy"] = MapEnergy(energy),
        };
    }

    private static IReadOnlyDictionary<string, object?> MapShield(
        ClientTargetShieldObservation shield)
    {
        return new Dictionary<string, object?>(
            StringComparer.Ordinal)
        {
            ["available"] = shield.IsAvailable,
            ["has_data"] = shield.HasShieldData,
            ["current"] = shield.HasCurrentShieldPower
                ? shield.CurrentShieldPower
                : null,
            ["maximum"] = shield.HasMaximumShieldPower
                ? shield.MaximumShieldPower
                : null,
            ["percent"] = shield.HasShieldPercent
                ? shield.ShieldPercent
                : null,
        };
    }

    private static IReadOnlyDictionary<string, object?> MapHull(
        ClientTargetHullObservation hull)
    {
        return new Dictionary<string, object?>(
            StringComparer.Ordinal)
        {
            ["available"] = hull.IsAvailable,
            ["has_data"] = hull.HasHullData,
            ["current"] = hull.HasHullPoints
                ? hull.HullPoints
                : null,
            ["maximum"] = hull.HasMaximumHullPoints
                ? hull.MaximumHullPoints
                : null,
            ["percent"] = hull.HasHullData
                ? hull.HullPercent
                : null,
        };
    }

    private static IReadOnlyDictionary<string, object?> MapEnergy(
        ClientTargetEnergyObservation energy)
    {
        return new Dictionary<string, object?>(
            StringComparer.Ordinal)
        {
            ["available"] = energy.IsAvailable,
            ["has_data"] = energy.HasEnergyData,
            ["current"] = energy.HasCompleteEnergyData
                ? energy.DerivedCurrentEnergyPower
                : null,
            ["maximum"] = energy.HasMaximumEnergyPower
                ? energy.MaximumEnergyPower
                : null,
            ["percent"] = energy.HasEnergyPercent
                ? energy.EnergyPercent
                : null,
            ["percent_change_per_tick"] =
                energy.HasEnergyChangePerTick
                    ? energy.EnergyPercentChangePerTick
                    : null,
            ["draining"] = energy.IsEnergyDraining,
            ["recovering"] = energy.IsEnergyRecovering,
        };
    }

    private static IReadOnlyDictionary<string, object?> MapShipFlags(
        ClientShipControlFlagsObservation flags)
    {
        return new Dictionary<string, object?>(
            StringComparer.Ordinal)
        {
            ["lock_speed"] = flags.LockSpeed,
            ["lock_orientation"] = flags.LockOrient,
            ["auto_level"] = flags.AutoLevel,
            ["cloaked"] = flags.IsCloaked,
            ["countermeasure_active"] =
                flags.IsCountermeasureActive,
            ["incapacitated"] = flags.IsIncapacitated,
            ["organic"] = flags.IsOrganic,
            ["pvp"] = flags.IsInPvp,
            ["auto_following"] = flags.IsAutoFollowing,
            ["rescue_beacon_active"] =
                flags.IsRescueBeaconActive,
        };
    }

    private static IReadOnlyDictionary<string, object?> MapShipRuntime(
        ClientShipRuntimeStateObservation runtime)
    {
        return new Dictionary<string, object?>(
            StringComparer.Ordinal)
        {
            ["warping"] = runtime.HasActiveWarpState,
            ["warp_available"] = runtime.WarpAvailable.HasValue
                ? runtime.WarpAvailable.Value > 0
                : null,
            ["engine_thrust"] = runtime.HasEngineThrust,
            ["target_threat"] = Normalize(runtime.TargetThreat),
            ["target_threat_sound"] =
                Normalize(runtime.TargetThreatSound),
            ["target_threat_level"] =
                runtime.TargetThreatLevel,
            ["interruptible_ability"] =
                Normalize(runtime.InterruptibleAbilityName),
            ["interrupt_progress_percent"] =
                runtime.InterruptProgressPercent,
        };
    }

    private static IReadOnlyDictionary<string, object?> MapShipMovement(
        ClientShipMovementObservation movement)
    {
        return new Dictionary<string, object?>(
            StringComparer.Ordinal)
        {
            ["maximum_tilt_rate"] = movement.MaximumTiltRate,
            ["maximum_turn_rate"] = movement.MaximumTurnRate,
            ["maximum_tilt_angle"] = movement.MaximumTiltAngle,
            ["maximum_speed"] = movement.MaximumSpeed,
            ["minimum_speed"] = movement.MinimumSpeed,
            ["acceleration"] = movement.Acceleration,
        };
    }

    private static IReadOnlyDictionary<string, object?> MapShipStats(
        ClientShipStatsObservation stats)
    {
        return new Dictionary<string, object?>(
            StringComparer.Ordinal)
        {
            ["defense"] = stats.Defense,
            ["missile_defense"] = stats.MissileDefense,
            ["speed"] = stats.Speed,
            ["warp_speed"] = stats.WarpSpeed,
            ["warp_power_level"] = stats.WarpPowerLevel,
            ["turn_rate"] = stats.TurnRate,
            ["scan_range"] = stats.ScanRange,
            ["visibility"] = stats.Visibility,
            ["resist_impact"] = stats.ResistImpact,
            ["resist_explosive"] = stats.ResistExplosive,
            ["resist_plasma"] = stats.ResistPlasma,
            ["resist_energy"] = stats.ResistEnergy,
            ["resist_emp"] = stats.ResistEmp,
            ["resist_chemical"] = stats.ResistChemical,
            ["resist_psionic"] = stats.ResistPsionic,
        };
    }

    private static IReadOnlyDictionary<string, object?> MapTarget(
        ClientTargetObservation target,
        ClientTargetInteractionObservation interaction)
    {
        return new Dictionary<string, object?>(
            StringComparer.Ordinal)
        {
            ["available"] = target.IsAvailable,
            ["has_target"] = target.HasTarget,
            ["name"] = target.HasTarget
                ? Normalize(target.Name)
                : null,
            ["kind"] = NormalizeEnum(target.Kind),
            ["relation"] = NormalizeEnum(target.Relation),
            ["self"] = target.IsSelf,
            ["group_member"] = target.IsGroupMember,
            ["hostile_attacking"] =
                target.IsHostileAttacking,
            ["identity"] = MapTargetIdentity(
                target,
                target.Operational),
            ["interaction"] = MapTargetInteraction(
                target,
                interaction),
            ["distance"] = new Dictionary<string, object?>(
                StringComparer.Ordinal)
            {
                ["available"] = target.Distance.IsAvailable,
                ["surface"] = target.Distance.IsAvailable
                    ? target.Distance.SurfaceDistance
                    : null,
                ["display_text"] =
                    Normalize(target.Distance.NativeReadoutText),
            },
            ["spatial"] = new Dictionary<string, object?>(
                StringComparer.Ordinal)
            {
                ["local"] = MapFunctionalSpatial(
                    target.Distance.Local),
                ["target"] = MapFunctionalSpatial(
                    target.Distance.Target),
            },
            ["vitals"] = MapVitals(
                target.Shield,
                target.Hull,
                target.Energy),
            ["ship"] = MapTargetShip(target.Operational),
            ["corpse"] = MapCorpse(target.Corpse),
            ["asteroid"] = MapAsteroid(target.Asteroid),
        };
    }

    private static IReadOnlyDictionary<string, object?>
        MapTargetIdentity(
            ClientTargetObservation target,
            ClientShipOperationalObservation operational)
    {
        var identity = operational.Identity;

        return new Dictionary<string, object?>(
            StringComparer.Ordinal)
        {
            ["available"] = operational.IsAvailable,
            ["name"] = Normalize(identity.Name) ??
                       Normalize(target.Name),
            ["owner_name"] = Normalize(identity.Owner),
            ["title"] = Normalize(identity.Title),
            ["rank"] = Normalize(identity.Rank),
            ["profession"] =
                Normalize(identity.ProfessionName),
            ["guild_name"] = Normalize(identity.GuildName),
            ["guild_rank"] =
                Normalize(identity.GuildRankName),
            ["combat_level"] = identity.CombatLevel,
        };
    }

    private static IReadOnlyDictionary<string, object?>
        MapTargetInteraction(
            ClientTargetObservation target,
            ClientTargetInteractionObservation interaction)
    {
        var matchesTarget =
            target.HasTarget &&
            interaction.HasTarget &&
            target.ObjectId != 0 &&
            target.ObjectId == interaction.TargetObjectId;

        List<object?> actions = [];

        if (matchesTarget)
        {
            foreach (var action in interaction.Actions)
            {
                var mappedAction =
                    MapTargetInteractionAction(
                        action,
                        interaction);

                if (mappedAction != null)
                {
                    actions.Add(mappedAction);
                }
            }
        }

        return new Dictionary<string, object?>(
            StringComparer.Ordinal)
        {
            ["available"] =
                matchesTarget && interaction.IsAvailable,
            ["active"] =
                matchesTarget && interaction.IsActive,
            ["actions"] = actions,
            ["can_execute"] =
                new Dictionary<string, object?>(
                StringComparer.Ordinal)
            {
                ["scan"] = CanExecuteTargetVerb(
                    matchesTarget,
                    interaction,
                    ClientTargetVerb.Scan),
                ["land"] = CanExecuteTargetVerb(
                    matchesTarget,
                    interaction,
                    ClientTargetVerb.Land),
                ["trade"] = CanExecuteTargetVerb(
                    matchesTarget,
                    interaction,
                    ClientTargetVerb.Trade),
                ["tractor"] = CanExecuteTargetVerb(
                    matchesTarget,
                    interaction,
                    ClientTargetVerb.Tractor),
                ["dock"] = CanExecuteTargetVerb(
                    matchesTarget,
                    interaction,
                    ClientTargetVerb.Dock),
                ["gate"] = CanExecuteTargetVerb(
                    matchesTarget,
                    interaction,
                    ClientTargetVerb.Gate),
                ["register"] = CanExecuteTargetVerb(
                    matchesTarget,
                    interaction,
                    ClientTargetVerb.Register),
                ["jumpstart"] = CanExecuteTargetVerb(
                    matchesTarget,
                    interaction,
                    ClientTargetVerb.Jumpstart),
                ["follow"] = CanExecuteTargetVerb(
                    matchesTarget,
                    interaction,
                    ClientTargetVerb.Follow),
            },
        };
    }

    private static IReadOnlyDictionary<string, object?>?
        MapTargetInteractionAction(
            ClientTargetVerbActionObservation action,
            ClientTargetInteractionObservation interaction)
    {
        var verb = NormalizeTargetVerb(action.Verb);

        if (verb == null)
        {
            return null;
        }

        var executable =
            interaction.IsAvailable &&
            interaction.IsActive &&
            action.IsExecutable;

        return new Dictionary<string, object?>(
            StringComparer.Ordinal)
        {
            ["verb"] = verb,
            ["executable"] = executable,
            ["unavailable_reason"] = executable
                ? null
                : !interaction.IsAvailable
                    ? "unavailable"
                    : !interaction.IsActive
                        ? "inactive"
                        : NormalizeTargetVerbUnavailableReason(
                            action.KnownUnavailableReason),
        };
    }

    private static bool CanExecuteTargetVerb(
        bool matchesTarget,
        ClientTargetInteractionObservation interaction,
        ClientTargetVerb verb)
    {
        return matchesTarget && interaction.CanExecute(verb);
    }

    private static string? NormalizeTargetVerb(
        ClientTargetVerb verb)
    {
        return verb switch
        {
            ClientTargetVerb.Scan => "scan",
            ClientTargetVerb.Land => "land",
            ClientTargetVerb.Trade => "trade",
            ClientTargetVerb.Tractor => "tractor",
            ClientTargetVerb.Dock => "dock",
            ClientTargetVerb.Gate => "gate",
            ClientTargetVerb.Register => "register",
            ClientTargetVerb.Jumpstart => "jumpstart",
            ClientTargetVerb.Follow => "follow",
            _ => null,
        };
    }

    private static string NormalizeTargetVerbUnavailableReason(
        ClientTargetVerbUnavailableReason? reason)
    {
        return reason switch
        {
            ClientTargetVerbUnavailableReason.PlayerAlreadyInGroup =>
                "player_already_in_group",
            ClientTargetVerbUnavailableReason.TooFar => "too_far",
            _ => "unavailable",
        };
    }

    private static IReadOnlyDictionary<string, object?> MapTargetShip(
        ClientShipOperationalObservation operational)
    {
        return new Dictionary<string, object?>(
            StringComparer.Ordinal)
        {
            ["available"] = operational.IsAvailable,
            ["flags"] = MapShipFlags(operational.Flags),
            ["runtime"] = MapShipRuntime(operational.Runtime),
            ["movement"] = MapShipMovement(operational.Movement),
            ["base_stats"] = MapShipStats(operational.BaseStats),
            ["current_stats"] =
                MapShipStats(operational.CurrentStats),
            ["quadrants"] = MapShipQuadrants(
                operational.Quadrants),
            ["radar"] = MapShipRadar(operational.Radar),
        };
    }

    private static IReadOnlyDictionary<string, object?> MapCorpse(
        ClientCorpseObservation corpse)
    {
        return new Dictionary<string, object?>(
            StringComparer.Ordinal)
        {
            ["available"] = corpse.IsAvailable,
            ["has_loot"] = corpse.HasLoot,
            ["known_empty"] = corpse.IsKnownEmpty,
            ["occupied_count"] = corpse.OccupiedSlotCount,
            ["items"] = corpse.LootItems
                .Select(
                    item =>
                        (object?)MapResourceItem(
                            item.Slot,
                            item.ItemTemplateId,
                            item.StackCount,
                            item.QualityPercent,
                            item.StructurePercent,
                            item.AverageCost,
                            item.BuilderName,
                            item.InstanceInfo,
                            item.InstanceActivatedEffectInfo,
                            item.InstanceEquipEffectInfo))
                .ToArray(),
        };
    }

    private static IReadOnlyDictionary<string, object?> MapAsteroid(
        ClientAsteroidObservation asteroid)
    {
        return new Dictionary<string, object?>(
            StringComparer.Ordinal)
        {
            ["available"] = asteroid.IsAvailable,
            ["tech_level"] = asteroid.TechLevel,
            ["percent_full"] = asteroid.PercentFullPercent,
            ["depleted"] = asteroid.IsDepleted,
            ["resources_may_be_outdated"] =
                asteroid.HasStaleResourceManifest,
            ["resource_count"] = asteroid.ResourceCount,
            ["resources"] = asteroid.Resources
                .Select(
                    item =>
                        (object?)MapResourceItem(
                            item.Slot,
                            item.ItemTemplateId,
                            item.StackCount,
                            item.QualityPercent,
                            item.StructurePercent,
                            item.AverageCost,
                            item.BuilderName,
                            item.InstanceInfo,
                            item.InstanceActivatedEffectInfo,
                            item.InstanceEquipEffectInfo))
                .ToArray(),
        };
    }

    private static IReadOnlyDictionary<string, object?> MapResourceItem(
        int slot,
        int? itemTemplateId,
        int? stackCount,
        float? qualityPercent,
        float? structurePercent,
        float? averageCost,
        string? builderName,
        string? instanceInfo,
        string? activatedEffect,
        string? equipEffect)
    {
        return new Dictionary<string, object?>(
            StringComparer.Ordinal)
        {
            ["slot"] = slot,
            ["name"] = ResolvePublicItemName(itemTemplateId),
            ["stack_count"] = stackCount,
            ["quality_percent"] = qualityPercent,
            ["structure_percent"] = structurePercent,
            ["average_cost"] = averageCost,
            ["builder_name"] = Normalize(builderName),
            ["instance_info"] = Normalize(instanceInfo),
            ["activated_effect_info"] =
                Normalize(activatedEffect),
            ["equip_effect_info"] = Normalize(equipEffect),
        };
    }

    private static IReadOnlyDictionary<string, object?> MapNearbyTargets(
        ClientGutterRadarObservation nearbyTargets,
        ClientTargetObservation selectedTarget)
    {
        var selectedObjectId =
            selectedTarget.HasTarget
                ? selectedTarget.ObjectId
                : 0u;

        return new Dictionary<string, object?>(
            StringComparer.Ordinal)
        {
            ["available"] = nearbyTargets.IsAvailable,
            // Compatibility-only field used by the bundled DPS addons.
            ["active_sector_id"] =
                nearbyTargets.ActiveSectorNumber != 0
                    ? nearbyTargets.ActiveSectorNumber
                    : null,
            ["count"] = nearbyTargets.Targets.Count,
            ["inside_viewport_count"] =
                nearbyTargets.InsideViewportCount,
            ["gutter_count"] = nearbyTargets.GutterCount,
            ["targets"] = nearbyTargets.Targets
                .Where(target => target.IsAvailable)
                .OrderBy(target => target.ObjectId)
                .Select(
                    target =>
                        (object?)new Dictionary<string, object?>(
                            StringComparer.Ordinal)
                        {
                            // Compatibility-only field used by the bundled DPS Meter.
                            ["id"] = target.ObjectId,
                            ["name"] = Normalize(target.Name),
                            ["display_name"] =
                                Normalize(target.DisplayName),
                            ["owner_name"] = Normalize(target.Owner),
                            ["title"] = Normalize(target.Title),
                            ["rank"] = Normalize(target.Rank),
                            ["kind"] = NormalizeEnum(target.Kind),
                            ["inside_viewport"] =
                                target.IsInsideViewport,
                            ["on_gutter"] =
                                !target.IsInsideViewport,
                            ["hovered"] = target.IsHovered,
                            ["selected"] =
                                selectedObjectId != 0 &&
                                target.ObjectId == selectedObjectId,
                            ["hull"] = MapHull(target.Hull),
                            ["shield"] = MapShield(target.Shield),
                            ["screen_position"] =
                                new Dictionary<string, object?>(
                                    StringComparer.Ordinal)
                                {
                                    ["x"] = target.NormalizedX,
                                    ["y"] = target.NormalizedY,
                                },
                        })
                .ToArray(),
        };
    }

    private static IReadOnlyDictionary<string, object?> MapGroup(
        ClientGroupObservation group)
    {
        return new Dictionary<string, object?>(
            StringComparer.Ordinal)
        {
            ["available"] = group.IsAvailable,
            ["in_group"] = group.IsInGroup,
            ["leader"] = group.IsLeader,
            ["looking_for_group"] = group.LookingForGroup,
            ["allows_invites"] = group.AllowsGroupInvites,
            ["shows_non_combat_activities"] =
                group.ShowsNonCombatActivities,
            ["auto_split"] = group.ForcesAutoSplit,
            ["restricted_looting"] =
                group.HasRestrictedLootingRights,
            ["auto_release_loot_restrictions"] =
                group.AutoReleasesLootingRestrictions,
            ["formation"] = new Dictionary<string, object?>(
                StringComparer.Ordinal)
            {
                ["name"] = Normalize(group.FormationName),
                ["position"] = group.FormationPosition,
            },
            ["members"] = group.Members
                .Where(member => member.IsPresent)
                .Select(
                    member =>
                        (object?)new Dictionary<string, object?>(
                            StringComparer.Ordinal)
                        {
                            ["slot"] = member.Slot,
                            ["name"] = Normalize(member.Name),
                            ["formation_position"] =
                                member.FormationPosition,
                            ["details_available"] =
                                member.IsObjectResolved,
                            ["distance"] =
                                member.Distance.IsAvailable
                                    ? member.Distance.SurfaceDistance
                                    : null,
                            ["shield"] =
                                MapShield(member.Shield),
                            ["hull"] = MapHull(member.Hull),
                        })
                .ToArray(),
        };
    }

    private static IReadOnlyDictionary<string, object?> MapInventory(
        ClientLocalPlayerObservation player)
    {
        var inventory = player.Inventory;
        var cargoSlots = inventory.CargoSlots
            .Select(slot => InventorySlotProjector.Project(inventory, slot))
            .ToArray();
        var equipmentSlots =
            InventorySlotProjector.ProjectEquipmentSlots(inventory);
        var ammoSlots = InventorySlotProjector.ProjectAmmoSlots(inventory);

        return new Dictionary<string, object?>(
            StringComparer.Ordinal)
        {
            ["available"] = inventory.IsAvailable,
            ["cargo"] = new Dictionary<string, object?>(
                StringComparer.Ordinal)
            {
                ["capacity"] = inventory.CargoCapacity,
                ["used"] = inventory.CargoUsedSlotCount,
                ["free"] = inventory.CargoFreeSlotCount,
                ["unavailable"] =
                    inventory.CargoUnavailableSlotCount,
                ["slots"] = MapInventorySlots(cargoSlots),
                ["items"] = MapInventorySlots(
                    cargoSlots.Where(slot => slot.IsOccupied)),
            },
            ["equipment"] = new Dictionary<string, object?>(
                StringComparer.Ordinal)
            {
                ["weapon_slot_count"] =
                    inventory.FutureWeaponSlotCount,
                ["device_slot_count"] =
                    inventory.FutureDeviceSlotCount,
                ["occupied_weapon_count"] =
                    inventory.OccupiedWeaponSlotCount,
                ["occupied_device_count"] =
                    inventory.OccupiedDeviceSlotCount,
                ["usable_weapon_slot_count"] =
                    inventory.UsableWeaponSlotCount,
                ["usable_device_slot_count"] =
                    inventory.UsableDeviceSlotCount,
                ["busy_count"] =
                    inventory.BusyEquipmentSlotCount,
                ["ready_weapon_count"] =
                    inventory.OperationallyReadyWeaponCount,
                ["ready_device_count"] =
                    inventory.OperationallyReadyDeviceCount,
                ["slots"] = MapInventorySlots(equipmentSlots),
                ["items"] = MapInventorySlots(
                    equipmentSlots.Where(slot => slot.IsOccupied)),
            },
            ["ammo"] = new Dictionary<string, object?>(
                StringComparer.Ordinal)
            {
                ["slots"] = MapInventorySlots(ammoSlots),
                ["items"] = MapInventorySlots(
                    ammoSlots.Where(slot => slot.IsOccupied)),
            },
            ["secure"] = MapSecureInventory(
                player.SecureInventory),
            ["reward"] = MapSecondaryInventory(
                player.RewardOverflowInventory.IsAvailable,
                player.RewardOverflowInventory.RewardSlots,
                player.RewardOverflowInventory
                    .RewardUnavailableSlotCount),
            ["overflow"] = MapSecondaryInventory(
                player.RewardOverflowInventory.IsAvailable,
                player.RewardOverflowInventory.OverflowSlots,
                player.RewardOverflowInventory
                    .OverflowUnavailableSlotCount),
            ["vendor"] = MapVendorInventory(
                player.VendorInventory),
        };
    }

    private static object?[] MapInventorySlots(
        IEnumerable<AddonInventorySlotSnapshot> slots)
    {
        return slots
            .Select(slot =>
                (object?)InventorySlotProjector.ToPublicData(slot))
            .ToArray();
    }

    private static IReadOnlyDictionary<string, object?> MapSecureInventory(
        ClientSecureInventoryObservation secure)
    {
        var slots = secure.Slots
            .Select(InventorySlotProjector.Project)
            .ToArray();

        return new Dictionary<string, object?>(
            StringComparer.Ordinal)
        {
            ["available"] = secure.IsAvailable,
            ["used"] = secure.OccupiedSlotCount,
            ["free"] = secure.FreeSlotCount,
            ["unavailable"] =
                secure.UnavailableSlotCount,
            ["slots"] = MapInventorySlots(slots),
            ["items"] = MapInventorySlots(
                slots.Where(slot => slot.IsOccupied)),
        };
    }

    private static IReadOnlyDictionary<string, object?>
        MapSecondaryInventory(
            bool available,
            IReadOnlyList<ClientInventoryItemObservation> observedSlots,
            int unavailableSlotCount)
    {
        var slots = observedSlots
            .Select(InventorySlotProjector.Project)
            .ToArray();

        return new Dictionary<string, object?>(
            StringComparer.Ordinal)
        {
            ["available"] = available,
            ["used"] = slots.Count(slot => slot.IsOccupied),
            ["unavailable"] = unavailableSlotCount,
            ["slots"] = MapInventorySlots(slots),
            ["items"] = MapInventorySlots(
                slots.Where(slot => slot.IsOccupied)),
        };
    }

    private static IReadOnlyDictionary<string, object?> MapVendorInventory(
        ClientVendorInventoryObservation vendor)
    {
        return new Dictionary<string, object?>(
            StringComparer.Ordinal)
        {
            ["available"] = vendor.IsAvailable,
            ["loaded"] = vendor.HasLoadedSnapshot,
            ["current_credits"] = vendor.CurrentCredits,
            ["item_count"] = vendor.OccupiedSlotCount,
            ["affordable_count"] =
                vendor.AffordableItemCount,
            ["unaffordable_count"] =
                vendor.UnaffordableItemCount,
            ["items"] = vendor.Items
                .Select(
                    item =>
                        (object?)new Dictionary<string, object?>(
                            StringComparer.Ordinal)
                        {
                            ["slot"] = item.Slot,
                            ["name"] = Normalize(item.ItemName) ??
                                       ResolvePublicItemName(
                                           item.ItemTemplateId),
                            ["stack_count"] = item.StackCount,
                            ["quality_percent"] =
                                item.QualityPercent,
                            ["structure_percent"] =
                                item.StructurePercent,
                            ["price"] = item.Price,
                            ["affordable"] = item.IsAffordable,
                        })
                .ToArray(),
        };
    }

    private static IReadOnlyDictionary<string, object?> MapBuffs(
        ClientBuffsObservation buffs)
    {
        return new Dictionary<string, object?>(
            StringComparer.Ordinal)
        {
            ["available"] = buffs.IsAvailable,
            ["active_count"] = buffs.ActiveBuffCount,
            ["permanent_count"] = buffs.PermanentBuffCount,
            ["timed_count"] = buffs.TimedBuffCount,
            ["nominally_expired_count"] =
                buffs.NominallyExpiredBuffCount,
            ["items"] = buffs.ActiveBuffs
                .Select(
                    buff =>
                        (object?)new Dictionary<string, object?>(
                            StringComparer.Ordinal)
                        {
                            ["slot"] = buff.Slot,
                            ["name"] = buff.DisplayName,
                            ["type"] = Normalize(buff.BuffType),
                            ["permanent"] = buff.IsPermanent,
                            ["timed"] = buff.IsTimed,
                            ["remaining_milliseconds"] =
                                buff.NominalRemainingMilliseconds,
                            ["nominally_expired"] =
                                buff.IsNominallyExpired,
                        })
                .ToArray(),
        };
    }

    private static IReadOnlyDictionary<string, object?> MapMissions(
        ClientMissionLogObservation missions)
    {
        return new Dictionary<string, object?>(
            StringComparer.Ordinal)
        {
            ["available"] = missions.IsAvailable,
            ["capacity"] = missions.Capacity,
            ["count"] = missions.OccupiedSlotCount,
            ["items"] = missions.Missions
                .Select(
                    mission =>
                        (object?)new Dictionary<string, object?>(
                            StringComparer.Ordinal)
                        {
                            ["slot"] = mission.Slot,
                            ["name"] = mission.Name,
                            ["summary"] = mission.Summary,
                            ["reward"] = mission.Reward,
                            ["failure_consequence"] =
                                mission.FailureConsequence,
                            ["issuing_faction"] =
                                mission.IssuingFaction,
                            ["stage"] = mission.Stage,
                            ["stage_count"] =
                                mission.StageCount,
                            ["timed"] = mission.IsTimed,
                            ["forfeitable"] =
                                mission.IsForfeitable,
                            ["complete"] = mission.IsComplete,
                            ["failed"] = mission.IsFailed,
                            ["expired"] = mission.IsExpired,
                            ["fully_visible"] =
                                mission.IsFullyVisible,
                            ["remaining_milliseconds"] =
                                mission.RemainingMilliseconds,
                            ["terminal"] = mission.IsTerminal,
                            ["current_stage_text"] =
                                Normalize(mission.CurrentStageText),
                            ["stages"] = mission.Stages
                                .Select(
                                    stage =>
                                        (object?)new Dictionary<string, object?>(
                                            StringComparer.Ordinal)
                                        {
                                            ["number"] = stage.Index + 1,
                                            ["available"] =
                                                stage.IsAvailable,
                                            ["text"] = stage.Text,
                                            ["timed"] = stage.IsTimed,
                                        })
                                .ToArray(),
                        })
                .ToArray(),
        };
    }

    private static IReadOnlyDictionary<string, object?> MapReputations(
        ClientReputationObservation reputation)
    {
        return new Dictionary<string, object?>(
            StringComparer.Ordinal)
        {
            ["available"] = reputation.IsAvailable,
            ["affiliation"] = Normalize(reputation.Affiliation),
            ["count"] = reputation.OccupiedSlotCount,
            ["factions"] = reputation.Factions
                .Select(
                    faction =>
                        (object?)new Dictionary<string, object?>(
                            StringComparer.Ordinal)
                        {
                            ["name"] = faction.DisplayName,
                            ["description"] =
                                faction.Description,
                            ["reaction"] = faction.Reaction,
                            ["disposition"] =
                                faction.NormalizedReaction,
                        })
                .ToArray(),
        };
    }

    private static IReadOnlyDictionary<string, object?> MapNavigation(
        ClientNavigationObservation navigation,
        ClientNavigationStateObservation navigationState,
        AddonNavigationRouteSnapshot? routeSnapshot)
    {
        return new Dictionary<string, object?>(
            StringComparer.Ordinal)
        {
            ["available"] = navigation.IsAvailable,
            ["target_count"] = navigation.Targets.Count,
            ["visited_count"] = navigation.VisitedCount,
            ["undiscovered_count"] =
                navigation.UndiscoveredCount,
            ["route_candidate_count"] =
                navigation.RouteCandidateCount,
            ["huge_count"] = navigation.HugeCount,
            ["route"] = MapNavigationRoute(routeSnapshot),
            ["control"] = MapNavigationControl(navigationState),
            ["targets"] = navigation.Targets
                .Where(target => target.IsAvailable)
                .Select(
                    target =>
                        (object?)new Dictionary<string, object?>(
                            StringComparer.Ordinal)
                        {
                            ["name"] = Normalize(target.Name),
                            ["owner_name"] = Normalize(target.Owner),
                            ["title"] = Normalize(target.Title),
                            ["rank"] = Normalize(target.Rank),
                            ["kind"] = NormalizeNavigationTargetKind(
                                target.RawObjectType),
                            ["display_name"] =
                                Normalize(target.MapDisplayName),
                            ["signature"] = target.Signature,
                            ["visited"] =
                                target.PlayerHasVisited,
                            ["huge"] = target.IsHuge,
                            ["route_candidate"] =
                                target.IsRouteCandidate,
                            ["spatial"] =
                                MapSpatial(target.Spatial),
                        })
                .ToArray(),
        };
    }

    private static IReadOnlyDictionary<string, object?> MapNavigationRoute(
        AddonNavigationRouteSnapshot? snapshot)
    {
        snapshot ??= new AddonNavigationRouteSnapshot
        {
            StatusText = "Navigation route service is unavailable",
        };

        return new Dictionary<string, object?>(
            StringComparer.Ordinal)
        {
            ["available"] = snapshot.IsAvailable,
            ["status"] = snapshot.Status,
            ["status_text"] = Normalize(snapshot.StatusText),
            ["has_route"] = snapshot.HasRoute,
            ["plan"] = snapshot.Route == null
                ? null
                : MapNavigationPlannedRoute(snapshot.Route),
            ["journey"] = new Dictionary<string, object?>(
                StringComparer.Ordinal)
            {
                ["state"] = snapshot.Journey.State,
                ["status_text"] =
                    Normalize(snapshot.Journey.StatusText),
                ["stop_reason"] =
                    Normalize(snapshot.Journey.StopReason),
                ["started_at"] = snapshot.Journey.StartedAt?
                    .ToUnixTimeMilliseconds(),
                ["pause_reason"] =
                    Normalize(snapshot.Journey.PauseReason),
                ["is_active"] = snapshot.Journey.IsActive,
                ["expected_target_name"] =
                    Normalize(snapshot.Journey.ExpectedTargetName),
                ["expected_sector_name"] =
                    Normalize(snapshot.Journey.ExpectedSectorName),
                ["current_energy"] = snapshot.Journey.CurrentEnergy,
                ["required_energy"] = snapshot.Journey.RequiredEnergy,
                ["can_start"] = snapshot.Journey.CanStart,
                ["can_stop"] = snapshot.Journey.CanStop,
                ["can_select_next_target"] =
                    snapshot.Journey.CanSelectNextTarget,
                ["can_pause"] = snapshot.Journey.CanPause,
                ["can_resume"] = snapshot.Journey.CanResume,
                ["can_clear"] = snapshot.Journey.CanClear,
                ["can_plan_return_trip"] =
                    snapshot.Journey.CanPlanReturnTrip,
            },
        };
    }

    private static IReadOnlyDictionary<string, object?>
        MapNavigationPlannedRoute(
            AddonNavigationPlannedRouteSnapshot route)
    {
        return new Dictionary<string, object?>(
            StringComparer.Ordinal)
        {
            ["created_at"] = route.CreatedAt.ToUnixTimeMilliseconds(),
            ["updated_at"] = route.UpdatedAt.ToUnixTimeMilliseconds(),
            ["origin"] = MapNavigationLocation(route.Origin),
            ["origin_destination"] =
                MapNavigationDestination(
                    route.OriginDestination),
            ["current"] = MapNavigationLocation(route.Current),
            ["destination"] =
                MapNavigationDestination(route.Destination),
            ["completed_hops"] = route.CompletedHopCount,
            ["remaining_hops"] = route.RemainingHopCount,
            ["total_hops"] = route.TotalHopCount,
            ["next_step"] = route.NextStep == null
                ? null
                : MapNavigationRouteStep(route.NextStep),
            ["steps"] = route.Steps
                .Select(
                    step =>
                        (object?)MapNavigationRouteStep(step))
                .ToArray(),
            ["warnings"] = route.Warnings.Cast<object?>().ToArray(),
        };
    }

    private static IReadOnlyDictionary<string, object?>
        MapNavigationDestination(
            AddonNavigationDestinationSnapshot destination)
    {
        return new Dictionary<string, object?>(
            StringComparer.Ordinal)
        {
            ["kind"] = destination.Kind,
            ["sector"] =
                MapNavigationLocation(destination.Sector),
            ["target"] = destination.Target == null
                ? null
                : MapNavigationTarget(destination.Target),
        };
    }

    private static IReadOnlyDictionary<string, object?>
        MapNavigationRouteStep(
            AddonNavigationRouteStepSnapshot step)
    {
        return new Dictionary<string, object?>(
            StringComparer.Ordinal)
        {
            ["number"] = step.Number,
            ["kind"] = step.Kind,
            ["from"] = MapNavigationLocation(step.From),
            ["to"] = MapNavigationLocation(step.To),
            ["departure_target"] = step.DepartureTarget == null
                ? null
                : MapNavigationTarget(step.DepartureTarget),
            ["final_target"] = step.FinalTarget == null
                ? null
                : MapNavigationTarget(step.FinalTarget),
            ["access_requirement"] =
                Normalize(step.AccessRequirement),
        };
    }

    private static IReadOnlyDictionary<string, object?>
        MapNavigationTarget(
            AddonNavigationTargetSnapshot target)
    {
        return new Dictionary<string, object?>(
            StringComparer.Ordinal)
        {
            ["name"] = target.Name,
            ["type"] = target.Type,
            ["has_position"] = target.HasPosition,
            ["x"] = target.HasPosition ? target.X : null,
            ["y"] = target.HasPosition ? target.Y : null,
            ["z"] = target.HasPosition ? target.Z : null,
        };
    }

    private static IReadOnlyDictionary<string, object?>
        MapNavigationLocation(
            AddonNavigationLocationSnapshot location)
    {
        return new Dictionary<string, object?>(
            StringComparer.Ordinal)
        {
            ["sector_name"] = location.SectorName,
            ["system_name"] = location.SystemName,
        };
    }

    private static IReadOnlyDictionary<string, object?>
        MapFunctionalSpatial(
            ClientSpatialObservation spatial)
    {
        return new Dictionary<string, object?>(
            StringComparer.Ordinal)
        {
            ["available"] = spatial.IsAvailable,
            ["position"] = spatial.IsAvailable
                ? new Dictionary<string, object?>(
                    StringComparer.Ordinal)
                {
                    ["x"] = spatial.Position.X,
                    ["y"] = spatial.Position.Y,
                    ["z"] = spatial.Position.Z,
                }
                : null,
            ["targeting_distance_radius"] =
                spatial.IsAvailable
                    ? spatial.TargetingDistanceRadius
                    : null,
        };
    }

    private static string NormalizeNavigationTargetKind(
        byte rawObjectType)
    {
        return rawObjectType switch
        {
            3 => "planet",
            11 => "sector_gate",
            12 => "station",
            37 => "navigation_point",
            38 => "asteroid",
            _ => "unknown",
        };
    }

    private static string? ResolveItemName(int? itemTemplateId)
    {
        return ClientItemTemplateNameResolver.GetKnownName(
            itemTemplateId);
    }

    private static string? ResolvePublicItemName(int? itemTemplateId)
    {
        if (!itemTemplateId.HasValue || itemTemplateId.Value <= 0)
        {
            return null;
        }

        return ResolveItemName(itemTemplateId) ?? "Unknown item";
    }

    private static IReadOnlyDictionary<string, object?> MapSpatial(
        ClientSpatialObservation spatial)
    {
        return new Dictionary<string, object?>(
            StringComparer.Ordinal)
        {
            ["available"] = spatial.IsAvailable,
            ["position"] = spatial.IsAvailable
                ? new Dictionary<string, object?>(
                    StringComparer.Ordinal)
                {
                    ["x"] = spatial.Position.X,
                    ["y"] = spatial.Position.Y,
                    ["z"] = spatial.Position.Z,
                }
                : null,
            ["targeting_distance_radius"] =
                spatial.IsAvailable
                    ? spatial.TargetingDistanceRadius
                    : null,
        };
    }

    private static IReadOnlyDictionary<string, object?> MapStarbase(
        ClientStarbaseContextObservation starbase)
    {
        return new Dictionary<string, object?>(
            StringComparer.Ordinal)
        {
            ["available"] = starbase.IsAvailable,
            ["interaction"] =
                MapStarbaseInteraction(starbase),
            ["current_room"] = starbase.CurrentRoom == null
                ? null
                : MapStarbaseRoom(starbase.CurrentRoom),
            ["rooms"] = starbase.Rooms
                .Select(
                    room =>
                        (object?)MapStarbaseRoom(room))
                .ToArray(),
        };
    }

    private static IReadOnlyDictionary<string, object?>
        MapStarbaseInteraction(
            ClientStarbaseContextObservation starbase)
    {
        var interaction = starbase.Interaction;
        var interactionRoom = starbase.Rooms.FirstOrDefault(
            room => room.RoomClass == interaction.RoomClass) ??
            starbase.CurrentRoom;
        var npc = interactionRoom?.Npcs.FirstOrDefault(
            candidate =>
                interaction.NpcSlot >= 0 &&
                candidate.Slot == interaction.NpcSlot) ??
            interactionRoom?.Npcs.FirstOrDefault(
                candidate =>
                    !string.IsNullOrWhiteSpace(interaction.NpcName) &&
                    string.Equals(
                        candidate.Name,
                        interaction.NpcName,
                        StringComparison.OrdinalIgnoreCase));

        return new Dictionary<string, object?>(
            StringComparer.Ordinal)
        {
            ["active"] = interaction.IsActive,
            ["kind"] = NormalizeEnum(interaction.Kind),
            ["facility_name"] =
                Normalize(interaction.FacilityName),
            ["npc_name"] = Normalize(
                string.IsNullOrWhiteSpace(interaction.NpcName)
                    ? npc?.Name
                    : interaction.NpcName),
            ["npc"] = npc == null
                ? null
                : MapStarbaseNpc(npc),
        };
    }

    private static IReadOnlyDictionary<string, object?> MapStarbaseRoom(
        ClientStarbaseRoomObservation room)
    {
        return new Dictionary<string, object?>(
            StringComparer.Ordinal)
        {
            ["current"] = room.IsCurrent,
            ["facilities"] = room.Facilities
                .Select(
                    facility =>
                        (object?)new Dictionary<string, object?>(
                            StringComparer.Ordinal)
                        {
                            ["name"] =
                                Normalize(facility.FacilityTypeName),
                            ["interaction_kind"] =
                                NormalizeEnum(
                                    facility.InteractionKind),
                            ["panel_kind"] =
                                facility.PanelKind.HasValue
                                    ? NormalizeEnum(
                                        facility.PanelKind.Value)
                                    : null,
                        })
                .ToArray(),
            ["npcs"] = room.Npcs
                .Select(
                    npc =>
                        (object?)MapStarbaseNpc(npc))
                .ToArray(),
        };
    }

    private static IReadOnlyDictionary<string, object?> MapStarbaseNpc(
        ClientStarbaseNpcObservation npc)
    {
        return new Dictionary<string, object?>(
            StringComparer.Ordinal)
        {
            ["name"] = Normalize(npc.Name),
            ["vendor_type"] = NormalizeEnum(npc.VendorType),
            ["ambient_type"] = NormalizeEnum(npc.AmbientType),
            ["is_vendor"] = npc.IsVendor,
        };
    }

    private static IReadOnlyDictionary<string, object?> MapPanels(
        ClientPanelPresentationObservation panels,
        ClientStarMapPresentationObservation starMap,
        ClientMissionLogObservation missions,
        ClientReputationObservation reputation)
    {
        return new Dictionary<string, object?>(
            StringComparer.Ordinal)
        {
            ["available"] =
                panels.IsAvailable || starMap.IsAvailable,
            ["inventory"] = new Dictionary<string, object?>(
                StringComparer.Ordinal)
            {
                ["available"] = panels.IsAvailable,
                ["displayed"] =
                    panels.IsInventoryDisplayed ||
                    panels.IsEquipmentDisplayed ||
                    panels.IsVaultDisplayed,
                ["mode"] = NormalizeEnum(panels.InventoryMode),
            },
            ["character"] = new Dictionary<string, object?>(
                StringComparer.Ordinal)
            {
                ["available"] = panels.IsAvailable,
                ["displayed"] =
                    panels.IsCharacterInfoDisplayed,
                ["active_tab"] =
                    NormalizeEnum(panels.ActiveCharacterInfoTab),
                ["last_selected_tab"] =
                    NormalizeEnum(
                        panels.LastSelectedCharacterInfoTab),
                ["mission_details"] =
                    MapMissionDetails(
                        panels.MissionDetails,
                        missions),
                ["faction_details"] =
                    MapFactionDetails(
                        panels.FactionDetails,
                        reputation),
            },
            ["star_map"] = new Dictionary<string, object?>(
                StringComparer.Ordinal)
            {
                ["available"] = starMap.IsAvailable,
                ["displayed"] = starMap.IsDisplayed,
                ["maximized"] = starMap.IsMaximized,
                ["presentation"] =
                    NormalizeEnum(
                        starMap.SelectedPresentation),
            },
        };
    }

    private static IReadOnlyDictionary<string, object?> MapMissionDetails(
        ClientMissionDetailsPresentationObservation details,
        ClientMissionLogObservation missions)
    {
        var selectedMission =
            details.IsDisplayed &&
            missions.IsAvailable
                ? missions.GetByAddress(
                    details.SelectedMissionAddress)
                : null;

        return new Dictionary<string, object?>(
            StringComparer.Ordinal)
        {
            ["available"] = details.IsAvailable,
            ["displayed"] = details.IsDisplayed,
            ["has_mission"] = selectedMission != null,
            ["mission"] = selectedMission == null
                ? null
                : new Dictionary<string, object?>(
                    StringComparer.Ordinal)
                {
                    ["slot"] = selectedMission.Slot,
                    ["name"] = selectedMission.Name,
                    ["stage"] = selectedMission.Stage,
                    ["stage_count"] =
                        selectedMission.StageCount,
                    ["current_stage_text"] =
                        Normalize(
                            selectedMission.CurrentStageText),
                },
        };
    }

    private static IReadOnlyDictionary<string, object?> MapLoot(
        ClientLootingObservation looting,
        ClientLootTractorObservation tractor)
    {
        return new Dictionary<string, object?>(
            StringComparer.Ordinal)
        {
            ["available"] = looting.IsAvailable,
            ["panel_displayed"] =
                looting.IsLootPanelDisplayed,
            ["has_target"] = looting.HasAttachedLootTarget,
            ["active"] = looting.IsLootSessionActive,
            ["tractor"] = MapLootTractor(tractor),
        };
    }

    private static IReadOnlyDictionary<string, object?> MapLootTractor(
        ClientLootTractorObservation tractor)
    {
        return new Dictionary<string, object?>(
            StringComparer.Ordinal)
        {
            ["available"] = tractor.IsAvailable,
            ["is_tractoring"] = tractor.IsTractoring,
            ["recently_completed"] = tractor.WasRecentlyCompleted,
            ["recently_interrupted"] = tractor.WasRecentlyInterrupted,
            ["item_name"] = Normalize(tractor.ItemName),
            ["started_at"] = tractor.StartedAt?.ToUnixTimeMilliseconds(),
            ["completed_at"] = tractor.CompletedAt?.ToUnixTimeMilliseconds(),
        };
    }

    private static IReadOnlyDictionary<string, object?> MapStats(
        ClientNetworkTrafficObservation network,
        ClientFrameRateObservation frameRate)
    {
        return new Dictionary<string, object?>(
            StringComparer.Ordinal)
        {
            ["network"] = new Dictionary<string, object?>(
                StringComparer.Ordinal)
            {
                ["available"] = network.IsAvailable,
                ["receive_bytes_per_second"] =
                    network.ReceiveBytesPerSecond,
                ["send_bytes_per_second"] =
                    network.SendBytesPerSecond,
                ["averaging_window_seconds"] =
                    network.AveragingWindowSeconds,
            },
            ["frame_rate"] = new Dictionary<string, object?>(
                StringComparer.Ordinal)
            {
                ["available"] = frameRate.IsAvailable,
                ["smoothed"] =
                    frameRate.SmoothedFramesPerSecond,
                ["minimum"] =
                    frameRate.MinimumFramesPerSecond,
                ["maximum"] =
                    frameRate.MaximumFramesPerSecond,
            },
        };
    }

    private static IReadOnlyDictionary<string, object?> MapCombat(
        ClientCombatObservation combat,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> recentEvents)
    {
        return new Dictionary<string, object?>(
            StringComparer.Ordinal)
        {
            ["available"] = combat.IsAvailable,
            ["active"] =
                combat.PollingMode is not
                    ClientCombatPollingMode.Dormant and not
                    ClientCombatPollingMode.Idle,
            ["combat_music"] =
                combat.IsCombatMusicContext,
            ["last_damage_observed_at"] =
                combat.LastDamageObservedAt,
            ["event_count"] = combat.DamagePacketCount,
            ["recent_events"] = recentEvents,
        };
    }

    private static IReadOnlyDictionary<string, object?> MapCombatEvent(
        ClientCombatEventObservation combatEvent)
    {
        return new Dictionary<string, object?>(
            StringComparer.Ordinal)
        {
            // Compatibility-only ordering values used by the bundled DPS addons.
            ["sequence"] = combatEvent.Sequence,
            ["observed_at"] = combatEvent.ObservedAt,
            ["client_time"] = combatEvent.ClientTime,
            ["damage"] = combatEvent.Damage,
            ["modifier"] = combatEvent.Modifier,
            ["unmodified_damage"] =
                combatEvent.UnmodifiedDamage,
            ["damage_type"] =
                combatEvent.KnownDamageType is { } damageType
                    ? NormalizeEnum(damageType)
                    : "unknown",
            ["critical"] = combatEvent.IsCritical,
            ["direction"] =
                NormalizeEnum(combatEvent.Direction),
            ["source"] =
                MapCombatIdentity(
                    combatEvent.SourceIdentity),
            ["victim"] =
                MapCombatIdentity(
                    combatEvent.VictimIdentity),
        };
    }

    private static IReadOnlyDictionary<string, object?> MapCombatIdentity(
        ClientCombatActorIdentityObservation identity)
    {
        return new Dictionary<string, object?>(
            StringComparer.Ordinal)
        {
            // Compatibility-only identity used by the bundled DPS addons to
            // correlate combat actors with the nearby-target list.
            ["id"] = identity.ObjectId != 0
                ? identity.ObjectId
                : null,
            ["identified"] = identity.IsResolved,
            ["display_name"] =
                Normalize(identity.DisplayName),
            ["name"] = Normalize(identity.Name),
            ["owner_name"] = Normalize(identity.Owner),
            ["title"] = Normalize(identity.Title),
            ["rank"] = Normalize(identity.Rank),
        };
    }

    private static IReadOnlyDictionary<string, object?> MapSyntheticTrack(
        int level)
    {
        return new Dictionary<string, object?>(
            StringComparer.Ordinal)
        {
            ["level"] = level,
            ["progress"] = 0.5,
            ["progress_percent"] = 50.0,
            ["maximum_level"] = false,
            ["next_level"] = level + 1,
            ["complete"] = true,
        };
    }

    private static IReadOnlyDictionary<string, object?> MapSyntheticVital(
        int percent)
    {
        return new Dictionary<string, object?>(
            StringComparer.Ordinal)
        {
            ["available"] = true,
            ["has_data"] = true,
            ["current"] = percent,
            ["maximum"] = 100,
            ["percent"] = percent,
        };
    }

    private static IReadOnlyDictionary<string, object?> EmptyDomain()
    {
        return new Dictionary<string, object?>(
            StringComparer.Ordinal)
        {
            ["available"] = false,
        };
    }

    private static string NormalizeLifecycle(
        ClientLifecycleState state)
    {
        return state switch
        {
            ClientLifecycleState.Unknown => "unknown",
            ClientLifecycleState.ApplicationStarted => "application_started",
            ClientLifecycleState.IntroScene => "intro",
            ClientLifecycleState.LoginScreen => "login",
            ClientLifecycleState.CharacterSelection => "character_selection",
            ClientLifecycleState.InGame => "in_game",
            _ => "unknown",
        };
    }

    private static string NormalizeEnvironment(
        ClientWorldEnvironment environment)
    {
        return environment switch
        {
            ClientWorldEnvironment.Unknown => "unknown",
            ClientWorldEnvironment.Transitioning => "transitioning",
            ClientWorldEnvironment.Space => "space",
            ClientWorldEnvironment.Planet => "planet",
            ClientWorldEnvironment.Initializing => "initializing",
            ClientWorldEnvironment.Movie3D => "movie",
            ClientWorldEnvironment.Starbase => "starbase",
            ClientWorldEnvironment.GasGiant => "gas_giant",
            ClientWorldEnvironment.ScriptedMovie => "scripted_movie",
            _ => "unknown",
        };
    }

    private static string NormalizeEnum<T>(T value)
        where T : struct, Enum
    {
        return ToSnakeCase(value.ToString());
    }

    private static string ToSnakeCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        var result = new System.Text.StringBuilder(
            value.Length + 8);

        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];

            if (char.IsUpper(character) &&
                index > 0 &&
                value[index - 1] != '_')
            {
                result.Append('_');
            }

            result.Append(
                char.ToLowerInvariant(character));
        }

        return result.ToString();
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private sealed class ProjectionCache
    {
        private readonly Dictionary<string, CachedDomainProjection>
            domains = new(StringComparer.Ordinal);

        public CachedDomainProjection GetOrCreate(
            string name,
            Func<DomainProjection> factory,
            params object?[] sources)
        {
            if (this.domains.TryGetValue(
                    name,
                    out var existing) &&
                existing.Matches(sources))
            {
                return existing;
            }

            var projection = factory();
            var updated = new CachedDomainProjection
            {
                Name = name,
                Sources = sources,
                Value = projection.Value,
                Fingerprint = projection.Fingerprint,
                EventFingerprint = projection.EventFingerprint,
                RecentCombatEvents =
                    projection.RecentCombatEvents,
            };

            this.domains[name] = updated;
            return updated;
        }
    }

    private sealed class CachedDomainProjection
    {
        public required string Name { get; init; }

        public required object?[] Sources { get; init; }

        public required IReadOnlyDictionary<string, object?> Value
        { get; init; }

        public required string Fingerprint { get; init; }

        public required string EventFingerprint { get; init; }

        public required IReadOnlyList<
            IReadOnlyDictionary<string, object?>>
            RecentCombatEvents
        { get; init; }

        public bool Matches(object?[] sources)
        {
            if (this.Sources.Length != sources.Length)
            {
                return false;
            }

            for (var index = 0; index < sources.Length; index++)
            {
                var first = this.Sources[index];
                var second = sources[index];

                if (ReferenceEquals(first, second))
                {
                    continue;
                }

                if (first == null ||
                    second == null ||
                    first.GetType().IsValueType ||
                    first is string)
                {
                    if (Equals(first, second))
                    {
                        continue;
                    }
                }

                return false;
            }

            return true;
        }
    }

    private sealed record DomainProjection
    {
        public required IReadOnlyDictionary<string, object?> Value
        { get; init; }

        public required string Fingerprint { get; init; }

        public required string EventFingerprint { get; init; }

        public required IReadOnlyList<
            IReadOnlyDictionary<string, object?>>
            RecentCombatEvents
        { get; init; }
    }

    private sealed record CachedPublicProjection
    {
        public required IReadOnlyDictionary<string, object?> PublicData
        { get; init; }

        public required IReadOnlyDictionary<string, string>
            DomainFingerprints
        { get; init; }

        public required IReadOnlyDictionary<string, string>
            EventDomainFingerprints
        { get; init; }

        public required IReadOnlyList<
            IReadOnlyDictionary<string, object?>>
            RecentCombatEvents
        { get; init; }
    }
}
