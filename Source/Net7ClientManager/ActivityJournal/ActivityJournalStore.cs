namespace Net7ClientManager.ActivityJournal;

using System.Globalization;
using Microsoft.Data.Sqlite;

/// <summary>
/// Stores the optional, chronological per-pilot activity stream. Rich domain
/// journals remain authoritative; Activity History keeps concise linked events.
/// </summary>
public sealed class ActivityJournalStore
{
    private const int CurrentSchemaVersion = 4;
    private const ActivityJournalCategory AllCategories =
        ActivityJournalCategory.Navigation |
        ActivityJournalCategory.Missions |
        ActivityJournalCategory.Reputation |
        ActivityJournalCategory.Credits |
        ActivityJournalCategory.Loot |
        ActivityJournalCategory.Combat;

    private readonly string databasePath;
    private readonly string connectionString;
    private readonly Lock sync = new();

    public ActivityJournalStore()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Net7ClientManager");
        this.databasePath = Path.Combine(directory, "activity-journal.db");
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
            var version = ExecuteScalarLong(connection, "PRAGMA user_version;");

            if (version > CurrentSchemaVersion)
            {
                throw new InvalidOperationException(
                    string.Concat(
                        "Activity Journal schema ",
                        version.ToString(CultureInfo.InvariantCulture),
                        " is newer than supported schema ",
                        CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture),
                        "."));
            }

            if (version == 0)
            {
                this.CreateSchema(connection);
                return;
            }

            if (version == 1)
            {
                this.UpgradeFromVersion1(connection);
                version = 2;
            }

            if (version == 2)
            {
                this.UpgradeFromVersion2(connection);
                version = 3;
            }

            if (version == 3)
            {
                this.UpgradeFromVersion3(connection);
            }
        }
    }

    internal long Append(ActivityJournalEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        lock (this.sync)
        {
            using var connection = this.OpenConnection();
            using var transaction = connection.BeginTransaction();
            var eventId = InsertActivity(connection, transaction, entry);
            transaction.Commit();
            return eventId;
        }
    }

    internal long AppendReputation(
        ActivityJournalEntry activity,
        ReputationJournalEntry reputation)
    {
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(reputation);

        lock (this.sync)
        {
            using var connection = this.OpenConnection();
            using var transaction = connection.BeginTransaction();
            var eventId = InsertActivity(connection, transaction, activity);

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO reputation_events (
                    activity_event_id, character_id, occurred_at,
                    faction_key, display_name, previous_reaction,
                    current_reaction, system_name, sector_name,
                    starbase_name, nearest_nav_name, reason,
                    related_mission_episode_id, related_combat_encounter_id)
                VALUES (
                    @activity_event_id, @character_id, @occurred_at,
                    @faction_key, @display_name, @previous_reaction,
                    @current_reaction, @system_name, @sector_name,
                    @starbase_name, @nearest_nav_name, @reason,
                    @related_mission_episode_id, @related_combat_encounter_id);
                """;
            command.Parameters.AddWithValue("@activity_event_id", eventId);
            command.Parameters.AddWithValue(
                "@character_id",
                (long)reputation.CharacterId);
            command.Parameters.AddWithValue(
                "@occurred_at",
                FormatTimestamp(reputation.OccurredAt));
            command.Parameters.AddWithValue("@faction_key", reputation.FactionKey);
            command.Parameters.AddWithValue("@display_name", reputation.DisplayName);
            command.Parameters.AddWithValue(
                "@previous_reaction",
                reputation.PreviousReaction);
            command.Parameters.AddWithValue(
                "@current_reaction",
                reputation.CurrentReaction);
            command.Parameters.AddWithValue("@system_name", reputation.SystemName);
            command.Parameters.AddWithValue("@sector_name", reputation.SectorName);
            command.Parameters.AddWithValue("@starbase_name", reputation.StarbaseName);
            command.Parameters.AddWithValue(
                "@nearest_nav_name",
                reputation.NearestNavName);
            command.Parameters.AddWithValue("@reason", reputation.Reason);
            command.Parameters.AddWithValue(
                "@related_mission_episode_id",
                reputation.RelatedMissionEpisodeId);
            command.Parameters.AddWithValue(
                "@related_combat_encounter_id",
                reputation.RelatedCombatEncounterId);
            command.ExecuteNonQuery();
            transaction.Commit();
            return eventId;
        }
    }

    internal long UpsertLootSession(
        ActivityJournalEntry activity,
        LootJournalSession session)
    {
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(session);

        lock (this.sync)
        {
            using var connection = this.OpenConnection();
            using var transaction = connection.BeginTransaction();
            var eventId = GetLootActivityEventId(
                connection,
                transaction,
                session.SessionId);

            if (eventId == 0)
            {
                eventId = InsertActivity(connection, transaction, activity);

                using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO loot_sessions (
                        session_id, activity_event_id, character_id,
                        pilot_name, started_at, last_updated_at,
                        source_object_id, source_name, credits,
                        system_name, sector_name, starbase_name,
                        nearest_nav_name)
                    VALUES (
                        @session_id, @activity_event_id, @character_id,
                        @pilot_name, @started_at, @last_updated_at,
                        @source_object_id, @source_name, @credits,
                        @system_name, @sector_name, @starbase_name,
                        @nearest_nav_name);
                    """;
                BindLootSession(insert, eventId, session);
                insert.ExecuteNonQuery();
            }
            else
            {
                UpdateActivity(connection, transaction, eventId, activity);

                using var update = connection.CreateCommand();
                update.Transaction = transaction;
                update.CommandText = """
                    UPDATE loot_sessions
                    SET pilot_name = @pilot_name,
                        last_updated_at = @last_updated_at,
                        source_object_id = @source_object_id,
                        source_name = @source_name,
                        credits = @credits,
                        system_name = @system_name,
                        sector_name = @sector_name,
                        starbase_name = @starbase_name,
                        nearest_nav_name = @nearest_nav_name
                    WHERE session_id = @session_id;
                    """;
                BindLootSession(update, eventId, session);
                update.ExecuteNonQuery();
            }

            using (var deleteItems = connection.CreateCommand())
            {
                deleteItems.Transaction = transaction;
                deleteItems.CommandText =
                    "DELETE FROM loot_items WHERE session_id = @session_id;";
                deleteItems.Parameters.AddWithValue(
                    "@session_id",
                    session.SessionId);
                deleteItems.ExecuteNonQuery();
            }

            foreach (var item in session.Items.OrderBy(item => item.Ordinal))
            {
                using var insertItem = connection.CreateCommand();
                insertItem.Transaction = transaction;
                insertItem.CommandText = """
                    INSERT INTO loot_items (
                        session_id, ordinal, item_template_id,
                        item_name, quantity, quality_percent)
                    VALUES (
                        @session_id, @ordinal, @item_template_id,
                        @item_name, @quantity, @quality_percent);
                    """;
                insertItem.Parameters.AddWithValue("@session_id", session.SessionId);
                insertItem.Parameters.AddWithValue("@ordinal", item.Ordinal);
                AddParameter(insertItem, "@item_template_id", item.ItemTemplateId);
                insertItem.Parameters.AddWithValue("@item_name", item.Name);
                insertItem.Parameters.AddWithValue("@quantity", item.Quantity);
                AddParameter(insertItem, "@quality_percent", item.QualityPercent);
                insertItem.ExecuteNonQuery();
            }

            transaction.Commit();
            return eventId;
        }
    }

    public IReadOnlyList<ActivityJournalEntry> GetHistory(
        uint characterId,
        int maximumResults = 5000) =>
        this.GetHistory(characterId, AllCategories, maximumResults);

    public IReadOnlyList<ActivityJournalEntry> GetHistory(
        uint characterId,
        ActivityJournalCategory categories,
        int maximumResults = 5000)
    {
        categories &= AllCategories;

        if (categories == ActivityJournalCategory.None)
        {
            return [];
        }

        maximumResults = Math.Clamp(maximumResults, 1, 10000);

        lock (this.sync)
        {
            using var connection = this.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT event_id, character_id, pilot_name, occurred_at,
                       category, kind, summary, details, system_name,
                       sector_name, starbase_name, nearest_nav_name,
                       related_mission_episode_id, related_combat_encounter_id,
                       related_loot_session_id, payload_version, payload_json
                FROM activity_events
                WHERE character_id = @character_id
                  AND (category & @category_mask) <> 0
                ORDER BY occurred_at DESC, event_id DESC
                LIMIT @maximum_results;
                """;
            command.Parameters.AddWithValue("@character_id", (long)characterId);
            command.Parameters.AddWithValue("@category_mask", (int)categories);
            command.Parameters.AddWithValue("@maximum_results", maximumResults);

            List<ActivityJournalEntry> entries = [];
            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                entries.Add(ReadActivity(reader));
            }

            return entries;
        }
    }

    public IReadOnlyList<ReputationJournalEntry> GetReputationHistory(
        uint characterId,
        string factionKey,
        int maximumResults = 2000)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(factionKey);
        maximumResults = Math.Clamp(maximumResults, 1, 10000);

        lock (this.sync)
        {
            using var connection = this.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT reputation_event_id, activity_event_id, character_id,
                       occurred_at, faction_key, display_name,
                       previous_reaction, current_reaction,
                       system_name, sector_name, starbase_name,
                       nearest_nav_name, reason, related_mission_episode_id,
                       related_combat_encounter_id
                FROM reputation_events
                WHERE character_id = @character_id
                  AND faction_key = @faction_key
                ORDER BY occurred_at DESC, reputation_event_id DESC
                LIMIT @maximum_results;
                """;
            command.Parameters.AddWithValue("@character_id", (long)characterId);
            command.Parameters.AddWithValue("@faction_key", factionKey);
            command.Parameters.AddWithValue("@maximum_results", maximumResults);

            List<ReputationJournalEntry> entries = [];
            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                entries.Add(new ReputationJournalEntry
                {
                    ReputationEventId = reader.GetInt64(0),
                    ActivityEventId = reader.GetInt64(1),
                    CharacterId = checked((uint)reader.GetInt64(2)),
                    OccurredAt = ParseTimestamp(reader.GetString(3)),
                    FactionKey = reader.GetString(4),
                    DisplayName = reader.GetString(5),
                    PreviousReaction = (float)reader.GetDouble(6),
                    CurrentReaction = (float)reader.GetDouble(7),
                    SystemName = reader.GetString(8),
                    SectorName = reader.GetString(9),
                    StarbaseName = reader.GetString(10),
                    NearestNavName = reader.GetString(11),
                    Reason = reader.GetString(12),
                    RelatedMissionEpisodeId = reader.GetString(13),
                    RelatedCombatEncounterId = reader.GetString(14),
                });
            }

            return entries;
        }
    }

    public LootJournalSession? GetLootSession(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        lock (this.sync)
        {
            using var connection = this.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT activity_event_id, character_id, pilot_name,
                       started_at, last_updated_at, source_object_id,
                       source_name, credits, system_name, sector_name,
                       starbase_name, nearest_nav_name
                FROM loot_sessions
                WHERE session_id = @session_id;
                """;
            command.Parameters.AddWithValue("@session_id", sessionId);

            long activityEventId;
            uint characterId;
            string pilotName;
            DateTimeOffset startedAt;
            DateTimeOffset lastUpdatedAt;
            uint sourceObjectId;
            string sourceName;
            long credits;
            string systemName;
            string sectorName;
            string starbaseName;
            string nearestNavName;

            using (var reader = command.ExecuteReader())
            {
                if (!reader.Read())
                {
                    return null;
                }

                activityEventId = reader.GetInt64(0);
                characterId = checked((uint)reader.GetInt64(1));
                pilotName = reader.GetString(2);
                startedAt = ParseTimestamp(reader.GetString(3));
                lastUpdatedAt = ParseTimestamp(reader.GetString(4));
                sourceObjectId = checked((uint)reader.GetInt64(5));
                sourceName = reader.GetString(6);
                credits = reader.GetInt64(7);
                systemName = reader.GetString(8);
                sectorName = reader.GetString(9);
                starbaseName = reader.GetString(10);
                nearestNavName = reader.GetString(11);
            }

            using var itemCommand = connection.CreateCommand();
            itemCommand.CommandText = """
                SELECT ordinal, item_template_id, item_name,
                       quantity, quality_percent
                FROM loot_items
                WHERE session_id = @session_id
                ORDER BY ordinal;
                """;
            itemCommand.Parameters.AddWithValue("@session_id", sessionId);
            List<LootJournalItem> items = [];
            using var itemReader = itemCommand.ExecuteReader();

            while (itemReader.Read())
            {
                items.Add(new LootJournalItem
                {
                    Ordinal = itemReader.GetInt32(0),
                    ItemTemplateId = itemReader.IsDBNull(1)
                        ? null
                        : itemReader.GetInt32(1),
                    Name = itemReader.GetString(2),
                    Quantity = itemReader.GetInt32(3),
                    QualityPercent = itemReader.IsDBNull(4)
                        ? null
                        : (float)itemReader.GetDouble(4),
                });
            }

            return new LootJournalSession
            {
                SessionId = sessionId,
                ActivityEventId = activityEventId,
                CharacterId = characterId,
                PilotName = pilotName,
                StartedAt = startedAt,
                LastUpdatedAt = lastUpdatedAt,
                SourceObjectId = sourceObjectId,
                SourceName = sourceName,
                Credits = credits,
                SystemName = systemName,
                SectorName = sectorName,
                StarbaseName = starbaseName,
                NearestNavName = nearestNavName,
                Items = items,
            };
        }
    }

    public DateTimeOffset? GetLastRecordedAt(uint characterId)
    {
        lock (this.sync)
        {
            using var connection = this.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT MAX(occurred_at)
                FROM activity_events
                WHERE character_id = @character_id;
                """;
            command.Parameters.AddWithValue("@character_id", (long)characterId);
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
            CREATE TABLE activity_events (
                event_id INTEGER PRIMARY KEY AUTOINCREMENT,
                character_id INTEGER NOT NULL,
                pilot_name TEXT NOT NULL,
                occurred_at TEXT NOT NULL,
                category INTEGER NOT NULL,
                kind INTEGER NOT NULL,
                summary TEXT NOT NULL,
                details TEXT NOT NULL,
                system_name TEXT NOT NULL,
                sector_name TEXT NOT NULL,
                starbase_name TEXT NOT NULL,
                nearest_nav_name TEXT NOT NULL,
                related_mission_episode_id TEXT NOT NULL,
                related_combat_encounter_id TEXT NOT NULL,
                related_loot_session_id TEXT NOT NULL,
                payload_version INTEGER NOT NULL,
                payload_json TEXT NOT NULL
            );

            CREATE INDEX ix_activity_events_character_time
            ON activity_events(character_id, occurred_at DESC, event_id DESC);

            CREATE INDEX ix_activity_events_character_category_time
            ON activity_events(character_id, category, occurred_at DESC);

            CREATE TABLE reputation_events (
                reputation_event_id INTEGER PRIMARY KEY AUTOINCREMENT,
                activity_event_id INTEGER NOT NULL UNIQUE,
                character_id INTEGER NOT NULL,
                occurred_at TEXT NOT NULL,
                faction_key TEXT NOT NULL,
                display_name TEXT NOT NULL,
                previous_reaction REAL NOT NULL,
                current_reaction REAL NOT NULL,
                system_name TEXT NOT NULL,
                sector_name TEXT NOT NULL,
                starbase_name TEXT NOT NULL,
                nearest_nav_name TEXT NOT NULL,
                reason TEXT NOT NULL,
                related_mission_episode_id TEXT NOT NULL,
                related_combat_encounter_id TEXT NOT NULL,
                FOREIGN KEY(activity_event_id)
                    REFERENCES activity_events(event_id) ON DELETE CASCADE
            );

            CREATE INDEX ix_reputation_events_character_faction_time
            ON reputation_events(
                character_id, faction_key, occurred_at DESC,
                reputation_event_id DESC);

            CREATE TABLE loot_sessions (
                session_id TEXT PRIMARY KEY,
                activity_event_id INTEGER NOT NULL UNIQUE,
                character_id INTEGER NOT NULL,
                pilot_name TEXT NOT NULL,
                started_at TEXT NOT NULL,
                last_updated_at TEXT NOT NULL,
                source_object_id INTEGER NOT NULL,
                source_name TEXT NOT NULL,
                credits INTEGER NOT NULL,
                system_name TEXT NOT NULL,
                sector_name TEXT NOT NULL,
                starbase_name TEXT NOT NULL,
                nearest_nav_name TEXT NOT NULL,
                FOREIGN KEY(activity_event_id)
                    REFERENCES activity_events(event_id) ON DELETE CASCADE
            );

            CREATE INDEX ix_loot_sessions_character_time
            ON loot_sessions(character_id, started_at DESC);

            CREATE TABLE loot_items (
                session_id TEXT NOT NULL,
                ordinal INTEGER NOT NULL,
                item_template_id INTEGER NULL,
                item_name TEXT NOT NULL,
                quantity INTEGER NOT NULL,
                quality_percent REAL NULL,
                PRIMARY KEY(session_id, ordinal),
                FOREIGN KEY(session_id)
                    REFERENCES loot_sessions(session_id) ON DELETE CASCADE
            );

            PRAGMA user_version = 4;
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private void UpgradeFromVersion1(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE reputation_events (
                reputation_event_id INTEGER PRIMARY KEY AUTOINCREMENT,
                activity_event_id INTEGER NOT NULL UNIQUE,
                character_id INTEGER NOT NULL,
                occurred_at TEXT NOT NULL,
                faction_key TEXT NOT NULL,
                display_name TEXT NOT NULL,
                previous_reaction REAL NOT NULL,
                current_reaction REAL NOT NULL,
                system_name TEXT NOT NULL,
                sector_name TEXT NOT NULL,
                starbase_name TEXT NOT NULL,
                reason TEXT NOT NULL,
                related_mission_episode_id TEXT NOT NULL,
                FOREIGN KEY(activity_event_id)
                    REFERENCES activity_events(event_id) ON DELETE CASCADE
            );

            CREATE INDEX ix_reputation_events_character_faction_time
            ON reputation_events(
                character_id, faction_key, occurred_at DESC,
                reputation_event_id DESC);

            CREATE TABLE loot_sessions (
                session_id TEXT PRIMARY KEY,
                activity_event_id INTEGER NOT NULL UNIQUE,
                character_id INTEGER NOT NULL,
                pilot_name TEXT NOT NULL,
                started_at TEXT NOT NULL,
                last_updated_at TEXT NOT NULL,
                source_object_id INTEGER NOT NULL,
                source_name TEXT NOT NULL,
                credits INTEGER NOT NULL,
                system_name TEXT NOT NULL,
                sector_name TEXT NOT NULL,
                starbase_name TEXT NOT NULL,
                FOREIGN KEY(activity_event_id)
                    REFERENCES activity_events(event_id) ON DELETE CASCADE
            );

            CREATE INDEX ix_loot_sessions_character_time
            ON loot_sessions(character_id, started_at DESC);

            CREATE TABLE loot_items (
                session_id TEXT NOT NULL,
                ordinal INTEGER NOT NULL,
                item_template_id INTEGER NULL,
                item_name TEXT NOT NULL,
                quantity INTEGER NOT NULL,
                quality_percent REAL NULL,
                PRIMARY KEY(session_id, ordinal),
                FOREIGN KEY(session_id)
                    REFERENCES loot_sessions(session_id) ON DELETE CASCADE
            );

            PRAGMA user_version = 2;
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private void UpgradeFromVersion2(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            ALTER TABLE activity_events
                ADD COLUMN nearest_nav_name TEXT NOT NULL DEFAULT '';
            ALTER TABLE reputation_events
                ADD COLUMN nearest_nav_name TEXT NOT NULL DEFAULT '';
            ALTER TABLE loot_sessions
                ADD COLUMN nearest_nav_name TEXT NOT NULL DEFAULT '';
            PRAGMA user_version = 3;
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private void UpgradeFromVersion3(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            ALTER TABLE reputation_events
                ADD COLUMN related_combat_encounter_id TEXT NOT NULL DEFAULT '';
            PRAGMA user_version = 4;
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private static long InsertActivity(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ActivityJournalEntry entry)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO activity_events (
                character_id, pilot_name, occurred_at, category, kind,
                summary, details, system_name, sector_name, starbase_name,
                nearest_nav_name, related_mission_episode_id,
                related_combat_encounter_id,
                related_loot_session_id, payload_version, payload_json)
            VALUES (
                @character_id, @pilot_name, @occurred_at, @category, @kind,
                @summary, @details, @system_name, @sector_name, @starbase_name,
                @nearest_nav_name, @related_mission_episode_id,
                @related_combat_encounter_id,
                @related_loot_session_id, @payload_version, @payload_json);
            SELECT last_insert_rowid();
            """;
        BindActivity(command, entry);
        return Convert.ToInt64(
            command.ExecuteScalar(),
            CultureInfo.InvariantCulture);
    }

    private static void UpdateActivity(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long eventId,
        ActivityJournalEntry entry)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE activity_events
            SET pilot_name = @pilot_name,
                occurred_at = @occurred_at,
                category = @category,
                kind = @kind,
                summary = @summary,
                details = @details,
                system_name = @system_name,
                sector_name = @sector_name,
                starbase_name = @starbase_name,
                nearest_nav_name = @nearest_nav_name,
                related_mission_episode_id = @related_mission_episode_id,
                related_combat_encounter_id = @related_combat_encounter_id,
                related_loot_session_id = @related_loot_session_id,
                payload_version = @payload_version,
                payload_json = @payload_json
            WHERE event_id = @event_id;
            """;
        BindActivity(command, entry);
        command.Parameters.AddWithValue("@event_id", eventId);
        command.ExecuteNonQuery();
    }

    private static void BindActivity(
        SqliteCommand command,
        ActivityJournalEntry entry)
    {
        AddParameter(command, "@character_id", (long)entry.CharacterId);
        AddParameter(command, "@pilot_name", entry.PilotName);
        AddParameter(command, "@occurred_at", FormatTimestamp(entry.OccurredAt));
        AddParameter(command, "@category", (int)entry.Category);
        AddParameter(command, "@kind", (int)entry.Kind);
        AddParameter(command, "@summary", entry.Summary);
        AddParameter(command, "@details", entry.Details);
        AddParameter(command, "@system_name", entry.SystemName);
        AddParameter(command, "@sector_name", entry.SectorName);
        AddParameter(command, "@starbase_name", entry.StarbaseName);
        AddParameter(command, "@nearest_nav_name", entry.NearestNavName);
        AddParameter(
            command,
            "@related_mission_episode_id",
            entry.RelatedMissionEpisodeId);
        AddParameter(
            command,
            "@related_combat_encounter_id",
            entry.RelatedCombatEncounterId);
        AddParameter(
            command,
            "@related_loot_session_id",
            entry.RelatedLootSessionId);
        AddParameter(command, "@payload_version", entry.PayloadVersion);
        AddParameter(command, "@payload_json", entry.PayloadJson);
    }

    private static ActivityJournalEntry ReadActivity(SqliteDataReader reader)
    {
        return new ActivityJournalEntry
        {
            EventId = reader.GetInt64(0),
            CharacterId = checked((uint)reader.GetInt64(1)),
            PilotName = reader.GetString(2),
            OccurredAt = ParseTimestamp(reader.GetString(3)),
            Category = (ActivityJournalCategory)reader.GetInt32(4),
            Kind = (ActivityJournalKind)reader.GetInt32(5),
            Summary = reader.GetString(6),
            Details = reader.GetString(7),
            SystemName = reader.GetString(8),
            SectorName = reader.GetString(9),
            StarbaseName = reader.GetString(10),
            NearestNavName = reader.GetString(11),
            RelatedMissionEpisodeId = reader.GetString(12),
            RelatedCombatEncounterId = reader.GetString(13),
            RelatedLootSessionId = reader.GetString(14),
            PayloadVersion = reader.GetInt32(15),
            PayloadJson = reader.GetString(16),
        };
    }

    private static long GetLootActivityEventId(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sessionId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT activity_event_id
            FROM loot_sessions
            WHERE session_id = @session_id;
            """;
        command.Parameters.AddWithValue("@session_id", sessionId);
        var value = command.ExecuteScalar();
        return value == null || value == DBNull.Value
            ? 0
            : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    private static void BindLootSession(
        SqliteCommand command,
        long eventId,
        LootJournalSession session)
    {
        command.Parameters.AddWithValue("@session_id", session.SessionId);
        command.Parameters.AddWithValue("@activity_event_id", eventId);
        command.Parameters.AddWithValue("@character_id", (long)session.CharacterId);
        command.Parameters.AddWithValue("@pilot_name", session.PilotName);
        command.Parameters.AddWithValue(
            "@started_at",
            FormatTimestamp(session.StartedAt));
        command.Parameters.AddWithValue(
            "@last_updated_at",
            FormatTimestamp(session.LastUpdatedAt));
        command.Parameters.AddWithValue(
            "@source_object_id",
            (long)session.SourceObjectId);
        command.Parameters.AddWithValue("@source_name", session.SourceName);
        command.Parameters.AddWithValue("@credits", session.Credits);
        command.Parameters.AddWithValue("@system_name", session.SystemName);
        command.Parameters.AddWithValue("@sector_name", session.SectorName);
        command.Parameters.AddWithValue("@starbase_name", session.StarbaseName);
        command.Parameters.AddWithValue(
            "@nearest_nav_name",
            session.NearestNavName);
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

    private static void AddParameter(
        SqliteCommand command,
        string name,
        object? value)
    {
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }

    private static string FormatTimestamp(DateTimeOffset timestamp) =>
        timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);
}
