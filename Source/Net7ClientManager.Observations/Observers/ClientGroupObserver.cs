namespace Net7ClientManager.Observations.Observers;

using System.Globalization;
using System.Runtime.InteropServices;
using Net7ClientManager.Observations.Models;

internal sealed class ClientGroupObserver
{
    private const uint ImageBase = 0x00400000;

    private const uint GroupInfoVTableStatic =
        0x00b038b8;

    private const uint GroupInfoVTableRva =
        GroupInfoVTableStatic - ImageBase;

    private const uint LocalPlayerAuxDataGroupInfo =
        0x142c;

    private const uint GroupInfoValid =
        0x70;

    private const uint GroupInfoLeader =
        0x120;

    private const uint GroupInfoLookingForGroup =
        0x1a8;

    private const uint GroupInfoAllowGroupInvite =
        0x230;

    private const uint GroupInfoShowNonCombatActivities =
        0x2b8;

    private const uint GroupInfoForceAutoSplit =
        0x340;

    private const uint GroupInfoRestrictedLootingRights =
        0x3c8;

    private const uint GroupInfoAutoReleaseLootingRestrictions =
        0x450;

    private const uint GroupInfoFormationName =
        0x4d8;

    private const uint GroupInfoFormation =
        0x560;

    private const uint GroupInfoPosition =
        0x5e8;

    private const uint GroupInfoMembersValid =
        0x65c;

    private const uint GroupInfoMemberArray =
        0x674;

    private const uint GroupMemberName =
        0x120;

    private const uint GroupMemberObjectId =
        0x1a8;

    private const uint GroupMemberFormation =
        0x230;

    private const uint GroupMemberPosition =
        0x2b8;

    private const uint ClientObjectAuxData =
        0x88;

    private const uint ClientObjectObjectId =
        0x90;

    private const int GroupMemberCount = 5;
    private const int MaximumMemberNameLength = 64;
    private const int MaximumFormationNameLength = 128;

    private readonly ClientObjectResolver objectResolver =
        new();

    private readonly ClientAuxDataLookupReader auxDataReader =
        new();

    private readonly ClientShipAuxDataObserver shipAuxDataObserver =
        new();

    private readonly ClientSpatialObserver spatialObserver =
        new();

    private readonly Dictionary<MemberCacheKey, MemberRuntimeCache>
        memberCaches = [];

    private readonly Dictionary<int, uint>
        processGroupInfoAddresses = [];

