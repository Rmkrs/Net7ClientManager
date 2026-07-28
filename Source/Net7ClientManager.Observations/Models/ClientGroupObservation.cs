namespace Net7ClientManager.Observations.Models;

public sealed record ClientGroupObservation
{
    public bool IsAvailable { get; init; }

    public string Status { get; init; } = "";

    public uint Address { get; init; }

    public bool IsValid { get; init; }

    public bool IsInGroup { get; init; }

    public bool IsLeader { get; init; }

    public bool LookingForGroup { get; init; }

    public bool AllowsGroupInvites { get; init; }

    public bool ShowsNonCombatActivities { get; init; }

    public bool ForcesAutoSplit { get; init; }

    public bool HasRestrictedLootingRights { get; init; }

    public bool AutoReleasesLootingRestrictions { get; init; }

    public string FormationName { get; init; } = "";

    public uint FormationRaw { get; init; }

    public int FormationPosition { get; init; } = -1;

    public bool MembersCollectionValid { get; init; }

    public IReadOnlyList<ClientGroupMemberObservation>
        Members
    { get; init; } = [];

    public bool ContainsObjectId(
        uint objectId)
    {
        if (!this.IsAvailable ||
            !this.IsValid ||
            !this.IsInGroup ||
            objectId is 0 or uint.MaxValue)
        {
            return false;
        }

        return this.Members.Any(
            member =>
                member.ObjectId == objectId);
    }

    public static ClientGroupObservation Unavailable(
        string status)
    {
        return new ClientGroupObservation
        {
            Status = status,
        };
    }
}
