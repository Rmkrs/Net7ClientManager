namespace Net7ClientManager.Observations.Models;

public sealed record ClientGroupMemberObservation(
    int Slot,
    uint Address,
    uint NameAddress,
    string Name,
    uint ObjectId)
{
    public bool IsPresent =>
        this.ObjectId is not 0 and
        not uint.MaxValue;

    public uint FormationRaw { get; init; }

    public int FormationPosition { get; init; } = -1;

    public bool IsObjectResolved { get; init; }

    public string ResolveStatus { get; init; } = "";

    public uint ClientObjectAddress { get; init; }

    public uint AuxDataAddress { get; init; }

    public ClientTargetDistanceObservation Distance { get; init; } =
        ClientTargetDistanceObservation.Unavailable(
            "Group-member distance was not observed");

    public ClientTargetShieldObservation Shield { get; init; } =
        ClientTargetShieldObservation.Unavailable(
            "Group-member shield state was not observed");

    public ClientTargetHullObservation Hull { get; init; } =
        ClientTargetHullObservation.Unavailable(
            "Group-member hull state was not observed");
}