    public void Refresh(
        ProcessMemoryReader memory,
        ObservedClientState state)
    {
        if (!state.HasDirectClientState)
        {
            this.RemoveProcessCaches(
                state.ProcessId);

            state.Group =
                ClientGroupObservation.Unavailable(
                    "Direct SClient state is unavailable");

            return;
        }

        if (state.LocalPlayerAuxDataAddress == 0)
        {
            this.RemoveProcessCaches(
                state.ProcessId);

            state.Group =
                ClientGroupObservation.Unavailable(
                    "LocalPlayerAuxData is unavailable");

            return;
        }

        uint groupInfoAddress;

        try
        {
            groupInfoAddress = checked(
                state.LocalPlayerAuxDataAddress +
                LocalPlayerAuxDataGroupInfo);
        }
        catch (OverflowException)
        {
            state.Group =
                ClientGroupObservation.Unavailable(
                    "GroupInfo address overflow");

            return;
        }

        if (this.processGroupInfoAddresses.TryGetValue(
                state.ProcessId,
                out var previousGroupInfoAddress) &&
            previousGroupInfoAddress != groupInfoAddress)
        {
            this.RemoveProcessCaches(
                state.ProcessId);
        }

        this.processGroupInfoAddresses[state.ProcessId] =
            groupInfoAddress;

        var expectedVTable = checked(
            state.ModuleBaseAddress +
            GroupInfoVTableRva);

        if (!memory.TryReadUInt32(
                groupInfoAddress,
                out var actualVTable))
        {
            state.Group =
                ClientGroupObservation.Unavailable(
                    $"Could not read GroupInfo vtable at 0x{groupInfoAddress:X8}");

            return;
        }

        if (actualVTable != expectedVTable)
        {
            state.Group =
                ClientGroupObservation.Unavailable(
                    $"Unexpected GroupInfo vtable 0x{actualVTable:X8} at 0x{groupInfoAddress:X8}; expected 0x{expectedVTable:X8}");

            return;
        }

        if (!memory.TryReadUInt32(
                groupInfoAddress +
                GroupInfoValid,
                out var validValue))
        {
            state.Group =
                ClientGroupObservation.Unavailable(
                    $"Could not read GroupInfo valid state at 0x{groupInfoAddress + GroupInfoValid:X8}");

            return;
        }

        if (!this.TryReadGroupByte(
                memory,
                groupInfoAddress,
                GroupInfoLeader,
                "leader",
                out var leaderValue,
                out var fieldError) ||
            !this.TryReadGroupByte(
                memory,
                groupInfoAddress,
                GroupInfoLookingForGroup,
                "looking-for-group",
                out var lookingForGroupValue,
                out fieldError) ||
            !this.TryReadGroupByte(
                memory,
                groupInfoAddress,
                GroupInfoAllowGroupInvite,
                "allow-group-invite",
                out var allowGroupInviteValue,
                out fieldError) ||
            !this.TryReadGroupByte(
                memory,
                groupInfoAddress,
                GroupInfoShowNonCombatActivities,
                "show-non-combat-activities",
                out var showNonCombatActivitiesValue,
                out fieldError) ||
            !this.TryReadGroupByte(
                memory,
                groupInfoAddress,
                GroupInfoForceAutoSplit,
                "force-auto-split",
                out var forceAutoSplitValue,
                out fieldError) ||
            !this.TryReadGroupByte(
                memory,
                groupInfoAddress,
                GroupInfoRestrictedLootingRights,
                "restricted-looting-rights",
                out var restrictedLootingRightsValue,
                out fieldError) ||
            !this.TryReadGroupByte(
                memory,
                groupInfoAddress,
                GroupInfoAutoReleaseLootingRestrictions,
                "auto-release-looting-restrictions",
                out var autoReleaseLootingRestrictionsValue,
                out fieldError))
        {
            state.Group =
                ClientGroupObservation.Unavailable(
                    fieldError);

            return;
        }

        if (!memory.TryReadUInt32(
                groupInfoAddress +
                GroupInfoFormationName,
                out var formationNameAddress))
        {
            state.Group =
                ClientGroupObservation.Unavailable(
                    $"Could not read GroupInfo formation-name pointer at 0x{groupInfoAddress + GroupInfoFormationName:X8}");

            return;
        }

        var formationName = "";

        if (formationNameAddress != 0 &&
            !memory.TryReadNullTerminatedLatin1String(
                formationNameAddress,
                MaximumFormationNameLength,
                out formationName))
        {
            state.Group =
                ClientGroupObservation.Unavailable(
                    $"Could not read GroupInfo formation name at 0x{formationNameAddress:X8}");

            return;
        }

        if (!memory.TryReadUInt32(
                groupInfoAddress +
                GroupInfoFormation,
                out var formationRaw) ||
            !memory.TryReadUInt32(
                groupInfoAddress +
                GroupInfoPosition,
                out var formationPositionRaw) ||
            !memory.TryReadUInt32(
                groupInfoAddress +
                GroupInfoMembersValid,
                out var membersValidValue))
        {
            state.Group =
                ClientGroupObservation.Unavailable(
                    "Could not read GroupInfo formation or member-collection state");

            return;
        }

        if (!memory.TryReadUInt32(
                groupInfoAddress +
                GroupInfoMemberArray,
                out var memberArrayAddress) ||
            memberArrayAddress == 0)
        {
            state.Group =
                ClientGroupObservation.Unavailable(
                    $"Could not resolve GroupInfo member array at 0x{groupInfoAddress + GroupInfoMemberArray:X8}");

            return;
        }

        List<ClientGroupMemberObservation> members = [];
        HashSet<uint> activeObjectIds = [];
        ClientSpatialObservation? localSpatial = null;

        for (var index = 0;
             index < GroupMemberCount;
             index++)
        {
            var memberPointerAddress = checked(
                memberArrayAddress +
                (uint)(index * sizeof(uint)));

            if (!memory.TryReadUInt32(
                    memberPointerAddress,
                    out var memberAddress))
            {
                state.Group =
                    ClientGroupObservation.Unavailable(
                        string.Create(CultureInfo.InvariantCulture, $"Could not read group member {index + 1} pointer at 0x{memberPointerAddress:X8}"));

                return;
            }

            if (memberAddress == 0)
            {
                members.Add(
                    new ClientGroupMemberObservation(
                        index + 1,
                        0,
                        0,
                        "",
                        0));

                continue;
            }

            if (!memory.TryReadUInt32(
                    memberAddress +
                    GroupMemberName,
                    out var nameAddress))
            {
                state.Group =
                    ClientGroupObservation.Unavailable(
                        string.Create(CultureInfo.InvariantCulture, $"Could not read group member {index + 1} name pointer at 0x{memberAddress + GroupMemberName:X8}"));

                return;
            }

            string name;

            if (nameAddress == 0)
            {
                name = "";
            }
            else if (!memory.TryReadNullTerminatedLatin1String(
                         nameAddress,
                         MaximumMemberNameLength,
                         out name))
            {
                state.Group =
                    ClientGroupObservation.Unavailable(
                        string.Create(CultureInfo.InvariantCulture, $"Could not read group member {index + 1} name at 0x{nameAddress:X8}"));

                return;
            }

            if (!memory.TryReadUInt32(
                    memberAddress +
                    GroupMemberObjectId,
                    out var objectId) ||
                !memory.TryReadUInt32(
                    memberAddress +
                    GroupMemberFormation,
                    out var memberFormationRaw) ||
                !memory.TryReadUInt32(
                    memberAddress +
                    GroupMemberPosition,
                    out var memberPositionRaw))
            {
                state.Group =
                    ClientGroupObservation.Unavailable(
                        string.Create(CultureInfo.InvariantCulture, $"Could not read group member {index + 1} identity or formation state at 0x{memberAddress:X8}"));

                return;
            }

            var member =
                new ClientGroupMemberObservation(
                    index + 1,
                    memberAddress,
                    nameAddress,
                    name,
                    objectId)
                {
                    FormationRaw =
                        memberFormationRaw,
                    FormationPosition =
                        unchecked((int)memberPositionRaw),
                };

            if (!member.IsPresent)
            {
                members.Add(member);
                continue;
            }

            activeObjectIds.Add(objectId);

            members.Add(
                this.EnrichMember(
                    memory,
                    state,
                    groupInfoAddress,
                    member,
                    ref localSpatial));
        }

        this.RemoveInactiveMemberCaches(
            state.ProcessId,
            activeObjectIds);

        var isValid =
            validValue != 0;

        var isInGroup =
            isValid &&
            members.Exists(
                member =>
                    member.IsPresent);

        var isLeader =
            isValid &&
            leaderValue != 0;

        var resolvedCount = members.Count(
            member =>
                member.IsObjectResolved);

        var occupiedCount = members.Count(
            member =>
                member.IsPresent);

        state.Group =
            new ClientGroupObservation
            {
                IsAvailable = true,
                Status = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Available; {occupiedCount} remote member(s), {resolvedCount} live object(s) resolved"),
                Address = groupInfoAddress,
                IsValid = isValid,
                IsInGroup = isInGroup,
                IsLeader = isLeader,
                LookingForGroup =
                    lookingForGroupValue != 0,
                AllowsGroupInvites =
                    allowGroupInviteValue != 0,
                ShowsNonCombatActivities =
                    showNonCombatActivitiesValue != 0,
                ForcesAutoSplit =
                    forceAutoSplitValue != 0,
                HasRestrictedLootingRights =
                    restrictedLootingRightsValue != 0,
                AutoReleasesLootingRestrictions =
                    autoReleaseLootingRestrictionsValue != 0,
                FormationName = formationName,
                FormationRaw = formationRaw,
                FormationPosition =
                    unchecked((int)formationPositionRaw),
                MembersCollectionValid =
                    membersValidValue != 0,
                Members = members,
            };
    }

