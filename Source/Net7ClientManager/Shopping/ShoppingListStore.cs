namespace Net7ClientManager.Shopping;

using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

internal sealed class ShoppingListStore
{
    private const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions jsonOptions = new()
    {
        WriteIndented = false,
    };

    private readonly string databasePath;
    private readonly string connectionString;
    private readonly Lock sync = new();

    public ShoppingListStore()
        : this(Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData),
            "Net7ClientManager",
            "shopping-lists.db"))
    {
    }

    internal ShoppingListStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        this.databasePath = Path.GetFullPath(databasePath);
        this.connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = this.databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        }.ToString();
    }

    public string DatabasePath => this.databasePath;

    public void Initialize()
    {
        lock (this.sync)
        {
            Directory.CreateDirectory(
                Path.GetDirectoryName(this.databasePath)!);
            using var connection = this.OpenConnection();
            var version = ExecuteScalarLong(
                connection,
                "PRAGMA user_version;");

            if (version > CurrentSchemaVersion)
            {
                throw new InvalidOperationException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Shopping-list schema {version} is newer than supported schema {CurrentSchemaVersion}."));
            }

            if (version == 0)
            {
                this.CreateSchema(connection);
            }
        }
    }

    public IReadOnlyList<ShoppingListSummary> GetLists()
    {
        lock (this.sync)
        {
            using var connection = this.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT document_json
                FROM shopping_lists
                ORDER BY updated_at_utc DESC, name COLLATE NOCASE;
                """;

            List<ShoppingListSummary> result = [];
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var document = Deserialize(reader.GetString(0));
                if (document == null)
                {
                    continue;
                }

                result.Add(new ShoppingListSummary
                {
                    ListId = document.ListId,
                    Name = document.Name,
                    Notes = document.Notes,
                    UpdatedAtUtc = document.UpdatedAtUtc,
                    RequestedOutputCount = document.RequestedOutputs.Count,
                    RequestedUnitCount = document.RequestedOutputs.Sum(
                        output => output.Quantity),
                });
            }

            return result;
        }
    }

    public ShoppingListDocument? GetList(string listId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(listId);

        lock (this.sync)
        {
            using var connection = this.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT document_json
                FROM shopping_lists
                WHERE list_id = @list_id;
                """;
            command.Parameters.AddWithValue(
                "@list_id",
                listId.Trim());
            return command.ExecuteScalar() is string json
                ? Deserialize(json)
                : null;
        }
    }

    public ShoppingListDocument Create(string name)
    {
        name = NormalizeName(name);
        var now = DateTimeOffset.UtcNow;
        var document = new ShoppingListDocument
        {
            ListId = Guid.NewGuid().ToString("N"),
            Name = name,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        var saved = this.Save(document);

        if (this.GetActiveListId() == null)
        {
            this.SetActiveList(saved.ListId);
        }

        return saved;
    }

    public ShoppingListDocument Save(ShoppingListDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var normalized = Normalize(document);
        var json = JsonSerializer.Serialize(normalized, jsonOptions);

        lock (this.sync)
        {
            using var connection = this.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO shopping_lists (
                    list_id, name, created_at_utc,
                    updated_at_utc, document_json)
                VALUES (
                    @list_id, @name, @created_at_utc,
                    @updated_at_utc, @document_json)
                ON CONFLICT(list_id) DO UPDATE SET
                    name = excluded.name,
                    updated_at_utc = excluded.updated_at_utc,
                    document_json = excluded.document_json;
                """;
            command.Parameters.AddWithValue(
                "@list_id",
                normalized.ListId);
            command.Parameters.AddWithValue("@name", normalized.Name);
            command.Parameters.AddWithValue(
                "@created_at_utc",
                FormatTimestamp(normalized.CreatedAtUtc));
            command.Parameters.AddWithValue(
                "@updated_at_utc",
                FormatTimestamp(normalized.UpdatedAtUtc));
            command.Parameters.AddWithValue("@document_json", json);
            command.ExecuteNonQuery();
        }

        return normalized;
    }

    public bool Delete(string listId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(listId);
        listId = listId.Trim();

        lock (this.sync)
        {
            using var connection = this.OpenConnection();
            using var transaction = connection.BeginTransaction();
            using var delete = connection.CreateCommand();
            delete.Transaction = transaction;
            delete.CommandText = """
                DELETE FROM shopping_lists
                WHERE list_id = @list_id;
                """;
            delete.Parameters.AddWithValue("@list_id", listId);
            var deleted = delete.ExecuteNonQuery() != 0;

            using var clearActive = connection.CreateCommand();
            clearActive.Transaction = transaction;
            clearActive.CommandText = """
                DELETE FROM shopping_state
                WHERE key = 'active-list-id'
                  AND value = @list_id;
                """;
            clearActive.Parameters.AddWithValue("@list_id", listId);
            clearActive.ExecuteNonQuery();
            transaction.Commit();
            return deleted;
        }
    }

    public string? GetActiveListId()
    {
        lock (this.sync)
        {
            using var connection = this.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT value
                FROM shopping_state
                WHERE key = 'active-list-id';
                """;
            return command.ExecuteScalar() as string;
        }
    }

    public void SetActiveList(string? listId)
    {
        listId = string.IsNullOrWhiteSpace(listId)
            ? null
            : listId.Trim();

        lock (this.sync)
        {
            using var connection = this.OpenConnection();
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;

            if (listId == null)
            {
                command.CommandText = """
                    DELETE FROM shopping_state
                    WHERE key = 'active-list-id';
                    """;
            }
            else
            {
                using var exists = connection.CreateCommand();
                exists.Transaction = transaction;
                exists.CommandText = """
                    SELECT COUNT(*)
                    FROM shopping_lists
                    WHERE list_id = @list_id;
                    """;
                exists.Parameters.AddWithValue("@list_id", listId);
                if (Convert.ToInt64(
                        exists.ExecuteScalar(),
                        CultureInfo.InvariantCulture) == 0)
                {
                    throw new InvalidOperationException(
                        "The shopping list no longer exists.");
                }

                command.CommandText = """
                    INSERT INTO shopping_state (key, value)
                    VALUES ('active-list-id', @list_id)
                    ON CONFLICT(key) DO UPDATE SET
                        value = excluded.value;
                    """;
                command.Parameters.AddWithValue("@list_id", listId);
            }

            command.ExecuteNonQuery();
            transaction.Commit();
        }
    }

    private static ShoppingListDocument Normalize(
        ShoppingListDocument document)
    {
        var listId = string.IsNullOrWhiteSpace(document.ListId)
            ? Guid.NewGuid().ToString("N")
            : document.ListId.Trim();
        var createdAt = document.CreatedAtUtc == default
            ? DateTimeOffset.UtcNow
            : document.CreatedAtUtc;
        var outputs = document.RequestedOutputs
            .Where(output =>
                output.ItemTemplateId > 0 &&
                output.Quantity > 0)
            .GroupBy(output => output.ItemTemplateId)
            .Select(group => new ShoppingListRequestedOutput
            {
                ItemTemplateId = group.Key,
                Quantity = checked(group.Sum(output => output.Quantity)),
            })
            .OrderBy(output => output.ItemTemplateId)
            .ToArray();
        var selections = document.RecipeSelections
            .Where(selection =>
                selection.OutputItemTemplateId > 0 &&
                !string.IsNullOrWhiteSpace(selection.RecipeIdentity))
            .GroupBy(selection => selection.OutputItemTemplateId)
            .Select(group => group.Last())
            .Select(selection => new ShoppingListRecipeSelection
            {
                OutputItemTemplateId = selection.OutputItemTemplateId,
                RecipeIdentity = selection.RecipeIdentity.Trim(),
            })
            .OrderBy(selection => selection.OutputItemTemplateId)
            .ToArray();

        return document with
        {
            ListId = listId,
            Name = NormalizeName(document.Name),
            Notes = NormalizeNotes(document.Notes),
            CreatedAtUtc = createdAt,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            RequestedOutputs = outputs,
            RecipeSelections = selections,
        };
    }

    private static string NormalizeName(string? name)
    {
        name = name?.Trim();
        return string.IsNullOrWhiteSpace(name)
            ? "Shopping List"
            : name.Length <= 120
                ? name
                : name[..120];
    }



    private static string NormalizeNotes(string? notes)
    {
        notes = notes?.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
        if (string.IsNullOrEmpty(notes))
        {
            return "";
        }

        return notes.Length <= 4000
            ? notes
            : notes[..4000];
    }

    private static ShoppingListDocument? Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<ShoppingListDocument>(
                json,
                jsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(this.connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA foreign_keys = ON;
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            PRAGMA busy_timeout = 5000;
            """;
        command.ExecuteNonQuery();
        return connection;
    }

    private void CreateSchema(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE shopping_lists (
                list_id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL,
                document_json TEXT NOT NULL
            );

            CREATE TABLE shopping_state (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );

            PRAGMA user_version = 1;
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private static long ExecuteScalarLong(
        SqliteConnection connection,
        string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(
            command.ExecuteScalar(),
            CultureInfo.InvariantCulture);
    }

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
