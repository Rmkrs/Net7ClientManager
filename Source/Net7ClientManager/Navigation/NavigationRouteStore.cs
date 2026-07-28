namespace Net7ClientManager.Navigation;

using System.Text.Json;
using System.Text.Json.Serialization;

internal sealed class NavigationRouteStore
{
    private const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters =
        {
            new JsonStringEnumConverter(),
        },
    };

    private readonly string directoryPath;
    private readonly string filePath;

    public NavigationRouteStore()
    {
        this.directoryPath = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData),
            "Net7ClientManager",
            "NavigationRoutes");

        this.filePath = Path.Combine(
            this.directoryPath,
            "routes.json");
    }

    public IReadOnlyDictionary<string, NavigationPersistedRoute> Load()
    {
        if (!File.Exists(this.filePath))
        {
            return new Dictionary<string, NavigationPersistedRoute>(
                StringComparer.Ordinal);
        }

        try
        {
            var json = File.ReadAllText(this.filePath);
            var document = JsonSerializer.Deserialize<NavigationRouteDocument>(
                json,
                jsonOptions);

            if (document == null ||
                document.SchemaVersion != CurrentSchemaVersion)
            {
                return new Dictionary<string, NavigationPersistedRoute>(
                    StringComparer.Ordinal);
            }

            Dictionary<string, NavigationPersistedRoute> routes =
                new(StringComparer.Ordinal);

            foreach (var route in document.Routes)
            {
                if (string.IsNullOrWhiteSpace(route.CharacterKey) ||
                    string.IsNullOrWhiteSpace(route.CharacterName) ||
                    string.IsNullOrWhiteSpace(route.OriginSectorKey) ||
                    !IsValidDestination(route.Destination) ||
                    (route.OriginDestination != null &&
                     !IsValidDestination(route.OriginDestination)))
                {
                    continue;
                }

                routes[route.CharacterKey] = route with
                {
                    VisitedSectorKeys = route.VisitedSectorKeys,
                };
            }

            return routes;
        }
        catch
        {
            this.TryQuarantineBrokenFile();

            return new Dictionary<string, NavigationPersistedRoute>(
                StringComparer.Ordinal);
        }
    }

    private static bool IsValidDestination(
        NavigationDestination destination)
    {
        return !string.IsNullOrWhiteSpace(destination.SectorKey) &&
               (destination.Kind != NavigationDestinationKind.Target ||
                (!string.IsNullOrWhiteSpace(destination.TargetKey) &&
                 !string.IsNullOrWhiteSpace(destination.TargetName) &&
                 !string.IsNullOrWhiteSpace(destination.TargetType)));
    }

    public bool TrySave(
        IReadOnlyCollection<NavigationPersistedRoute> routes,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(routes);
        error = "";

        var temporaryPath = this.filePath + ".tmp";

        try
        {
            Directory.CreateDirectory(this.directoryPath);

            var document = new NavigationRouteDocument
            {
                SchemaVersion = CurrentSchemaVersion,
                SavedAt = DateTimeOffset.UtcNow,
                Routes =
                [
                    .. routes
                        .OrderBy(
                            route => route.CharacterName,
                            StringComparer.OrdinalIgnoreCase)
                        .ThenBy(
                            route => route.CharacterKey,
                            StringComparer.Ordinal),
                ],
            };

            var json = JsonSerializer.Serialize(document, jsonOptions);

            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, this.filePath, overwrite: true);
            return true;
        }
        catch (Exception ex)
            when (ex is IOException or
                  UnauthorizedAccessException or
                  JsonException)
        {
            error = string.Concat(
                "Could not persist navigation routes: ",
                ex.Message);

            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch
            {
                // The original persistence error is the useful diagnostic.
            }

            return false;
        }
    }

    private void TryQuarantineBrokenFile()
    {
        try
        {
            var brokenPath = this.filePath + ".broken";

            if (File.Exists(brokenPath))
            {
                File.Delete(brokenPath);
            }

            if (File.Exists(this.filePath))
            {
                File.Move(this.filePath, brokenPath);
            }
        }
        catch
        {
            // A broken route file must never prevent the manager from starting.
        }
    }
}