    private ClientGroupMemberObservation EnrichMember(
        ProcessMemoryReader memory,
        ObservedClientState state,
        uint groupInfoAddress,
        ClientGroupMemberObservation member,
        ref ClientSpatialObservation? localSpatial)
    {
        var cacheKey =
            new MemberCacheKey(
                state.ProcessId,
                member.ObjectId);

        if (!this.memberCaches.TryGetValue(
                cacheKey,
                out var cache) ||
            cache.GroupInfoAddress != groupInfoAddress ||
            !this.TryValidateCachedObject(
                memory,
                member.ObjectId,
                cache))
        {
            this.memberCaches.Remove(
                cacheKey);

            if (!this.objectResolver.TryLookupClientObject(
                    memory,
                    state.ClientContextAddress,
                    member.ObjectId,
                    out var clientObjectAddress,
                    out var resolveError,
                    out _))
            {
                return member with
                {
                    ResolveStatus = resolveError,
                    Distance =
                        ClientTargetDistanceObservation.Unavailable(
                            resolveError),
                    Shield =
                        ClientTargetShieldObservation.Unavailable(
                            resolveError),
                    Hull =
                        ClientTargetHullObservation.Unavailable(
                            resolveError),
                };
            }

            if (!memory.TryReadUInt32(
                    clientObjectAddress +
                    ClientObjectAuxData,
                    out var auxDataAddress) ||
                auxDataAddress == 0)
            {
                var error = $"Could not resolve group-member ObjectAuxData at 0x{clientObjectAddress + ClientObjectAuxData:X8}";

                return member with
                {
                    ResolveStatus = error,
                    ClientObjectAddress =
                        clientObjectAddress,
                    Distance =
                        ClientTargetDistanceObservation.Unavailable(
                            error),
                    Shield =
                        ClientTargetShieldObservation.Unavailable(
                            error),
                    Hull =
                        ClientTargetHullObservation.Unavailable(
                            error),
                };
            }

            cache =
                new MemberRuntimeCache(
                    groupInfoAddress,
                    clientObjectAddress,
                    auxDataAddress);

            this.memberCaches[cacheKey] =
                cache;
        }

        if (!cache.HasVitalsLookup)
        {
            if (!this.auxDataReader.TryOpen(
                    memory,
                    state.ModuleBaseAddress,
                    cache.AuxDataAddress,
                    ClientShipAuxDataObserver.RemoteGroupVitalPropertyNames,
                    out var lookup,
                    out var lookupError))
            {
                return member with
                {
                    IsObjectResolved = true,
                    ResolveStatus =
                        $"Object resolved; vital lookup unavailable: {lookupError}",
                    ClientObjectAddress =
                        cache.ClientObjectAddress,
                    AuxDataAddress =
                        cache.AuxDataAddress,
                    Distance = this.ObserveDistance(
                        memory,
                        state,
                        cache.ClientObjectAddress,
                        ref localSpatial),
                    Shield =
                        ClientTargetShieldObservation.Unavailable(
                            lookupError),
                    Hull =
                        ClientTargetHullObservation.Unavailable(
                            lookupError),
                };
            }

            cache.VitalsLookup = lookup;
            cache.HasVitalsLookup = true;
        }

        var (shield, hull) = this.shipAuxDataObserver.ObserveRemoteGroupVitals(
            memory,
            state.CurrentClientTime,
            cache.VitalsLookup);

        return member with
        {
            IsObjectResolved = true,
            ResolveStatus = "Resolved through object registry",
            ClientObjectAddress =
                cache.ClientObjectAddress,
            AuxDataAddress =
                cache.AuxDataAddress,
            Distance = this.ObserveDistance(
                memory,
                state,
                cache.ClientObjectAddress,
                ref localSpatial),
            Shield = shield,
            Hull = hull,
        };
    }

