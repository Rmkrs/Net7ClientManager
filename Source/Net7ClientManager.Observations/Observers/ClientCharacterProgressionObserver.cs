namespace Net7ClientManager.Observations.Observers;

using System.Globalization;
using Net7ClientManager.Observations.Models;

internal sealed class ClientCharacterProgressionObserver
{
    private const string CombatExperienceProperty =
        "RPGInfo.CombatExp";

    private const string CombatLevelProperty =
        "RPGInfo.CombatLevel";

    private const string ExploreExperienceProperty =
        "RPGInfo.ExploreExp";

    private const string ExploreLevelProperty =
        "RPGInfo.ExploreLevel";

    private const string TradeExperienceProperty =
        "RPGInfo.TradeExp";

    private const string TradeLevelProperty =
        "RPGInfo.TradeLevel";

    private const string HullUpgradeLevelProperty =
        "RPGInfo.HullUpgradeLevel";

    private const string SkillPointsProperty =
        "RPGInfo.SkillPoints";

    private const string RpgInfoProperty =
        "Hull.RPGInfo";

    private const string RaceProperty =
        "Hull.RPGInfo.Race";

    private const string ProfessionProperty =
        "Hull.RPGInfo.Profession";

    private const string SkillsProperty =
        "Hull.RPGInfo.Skills";

    private static readonly string[] progressionPropertyNames =
    [
        CombatExperienceProperty,
        CombatLevelProperty,
        ExploreExperienceProperty,
        ExploreLevelProperty,
        TradeExperienceProperty,
        TradeLevelProperty,
        HullUpgradeLevelProperty,
        SkillPointsProperty,
    ];

    private static readonly string[] hullPropertyNames =
    [
        RpgInfoProperty,
    ];

    private static readonly string[] rpgInfoPropertyNames =
    [
        RaceProperty,
        ProfessionProperty,
        SkillsProperty,
    ];

    private readonly ClientAuxDataLookupReader auxDataReader =
        new();

    private readonly ClientCharacterSkillObserver skillObserver =
        new();

    private readonly Dictionary<int, CachedProgressionLookup>
        cachedLookups = [];

