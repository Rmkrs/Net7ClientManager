namespace Net7ClientManager.Observations.Models;

public sealed record ClientCharacterDetailsObservation
{
    public bool IsAvailable { get; init; }

    public string Status { get; init; } = "";

    public uint HullAuxDataAddress { get; init; }

    public uint MoneyPropertyAddress { get; init; }

    public uint MoneyValidState { get; init; }

    public ulong? Credits { get; init; }

    public uint ExperienceDebtPropertyAddress { get; init; }

    public uint ExperienceDebtValidState { get; init; }

    public int? ExperienceDebt { get; init; }

    public uint RegistrationStarbasePropertyAddress { get; init; }

    public uint RegistrationStarbaseValidState { get; init; }

    public uint RegistrationStarbaseStringAddress { get; init; }

    public string RegistrationStarbase { get; init; } = "";

    public uint RegistrationStarbaseSectorPropertyAddress { get; init; }

    public uint RegistrationStarbaseSectorValidState { get; init; }

    public uint RegistrationStarbaseSectorStringAddress { get; init; }

    public string RegistrationStarbaseSector { get; init; } = "";

    public static ClientCharacterDetailsObservation Unavailable(
        string status,
        uint hullAuxDataAddress = 0)
    {
        return new ClientCharacterDetailsObservation
        {
            Status = status,
            HullAuxDataAddress =
                hullAuxDataAddress,
        };
    }
}
