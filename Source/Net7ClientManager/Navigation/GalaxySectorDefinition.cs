namespace Net7ClientManager.Navigation;

using System.Globalization;

public sealed record GalaxySectorDefinition
{
    public required string Key { get; init; }

    public required string Name { get; init; }

    public required string SystemName { get; init; }

    public string? RequiredProfession { get; init; }

    public string? RequiredFaction { get; init; }

    public int? MinimumFactionStanding { get; init; }

    public IReadOnlyList<string> Aliases { get; init; } = [];

    public IReadOnlyList<string> Connections { get; init; } = [];

    public bool CanEnter(string? pilotProfession)
    {
        if (!string.IsNullOrWhiteSpace(this.RequiredFaction))
        {
            // The current pilot identity does not expose faction standings yet.
            // Treat known faction gates conservatively instead of routing a pilot
            // into a red gate.
            return false;
        }

        return string.IsNullOrWhiteSpace(this.RequiredProfession) ||
               string.IsNullOrWhiteSpace(pilotProfession) ||
               string.Equals(
                   this.RequiredProfession,
                   pilotProfession,
                   StringComparison.OrdinalIgnoreCase);
    }

    public string GetAccessRequirementDescription()
    {
        if (!string.IsNullOrWhiteSpace(this.RequiredProfession))
        {
            return $"Requires {this.RequiredProfession}";
        }

        if (!string.IsNullOrWhiteSpace(this.RequiredFaction))
        {
            return this.MinimumFactionStanding.HasValue
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"Requires {this.RequiredFaction} faction ≥ {this.MinimumFactionStanding.Value}")
                : $"Requires {this.RequiredFaction} faction (threshold unknown)";
        }

        return "Accessible";
    }

    public override string ToString()
    {
        return this.Name;
    }
}