    private bool TryValidateCachedObject(
        ProcessMemoryReader memory,
        uint expectedObjectId,
        MemberRuntimeCache cache)
    {
        if (!memory.TryReadUInt32(
                cache.ClientObjectAddress +
                ClientObjectObjectId,
                out var observedObjectId) ||
            observedObjectId != expectedObjectId ||
            !memory.TryReadUInt32(
                cache.ClientObjectAddress +
                ClientObjectAuxData,
                out var observedAuxDataAddress) ||
            observedAuxDataAddress == 0)
        {
            return false;
        }

        if (observedAuxDataAddress !=
            cache.AuxDataAddress)
        {
            cache.AuxDataAddress =
                observedAuxDataAddress;
            cache.HasVitalsLookup = false;
            cache.VitalsLookup = default;
        }

        return true;
    }

    private ClientTargetDistanceObservation ObserveDistance(
        ProcessMemoryReader memory,
        ObservedClientState state,
        uint memberClientObjectAddress,
        ref ClientSpatialObservation? localSpatial)
    {
        if (state.LocalPlayer.ClientObjectAddress == 0)
        {
            return ClientTargetDistanceObservation.Unavailable(
                "Local ClientGameObject is unavailable");
        }

        localSpatial ??=
            this.spatialObserver.Observe(
                memory,
                state.ModuleBaseAddress,
                state.CurrentClientTime,
                state.LocalPlayer.ClientObjectAddress);

        if (!localSpatial.IsAvailable)
        {
            return new ClientTargetDistanceObservation
            {
                Status =
                    $"Local spatial state unavailable: {localSpatial.Status}",
                Local = localSpatial,
            };
        }

        var memberSpatial =
            this.spatialObserver.Observe(
                memory,
                state.ModuleBaseAddress,
                state.CurrentClientTime,
                memberClientObjectAddress);

        if (!memberSpatial.IsAvailable)
        {
            return new ClientTargetDistanceObservation
            {
                Status =
                    $"Group-member spatial state unavailable: {memberSpatial.Status}",
                Local = localSpatial,
                Target = memberSpatial,
            };
        }

        var deltaX =
            memberSpatial.Position.X -
            localSpatial.Position.X;

        var deltaY =
            memberSpatial.Position.Y -
            localSpatial.Position.Y;

        var deltaZ =
            memberSpatial.Position.Z -
            localSpatial.Position.Z;

        var centerDistance = MathF.Sqrt(
            deltaX * deltaX +
            deltaY * deltaY +
            deltaZ * deltaZ);

        var surfaceDistance =
            centerDistance -
            (localSpatial.TargetingDistanceRadius +
             memberSpatial.TargetingDistanceRadius);

        if (surfaceDistance < 0)
        {
            surfaceDistance = 0;
        }

        return new ClientTargetDistanceObservation
        {
            IsAvailable = true,
            Status = "Available for resolved group member",
            SurfaceDistance = surfaceDistance,
            Local = localSpatial,
            Target = memberSpatial,
        };
    }

