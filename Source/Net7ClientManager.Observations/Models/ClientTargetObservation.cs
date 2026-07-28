namespace Net7ClientManager.Observations.Models;

public sealed record ClientTargetObservation
{
    public bool IsAvailable { get; init; }

    public string Status { get; init; } = "";

    public bool HasTarget { get; init; }

    public uint TargetGameIdAddress { get; init; }

    public uint ObjectId { get; init; }

    public uint ClientObjectAddress { get; init; }

    public uint AuxDataAddress { get; init; }

    public uint NamePropertyAddress { get; init; }

    public uint NameAddress { get; init; }

    public bool NamePropertyValid { get; init; }

    public string Name { get; init; } = "";

    public byte ObjectType { get; init; }

    public ClientTargetKind Kind { get; init; }

    public ClientTargetRelation Relation { get; init; }

    public int EmbeddedStateValue { get; init; }

    public byte HostileAttackingState { get; init; }

    public bool IsSelf { get; init; }

    public bool IsGroupMember { get; init; }

    public ClientTargetDistanceObservation Distance { get; init; } =
        ClientTargetDistanceObservation.Unavailable(
            "Target distance was not observed");

    public ClientTargetShieldObservation Shield { get; init; } =
        ClientTargetShieldObservation.Unavailable(
            "Target shield state was not observed");

    public ClientTargetHullObservation Hull { get; init; } =
        ClientTargetHullObservation.Unavailable(
            "Target hull state was not observed");

    public ClientTargetEnergyObservation Energy { get; init; } =
        ClientTargetEnergyObservation.Unavailable(
            "Target energy state was not observed");

    public ClientShipOperationalObservation Operational { get; init; } =
        ClientShipOperationalObservation.Unavailable(
            "Target operational state was not observed");

    public ClientCorpseObservation Corpse { get; init; } =
        ClientCorpseObservation.Unavailable(
            "Target corpse state was not observed");

    public ClientAsteroidObservation Asteroid { get; init; } =
        ClientAsteroidObservation.Unavailable(
            "Target harvestable state was not observed");

    public bool IsHostileAttacking =>
        this.HostileAttackingState != 0;

    public static ClientTargetObservation Unavailable(
        string status)
    {
        return new ClientTargetObservation
        {
            Status = status,
        };
    }

    public static ClientTargetObservation NoTarget(
        uint targetGameIdAddress)
    {
        return new ClientTargetObservation
        {
            IsAvailable = true,
            Status = "No current target",
            HasTarget = false,
            TargetGameIdAddress = targetGameIdAddress,
            Distance =
                ClientTargetDistanceObservation.Unavailable(
                    "No current target"),
            Shield =
                ClientTargetShieldObservation.Unavailable(
                    "No current target"),
            Hull =
                ClientTargetHullObservation.Unavailable(
                    "No current target"),
            Energy =
                ClientTargetEnergyObservation.Unavailable(
                    "No current target"),
            Operational =
                ClientShipOperationalObservation.Unavailable(
                    "No current target"),
            Corpse =
                ClientCorpseObservation.Unavailable(
                    "No current target"),
            Asteroid =
                ClientAsteroidObservation.Unavailable(
                    "No current target"),
        };
    }
}
