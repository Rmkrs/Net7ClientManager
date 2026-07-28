namespace Net7ClientManager.Addons.Projection;

using Net7ClientManager.Addons.Contracts;
using Net7ClientManager.Observations.Models;

public sealed partial class GameSnapshotProjector
{
    private static DomainProjection CreateCharacterDomainProjection(
        ClientLocalPlayerObservation player,
        AddonCharacterSnapshot character)
    {
        var value = MapCharacter(player, character);
        var fingerprintValue = new Dictionary<string, object?>(
            value,
            StringComparer.Ordinal);

        // Position is available to addons for HUD and navigation use, but
        // ordinary movement must not turn character.changed/game.updated into
        // a continuous movement event stream. Availability and targeting
        // radius remain semantic character state.
        fingerprintValue["spatial"] =
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["available"] = player.Spatial.IsAvailable,
                ["targeting_distance_radius"] = player.Spatial.IsAvailable
                    ? player.Spatial.TargetingDistanceRadius
                    : null,
            };

        return CreateDomainProjectionWithEventFingerprint(
            value,
            fingerprintValue);
    }


    private static DomainProjection CreateTargetDomainProjection(
        ClientTargetObservation target,
        ClientTargetInteractionObservation interaction)
    {
        var value = MapTarget(target, interaction);
        var fingerprintValue = new Dictionary<string, object?>(
            value,
            StringComparer.Ordinal)
        {
            ["distance"] = new Dictionary<string, object?>(
                StringComparer.Ordinal)
            {
                ["available"] = target.Distance.IsAvailable,
            },
            ["spatial"] = new Dictionary<string, object?>(
                StringComparer.Ordinal)
            {
                ["local_available"] = target.Distance.Local.IsAvailable,
                ["target_available"] = target.Distance.Target.IsAvailable,
            },
        };

        // Distance and coordinates remain readable for target HUDs, but ship
        // movement must not create a target.changed event every observation.
        return CreateDomainProjectionWithEventFingerprint(
            value,
            fingerprintValue);
    }

    private static DomainProjection CreateNearbyTargetsDomainProjection(
        ClientGutterRadarObservation nearbyTargets,
        ClientTargetObservation selectedTarget)
    {
        var value = MapNearbyTargets(nearbyTargets, selectedTarget);
        var fingerprintValue = new Dictionary<string, object?>(
            value,
            StringComparer.Ordinal)
        {
            ["targets"] = GetProjectedTables(value, "targets")
                .Select(target =>
                    (object?)new Dictionary<string, object?>(
                        target.Where(item => !string.Equals(
                            item.Key,
                            "screen_position",
                            StringComparison.Ordinal)),
                        StringComparer.Ordinal))
                .ToArray(),
        };

        // Gutter coordinates continuously move while the camera or ships
        // move. Addons can read them, but only semantic roster/vital/selection
        // changes emit nearby_targets.changed.
        return CreateDomainProjectionWithEventFingerprint(
            value,
            fingerprintValue);
    }

    private static DomainProjection CreateNavigationDomainProjection(
        ClientNavigationObservation navigation,
        ClientNavigationStateObservation navigationState,
        AddonNavigationRouteSnapshot? routeSnapshot)
    {
        var value = MapNavigation(
            navigation,
            navigationState,
            routeSnapshot);
        var fingerprintValue = new Dictionary<string, object?>(
            value,
            StringComparer.Ordinal);
        fingerprintValue["control"] = CopyWithoutKeys(
            MapNavigationControl(navigationState),
            "target_distance",
            "current_energy",
            "energy_fraction");
        fingerprintValue["targets"] = GetProjectedTables(value, "targets")
            .Select(target =>
                (object?)CopyWithoutKeys(target, "spatial"))
            .ToArray();

        var route = GetProjectedTable(value, "route");
        if (route != null)
        {
            var routeFingerprint = new Dictionary<string, object?>(
                route,
                StringComparer.Ordinal);
            var journey = GetProjectedTable(route, "journey");
            if (journey != null)
            {
                routeFingerprint["journey"] = CopyWithoutKeys(
                    journey,
                    "current_energy");
            }

            fingerprintValue["route"] = routeFingerprint;
        }

        // These values are readable in game.navigation, but they can drift
        // every observation while moving or regenerating. Derived Warp events
        // and semantic route changes still fire without flooding addons with
        // per-tick distance, position or reactor updates.

        return CreateDomainProjectionWithEventFingerprint(
            value,
            fingerprintValue);
    }

    private static IReadOnlyDictionary<string, object?> CopyWithoutKeys(
        IReadOnlyDictionary<string, object?> source,
        params string[] excludedKeys)
    {
        var excluded = excludedKeys.ToHashSet(StringComparer.Ordinal);
        return new Dictionary<string, object?>(
            source.Where(item => !excluded.Contains(item.Key)),
            StringComparer.Ordinal);
    }

    private static IReadOnlyDictionary<string, object?>? GetProjectedTable(
        IReadOnlyDictionary<string, object?> source,
        string key)
    {
        return source.TryGetValue(key, out var value) &&
               value is IReadOnlyDictionary<string, object?> table
            ? table
            : null;
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>>
        GetProjectedTables(
            IReadOnlyDictionary<string, object?> source,
            string key)
    {
        if (!source.TryGetValue(key, out var value) ||
            value is not IEnumerable<object?> values)
        {
            return [];
        }

        return values
            .OfType<IReadOnlyDictionary<string, object?>>()
            .ToArray();
    }

    private static IReadOnlyDictionary<string, object?> MapNavigationControl(
        ClientNavigationStateObservation navigation)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["available"] = navigation.IsAvailable,
            ["phase"] = NormalizeEnum(navigation.Phase),
            ["world_present"] = navigation.IsWorldPresent,
            ["loading"] = navigation.IsLoading,
            ["selected_target_known"] = navigation.SelectedTargetKnown,
            ["has_selected_target"] = navigation.HasSelectedTarget,
            ["target_distance"] = navigation.TargetDistance,
            ["path_build_known"] = navigation.PathBuildStateKnown,
            ["path_build_busy"] = navigation.PathBuildBusy,
            ["warp_idle"] = navigation.IsWarpIdle,
            ["warp_starting"] = navigation.IsWarpStarting,
            ["warp_active"] = navigation.IsWarpActive,
            ["warp_recovering"] = navigation.IsWarpRecovering,
            ["gate_transition_locked"] = navigation.IsGateTransitionLocked,
            ["interaction_ready"] = navigation.IsInteractionControlReady,
            ["warp_ready"] = navigation.IsClientWarpReady,
            ["warp_readiness_reason"] = navigation.GetClientWarpReadinessReason(),
            ["cloaked"] = navigation.IsCloaked.IsAvailable
                ? navigation.IsCloaked.Value
                : null,
            ["current_energy"] = navigation.CurrentEnergyPower,
            ["maximum_energy"] = navigation.MaximumEnergyPower.IsAvailable
                ? navigation.MaximumEnergyPower.Value
                : null,
            ["energy_fraction"] = navigation.EnergyFraction.IsAvailable
                ? navigation.EnergyFraction.Value
                : null,
        };
    }

    private static IReadOnlyDictionary<string, object?> MapJobs(
        ClientJobTerminalObservation jobs)
    {
        var descriptions = jobs.Descriptions
            .GroupBy(description => description.JobId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(item => item.ObservedAt)
                    .First());

        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["available"] = jobs.IsAvailable,
            ["open"] = jobs.IsOpen,
            ["selected_category"] = NormalizeEnum(jobs.SelectedCategory),
            ["offer_count"] = jobs.Offers.Count,
            ["displayed_offer_count"] = jobs.DisplayedOfferCount,
            ["catalogue_settled"] = jobs.IsCatalogueSettled,
            ["offers"] = jobs.Offers
                .OrderBy(offer => offer.JobId)
                .Select(offer => (object?)MapJobOffer(
                    offer,
                    descriptions.GetValueOrDefault(offer.JobId),
                    jobs.DisplayedJobIds.Contains(offer.JobId),
                    jobs.SelectedJobId == offer.JobId))
                .ToArray(),
        };
    }

    private static IReadOnlyDictionary<string, object?> MapJobOffer(
        ClientJobOfferObservation offer,
        ClientJobDescriptionObservation? description,
        bool displayed,
        bool selected)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["category"] = NormalizeEnum(offer.Category),
            ["type"] = Normalize(offer.Type),
            ["level"] = offer.Level,
            ["level_text"] = Normalize(offer.LevelText),
            ["sponsor"] = Normalize(offer.Sponsor),
            ["reward"] = Normalize(offer.Reward),
            ["displayed"] = displayed,
            ["selected"] = selected,
            ["still_available"] = description?.StillAvailable,
            ["title"] = Normalize(description?.Title),
            ["description"] = Normalize(description?.Description),
            ["detail_reward"] = Normalize(description?.Reward),
            ["source"] = Normalize(description?.Source),
            ["described_at"] = description?.ObservedAt.ToUnixTimeMilliseconds(),
        };
    }

    private static IReadOnlyDictionary<string, object?> MapShortcuts(
        ClientShortcutStateObservation shortcuts)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["available"] = shortcuts.IsAvailable,
            ["bars"] = shortcuts.Bars
                .OrderBy(bar => bar.Bar)
                .Select(bar => (object?)new Dictionary<string, object?>(
                    StringComparer.Ordinal)
                {
                    ["bar"] = bar.Bar,
                    ["available"] = bar.IsAvailable,
                    ["current_group"] = bar.CurrentGroup,
                    ["slots"] = bar.Slots
                        .OrderBy(slot => slot.Group)
                        .ThenBy(slot => slot.Button)
                        .Select(slot => (object?)MapShortcutSlot(slot))
                        .ToArray(),
                })
                .ToArray(),
        };
    }

    private static IReadOnlyDictionary<string, object?> MapShortcutSlot(
        ClientShortcutSlotObservation slot)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["bar"] = slot.Bar,
            ["group"] = slot.Group,
            ["button"] = slot.Button,
            ["visible_key"] = slot.VisibleKey,
            ["visible"] = slot.IsVisible,
            ["occupied"] = slot.IsOccupied,
            ["identified"] = slot.IsResolved,
            ["kind"] = NormalizeEnum(slot.Kind),
            ["name"] = Normalize(slot.ResolvedName),
            ["name_exact"] = slot.ResolvedNameIsExact,
            ["family_name"] = Normalize(slot.FamilyName),
            ["inventory_collection"] = slot.InventoryCollection.HasValue
                ? NormalizeEnum(slot.InventoryCollection.Value)
                : null,
        };
    }

    private static IReadOnlyDictionary<string, object?> MapTooltips(
        ClientTooltipDelayObservation delay,
        ClientTooltipHoverObservation hover)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["available"] = delay.IsAvailable || hover.IsAvailable,
            ["delay"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["available"] = delay.IsAvailable,
                ["milliseconds"] = delay.IsAvailable
                    ? delay.DelayMilliseconds
                    : null,
                ["percent"] = delay.IsAvailable
                    ? delay.CurrentPercent
                    : null,
            },
            ["hover"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["available"] = hover.IsAvailable,
                ["view"] = NormalizeEnum(hover.ViewKind),
                ["has_control"] = hover.HasActiveGadget,
                ["displayed"] = hover.IsDisplayed,
                ["control_name"] = Normalize(hover.ControlName),
                ["text"] = Normalize(hover.NativeTooltipText),
            },
        };
    }

    private static IReadOnlyDictionary<string, object?> MapProduction(
        ClientProductionRecipeObservation production)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["available"] = production.IsAvailable,
            ["kind"] = production.IsAvailable
                ? NormalizeEnum(production.Kind)
                : null,
            ["output_name"] = production.OutputItemTemplateId > 0
                ? ResolveItemName(production.OutputItemTemplateId)
                : null,
            ["ingredients"] = production.Ingredients
                .Where(ingredient => ingredient.ItemTemplateId > 0)
                .Select(ingredient => (object?)new Dictionary<string, object?>(
                    StringComparer.Ordinal)
                {
                    ["name"] = ResolveItemName(ingredient.ItemTemplateId),
                    ["quantity"] = ingredient.Quantity,
                })
                .ToArray(),
        };
    }

    private static IReadOnlyDictionary<string, object?> MapAudio(
        ClientAudioCueObservation audio)
    {
        var cue = ResolveAudioCue(audio);

        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["available"] = audio.IsAvailable,
            ["current"] = cue != null,
            ["cue"] = cue,
        };
    }

    private static string? ResolveAudioCue(
        ClientAudioCueObservation audio)
    {
        if (!audio.IsAvailable || !audio.IsCurrent ||
            string.IsNullOrWhiteSpace(audio.ResourceName))
        {
            return null;
        }

        var compact = new string(
            audio.ResourceName
                .Where(char.IsLetterOrDigit)
                .Select(char.ToUpperInvariant)
                .ToArray());

        return compact == "MISSIONFORFEITED"
            ? "mission_forfeited"
            : null;
    }

    private static IReadOnlyDictionary<string, object?> MapFactionDetails(
        ClientFactionDetailsPresentationObservation faction,
        ClientReputationObservation reputation)
    {
        var selected = faction.HasSelection
            ? reputation.Factions.FirstOrDefault(candidate =>
                string.Equals(
                    candidate.FactionKey,
                    faction.SelectedFactionKey,
                    StringComparison.OrdinalIgnoreCase))
            : null;

        var visibleNames = faction.VisibleFactionKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => reputation.Factions.FirstOrDefault(candidate =>
                string.Equals(
                    candidate.FactionKey,
                    key,
                    StringComparison.OrdinalIgnoreCase))?.DisplayName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => (object?)name!.Trim())
            .ToArray();

        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["available"] = faction.IsAvailable,
            ["displayed"] = faction.IsDisplayed,
            ["panel_displayed"] = faction.IsFactionPanelDisplayed,
            ["details_displayed"] = faction.IsDetailsViewDisplayed,
            ["has_selection"] = faction.HasSelection,
            ["selected_faction"] = selected == null
                ? null
                : new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["name"] = selected.DisplayName,
                    ["description"] = selected.Description,
                    ["reaction"] = selected.Reaction,
                    ["disposition"] = selected.NormalizedReaction,
                },
            ["visible_factions"] = visibleNames,
        };
    }
}