    private bool TryReadGroupByte(
        ProcessMemoryReader memory,
        uint groupInfoAddress,
        uint offset,
        string fieldName,
        out byte value,
        out string error)
    {
        error = "";

        if (TryReadByte(
                memory,
                groupInfoAddress + offset,
                out value))
        {
            return true;
        }

        error = $"Could not read GroupInfo {fieldName} state at 0x{groupInfoAddress + offset:X8}";

        return false;
    }

    private void RemoveInactiveMemberCaches(
        int processId,
        IReadOnlySet<uint> activeObjectIds)
    {
        var staleKeys = this.memberCaches.Keys
            .Where(
                key =>
                    key.ProcessId == processId &&
                    !activeObjectIds.Contains(
                        key.ObjectId))
            .ToArray();

        foreach (var key in staleKeys)
        {
            this.memberCaches.Remove(key);
        }
    }

    private void RemoveProcessCaches(
        int processId)
    {
        var keys = this.memberCaches.Keys
            .Where(
                key =>
                    key.ProcessId == processId)
            .ToArray();

        foreach (var key in keys)
        {
            this.memberCaches.Remove(key);
        }

        this.processGroupInfoAddresses.Remove(
            processId);
    }

    private static bool TryReadByte(
        ProcessMemoryReader memory,
        uint address,
        out byte value)
    {
        value = 0;

        if (!memory.TryReadBytes(
                address,
                1,
                out var bytes))
        {
            return false;
        }

        value = bytes[0];
        return true;
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct MemberCacheKey(
        int ProcessId,
        uint ObjectId);

    private sealed class MemberRuntimeCache(
        uint groupInfoAddress,
        uint clientObjectAddress,
        uint auxDataAddress)
    {
        public uint GroupInfoAddress { get; } = groupInfoAddress;

        public uint ClientObjectAddress { get; } = clientObjectAddress;

        public uint AuxDataAddress { get; set; } = auxDataAddress;

        public bool HasVitalsLookup { get; set; }

        public ClientAuxDataLookupSnapshot VitalsLookup { get; set; }
    }
}
