namespace Net7ClientManager.Observations.Models;

public static class ClientLiveCharacterIdentityResolver
{
    public static ClientLiveCharacterIdentity Resolve(
        ClientObservationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return Resolve(
            snapshot.ProcessId,
            snapshot.LifecycleState,
            snapshot.ObservedAt,
            snapshot.LocalPlayer);
    }

    public static ClientLiveCharacterIdentity Resolve(
        int processId,
        ClientLifecycleState lifecycleState,
        DateTimeOffset observedAt,
        ClientLocalPlayerObservation localPlayer)
    {
        ArgumentNullException.ThrowIfNull(localPlayer);

        if (lifecycleState != ClientLifecycleState.InGame)
        {
            return UnavailableWithDiagnostics(
                processId,
                observedAt,
                string.Concat(
                    "Character identity is unavailable while lifecycle is ",
                    lifecycleState),
                localPlayer);
        }

        if (!localPlayer.IsAvailable)
        {
            return UnavailableWithDiagnostics(
                processId,
                observedAt,
                string.IsNullOrWhiteSpace(localPlayer.Status)
                    ? "Local player is unavailable"
                    : localPlayer.Status,
                localPlayer);
        }

        var operational = localPlayer.Operational;
        var identity = operational.Identity;
        var progression = localPlayer.CharacterProgression;
        var reputation = localPlayer.Reputation;
        var professionResolution =
            ClientProfessionResolver.ResolveDetailed(
                identity,
                progression,
                reputation);

        var name = Normalize(identity.Name);
        var profession = professionResolution.Profession;
        var isProfessionConflict =
            professionResolution.Status ==
            ClientProfessionResolutionStatus.Conflicting;

        var status = isProfessionConflict
            ? ClientLiveCharacterIdentityStatus.ProfessionConflict
            : !string.IsNullOrWhiteSpace(name) &&
              !string.IsNullOrWhiteSpace(profession)
                ? ClientLiveCharacterIdentityStatus.Available
                : ClientLiveCharacterIdentityStatus.Partial;

        string statusText;

        if (status ==
            ClientLiveCharacterIdentityStatus.Available)
        {
            statusText =
                "Live character identity is available";
        }
        else if (status ==
                 ClientLiveCharacterIdentityStatus.ProfessionConflict)
        {
            statusText =
                "Live profession sources conflict";
        }
        else if (string.IsNullOrWhiteSpace(name))
        {
            statusText =
                "Live character name is unavailable";
        }
        else
        {
            statusText =
                "Live profession is unresolved";
        }

        return new ClientLiveCharacterIdentity
        {
            ProcessId = processId,
            ObservedAt = observedAt,
            IsAvailable = true,
            Status = status,
            StatusText = statusText,
            CharacterObjectId = localPlayer.ObjectId is 0 or uint.MaxValue
                ? null
                : localPlayer.ObjectId,
            Name = name,
            Race = ClientProfessionResolver.RaceNameFromRaw(
                progression.Race),
            Profession = profession,
            ProfessionCode = Normalize(identity.FactionIdentifier),
            FactionAffiliation = Normalize(reputation.Affiliation),
            CombatLevel = progression.CombatLevel ?? identity.CombatLevel,
            ExploreLevel = progression.ExploreLevel,
            TradeLevel = progression.TradeLevel,
            OverallLevel = progression.OverallLevel,
            GuildName = Normalize(identity.GuildName),
            ProfessionResolution = professionResolution,
            Diagnostics = BuildDiagnostics(localPlayer),
        };
    }

    private static ClientLiveCharacterIdentity UnavailableWithDiagnostics(
        int processId,
        DateTimeOffset observedAt,
        string statusText,
        ClientLocalPlayerObservation localPlayer)
    {
        var professionResolution =
            ClientProfessionResolver.ResolveDetailed(
                localPlayer.Operational.Identity,
                localPlayer.CharacterProgression,
                localPlayer.Reputation);

        return new ClientLiveCharacterIdentity
        {
            ProcessId = processId,
            ObservedAt = observedAt,
            Status = ClientLiveCharacterIdentityStatus.Unavailable,
            StatusText = statusText,
            ProfessionResolution = professionResolution,
            Diagnostics = BuildDiagnostics(localPlayer),
        };
    }

    private static ClientLiveCharacterIdentityDiagnostics BuildDiagnostics(
        ClientLocalPlayerObservation localPlayer)
    {
        return new ClientLiveCharacterIdentityDiagnostics
        {
            OperationalAvailable = localPlayer.Operational.IsAvailable,
            OperationalStatus = localPlayer.Operational.Status,
            OperationalProfessionName = Normalize(
                localPlayer.Operational.Identity.ProfessionName),
            FactionIdentifier = Normalize(
                localPlayer.Operational.Identity.FactionIdentifier),
            ReputationAvailable = localPlayer.Reputation.IsAvailable,
            ReputationStatus = localPlayer.Reputation.Status,
            ReputationAffiliation = Normalize(
                localPlayer.Reputation.Affiliation),
            ProgressionAvailable =
                localPlayer.CharacterProgression.IsAvailable,
            ProgressionStatus =
                localPlayer.CharacterProgression.Status,
            RaceRaw = localPlayer.CharacterProgression.Race,
            ProfessionRaw =
                localPlayer.CharacterProgression.Profession,
        };
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }
}
