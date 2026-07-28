namespace Net7ClientManager.Observations.Models;

public sealed record ClientShipStatsObservation
{
    public int? Defense { get; init; }

    public int? MissileDefense { get; init; }

    public int? Speed { get; init; }

    public int? WarpSpeed { get; init; }

    public int? WarpPowerLevel { get; init; }

    public int? TurnRate { get; init; }

    public int? ScanRange { get; init; }

    public int? Visibility { get; init; }

    public int? ResistImpact { get; init; }

    public int? ResistExplosive { get; init; }

    public int? ResistPlasma { get; init; }

    public int? ResistEnergy { get; init; }

    public int? ResistEmp { get; init; }

    public int? ResistChemical { get; init; }

    public int? ResistPsionic { get; init; }
}
