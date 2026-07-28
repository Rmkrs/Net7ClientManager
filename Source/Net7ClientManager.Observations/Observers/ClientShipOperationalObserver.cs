namespace Net7ClientManager.Observations.Observers;

using System.Globalization;
using Net7ClientManager.Observations.Models;

internal sealed class ClientShipOperationalObserver
{
    private static readonly string[] stringPropertyNames =
    [
        "Name",
        "Owner",
        "Title",
        "Rank",
        "FactionIdentifier",
        "GuildName",
        "GuildRankName",
        "TargetThreat",
        "TargetThreatSound",
        "InterruptibleAbilityName",
    ];

    private static readonly string[] booleanPropertyNames =
    [
        "LockSpeed",
        "LockOrient",
        "AutoLevel",
        "IsCloaked",
        "IsCountermeasureActive",
        "IsIncapacitated",
        "IsOrganic",
        "IsInPVP",
        "IsAutoFollowing",
        "IsRescueBeaconActive",
        "AppearsInRadar",
    ];

    private static readonly string[] integerPropertyNames =
    [
        "CombatLevel",
        "PrivateWarpState",
        "GlobalWarpState",
        "WarpAvailable",
        "EngineThrustState",
        "EngineTrailType",
        "TargetThreatLevel",

        "BaseStats.Defense",
        "BaseStats.MissileDefense",
        "BaseStats.Speed",
        "BaseStats.WarpSpeed",
        "BaseStats.WarpPowerLevel",
        "BaseStats.TurnRate",
        "BaseStats.ScanRange",
        "BaseStats.Visibility",
        "BaseStats.ResistImpact",
        "BaseStats.ResistExplosive",
        "BaseStats.ResistPlasma",
        "BaseStats.ResistEnergy",
        "BaseStats.ResistEMP",
        "BaseStats.ResistChemical",
        "BaseStats.ResistPsionic",

        "CurrentStats.Defense",
        "CurrentStats.MissileDefense",
        "CurrentStats.Speed",
        "CurrentStats.WarpSpeed",
        "CurrentStats.WarpPowerLevel",
        "CurrentStats.TurnRate",
        "CurrentStats.ScanRange",
        "CurrentStats.Visibility",
        "CurrentStats.ResistImpact",
        "CurrentStats.ResistExplosive",
        "CurrentStats.ResistPlasma",
        "CurrentStats.ResistEnergy",
        "CurrentStats.ResistEMP",
        "CurrentStats.ResistChemical",
        "CurrentStats.ResistPsionic",
    ];

    private static readonly string[] floatPropertyNames =
    [
        "MaxTiltRate",
        "MaxTurnRate",
        "MaxTiltAngle",
        "MaxSpeed",
        "MinSpeed",
        "Acceleration",
        "InterruptProgress",
        "VisibleQuadrantDamagePercent.0",
        "VisibleQuadrantDamagePercent.1",
        "VisibleQuadrantDamagePercent.2",
        "VisibleQuadrantDamagePercent.3",
        "RadarRange",
    ];

    private static readonly string[] rawPropertyNames =
    [
        "GuildRank",
        "InterruptState",
        "InterruptibleActivationTime",
        "WarpTriggerTime",
    ];

    internal static readonly string[] PropertyNames =
    [
        .. stringPropertyNames,
        .. booleanPropertyNames,
        .. integerPropertyNames,
        .. floatPropertyNames,
        .. rawPropertyNames,
    ];

    private readonly ClientAuxDataLookupReader auxDataReader =
        new();

