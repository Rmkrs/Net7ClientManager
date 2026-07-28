namespace Net7ClientManager.MissionJournal;

using System.Globalization;
using Microsoft.Data.Sqlite;

/// <summary>
/// Stores local per-pilot mission episodes and their meaningful lifecycle
/// events. Active provenance and completed history share one durable source of
/// truth so the job guide and Pilot Archive cannot disagree after a restart.
/// </summary>
public sealed class MissionJournalStore
{
    private const int CurrentSchemaVersion = 1;

    private readonly string databasePath;
    private readonly string connectionString;
    private readonly Lock sync = new();

    public MissionJournalStore()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Net7ClientManager");
        this.databasePath = Path.Combine(directory, "mission-journal.db");
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
            Directory.CreateDirectory(Path.GetDirectoryName(this.databasePath)!);
            using var connection = this.OpenConnection();
            var version = ExecuteScalarLong(
                connection,
                transaction: null,
                "PRAGMA user_version;");

            if (version > CurrentSchemaVersion)
            {
                throw new InvalidOperationException(
                    string.Concat(
                        "Mission Journal schema ",
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

    internal void SaveEpisode(
        MissionJournalEntry entry,
        MissionJournalEventKind? eventKind = null,
        string eventDetails = "")
    {
        ArgumentNullException.ThrowIfNull(entry);

        lock (this.sync)
        {
            using var connection = this.OpenConnection();
            using var transaction = connection.BeginTransaction();
            this.UpsertEpisode(connection, transaction, entry);

            if (eventKind.HasValue)
            {
                this.InsertEvent(
                    connection,
                    transaction,
                    entry,
                    eventKind.Value,
                    eventDetails);
            }

            transaction.Commit();
        }
    }

    internal void DeleteEpisode(string episodeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(episodeId);

        lock (this.sync)
        {
            using var connection = this.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                DELETE FROM mission_episodes
                WHERE episode_id = @episode_id;
                """;
            command.Parameters.AddWithValue("@episode_id", episodeId);
            command.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<MissionJournalEntry> GetActiveEntries(
        uint characterId)
    {
        lock (this.sync)
        {
            using var connection = this.OpenConnection();
            return this.ReadEntries(
                connection,
                "WHERE character_id = @character_id AND status = @active " +
                "ORDER BY COALESCE(accepted_at, first_observed_at), episode_id",
                command =>
                {
                    command.Parameters.AddWithValue(
                        "@character_id",
                        (long)characterId);
                    command.Parameters.AddWithValue(
                        "@active",
                        (int)MissionJournalStatus.Active);
                },
                includeEvents: false);
        }
    }

    public IReadOnlyList<MissionJournalEntry> GetHistory(
        uint characterId,
        int maximumResults = 1000)
    {
        maximumResults = Math.Clamp(maximumResults, 1, 5000);

        lock (this.sync)
        {
            using var connection = this.OpenConnection();
            return this.ReadEntries(
                connection,
                "WHERE character_id = @character_id " +
                "ORDER BY CASE WHEN status = @active THEN 0 ELSE 1 END, " +
                "COALESCE(accepted_at, first_observed_at) DESC, episode_id DESC " +
                "LIMIT @maximum_results",
                command =>
                {
                    command.Parameters.AddWithValue(
                        "@character_id",
                        (long)characterId);
                    command.Parameters.AddWithValue(
                        "@active",
                        (int)MissionJournalStatus.Active);
                    command.Parameters.AddWithValue(
                        "@maximum_results",
                        maximumResults);
                },
                includeEvents: true);
        }
    }

    public DateTimeOffset? GetLastObservedAt(uint characterId)
    {
        lock (this.sync)
        {
            using var connection = this.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT MAX(COALESCE(ended_at, last_observed_at))
                FROM mission_episodes
                WHERE character_id = @character_id;
                """;
            command.Parameters.AddWithValue(
                "@character_id",
                (long)characterId);
            var value = command.ExecuteScalar() as string;
            return string.IsNullOrWhiteSpace(value)
                ? null
                : ParseTimestamp(value);
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
            CREATE TABLE mission_episodes (
                episode_id TEXT PRIMARY KEY,
                character_id INTEGER NOT NULL,
                pilot_name TEXT NOT NULL,
                source INTEGER NOT NULL,
                status INTEGER NOT NULL,
                job_id INTEGER NULL,
                job_category INTEGER NOT NULL,
                mission_raw_id INTEGER NULL,
                mission_start_time INTEGER NULL,
                semantic_fingerprint TEXT NOT NULL,
                name TEXT NOT NULL,
                summary TEXT NOT NULL,
                reward_text TEXT NOT NULL,
                failure_consequence TEXT NOT NULL,
                issuing_faction TEXT NOT NULL,
                accepted_at TEXT NULL,
                first_observed_at TEXT NOT NULL,
                last_observed_at TEXT NOT NULL,
                ended_at TEXT NULL,
                stage INTEGER NULL,
                stage_count INTEGER NULL,
                current_objective TEXT NOT NULL,
                accepted_system TEXT NOT NULL,
                accepted_sector TEXT NOT NULL,
                accepted_starbase TEXT NOT NULL,
                issuer_npc_name TEXT NOT NULL,
                completion_system TEXT NOT NULL,
                completion_sector TEXT NOT NULL,
                completion_starbase TEXT NOT NULL
            );

            CREATE TABLE mission_events (
                event_id INTEGER PRIMARY KEY AUTOINCREMENT,
                episode_id TEXT NOT NULL,
                occurred_at TEXT NOT NULL,
                kind INTEGER NOT NULL,
                stage INTEGER NULL,
                objective TEXT NOT NULL,
                system_name TEXT NOT NULL,
                sector_name TEXT NOT NULL,
                starbase_name TEXT NOT NULL,
                details TEXT NOT NULL,
                FOREIGN KEY(episode_id) REFERENCES mission_episodes(episode_id)
                    ON DELETE CASCADE
            );

            CREATE INDEX ix_mission_episodes_character_status
                ON mission_episodes(character_id, status);
            CREATE INDEX ix_mission_episodes_character_started
                ON mission_episodes(character_id, accepted_at, first_observed_at);
            CREATE INDEX ix_mission_episodes_active_identity
                ON mission_episodes(
                    character_id,
                    status,
                    mission_raw_id,
                    mission_start_time,
                    semantic_fingerprint);
            CREATE INDEX ix_mission_events_episode_time
                ON mission_events(episode_id, occurred_at, event_id);

            PRAGMA user_version = 1;
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private void UpsertEpisode(
        SqliteConnection connection,
        SqliteTransaction transaction,
        MissionJournalEntry entry)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO mission_episodes (
                episode_id, character_id, pilot_name, source, status,
                job_id, job_category, mission_raw_id, mission_start_time,
                semantic_fingerprint, name, summary, reward_text,
                failure_consequence, issuing_faction, accepted_at,
                first_observed_at, last_observed_at, ended_at,
                stage, stage_count, current_objective,
                accepted_system, accepted_sector, accepted_starbase,
                issuer_npc_name, completion_system, completion_sector,
                completion_starbase)
            VALUES (
                @episode_id, @character_id, @pilot_name, @source, @status,
                @job_id, @job_category, @mission_raw_id, @mission_start_time,
                @semantic_fingerprint, @name, @summary, @reward_text,
                @failure_consequence, @issuing_faction, @accepted_at,
                @first_observed_at, @last_observed_at, @ended_at,
                @stage, @stage_count, @current_objective,
                @accepted_system, @accepted_sector, @accepted_starbase,
                @issuer_npc_name, @completion_system, @completion_sector,
                @completion_starbase)
            ON CONFLICT(episode_id) DO UPDATE SET
                pilot_name = excluded.pilot_name,
                source = excluded.source,
                status = excluded.status,
                job_id = excluded.job_id,
                job_category = excluded.job_category,
                mission_raw_id = excluded.mission_raw_id,
                mission_start_time = excluded.mission_start_time,
                semantic_fingerprint = excluded.semantic_fingerprint,
                name = excluded.name,
                summary = excluded.summary,
                reward_text = excluded.reward_text,
                failure_consequence = excluded.failure_consequence,
                issuing_faction = excluded.issuing_faction,
                accepted_at = excluded.accepted_at,
                last_observed_at = excluded.last_observed_at,
                ended_at = excluded.ended_at,
                stage = excluded.stage,
                stage_count = excluded.stage_count,
                current_objective = excluded.current_objective,
                accepted_system = excluded.accepted_system,
                accepted_sector = excluded.accepted_sector,
                accepted_starbase = excluded.accepted_starbase,
                issuer_npc_name = excluded.issuer_npc_name,
                completion_system = excluded.completion_system,
                completion_sector = excluded.completion_sector,
                completion_starbase = excluded.completion_starbase;
            """;
        AddParameter(command, "@episode_id", entry.EpisodeId);
        AddParameter(command, "@character_id", (long)entry.CharacterId);
        AddParameter(command, "@pilot_name", entry.PilotName);
        AddParameter(command, "@source", (int)entry.Source);
        AddParameter(command, "@status", (int)entry.Status);
        AddParameter(command, "@job_id", entry.JobId.HasValue
            ? (long)entry.JobId.Value
            : null);
        AddParameter(command, "@job_category", (int)entry.JobCategory);
        AddParameter(command, "@mission_raw_id", entry.MissionRawId);
        AddParameter(command, "@mission_start_time", entry.MissionStartTime);
        AddParameter(command, "@semantic_fingerprint", entry.SemanticFingerprint);
        AddParameter(command, "@name", entry.Name);
        AddParameter(command, "@summary", entry.Summary);
        AddParameter(command, "@reward_text", entry.RewardText);
        AddParameter(command, "@failure_consequence", entry.FailureConsequence);
        AddParameter(command, "@issuing_faction", entry.IssuingFaction);
        AddParameter(command, "@accepted_at", entry.AcceptedAt.HasValue
            ? FormatTimestamp(entry.AcceptedAt.Value)
            : null);
        AddParameter(command, "@first_observed_at", FormatTimestamp(entry.FirstObservedAt));
        AddParameter(command, "@last_observed_at", FormatTimestamp(entry.LastObservedAt));
        AddParameter(command, "@ended_at", entry.EndedAt.HasValue
            ? FormatTimestamp(entry.EndedAt.Value)
            : null);
        AddParameter(command, "@stage", entry.Stage);
        AddParameter(command, "@stage_count", entry.StageCount);
        AddParameter(command, "@current_objective", entry.CurrentObjective);
        AddParameter(command, "@accepted_system", entry.AcceptedSystem);
        AddParameter(command, "@accepted_sector", entry.AcceptedSector);
        AddParameter(command, "@accepted_starbase", entry.AcceptedStarbase);
        AddParameter(command, "@issuer_npc_name", entry.IssuerNpcName);
        AddParameter(command, "@completion_system", entry.CompletionSystem);
        AddParameter(command, "@completion_sector", entry.CompletionSector);
        AddParameter(command, "@completion_starbase", entry.CompletionStarbase);
        command.ExecuteNonQuery();
    }

    private void InsertEvent(
        SqliteConnection connection,
        SqliteTransaction transaction,
        MissionJournalEntry entry,
        MissionJournalEventKind kind,
        string details)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO mission_events (
                episode_id, occurred_at, kind, stage, objective,
                system_name, sector_name, starbase_name, details)
            VALUES (
                @episode_id, @occurred_at, @kind, @stage, @objective,
                @system_name, @sector_name, @starbase_name, @details);
            """;
        AddParameter(command, "@episode_id", entry.EpisodeId);
        var occurredAt = kind is
            MissionJournalEventKind.Completed or
            MissionJournalEventKind.Forfeited or
            MissionJournalEventKind.Failed or
            MissionJournalEventKind.Expired or
            MissionJournalEventKind.NoLongerActive
                ? entry.EndedAt ?? entry.LastObservedAt
                : entry.LastObservedAt;
        AddParameter(command, "@occurred_at", FormatTimestamp(occurredAt));
        AddParameter(command, "@kind", (int)kind);
        AddParameter(command, "@stage", entry.Stage);
        AddParameter(command, "@objective", entry.CurrentObjective);
        var eventLocation = kind switch
        {
            MissionJournalEventKind.Observed or
            MissionJournalEventKind.Accepted or
            MissionJournalEventKind.SourceIdentified =>
                new EventLocation(
                    entry.AcceptedSystem,
                    entry.AcceptedSector,
                    entry.AcceptedStarbase),
            MissionJournalEventKind.Completed or
            MissionJournalEventKind.Forfeited or
            MissionJournalEventKind.Failed or
            MissionJournalEventKind.Expired or
            MissionJournalEventKind.NoLongerActive =>
                new EventLocation(
                    entry.CompletionSystem,
                    entry.CompletionSector,
                    entry.CompletionStarbase),
            _ => EventLocation.Empty,
        };
        AddParameter(command, "@system_name", eventLocation.SystemName);
        AddParameter(command, "@sector_name", eventLocation.SectorName);
        AddParameter(command, "@starbase_name", eventLocation.StarbaseName);
        AddParameter(command, "@details", details);
        command.ExecuteNonQuery();
    }

    private IReadOnlyList<MissionJournalEntry> ReadEntries(
        SqliteConnection connection,
        string whereAndOrderClause,
        Action<SqliteCommand> bind,
        bool includeEvents)
    {
        using var command = connection.CreateCommand();
        command.CommandText = string.Concat(
            """
            SELECT episode_id, character_id, pilot_name, source, status,
                   job_id, job_category, mission_raw_id, mission_start_time,
                   semantic_fingerprint, name, summary, reward_text,
                   failure_consequence, issuing_faction, accepted_at,
                   first_observed_at, last_observed_at, ended_at,
                   stage, stage_count, current_objective,
                   accepted_system, accepted_sector, accepted_starbase,
                   issuer_npc_name, completion_system, completion_sector,
                   completion_starbase
            FROM mission_episodes
            """,
            " ",
            whereAndOrderClause,
            ";");
        bind(command);

        List<MissionJournalEntry> entries = [];

        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                entries.Add(ReadEntry(reader));
            }
        }

        if (!includeEvents || entries.Count == 0)
        {
            return entries;
        }

        var eventsByEpisode = this.ReadEvents(
            connection,
            entries.Select(entry => entry.EpisodeId));

        return [.. entries.Select(entry => entry with
        {
            Events = eventsByEpisode.GetValueOrDefault(entry.EpisodeId) ?? [],
        })];
    }

    private Dictionary<string, IReadOnlyList<MissionJournalEvent>> ReadEvents(
        SqliteConnection connection,
        IEnumerable<string> episodeIds)
    {
        var ids = episodeIds.Distinct(StringComparer.Ordinal).ToArray();
        Dictionary<string, List<MissionJournalEvent>> mutableResult =
            new(StringComparer.Ordinal);

        const int batchSize = 400;

        foreach (var batch in ids.Chunk(batchSize))
        {
            using var command = connection.CreateCommand();
            var parameterNames = new string[batch.Length];

            for (var index = 0; index < batch.Length; index++)
            {
                var parameterName = string.Create(
                    CultureInfo.InvariantCulture,
                    $"@episode_{index}");
                parameterNames[index] = parameterName;
                command.Parameters.AddWithValue(
                    parameterName,
                    batch[index]);
            }

            command.CommandText = string.Concat(
                "SELECT episode_id, event_id, occurred_at, kind, stage, ",
                "objective, system_name, sector_name, starbase_name, details ",
                "FROM mission_events WHERE episode_id IN (",
                string.Join(", ", parameterNames),
                ") ORDER BY episode_id, occurred_at, event_id;");
            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                var episodeId = reader.GetString(0);

                if (!mutableResult.TryGetValue(episodeId, out var events))
                {
                    events = [];
                    mutableResult[episodeId] = events;
                }

                events.Add(
                    new MissionJournalEvent
                    {
                        EventId = reader.GetInt64(1),
                        OccurredAt = ParseTimestamp(reader.GetString(2)),
                        Kind =
                            (MissionJournalEventKind)reader.GetInt32(3),
                        Stage = GetNullableInt32(reader, 4),
                        Objective = reader.GetString(5),
                        SystemName = reader.GetString(6),
                        SectorName = reader.GetString(7),
                        StarbaseName = reader.GetString(8),
                        Details = reader.GetString(9),
                    });
            }
        }

        return mutableResult.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<MissionJournalEvent>)pair.Value,
            StringComparer.Ordinal);
    }

    private static MissionJournalEntry ReadEntry(SqliteDataReader reader)
    {
        return new MissionJournalEntry
        {
            EpisodeId = reader.GetString(0),
            CharacterId = checked((uint)reader.GetInt64(1)),
            PilotName = reader.GetString(2),
            Source = (MissionJournalSource)reader.GetInt32(3),
            Status = (MissionJournalStatus)reader.GetInt32(4),
            JobId = reader.IsDBNull(5)
                ? null
                : checked((uint)reader.GetInt64(5)),
            JobCategory = (MissionJournalJobCategory)reader.GetInt32(6),
            MissionRawId = GetNullableInt32(reader, 7),
            MissionStartTime = GetNullableInt32(reader, 8),
            SemanticFingerprint = reader.GetString(9),
            Name = reader.GetString(10),
            Summary = reader.GetString(11),
            RewardText = reader.GetString(12),
            FailureConsequence = reader.GetString(13),
            IssuingFaction = reader.GetString(14),
            AcceptedAt = reader.IsDBNull(15)
                ? null
                : ParseTimestamp(reader.GetString(15)),
            FirstObservedAt = ParseTimestamp(reader.GetString(16)),
            LastObservedAt = ParseTimestamp(reader.GetString(17)),
            EndedAt = reader.IsDBNull(18)
                ? null
                : ParseTimestamp(reader.GetString(18)),
            Stage = GetNullableInt32(reader, 19),
            StageCount = GetNullableInt32(reader, 20),
            CurrentObjective = reader.GetString(21),
            AcceptedSystem = reader.GetString(22),
            AcceptedSector = reader.GetString(23),
            AcceptedStarbase = reader.GetString(24),
            IssuerNpcName = reader.GetString(25),
            CompletionSystem = reader.GetString(26),
            CompletionSector = reader.GetString(27),
            CompletionStarbase = reader.GetString(28),
        };
    }

    private static void AddParameter(
        SqliteCommand command,
        string name,
        object? value)
    {
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }

    private static long ExecuteScalarLong(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string commandText)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        return Convert.ToInt64(
            command.ExecuteScalar(),
            CultureInfo.InvariantCulture);
    }

    private static string FormatTimestamp(DateTimeOffset timestamp) =>
        timestamp.ToUniversalTime().ToString(
            "O",
            CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);

    private static int? GetNullableInt32(
        SqliteDataReader reader,
        int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);

    private sealed record EventLocation(
        string SystemName,
        string SectorName,
        string StarbaseName)
    {
        public static EventLocation Empty { get; } = new("", "", "");
    }
}
