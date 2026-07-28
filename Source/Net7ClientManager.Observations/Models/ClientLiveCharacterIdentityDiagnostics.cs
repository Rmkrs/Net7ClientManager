namespace Net7ClientManager.Observations.Models;

public sealed record ClientLiveCharacterIdentityDiagnostics
{
    public bool OperationalAvailable { get; init; }

    public string OperationalStatus { get; init; } = "";

    public string? OperationalProfessionName { get; init; }

    public string? FactionIdentifier { get; init; }

    public bool ReputationAvailable { get; init; }

    public string ReputationStatus { get; init; } = "";

    public string? ReputationAffiliation { get; init; }

    public bool ProgressionAvailable { get; init; }

    public string ProgressionStatus { get; init; } = "";

    public int? RaceRaw { get; init; }

    public int? ProfessionRaw { get; init; }
}