    public ClientShipOperationalObservation Observe(
        ProcessMemoryReader memory,
        ClientAuxDataLookupSnapshot lookup)
    {
        List<string> errors = [];

        Dictionary<string, uint> propertyAddresses =
            new(StringComparer.Ordinal);

        Dictionary<string, ClientRawAuxDataValueObservation> rawValues =
            new(StringComparer.Ordinal);

        HashSet<string> trackedProperties =
            new(StringComparer.Ordinal);

        var presentPropertyCount = 0;
        var validPropertyCount = 0;

        void Track(
            string propertyName,
            uint propertyAddress,
            bool isValid)
        {
            if (propertyAddress == 0 ||
                !trackedProperties.Add(
                    propertyName))
            {
                return;
            }

            propertyAddresses[propertyName] =
                propertyAddress;

            presentPropertyCount++;

            if (isValid)
            {
                validPropertyCount++;
            }
        }

        void TrackReadFailure(
            string propertyName,
            string error)
        {
            errors.Add(error);

            if (lookup.Properties.TryGetValue(
                    propertyName,
                    out var propertyAddress))
            {
                Track(
                    propertyName,
                    propertyAddress,
                    isValid: false);
            }
        }

        string? ReadString(
            string propertyName)
        {
            if (!this.auxDataReader.TryReadStringProperty(
                    memory,
                    lookup,
                    propertyName,
                    out var sample,
                    out var error))
            {
                TrackReadFailure(
                    propertyName,
                    error);

                return null;
            }

            Track(
                propertyName,
                sample.PropertyAddress,
                sample.IsValid);

            return sample.IsValid
                ? sample.Value
                : null;
        }

        bool? ReadBoolean(
            string propertyName)
        {
            if (!this.auxDataReader.TryReadBooleanProperty(
                    memory,
                    lookup,
                    propertyName,
                    out var sample,
                    out var error))
            {
                TrackReadFailure(
                    propertyName,
                    error);

                return null;
            }

            Track(
                propertyName,
                sample.PropertyAddress,
                sample.IsValid);

            return sample.IsValid
                ? sample.Value
                : null;
        }

        int? ReadInt32(
            string propertyName)
        {
            if (!this.auxDataReader.TryReadInt32Property(
                    memory,
                    lookup,
                    propertyName,
                    out var sample,
                    out var error))
            {
                TrackReadFailure(
                    propertyName,
                    error);

                return null;
            }

            Track(
                propertyName,
                sample.PropertyAddress,
                sample.IsValid);

            return sample.IsValid
                ? sample.Value
                : null;
        }

        float? ReadFloat(
            string propertyName)
        {
            if (!this.auxDataReader.TryReadFloatProperty(
                    memory,
                    lookup,
                    propertyName,
                    out var sample,
                    out var error))
            {
                TrackReadFailure(
                    propertyName,
                    error);

                return null;
            }

            Track(
                propertyName,
                sample.PropertyAddress,
                sample.IsValid);

            return sample.IsValid
                ? sample.Value
                : null;
        }

        ClientRawAuxDataValueObservation? ReadRaw(
            string propertyName,
            bool readSecondaryValue = false)
        {
            if (!this.auxDataReader.TryReadRawProperty(
                    memory,
                    lookup,
                    propertyName,
                    readSecondaryValue,
                    out var sample,
                    out var error))
            {
                TrackReadFailure(
                    propertyName,
                    error);

                return null;
            }

            Track(
                propertyName,
                sample.PropertyAddress,
                sample.IsValid);

            if (sample.PropertyAddress == 0)
            {
                return null;
            }

            var observed =
                new ClientRawAuxDataValueObservation
                {
                    Name = propertyName,
                    PropertyAddress =
                        sample.PropertyAddress,
                    IsValid = sample.IsValid,
                    PrimaryValue =
                        sample.PrimaryValue,
                    SecondaryValue =
                        sample.SecondaryValue,
                    HasSecondaryValue =
                        sample.HasSecondaryValue,
                };

            rawValues[propertyName] = observed;

            return observed;
        }

        ClientShipStatsObservation ReadStats(
            string prefix)
        {
            return new ClientShipStatsObservation
            {
                Defense =
                    ReadInt32($"{prefix}.Defense"),
                MissileDefense =
                    ReadInt32($"{prefix}.MissileDefense"),
                Speed =
                    ReadInt32($"{prefix}.Speed"),
                WarpSpeed =
                    ReadInt32($"{prefix}.WarpSpeed"),
                WarpPowerLevel =
                    ReadInt32($"{prefix}.WarpPowerLevel"),
                TurnRate =
                    ReadInt32($"{prefix}.TurnRate"),
                ScanRange =
                    ReadInt32($"{prefix}.ScanRange"),
                Visibility =
                    ReadInt32($"{prefix}.Visibility"),
                ResistImpact =
                    ReadInt32($"{prefix}.ResistImpact"),
                ResistExplosive =
                    ReadInt32($"{prefix}.ResistExplosive"),
                ResistPlasma =
                    ReadInt32($"{prefix}.ResistPlasma"),
                ResistEnergy =
                    ReadInt32($"{prefix}.ResistEnergy"),
                ResistEmp =
                    ReadInt32($"{prefix}.ResistEMP"),
                ResistChemical =
                    ReadInt32($"{prefix}.ResistChemical"),
                ResistPsionic =
                    ReadInt32($"{prefix}.ResistPsionic"),
            };
        }

        var factionIdentifier =
            ReadString("FactionIdentifier");

        var guildRank =
            ReadRaw("GuildRank");

        var interruptState =
            ReadRaw("InterruptState");

        var interruptibleActivationTime =
            ReadRaw(
                "InterruptibleActivationTime",
                readSecondaryValue: true);

        var warpTriggerTime =
            ReadRaw(
                "WarpTriggerTime",
                readSecondaryValue: true);

        var identity =
            new ClientShipIdentityObservation
            {
                Name = ReadString("Name"),
                Owner = ReadString("Owner"),
                Title = ReadString("Title"),
                Rank = ReadString("Rank"),
                FactionIdentifier =
                    factionIdentifier,
                ProfessionName =
                    GetProfessionName(
                        factionIdentifier),
                GuildName =
                    ReadString("GuildName"),
                GuildRankName =
                    ReadString("GuildRankName"),
                CombatLevel =
                    ReadInt32("CombatLevel"),
                GuildRankRaw =
                    guildRank is { IsValid: true }
                        ? guildRank.PrimaryInt32
                        : null,
            };

        var flags =
            new ClientShipControlFlagsObservation
            {
                LockSpeed =
                    ReadBoolean("LockSpeed"),
                LockOrient =
                    ReadBoolean("LockOrient"),
                AutoLevel =
                    ReadBoolean("AutoLevel"),
                IsCloaked =
                    ReadBoolean("IsCloaked"),
                IsCountermeasureActive =
                    ReadBoolean(
                        "IsCountermeasureActive"),
                IsIncapacitated =
                    ReadBoolean("IsIncapacitated"),
                IsOrganic =
                    ReadBoolean("IsOrganic"),
                IsInPvp =
                    ReadBoolean("IsInPVP"),
                IsAutoFollowing =
                    ReadBoolean("IsAutoFollowing"),
                IsRescueBeaconActive =
                    ReadBoolean(
                        "IsRescueBeaconActive"),
            };

        var runtime =
            new ClientShipRuntimeStateObservation
            {
                PrivateWarpState =
                    ReadInt32("PrivateWarpState"),
                GlobalWarpState =
                    ReadInt32("GlobalWarpState"),
                WarpAvailable =
                    ReadInt32("WarpAvailable"),
                EngineThrustState =
                    ReadInt32("EngineThrustState"),
                EngineTrailType =
                    ReadInt32("EngineTrailType"),
                TargetThreat =
                    ReadString("TargetThreat"),
                TargetThreatSound =
                    ReadString("TargetThreatSound"),
                TargetThreatLevel =
                    ReadInt32("TargetThreatLevel"),
                InterruptibleAbilityName =
                    ReadString(
                        "InterruptibleAbilityName"),
                InterruptProgress =
                    ReadFloat("InterruptProgress"),
                InterruptStateRaw =
                    interruptState is { IsValid: true }
                        ? interruptState.PrimaryInt32
                        : null,
                InterruptibleActivationTimeRaw =
                    interruptibleActivationTime is { IsValid: true }
                        ? interruptibleActivationTime.CombinedUInt64
                        : null,
                WarpTriggerTimeRaw =
                    warpTriggerTime is { IsValid: true }
                        ? warpTriggerTime.CombinedUInt64
                        : null,
            };

        var movement =
            new ClientShipMovementObservation
            {
                MaximumTiltRate =
                    ReadFloat("MaxTiltRate"),
                MaximumTurnRate =
                    ReadFloat("MaxTurnRate"),
                MaximumTiltAngle =
                    ReadFloat("MaxTiltAngle"),
                MaximumSpeed =
                    ReadFloat("MaxSpeed"),
                MinimumSpeed =
                    ReadFloat("MinSpeed"),
                Acceleration =
                    ReadFloat("Acceleration"),
            };

        List<ClientShipQuadrantObservation> quadrants = [];

        for (var index = 0;
             index < 4;
             index++)
        {
            var propertyName = string.Create(CultureInfo.InvariantCulture, $"VisibleQuadrantDamagePercent.{index}");

            var value = ReadFloat(
                propertyName);

            if (!value.HasValue)
            {
                continue;
            }

            propertyAddresses.TryGetValue(
                propertyName,
                out var propertyAddress);

            quadrants.Add(
                new ClientShipQuadrantObservation
                {
                    Index = index,
                    PropertyAddress =
                        propertyAddress,
                    HealthFraction =
                        Math.Clamp(
                            value.Value,
                            0.0f,
                            1.0f),
                });
        }

        var radar =
            new ClientNavigationRadarObservation
            {
                AppearsInRadar =
                    ReadBoolean("AppearsInRadar"),
                RadarRange =
                    ReadFloat("RadarRange"),
            };

        var baseStats =
            ReadStats("BaseStats");

        var currentStats =
            ReadStats("CurrentStats");

        string status;

        if (presentPropertyCount == 0)
        {
            status =
                "Object exposes none of the promoted operational fields";
        }
        else if (validPropertyCount == 0)
        {
            status =
                "Promoted operational fields are present but not valid";
        }
        else if (errors.Count == 0)
        {
            status =
                string.Create(CultureInfo.InvariantCulture, $"Available; {validPropertyCount} promoted fields valid");
        }
        else
        {
            status =
                $"Available with {errors.Count} field read error(s): {errors[0]}";
        }

        return new ClientShipOperationalObservation
        {
            IsAvailable = true,
            Status = status,
            AuxDataLookupAddress =
                lookup.LookupAddress,
            PresentPropertyCount =
                presentPropertyCount,
            ValidPropertyCount =
                validPropertyCount,
            Identity = identity,
            Flags = flags,
            Runtime = runtime,
            Movement = movement,
            BaseStats = baseStats,
            CurrentStats = currentStats,
            Quadrants = quadrants,
            Radar = radar,
            PropertyAddresses =
                propertyAddresses,
            RawValues = rawValues,
        };
    }

    private static string? GetProfessionName(
        string? factionIdentifier)
    {
        return ClientProfessionResolver.FromFactionIdentifier(
            factionIdentifier);
    }
}


