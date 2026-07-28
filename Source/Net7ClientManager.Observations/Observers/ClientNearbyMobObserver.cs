namespace Net7ClientManager.Observations.Observers;

using Net7ClientManager.Observations.Models;

internal sealed class ClientNearbyMobObserver
{
    private static readonly string[] propertyNames =
    [
        "Name",
        "FactionIdentifier",
        "CombatLevel",
        "AutoLevel",
        "IsOrganic",
    ];

    private readonly ClientAuxDataLookupReader auxDataReader = new();
    private readonly ClientSpatialObserver spatialObserver = new();

    public ClientNearbyMobObservation Observe(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        uint clientTime,
        uint clientObjectAddress,
        uint auxDataAddress,
        string fallbackName)
    {
        var spatial = this.spatialObserver.Observe(
            memory,
            moduleBaseAddress,
            clientTime,
            clientObjectAddress);

        if (!this.auxDataReader.TryOpenAvailableFromPropertyVector(
                memory,
                moduleBaseAddress,
                auxDataAddress,
                propertyNames,
                out var lookup,
                out var openError))
        {
            return ClientNearbyMobObservation.Unavailable(
                $"Mob AuxData unavailable: {openError}",
                spatial);
        }

        if (!this.auxDataReader.TryReadStringProperty(
                memory,
                lookup,
                "Name",
                out var name,
                out var error) ||
            !this.auxDataReader.TryReadStringProperty(
                memory,
                lookup,
                "FactionIdentifier",
                out var faction,
                out error) ||
            !this.auxDataReader.TryReadInt32Property(
                memory,
                lookup,
                "CombatLevel",
                out var combatLevel,
                out error) ||
            !this.auxDataReader.TryReadBooleanProperty(
                memory,
                lookup,
                "AutoLevel",
                out var autoLevel,
                out error) ||
            !this.auxDataReader.TryReadBooleanProperty(
                memory,
                lookup,
                "IsOrganic",
                out var isOrganic,
                out error))
        {
            return ClientNearbyMobObservation.Unavailable(
                $"Mob AuxData read failed: {error}",
                spatial);
        }

        var resolvedName = name.IsValid &&
                           !string.IsNullOrWhiteSpace(name.Value)
            ? name.Value.Trim()
            : fallbackName.Trim();
        List<string> unavailable = [];

        if (string.IsNullOrWhiteSpace(resolvedName))
        {
            unavailable.Add("name");
        }

        if (!combatLevel.IsValid)
        {
            unavailable.Add("combat level");
        }

        if (!isOrganic.IsValid)
        {
            unavailable.Add("organic state");
        }

        if (!spatial.IsAvailable)
        {
            unavailable.Add("world position");
        }

        return new ClientNearbyMobObservation
        {
            IsAvailable = unavailable.Count == 0,
            Status = unavailable.Count == 0
                ? "Available"
                : $"Waiting for complete mob {string.Join(", ", unavailable)}",
            Name = resolvedName,
            FactionIdentifier = faction.IsValid
                ? NormalizeFaction(faction.Value)
                : "",
            CombatLevel = combatLevel.IsValid
                ? combatLevel.Value
                : null,
            AutoLevel = autoLevel.IsValid
                ? autoLevel.Value
                : null,
            IsOrganic = isOrganic.IsValid
                ? isOrganic.Value
                : null,
            Spatial = spatial,
        };
    }

    private static string NormalizeFaction(string value)
    {
        return value.Trim();
    }
}