    public ClientCharacterProgressionObservation Observe(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        int processId,
        uint hullAuxDataAddress)
    {
        if (hullAuxDataAddress == 0)
        {
            return ClientCharacterProgressionObservation.Unavailable(
                "SClient Hull AuxData (+0x12C0) is unavailable");
        }

        if (!this.cachedLookups.TryGetValue(
                processId,
                out var cachedLookup) ||
            cachedLookup.ModuleBaseAddress !=
                moduleBaseAddress ||
            cachedLookup.HullAuxDataAddress !=
                hullAuxDataAddress)
        {
            if (!this.TryOpenProgressionLookups(
                    memory,
                    moduleBaseAddress,
                    hullAuxDataAddress,
                    out cachedLookup,
                    out var openError))
            {
                this.cachedLookups.Remove(
                    processId);

                return ClientCharacterProgressionObservation.Unavailable(
                    openError,
                    hullAuxDataAddress);
            }

            this.cachedLookups[processId] =
                cachedLookup;
        }

        List<string> errors = [];
        var hardReadFailure = false;

        Dictionary<string, uint> propertyAddresses =
            new(StringComparer.Ordinal);

        int? ReadInt32(
            ClientAuxDataLookupSnapshot lookup,
            string propertyName)
        {
            if (!lookup.Properties.TryGetValue(
                    propertyName,
                    out var propertyAddress) ||
                propertyAddress == 0)
            {
                errors.Add(
                    $"{propertyName} was not found");

                return null;
            }

            propertyAddresses[propertyName] =
                propertyAddress;

            if (!this.auxDataReader.TryReadInt32Property(
                    memory,
                    lookup,
                    propertyName,
                    out var sample,
                    out var readError))
            {
                hardReadFailure = true;
                errors.Add(readError);
                return null;
            }

            if (!sample.IsValid)
            {
                errors.Add(
                    $"{propertyName} is present but not valid");

                return null;
            }

            return sample.Value;
        }

        float? ReadProgressFraction(
            ClientAuxDataLookupSnapshot lookup,
            string propertyName)
        {
            if (!lookup.Properties.TryGetValue(
                    propertyName,
                    out var propertyAddress) ||
                propertyAddress == 0)
            {
                errors.Add(
                    $"{propertyName} was not found");

                return null;
            }

            propertyAddresses[propertyName] =
                propertyAddress;

            if (!this.auxDataReader.TryReadFloatProperty(
                    memory,
                    lookup,
                    propertyName,
                    out var sample,
                    out var readError))
            {
                hardReadFailure = true;
                errors.Add(readError);
                return null;
            }

            if (!sample.IsValid)
            {
                errors.Add(
                    $"{propertyName} is present but not valid");

                return null;
            }

            if (sample.Value is < 0.0f or > 1.0f)
            {
                errors.Add(string.Create(CultureInfo.InvariantCulture, $"{propertyName} value {sample.Value} is outside the expected 0..1 range"));

                return null;
            }

            return sample.Value;
        }

        var combatExperience =
            ReadProgressFraction(
                cachedLookup.ProgressionLookup,
                CombatExperienceProperty);

        var combatLevel =
            ReadInt32(
                cachedLookup.ProgressionLookup,
                CombatLevelProperty);

        var exploreExperience =
            ReadProgressFraction(
                cachedLookup.ProgressionLookup,
                ExploreExperienceProperty);

        var exploreLevel =
            ReadInt32(
                cachedLookup.ProgressionLookup,
                ExploreLevelProperty);

        var tradeExperience =
            ReadProgressFraction(
                cachedLookup.ProgressionLookup,
                TradeExperienceProperty);

        var tradeLevel =
            ReadInt32(
                cachedLookup.ProgressionLookup,
                TradeLevelProperty);

        var hullUpgradeLevel =
            ReadInt32(
                cachedLookup.ProgressionLookup,
                HullUpgradeLevelProperty);

        var skillPoints =
            ReadInt32(
                cachedLookup.ProgressionLookup,
                SkillPointsProperty);

        var race =
            ReadInt32(
                cachedLookup.RpgInfoLookup,
                RaceProperty);

        var profession =
            ReadInt32(
                cachedLookup.RpgInfoLookup,
                ProfessionProperty);

        propertyAddresses[RpgInfoProperty] =
            cachedLookup.RpgInfoAddress;

        propertyAddresses[SkillsProperty] =
            cachedLookup.SkillsPropertyAddress;

        var combat =
            ClientCharacterExperienceTrackObservation.Create(
                combatLevel,
                combatExperience);

        var explore =
            ClientCharacterExperienceTrackObservation.Create(
                exploreLevel,
                exploreExperience);

        var trade =
            ClientCharacterExperienceTrackObservation.Create(
                tradeLevel,
                tradeExperience);

        int? hullTier = null;
        int? currentHullUpgradeOverallLevel = null;
        int? nextHullUpgradeOverallLevel = null;

        if (hullUpgradeLevel.HasValue &&
            ClientProgressionRules.TryGetHullUpgradeStage(
                hullUpgradeLevel.Value,
                out var observedHullTier,
                out var observedCurrentOverallLevel,
                out var observedNextOverallLevel))
        {
            hullTier = observedHullTier;
            currentHullUpgradeOverallLevel =
                observedCurrentOverallLevel;
            nextHullUpgradeOverallLevel =
                observedNextOverallLevel;
        }
        else if (hullUpgradeLevel.HasValue)
        {
            errors.Add(string.Create(CultureInfo.InvariantCulture, $"{HullUpgradeLevelProperty} value {hullUpgradeLevel.Value} is outside the known 0..6 stage range"));
        }

        var skills = this.skillObserver.Observe(
            memory,
            moduleBaseAddress,
            processId,
            cachedLookup.SkillsPropertyAddress,
            cachedLookup.RpgInfoLookup.Int32PropertyTypeDescriptor,
            skillPoints);

        if (hardReadFailure)
        {
            this.cachedLookups.Remove(
                processId);
        }

        var validProgressionValueCount =
            (combatLevel.HasValue ? 1 : 0) +
            (combatExperience.HasValue ? 1 : 0) +
            (exploreLevel.HasValue ? 1 : 0) +
            (exploreExperience.HasValue ? 1 : 0) +
            (tradeLevel.HasValue ? 1 : 0) +
            (tradeExperience.HasValue ? 1 : 0) +
            (hullUpgradeLevel.HasValue ? 1 : 0) +
            (skillPoints.HasValue ? 1 : 0);

        const int expectedProgressionValueCount = 8;

        string status;

        if (validProgressionValueCount ==
                expectedProgressionValueCount &&
            skills.IsAvailable)
        {
            status =
                "Available; levels, XP progress, hull stage, skill points, and skill build are valid";
        }
        else if (validProgressionValueCount ==
                 expectedProgressionValueCount)
        {
            status =
                $"Partially available; progression values are valid; {skills.Status}";
        }
        else if (validProgressionValueCount > 0 ||
                 skills.IsAvailable)
        {
            status = string.Create(CultureInfo.InvariantCulture, $"Partially available; {validProgressionValueCount}/{expectedProgressionValueCount} progression values valid; {skills.Status}");
        }
        else if (errors.Count > 0)
        {
            status =
                $"Unavailable: {errors[0]}";
        }
        else
        {
            status =
                "RPGInfo progression properties are unavailable";
        }

        return new ClientCharacterProgressionObservation
        {
            IsAvailable =
                validProgressionValueCount > 0 ||
                skills.IsAvailable,
            Status = status,
            AuxDataAddress =
                hullAuxDataAddress,
            AuxDataLookupAddress =
                cachedLookup.ProgressionLookup.LookupAddress,
            RpgInfoAddress =
                cachedLookup.RpgInfoAddress,
            LookupTraversalNodeCount =
                cachedLookup.LookupTraversalNodeCount,
            Race = race,
            Profession = profession,
            Combat = combat,
            Explore = explore,
            Trade = trade,
            SkillPoints = skillPoints,
            HullUpgradeLevel =
                hullUpgradeLevel,
            HullTier = hullTier,
            CurrentHullUpgradeOverallLevel =
                currentHullUpgradeOverallLevel,
            NextHullUpgradeOverallLevel =
                nextHullUpgradeOverallLevel,
            Skills = skills,
            PropertyAddresses =
                propertyAddresses,
        };
    }

