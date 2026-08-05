namespace Net7ClientManager.ChatJournal;

using System.Globalization;
using Microsoft.Data.Sqlite;

/// <summary>
/// Stores the raw chat lines that Client Manager observed after attaching to
/// a pilot. The game's initial ring snapshot is deliberately not persisted,
/// because those recovered lines do not expose their original timestamps.
/// </summary>
internal sealed class ChatJournalStore
{
    private const int CurrentSchemaVersion = 1;

    private readonly string databasePath;
    private readonly string connectionString;
    private readonly Lock sync = new();

    public ChatJournalStore()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Net7ClientManager");
        this.databasePath = Path.Combine(directory, "chat-journal.db");
        this.connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = this.databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        }.ToString();
    }

    public void Initialize()
    {
        lock (this.sync)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(this.databasePath)!);
            using var connection = this.OpenConnection();
            var version = ExecuteScalarLong(
                connection,
                "PRAGMA user_version;");

            if (version > CurrentSchemaVersion)
            {
                throw new InvalidOperationException(
                    string.Concat(
                        "Chat Journal schema ",
                        version.ToString(CultureInfo.InvariantCulture),
                        " is newer than supported schema ",
                        CurrentSchemaVersion.ToString(
                            CultureInfo.InvariantCulture),
                        "."));
            }

            if (version == 0)
            {
                this.CreateSchema(connection);
            }
        }
    }

    public bool Save(ChatJournalEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.IsSnapshot)
        {
            return false;
        }

        lock (this.sync)
        {
            using var connection = this.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT OR IGNORE INTO chat_messages (
                    entry_id,
                    session_id,
                    character_id,
                    pilot_name,
                    process_id,
                    process_started_at,
                    sequence,
                    channel,
                    message_text,
                    observed_at
                ) VALUES (
                    @entry_id,
                    @session_id,
                    @character_id,
                    @pilot_name,
                    @process_id,
                    @process_started_at,
                    @sequence,
                    @channel,
                    @message_text,
                    @observed_at
                );
                """;
            BindEntry(command, entry);
            return command.ExecuteNonQuery() > 0;
        }
    }

    public bool ContainsObservedSource(
        uint characterId,
        int processId,
        DateTimeOffset processStartedAt,
        uint sequence,
        int channel,
        string text)
    {
        lock (this.sync)
        {
            using var connection = this.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT 1
                FROM chat_messages
                WHERE character_id = @character_id
                  AND process_id = @process_id
                  AND process_started_at = @process_started_at
                  AND sequence = @sequence
                  AND channel = @channel
                  AND message_text = @message_text
                LIMIT 1;
                """;
            command.Parameters.AddWithValue(
                "@character_id",
                (long)characterId);
            command.Parameters.AddWithValue("@process_id", processId);
            command.Parameters.AddWithValue(
                "@process_started_at",
                FormatTimestamp(processStartedAt));
            command.Parameters.AddWithValue(
                "@sequence",
                (long)sequence);
            command.Parameters.AddWithValue("@channel", channel);
            command.Parameters.AddWithValue("@message_text", text);
            return command.ExecuteScalar() != null;
        }
    }

    public IReadOnlyList<ChatJournalEntry> GetHistory(
        uint characterId,
        int maximumResults)
    {
        maximumResults = Math.Clamp(maximumResults, 1, 10000);

        lock (this.sync)
        {
            using var connection = this.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                    entry_id,
                    session_id,
                    character_id,
                    pilot_name,
                    process_id,
                    process_started_at,
                    sequence,
                    channel,
                    message_text,
                    observed_at
                FROM chat_messages
                WHERE character_id = @character_id
                ORDER BY observed_at DESC, message_id DESC
                LIMIT @maximum_results;
                """;
            command.Parameters.AddWithValue(
                "@character_id",
                (long)characterId);
            command.Parameters.AddWithValue(
                "@maximum_results",
                maximumResults);

            using var reader = command.ExecuteReader();
            List<ChatJournalEntry> entries = [];

            while (reader.Read())
            {
                entries.Add(new ChatJournalEntry
                {
                    EntryId = reader.GetString(0),
                    SessionId = reader.GetString(1),
                    CharacterId = checked((uint)reader.GetInt64(2)),
                    PilotName = reader.GetString(3),
                    ProcessId = reader.GetInt32(4),
                    ProcessStartedAt = ParseTimestamp(reader.GetString(5)),
                    Sequence = checked((uint)reader.GetInt64(6)),
                    Channel = reader.GetInt32(7),
                    Text = reader.GetString(8),
                    ObservedAt = ParseTimestamp(reader.GetString(9)),
                    IsSnapshot = false,
                });
            }

            entries.Reverse();
            return entries;
        }
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(this.connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
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
            CREATE TABLE chat_messages (
                message_id INTEGER PRIMARY KEY AUTOINCREMENT,
                entry_id TEXT NOT NULL UNIQUE,
                session_id TEXT NOT NULL,
                character_id INTEGER NOT NULL,
                pilot_name TEXT NOT NULL,
                process_id INTEGER NOT NULL,
                process_started_at TEXT NOT NULL,
                sequence INTEGER NOT NULL,
                channel INTEGER NOT NULL,
                message_text TEXT NOT NULL,
                observed_at TEXT NOT NULL
            );

            CREATE UNIQUE INDEX ux_chat_messages_session_sequence
                ON chat_messages(session_id, sequence);

            CREATE INDEX ix_chat_messages_character_observed
                ON chat_messages(character_id, observed_at);

            CREATE INDEX ix_chat_messages_observed_source
                ON chat_messages(
                    character_id,
                    process_id,
                    process_started_at,
                    sequence,
                    channel);

            PRAGMA user_version = 1;
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private static void BindEntry(
        SqliteCommand command,
        ChatJournalEntry entry)
    {
        command.Parameters.AddWithValue("@entry_id", entry.EntryId);
        command.Parameters.AddWithValue("@session_id", entry.SessionId);
        command.Parameters.AddWithValue(
            "@character_id",
            (long)entry.CharacterId);
        command.Parameters.AddWithValue("@pilot_name", entry.PilotName);
        command.Parameters.AddWithValue("@process_id", entry.ProcessId);
        command.Parameters.AddWithValue(
            "@process_started_at",
            FormatTimestamp(entry.ProcessStartedAt));
        command.Parameters.AddWithValue(
            "@sequence",
            (long)entry.Sequence);
        command.Parameters.AddWithValue("@channel", entry.Channel);
        command.Parameters.AddWithValue("@message_text", entry.Text);
        command.Parameters.AddWithValue(
            "@observed_at",
            FormatTimestamp(entry.ObservedAt));
    }

    private static long ExecuteScalarLong(
        SqliteConnection connection,
        string commandText)
    {
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        return Convert.ToInt64(
            command.ExecuteScalar(),
            CultureInfo.InvariantCulture);
    }

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);
}
