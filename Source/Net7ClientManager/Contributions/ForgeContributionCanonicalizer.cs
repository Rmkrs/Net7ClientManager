namespace Net7ClientManager.Contributions;

using System.Globalization;
using System.Text;

internal static class ForgeContributionCanonicalizer
{
    public static byte[] Canonicalize(
        ForgeAddonPublicationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var builder = new StringBuilder();
        Append(builder, request.ProtocolVersion.ToString(CultureInfo.InvariantCulture));
        Append(builder, request.ContributorId);
        Append(builder, request.RequestId);
        Append(builder, request.SubmittedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        Append(builder, request.ClientVersion);
        Append(builder, request.LivePilotName);
        Append(builder, request.AddonId);
        Append(builder, request.Version);
        Append(builder, request.Summary ?? "");
        Append(builder, request.SourcePackageSha256);
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    public static byte[] Canonicalize(
        ForgeContributorRegistrationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var builder = new StringBuilder();
        Append(builder, request.ProtocolVersion.ToString(CultureInfo.InvariantCulture));
        Append(builder, request.PublicKey);
        Append(builder, request.LivePilotName);
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    public static byte[] Canonicalize(
        ForgeContributorRecoveryStartRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var builder = new StringBuilder();
        Append(builder, request.ProtocolVersion.ToString(CultureInfo.InvariantCulture));
        Append(builder, request.PublicKey);
        Append(builder, request.LivePilotName);
        Append(builder, request.DeviceLabel ?? "");
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    public static byte[] Canonicalize(
        ForgeNpcPresenceContributionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var builder = new StringBuilder();
        Append(builder, request.ProtocolVersion.ToString(CultureInfo.InvariantCulture));
        Append(builder, request.ContributorId);
        Append(builder, request.RequestId);
        Append(builder, request.SubmittedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        Append(builder, request.FirstObservedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        Append(builder, request.LastObservedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        Append(builder, request.ObservationCount.ToString(CultureInfo.InvariantCulture));
        Append(builder, request.ClientVersion);
        Append(builder, request.DatasetRevision.ToString(CultureInfo.InvariantCulture));
        Append(builder, request.Attribution);
        Append(builder, request.LivePilotName);
        Append(builder, request.StarbaseId.ToString(CultureInfo.InvariantCulture));
        Append(builder, request.StationName);
        Append(builder, request.SectorName);
        Append(builder, request.ActiveSectorNumber.ToString(CultureInfo.InvariantCulture));

        foreach (var npc in request.Npcs
                     .OrderBy(item => item.DefinitionKey)
                     .ThenBy(item => item.DefinitionSecondaryId)
                     .ThenBy(item => item.Name, StringComparer.Ordinal)
                     .ThenBy(item => item.RoomClass)
                     .ThenBy(item => item.RoomDefinitionKey)
                     .ThenBy(item => item.RoomNpcSlot))
        {
            Append(builder, npc.RoomClass.ToString(CultureInfo.InvariantCulture));
            Append(builder, npc.RoomDefinitionKey.ToString(CultureInfo.InvariantCulture));
            Append(builder, npc.RoomNpcSlot.ToString(CultureInfo.InvariantCulture));
            Append(builder, npc.DefinitionKey.ToString(CultureInfo.InvariantCulture));
            Append(builder, npc.DefinitionSecondaryId.ToString(CultureInfo.InvariantCulture));
            Append(builder, npc.Name);
            Append(builder, npc.Role.ToString(CultureInfo.InvariantCulture));
            Append(builder, npc.Classification.ToString(CultureInfo.InvariantCulture));
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    public static byte[] Canonicalize(
        ForgeNavigationObjectsContributionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var builder = new StringBuilder();
        Append(builder, request.ProtocolVersion.ToString(CultureInfo.InvariantCulture));
        Append(builder, request.ContributorId);
        Append(builder, request.RequestId);
        Append(builder, request.SubmittedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        Append(builder, request.FirstObservedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        Append(builder, request.LastObservedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        Append(builder, request.ObservationCount.ToString(CultureInfo.InvariantCulture));
        Append(builder, request.ClientVersion);
        Append(builder, request.DatasetRevision.ToString(CultureInfo.InvariantCulture));
        Append(builder, request.Attribution);
        Append(builder, request.LivePilotName);
        Append(builder, request.SectorId);
        Append(builder, request.SectorKey);
        Append(builder, request.SectorName);
        Append(builder, request.SystemName);
        Append(builder, request.ActiveSectorNumber.ToString(CultureInfo.InvariantCulture));

        foreach (var target in request.Targets
                     .OrderBy(item => item.ObjectId)
                     .ThenBy(item => item.RawObjectType)
                     .ThenBy(item => item.Name, StringComparer.Ordinal))
        {
            Append(builder, target.ObjectId.ToString(CultureInfo.InvariantCulture));
            Append(builder, target.Name);
            Append(builder, target.MapDisplayName);

            if (request.ProtocolVersion >= 2)
            {
                Append(builder, target.SelectionContext);
                Append(builder, target.Signature?.ToString("R", CultureInfo.InvariantCulture) ?? "");
                Append(builder, target.RawObjectType.ToString(CultureInfo.InvariantCulture));
                Append(builder, target.NavType?.ToString(CultureInfo.InvariantCulture) ?? "");
                Append(builder, target.IsHuge.HasValue
                    ? target.IsHuge.Value ? "1" : "0"
                    : "");
            }
            else
            {
                Append(builder, target.Signature!.Value.ToString("R", CultureInfo.InvariantCulture));
                Append(builder, target.RawObjectType.ToString(CultureInfo.InvariantCulture));
                Append(builder, target.NavType!.Value.ToString(CultureInfo.InvariantCulture));
                Append(builder, target.IsHuge!.Value ? "1" : "0");
            }

            Append(builder, target.HasPosition ? "1" : "0");
            Append(builder, target.X.ToString("R", CultureInfo.InvariantCulture));
            Append(builder, target.Y.ToString("R", CultureInfo.InvariantCulture));
            Append(builder, target.Z.ToString("R", CultureInfo.InvariantCulture));
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    public static byte[] Canonicalize(
        ForgeMobSightingsContributionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var builder = new StringBuilder();
        Append(builder, request.ProtocolVersion.ToString(CultureInfo.InvariantCulture));
        Append(builder, request.ContributorId);
        Append(builder, request.RequestId);
        Append(builder, request.SubmittedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        Append(builder, request.FirstObservedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        Append(builder, request.LastObservedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        Append(builder, request.ObservationCount.ToString(CultureInfo.InvariantCulture));
        Append(builder, request.ClientVersion);
        Append(builder, request.DatasetRevision.ToString(CultureInfo.InvariantCulture));
        Append(builder, request.Attribution);
        Append(builder, request.LivePilotName);
        Append(builder, request.SectorId);
        Append(builder, request.SectorKey);
        Append(builder, request.SectorName);
        Append(builder, request.SystemName);
        Append(builder, request.ActiveSectorNumber.ToString(CultureInfo.InvariantCulture));

        foreach (var sighting in request.Sightings
                     .OrderBy(item => item.ObjectId)
                     .ThenBy(item => request.ProtocolVersion >= 2
                         ? item.FirstObservedAtUtc
                         : DateTimeOffset.MinValue)
                     .ThenBy(item => item.RawObjectType)
                     .ThenBy(item => item.Name, StringComparer.Ordinal))
        {
            Append(builder, sighting.ObjectId.ToString(CultureInfo.InvariantCulture));
            Append(builder, sighting.Name);
            Append(builder, sighting.RawObjectType.ToString(CultureInfo.InvariantCulture));
            Append(builder, sighting.CombatLevel.ToString(CultureInfo.InvariantCulture));
            Append(builder, sighting.FactionIdentifier);
            Append(builder, sighting.IsOrganic ? "1" : "0");
            Append(builder, sighting.AutoLevel == null ? "" : sighting.AutoLevel.Value ? "1" : "0");
            Append(builder, sighting.X.ToString("R", CultureInfo.InvariantCulture));
            Append(builder, sighting.Y.ToString("R", CultureInfo.InvariantCulture));
            Append(builder, sighting.Z.ToString("R", CultureInfo.InvariantCulture));

            if (request.ProtocolVersion >= 2)
            {
                Append(builder, sighting.FirstObservedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
                Append(builder, sighting.LastObservedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
                Append(builder, sighting.ObservationCount.ToString(CultureInfo.InvariantCulture));
                Append(builder, sighting.Radius.ToString("R", CultureInfo.InvariantCulture));
            }

            if (request.ProtocolVersion >= 3)
            {
                Append(builder, ((int)sighting.FactionBindingKind).ToString(CultureInfo.InvariantCulture));
                Append(builder, sighting.IntrinsicRelationshipRaw?.ToString(CultureInfo.InvariantCulture) ?? "");
                Append(builder, sighting.IntrinsicDisposition.HasValue
                    ? ((int)sighting.IntrinsicDisposition.Value).ToString(CultureInfo.InvariantCulture)
                    : "");
                Append(builder, sighting.ObservedRelationshipRaw?.ToString(CultureInfo.InvariantCulture) ?? "");
                Append(builder, sighting.ObservedAggressionRaw?.ToString(CultureInfo.InvariantCulture) ?? "");
                Append(builder, FormatNullableBoolean(
                    sighting.ObservedActivelyAggressive));
            }
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    public static byte[] Canonicalize(
        ForgeMobLootContributionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var builder = new StringBuilder();
        Append(builder, request.ProtocolVersion.ToString(CultureInfo.InvariantCulture));
        Append(builder, request.ContributorId);
        Append(builder, request.RequestId);
        Append(builder, request.SubmittedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        Append(builder, request.FirstObservedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        Append(builder, request.LastObservedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        Append(builder, request.ObservationCount.ToString(CultureInfo.InvariantCulture));
        Append(builder, request.ClientVersion);
        Append(builder, request.DatasetRevision.ToString(CultureInfo.InvariantCulture));
        Append(builder, request.Attribution);
        Append(builder, request.LivePilotName);

        foreach (var drop in request.Drops
                     .OrderBy(item => item.SectorId, StringComparer.Ordinal)
                     .ThenBy(item => item.MobName, StringComparer.Ordinal)
                     .ThenBy(item => item.CombatLevel)
                     .ThenBy(item => item.ItemTemplateId)
                     .ThenBy(item => item.MobObjectId)
                     .ThenBy(item => item.CorpseObjectId))
        {
            Append(builder, drop.SectorId);
            Append(builder, drop.SectorKey);
            Append(builder, drop.SectorName);
            Append(builder, drop.SystemName);
            Append(builder, drop.ActiveSectorNumber.ToString(CultureInfo.InvariantCulture));
            Append(builder, drop.MobObjectId.ToString(CultureInfo.InvariantCulture));
            Append(builder, drop.CorpseObjectId.ToString(CultureInfo.InvariantCulture));
            Append(builder, drop.MobName);
            Append(builder, drop.RawObjectType.ToString(CultureInfo.InvariantCulture));
            Append(builder, drop.CombatLevel.ToString(CultureInfo.InvariantCulture));
            Append(builder, drop.FactionIdentifier);

            if (request.ProtocolVersion >= 3)
            {
                Append(builder, ((int)drop.FactionBindingKind).ToString(CultureInfo.InvariantCulture));
                Append(builder, drop.IntrinsicRelationshipRaw?.ToString(CultureInfo.InvariantCulture) ?? "");
                Append(builder, drop.IntrinsicDisposition.HasValue
                    ? ((int)drop.IntrinsicDisposition.Value).ToString(CultureInfo.InvariantCulture)
                    : "");
                Append(builder, drop.ObservedRelationshipRaw?.ToString(CultureInfo.InvariantCulture) ?? "");
                Append(builder, drop.ObservedAggressionRaw?.ToString(CultureInfo.InvariantCulture) ?? "");
                Append(builder, FormatNullableBoolean(
                    drop.ObservedActivelyAggressive));
            }

            Append(builder, drop.IsOrganic ? "1" : "0");
            Append(builder, drop.AutoLevel == null ? "" : drop.AutoLevel.Value ? "1" : "0");
            Append(builder, drop.ItemTemplateId.ToString(CultureInfo.InvariantCulture));
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    public static byte[] Canonicalize(
        ForgeHarvestableResourcesContributionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var builder = new StringBuilder();
        Append(builder, request.ProtocolVersion.ToString(CultureInfo.InvariantCulture));
        Append(builder, request.ContributorId);
        Append(builder, request.RequestId);
        Append(builder, request.SubmittedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        Append(builder, request.FirstObservedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        Append(builder, request.LastObservedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        Append(builder, request.ObservationCount.ToString(CultureInfo.InvariantCulture));
        Append(builder, request.ClientVersion);
        Append(builder, request.DatasetRevision.ToString(CultureInfo.InvariantCulture));
        Append(builder, request.Attribution);
        Append(builder, request.LivePilotName);
        Append(builder, request.SectorId);
        Append(builder, request.SectorKey);
        Append(builder, request.SectorName);
        Append(builder, request.SystemName);
        Append(builder, request.ActiveSectorNumber.ToString(CultureInfo.InvariantCulture));

        foreach (var harvestable in request.Harvestables
                     .OrderBy(item => item.ObjectId)
                     .ThenBy(item => item.FirstObservedAtUtc)
                     .ThenBy(item => item.Name, StringComparer.Ordinal))
        {
            Append(builder, harvestable.ObjectId.ToString(CultureInfo.InvariantCulture));
            Append(builder, harvestable.Name);
            Append(builder, harvestable.RawObjectType.ToString(CultureInfo.InvariantCulture));
            Append(builder, harvestable.TechLevel.ToString(CultureInfo.InvariantCulture));
            Append(builder, harvestable.X.ToString("R", CultureInfo.InvariantCulture));
            Append(builder, harvestable.Y.ToString("R", CultureInfo.InvariantCulture));
            Append(builder, harvestable.Z.ToString("R", CultureInfo.InvariantCulture));
            Append(builder, harvestable.FirstObservedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            Append(builder, harvestable.LastObservedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            Append(builder, harvestable.ObservationCount.ToString(CultureInfo.InvariantCulture));

            foreach (var itemTemplateId in harvestable.ItemTemplateIds.Distinct().Order())
            {
                Append(builder, itemTemplateId.ToString(CultureInfo.InvariantCulture));
            }
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    public static byte[] Canonicalize(
        ForgeStationServicesContributionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var builder = new StringBuilder();
        Append(builder, request.ProtocolVersion.ToString(CultureInfo.InvariantCulture));
        Append(builder, request.ContributorId);
        Append(builder, request.RequestId);
        Append(builder, request.SubmittedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        Append(builder, request.FirstObservedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        Append(builder, request.LastObservedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        Append(builder, request.ObservationCount.ToString(CultureInfo.InvariantCulture));
        Append(builder, request.ClientVersion);
        Append(builder, request.DatasetRevision.ToString(CultureInfo.InvariantCulture));
        Append(builder, request.Attribution);
        Append(builder, request.LivePilotName);
        Append(builder, request.StarbaseId.ToString(CultureInfo.InvariantCulture));
        Append(builder, request.StarbaseDefinitionId.ToString(CultureInfo.InvariantCulture));
        Append(builder, request.StationName);
        Append(builder, request.SectorName);
        Append(builder, request.ActiveSectorNumber.ToString(CultureInfo.InvariantCulture));
        Append(builder, request.RoomCount.ToString(CultureInfo.InvariantCulture));
        Append(builder, request.FacilityDefinitionCount.ToString(CultureInfo.InvariantCulture));

        foreach (var facility in request.Facilities
                     .OrderBy(item => item.RoomDefinitionKey)
                     .ThenBy(item => item.RoomFacilitySlot)
                     .ThenBy(item => item.FacilityType)
                     .ThenBy(item => item.Name, StringComparer.Ordinal))
        {
            Append(builder, facility.RoomClass.ToString(CultureInfo.InvariantCulture));
            Append(builder, facility.RoomDefinitionKey.ToString(CultureInfo.InvariantCulture));
            Append(builder, facility.RoomFacilitySlot.ToString(CultureInfo.InvariantCulture));
            Append(builder, facility.DefinitionSlot.ToString(CultureInfo.InvariantCulture));
            Append(builder, facility.FacilityType.ToString(CultureInfo.InvariantCulture));
            Append(builder, facility.Name);
            Append(builder, facility.ReservedValue.ToString(CultureInfo.InvariantCulture));
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    public static byte[] Canonicalize(
        ForgeVendorInventoryContributionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var builder = new StringBuilder();
        Append(builder, request.ProtocolVersion.ToString(CultureInfo.InvariantCulture));
        Append(builder, request.ContributorId);
        Append(builder, request.RequestId);
        Append(builder, request.SubmittedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        Append(builder, request.FirstObservedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        Append(builder, request.LastObservedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        Append(builder, request.ObservationCount.ToString(CultureInfo.InvariantCulture));
        Append(builder, request.ClientVersion);
        Append(builder, request.DatasetRevision.ToString(CultureInfo.InvariantCulture));
        Append(builder, request.Attribution);
        Append(builder, request.LivePilotName);
        Append(builder, request.StarbaseId.ToString(CultureInfo.InvariantCulture));
        Append(builder, request.StationName);
        Append(builder, request.SectorName);
        Append(builder, request.ActiveSectorNumber.ToString(CultureInfo.InvariantCulture));
        Append(builder, request.RoomClass.ToString(CultureInfo.InvariantCulture));
        Append(builder, request.RoomDefinitionKey.ToString(CultureInfo.InvariantCulture));
        Append(builder, request.RoomNpcSlot.ToString(CultureInfo.InvariantCulture));
        Append(builder, request.DefinitionKey.ToString(CultureInfo.InvariantCulture));
        Append(builder, request.DefinitionSecondaryId.ToString(CultureInfo.InvariantCulture));
        Append(builder, request.VendorName);
        Append(builder, request.VendorType.ToString(CultureInfo.InvariantCulture));
        Append(builder, request.AmbientType.ToString(CultureInfo.InvariantCulture));
        Append(builder, request.CatalogFingerprint);

        foreach (var item in request.Items
                     .OrderBy(item => item.ItemTemplateId)
                     .ThenBy(item => item.Slot))
        {
            Append(builder, item.Slot.ToString(CultureInfo.InvariantCulture));
            Append(builder, item.ItemTemplateId.ToString(CultureInfo.InvariantCulture));
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }


    public static byte[] Canonicalize(
        ForgeProductionRecipeContributionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var builder = new StringBuilder();
        Append(builder, request.ProtocolVersion.ToString(CultureInfo.InvariantCulture));
        Append(builder, request.ContributorId);
        Append(builder, request.RequestId);
        Append(builder, request.SubmittedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        Append(builder, request.FirstObservedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        Append(builder, request.LastObservedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        Append(builder, request.ObservationCount.ToString(CultureInfo.InvariantCulture));
        Append(builder, request.ClientVersion);
        Append(builder, request.DatasetRevision.ToString(CultureInfo.InvariantCulture));
        Append(builder, request.Attribution);
        Append(builder, request.LivePilotName);

        foreach (var recipe in request.Recipes
                     .OrderBy(item => item.Kind)
                     .ThenBy(item => item.OutputItemTemplateId)
                     .ThenBy(item => item.RecipeFingerprint, StringComparer.Ordinal))
        {
            Append(builder, recipe.Kind.ToString(CultureInfo.InvariantCulture));
            Append(builder, recipe.OutputItemTemplateId.ToString(CultureInfo.InvariantCulture));
            Append(builder, recipe.RecipeFingerprint);

            foreach (var ingredient in recipe.Ingredients
                         .OrderBy(item => item.ItemTemplateId))
            {
                Append(builder, ingredient.ItemTemplateId.ToString(CultureInfo.InvariantCulture));
                Append(builder, ingredient.Quantity.ToString(CultureInfo.InvariantCulture));
            }
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }


    public static byte[] Canonicalize(
        ForgeGravityWellBoundaryContributionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var builder = new StringBuilder();
        Append(builder, request.ProtocolVersion.ToString(CultureInfo.InvariantCulture));
        Append(builder, request.ContributorId);
        Append(builder, request.RequestId);
        Append(builder, request.SubmittedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        Append(builder, request.ClientVersion);
        Append(builder, request.DatasetRevision.ToString(CultureInfo.InvariantCulture));
        Append(builder, request.Attribution);
        Append(builder, request.LivePilotName);
        Append(builder, request.SectorId);
        Append(builder, request.SectorKey);
        Append(builder, request.SectorName);
        Append(builder, request.SystemName);
        Append(builder, request.ActiveSectorNumber.ToString(CultureInfo.InvariantCulture));

        foreach (var observation in request.Observations
                     .OrderBy(item => item.ObservedAtUtc)
                     .ThenBy(item => item.Direction, StringComparer.Ordinal)
                     .ThenBy(item => item.RawMessage, StringComparer.Ordinal)
                     .ThenBy(item => item.X)
                     .ThenBy(item => item.Y)
                     .ThenBy(item => item.Z))
        {
            Append(builder, observation.ObservedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            Append(builder, observation.Direction);
            Append(builder, observation.X.ToString("R", CultureInfo.InvariantCulture));
            Append(builder, observation.Y.ToString("R", CultureInfo.InvariantCulture));
            Append(builder, observation.Z.ToString("R", CultureInfo.InvariantCulture));
            Append(builder, observation.MessageChannel.ToString(CultureInfo.InvariantCulture));
            Append(builder, observation.RawMessage);
            Append(builder, observation.Source);
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }


    public static byte[] Canonicalize(
        ForgeMissionContributionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var builder = new StringBuilder();
        Append(builder, request.ProtocolVersion.ToString(CultureInfo.InvariantCulture));
        Append(builder, request.ContributorId);
        Append(builder, request.RequestId);
        Append(builder, request.SubmittedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        Append(builder, request.FirstObservedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        Append(builder, request.LastObservedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        Append(builder, request.ObservationCount.ToString(CultureInfo.InvariantCulture));
        Append(builder, request.ClientVersion);
        Append(builder, request.DatasetRevision.ToString(CultureInfo.InvariantCulture));
        Append(builder, request.Attribution);
        Append(builder, request.LivePilotName);

        foreach (var mission in request.Missions
                     .OrderBy(item => item.SemanticFingerprint, StringComparer.Ordinal)
                     .ThenBy(item => item.EvidenceKind, StringComparer.Ordinal))
        {
            Append(builder, mission.SemanticFingerprint);
            Append(builder, mission.EvidenceKind);
            Append(builder, mission.Name);
            Append(builder, mission.Summary);
            Append(builder, mission.RewardText);
            Append(builder, mission.FailureConsequence);
            Append(builder, mission.IssuingFaction);
            Append(builder, mission.StageCount.ToString(CultureInfo.InvariantCulture));
            Append(builder, FormatNullableBoolean(mission.IsTimed));
            Append(builder, FormatNullableBoolean(mission.IsForfeitable));
            Append(builder, mission.IssuerNpcId);
            Append(builder, mission.CompletionNpcId);
            Append(builder, mission.CompletionSectorName);
            Append(builder, mission.CompletionStationName);

            foreach (var stage in mission.Stages.OrderBy(item => item.Index))
            {
                Append(builder, stage.Index.ToString(CultureInfo.InvariantCulture));
                Append(builder, stage.Text);
                Append(builder, FormatNullableBoolean(stage.IsTimed));
            }

            if (mission.Reward == null)
            {
                Append(builder, "");
            }
            else
            {
                Append(builder, "reward");
                Append(builder, mission.Reward.Credits.ToString(CultureInfo.InvariantCulture));
                Append(builder, mission.Reward.CombatExperience.ToString(CultureInfo.InvariantCulture));
                Append(builder, mission.Reward.ExploreExperience.ToString(CultureInfo.InvariantCulture));
                Append(builder, mission.Reward.TradeExperience.ToString(CultureInfo.InvariantCulture));

                foreach (var reputation in mission.Reward.Reputation
                             .OrderBy(item => item.FactionKey, StringComparer.Ordinal))
                {
                    Append(builder, reputation.FactionKey);
                    Append(builder, reputation.DisplayName);
                    Append(builder, reputation.ReactionDelta.ToString("R", CultureInfo.InvariantCulture));
                }

                foreach (var item in mission.Reward.Items
                             .OrderBy(item => item.ItemTemplateId))
                {
                    Append(builder, item.ItemTemplateId.ToString(CultureInfo.InvariantCulture));
                    Append(builder, item.Quantity.ToString(CultureInfo.InvariantCulture));
                }
            }
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    public static byte[] Canonicalize(
        ForgeJobOfferContributionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var builder = new StringBuilder();
        Append(builder, request.ProtocolVersion.ToString(CultureInfo.InvariantCulture));
        Append(builder, request.ContributorId);
        Append(builder, request.RequestId);
        Append(builder, request.SubmittedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        Append(builder, request.FirstObservedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        Append(builder, request.LastObservedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        Append(builder, request.ObservationCount.ToString(CultureInfo.InvariantCulture));
        Append(builder, request.ClientVersion);
        Append(builder, request.DatasetRevision.ToString(CultureInfo.InvariantCulture));
        Append(builder, request.Attribution);
        Append(builder, request.LivePilotName);
        Append(builder, request.StarbaseId.ToString(CultureInfo.InvariantCulture));
        Append(builder, request.StationName);
        Append(builder, request.SectorName);
        Append(builder, request.SystemName);
        Append(builder, request.ActiveSectorNumber.ToString(CultureInfo.InvariantCulture));
        Append(builder, request.FacilitySlot.ToString(CultureInfo.InvariantCulture));

        foreach (var offer in request.Offers
                     .OrderBy(item => item.SemanticFingerprint, StringComparer.Ordinal)
                     .ThenBy(item => item.ObservedJobId))
        {
            Append(builder, offer.FamilyFingerprint);
            Append(builder, offer.SemanticFingerprint);
            Append(builder, offer.ObservedJobId.ToString(CultureInfo.InvariantCulture));
            Append(builder, offer.CatalogueGeneration.ToString(CultureInfo.InvariantCulture));
            Append(builder, offer.Category.ToString(CultureInfo.InvariantCulture));
            Append(builder, offer.Level.ToString(CultureInfo.InvariantCulture));
            Append(builder, offer.Type);
            Append(builder, offer.Sponsor);
            Append(builder, offer.Title);
            Append(builder, offer.Description);
            Append(builder, offer.AdvertisedReward);
            Append(builder, offer.ObjectiveSummary);
            Append(builder, offer.StillAvailable ? "1" : "0");
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static string FormatNullableBoolean(bool? value)
    {
        if (!value.HasValue)
        {
            return "";
        }

        return value.Value ? "1" : "0";
    }

    private static void Append(StringBuilder builder, string value)
    {
        builder.Append(value.Length.ToString(CultureInfo.InvariantCulture));
        builder.Append(':');
        builder.Append(value);
        builder.Append('\n');
    }
}