    private bool TryOpenProgressionLookups(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        uint hullAuxDataAddress,
        out CachedProgressionLookup cachedLookup,
        out string error)
    {
        cachedLookup = default;
        error = "";

        if (!this.auxDataReader.TryOpenTargeted(
                memory,
                moduleBaseAddress,
                hullAuxDataAddress,
                progressionPropertyNames,
                out var progressionLookup,
                out var lookupTraversalNodeCount,
                out error))
        {
            return false;
        }

        if (!this.auxDataReader.TryOpenFromPropertyVector(
                memory,
                moduleBaseAddress,
                hullAuxDataAddress,
                hullPropertyNames,
                out var hullLookup,
                out error))
        {
            return false;
        }

        if (!hullLookup.Properties.TryGetValue(
                RpgInfoProperty,
                out var rpgInfoAddress) ||
            rpgInfoAddress == 0)
        {
            error =
                "Hull.RPGInfo was not found in the Hull property vector";

            return false;
        }

        if (!this.auxDataReader.TryOpenFromPropertyVector(
                memory,
                moduleBaseAddress,
                rpgInfoAddress,
                rpgInfoPropertyNames,
                out var rpgInfoLookup,
                out error))
        {
            return false;
        }

        if (!rpgInfoLookup.Properties.TryGetValue(
                SkillsProperty,
                out var skillsPropertyAddress) ||
            skillsPropertyAddress == 0)
        {
            error =
                "Hull.RPGInfo.Skills was not found in the RPGInfo property vector";

            return false;
        }

        cachedLookup =
            new CachedProgressionLookup(
                moduleBaseAddress,
                hullAuxDataAddress,
                progressionLookup,
                lookupTraversalNodeCount,
                rpgInfoAddress,
                rpgInfoLookup,
                skillsPropertyAddress);

        return true;
    }

    private readonly record struct CachedProgressionLookup(
        uint ModuleBaseAddress,
        uint HullAuxDataAddress,
        ClientAuxDataLookupSnapshot ProgressionLookup,
        int LookupTraversalNodeCount,
        uint RpgInfoAddress,
        ClientAuxDataLookupSnapshot RpgInfoLookup,
        uint SkillsPropertyAddress);
}
