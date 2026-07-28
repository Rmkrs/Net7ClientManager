// ReSharper disable StringLiteralTypo
// ReSharper disable CommentTypo
namespace Net7ClientManager.Observations.Observers;

using System.Text;

internal sealed class ClientFactionCatalog
{
    private const string CatalogFileName =
        "cfaction_t.ini";

    private readonly Lock stateLock = new();

    private ClientFactionCatalogSnapshot snapshot =
        ClientFactionCatalogSnapshot.NotLoaded();

    private string? lastAttemptedClientExecutablePath;

    private bool loadAttempted;

    public ClientFactionCatalogSnapshot Snapshot
    {
        get
        {
            lock (this.stateLock)
            {
                return this.snapshot;
            }
        }
    }

    public void EnsureLoaded(
        string? clientExecutablePath)
    {
        var normalizedClientExecutablePath =
            NormalizePath(
                clientExecutablePath);

        lock (this.stateLock)
        {
            if (this.snapshot.IsAvailable ||
                (this.loadAttempted &&
                 string.Equals(
                     this.lastAttemptedClientExecutablePath,
                     normalizedClientExecutablePath,
                     StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            this.snapshot = LoadCatalog(
                normalizedClientExecutablePath);

            this.lastAttemptedClientExecutablePath =
                normalizedClientExecutablePath;

            this.loadAttempted = true;
        }
    }

    private static ClientFactionCatalogSnapshot LoadCatalog(
        string? clientExecutablePath)
    {
        var path = FindCatalogPath(
            clientExecutablePath);

        if (path == null)
        {
            return ClientFactionCatalogSnapshot.Unavailable(
                "cfaction_t.ini was not found; faction keys will be used as display names");
        }

        try
        {
            Dictionary<string, ClientFactionDefinition>
                definitions = new(
                    StringComparer.Ordinal);

            string? currentKey = null;
            var currentName = "";
            var currentDescription = "";

            using var reader = new StreamReader(
                path,
                Encoding.Latin1,
                detectEncodingFromByteOrderMarks: true);

            while (reader.ReadLine() is { } rawLine)
            {
                var line = rawLine.Trim();

                if (line.Length == 0 ||
                    line[0] is ';' or '#')
                {
                    continue;
                }

                if (line[0] == '[' &&
                    line[^1] == ']')
                {
                    AddCurrentDefinition();

                    currentKey = line[1..^1]
                        .Trim();

                    currentName = "";
                    currentDescription = "";

                    continue;
                }

                if (currentKey == null)
                {
                    continue;
                }

                var separatorIndex = line.IndexOf('=', StringComparison.Ordinal);

                if (separatorIndex <= 0)
                {
                    continue;
                }

                var fieldName = line[..separatorIndex]
                    .Trim();

                var fieldValue = line[(separatorIndex + 1)..]
                    .Trim()
                    .Replace(
                        "\\n",
                        Environment.NewLine,
                        StringComparison.Ordinal);

                if (string.Equals(
                        fieldName,
                        "Name",
                        StringComparison.OrdinalIgnoreCase))
                {
                    currentName = fieldValue;
                }
                else if (string.Equals(
                             fieldName,
                             "Description",
                             StringComparison.OrdinalIgnoreCase))
                {
                    currentDescription = fieldValue;
                }
            }

            AddCurrentDefinition();

            if (definitions.Count == 0)
            {
                return ClientFactionCatalogSnapshot.Unavailable(
                    $"cfaction_t.ini at '{path}' contained no faction definitions",
                    path);
            }

            return new ClientFactionCatalogSnapshot(
                path,
                definitions,
                $"Loaded {definitions.Count} faction definitions from {path}");

            void AddCurrentDefinition()
            {
                if (string.IsNullOrWhiteSpace(
                        currentKey))
                {
                    return;
                }

                definitions[currentKey] =
                    new ClientFactionDefinition
                    {
                        Key = currentKey,
                        DisplayName =
                            string.IsNullOrWhiteSpace(
                                currentName)
                                ? currentKey
                                : currentName,
                        Description =
                            currentDescription,
                    };
            }
        }
        catch (Exception exception)
        {
            return ClientFactionCatalogSnapshot.Unavailable(
                $"Could not load cfaction_t.ini at '{path}': {exception.Message}",
                path);
        }
    }

    private static string? FindCatalogPath(
        string? clientExecutablePath)
    {
        List<string> candidates = [];

        AddCandidate(
            Environment.GetEnvironmentVariable(
                "NET7_CFACTION_PATH"));

        var pathFile = Path.Combine(
            AppContext.BaseDirectory,
            "cfaction-path.txt");

        if (File.Exists(pathFile))
        {
            AddCandidate(
                File.ReadAllText(pathFile));
        }

        AddCandidate(
            Path.Combine(
                AppContext.BaseDirectory,
                CatalogFileName));

        var clientDirectory =
            clientExecutablePath == null
                ? null
                : Path.GetDirectoryName(
                    clientExecutablePath);

        if (clientDirectory != null)
        {
            AddCandidate(
                Path.Combine(
                    clientDirectory,
                    CatalogFileName));

            var gameDirectory = Directory.GetParent(
                clientDirectory)?.FullName;

            if (gameDirectory != null)
            {
                // The stock client lives in <game>\release\client.exe,
                // while its faction catalog lives in
                // <game>\Data\client\ini\cfaction_t.ini.
                AddCandidate(
                    Path.Combine(
                        gameDirectory,
                        "Data",
                        "client",
                        "ini",
                        CatalogFileName));

                AddCandidate(
                    Path.Combine(
                        gameDirectory,
                        CatalogFileName));

                AddCandidate(
                    Path.Combine(
                        gameDirectory,
                        "release",
                        CatalogFileName));
            }
        }

        AddCandidate(
            @"C:\Games\Earth & Beyond\Data\client\ini\cfaction_t.ini");

        AddCandidate(
            @"C:\Games\Earth & Beyond\release\cfaction_t.ini");

        AddCandidate(
            @"C:\Program Files (x86)\EA GAMES\Earth & Beyond\release\cfaction_t.ini");

        return candidates
            .Distinct(
                StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(
                File.Exists);

        void AddCandidate(
            string? candidate)
        {
            var normalized = NormalizePath(
                candidate);

            if (normalized != null)
            {
                candidates.Add(
                    normalized);
            }
        }
    }

    private static string? NormalizePath(
        string? path)
    {
        if (string.IsNullOrWhiteSpace(
                path))
        {
            return null;
        }

        var trimmed = path
            .Trim()
            .Trim('"');

        try
        {
            return Path.GetFullPath(
                trimmed);
        }
        catch (Exception exception)
            when (exception is ArgumentException or
                  NotSupportedException or
                  PathTooLongException)
        {
            return trimmed;
        }
    }
}
