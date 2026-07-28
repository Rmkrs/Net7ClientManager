namespace Net7ClientManager.CombatJournal;

using System.Globalization;
using Microsoft.Data.Sqlite;

/// <summary>
/// Stores optional per-pilot combat encounters and packet-level damage events.
/// The host owns this journal so addon installation and addon storage never
/// determine whether combat history exists.
/// </summary>
public sealed class CombatJournalStore
{
    private const int CurrentSchemaVersion = 1;

    private readonly string databasePath;
    private readonly string connectionString;
    private readonly Lock sync = new();

    public CombatJournalStore()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Net7ClientManager");
        this.databasePath = Path.Combine(directory, "combat-journal.db");
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
                        "Combat Journal schema ",
                        version.ToString(CultureInfo.InvariantCulture),
                        " is newer than supported schema ",
                        CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture),
                        "."));
            }

            if (version == 0)
            {
                this.CreateSchema(connection);
            }

            using var interrupt = connection.CreateCommand();
            interrupt.CommandText = """
                UPDATE combat_encounters
                SET outcome = @interrupted,
                    ended_at = COALESCE(ended_at, last_event_at)
                WHERE outcome = @active;
                """;
            interrupt.Parameters.AddWithValue(
                "@interrupted",
                (int)CombatJournalOutcome.Interrupted);
            interrupt.Parameters.AddWithValue(
                "@active",
                (int)CombatJournalOutcome.Active);
            interrupt.ExecuteNonQuery();
        }
    }

    internal void SaveEncounter(
        CombatJournalEncounter encounter,
        CombatJournalEvent? combatEvent = null)
    {
        ArgumentNullException.ThrowIfNull(encounter);
        this.SaveBatch(
            [encounter],
            combatEvent == null ? [] : [combatEvent]);
    }

    internal void SaveBatch(
        IReadOnlyList<CombatJournalEncounter> encounters,
        IReadOnlyList<CombatJournalEvent> combatEvents)
    {
        ArgumentNullException.ThrowIfNull(encounters);
        ArgumentNullException.ThrowIfNull(combatEvents);

        if (encounters.Count == 0 && combatEvents.Count == 0)
        {
            return;
        }

        lock (this.sync)
        {
            using var connection = this.OpenConnection();
            using var transaction = connection.BeginTransaction();

            foreach (var encounter in encounters)
            {
                UpsertEncounter(connection, transaction, encounter);
            }

            foreach (var combatEvent in combatEvents)
            {
                InsertEvent(connection, transaction, combatEvent);
            }

            transaction.Commit();
        }
    }

    public IReadOnlyList<CombatJournalEncounter> GetHistory(
        uint characterId,
        int maximumResults = 1000)
    {
        maximumResults = Math.Clamp(maximumResults, 1, 5000);

        lock (this.sync)
        {
            using var connection = this.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT encounter_id, character_id, pilot_name,
                       target_object_id, target_name, target_combat_level,
                       started_at, last_event_at, ended_at, outcome,
                       system_name, sector_name, starbase_name,
                       nearest_nav_name, outgoing_damage, incoming_damage,
                       outgoing_hit_count, incoming_hit_count,
                       outgoing_critical_count, incoming_critical_count,
                       largest_outgoing_hit, largest_incoming_hit
                FROM combat_encounters
                WHERE character_id = @character_id
                ORDER BY CASE WHEN outcome = @active THEN 0 ELSE 1 END,
                         started_at DESC, encounter_id DESC
                LIMIT @maximum_results;
                """;
            command.Parameters.AddWithValue(
                "@character_id",
                (long)characterId);
            command.Parameters.AddWithValue(
                "@active",
                (int)CombatJournalOutcome.Active);
            command.Parameters.AddWithValue(
                "@maximum_results",
                maximumResults);
            using var reader = command.ExecuteReader();
            List<CombatJournalEncounter> encounters = [];

            while (reader.Read())
            {
                encounters.Add(ReadEncounter(reader));
            }

            return encounters;
        }
    }

    public CombatJournalEncounter? GetEncounter(string encounterId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(encounterId);

        lock (this.sync)
        {
            using var connection = this.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT encounter_id, character_id, pilot_name,
                       target_object_id, target_name, target_combat_level,
                       started_at, last_event_at, ended_at, outcome,
                       system_name, sector_name, starbase_name,
                       nearest_nav_name, outgoing_damage, incoming_damage,
                       outgoing_hit_count, incoming_hit_count,
                       outgoing_critical_count, incoming_critical_count,
                       largest_outgoing_hit, largest_incoming_hit
                FROM combat_encounters
                WHERE encounter_id = @encounter_id;
                """;
            command.Parameters.AddWithValue("@encounter_id", encounterId);
            using var reader = command.ExecuteReader();

            if (!reader.Read())
            {
                return null;
            }

            var encounter = ReadEncounter(reader);
            reader.Close();
            return this.AttachEvents(connection, [encounter])[0];
        }
    }

    public DateTimeOffset? GetLastRecordedAt(uint characterId)
    {
        lock (this.sync)
        {
            using var connection = this.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT MAX(COALESCE(ended_at, last_event_at))
                FROM combat_encounters
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

    private IReadOnlyList<CombatJournalEncounter> AttachEvents(
        SqliteConnection connection,
        IReadOnlyList<CombatJournalEncounter> encounters)
    {
        if (encounters.Count == 0)
        {
            return encounters;
        }

        var byId = encounters.ToDictionary(
            encounter => encounter.EncounterId,
            _ => new List<CombatJournalEvent>(),
            StringComparer.Ordinal);

        const int maximumIdsPerQuery = 500;

        for (var offset = 0; offset < encounters.Count; offset += maximumIdsPerQuery)
        {
            var count = Math.Min(maximumIdsPerQuery, encounters.Count - offset);
            using var command = connection.CreateCommand();
            var parameters = new List<string>(count);

            for (var index = 0; index < count; index++)
            {
                var parameter = string.Concat(
                    "@id",
                    index.ToString(CultureInfo.InvariantCulture));
                parameters.Add(parameter);
                command.Parameters.AddWithValue(
                    parameter,
                    encounters[offset + index].EncounterId);
            }

            command.CommandText = string.Concat(
                "SELECT event_id, encounter_id, sequence, occurred_at, direction, ",
                "damage, unmodified_damage, modifier, damage_type, is_critical, ",
                "source_object_id, source_name, victim_object_id, victim_name ",
                "FROM combat_events WHERE encounter_id IN (",
                string.Join(",", parameters),
                ") ORDER BY occurred_at, sequence, event_id;");
            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                var combatEvent = ReadEvent(reader);

                if (byId.TryGetValue(combatEvent.EncounterId, out var events))
                {
                    events.Add(combatEvent);
                }
            }
        }

        return encounters.Select(encounter => encounter with
        {
            Events = byId[encounter.EncounterId],
        }).ToArray();
    }

    private void CreateSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA foreign_keys = ON;

            CREATE TABLE combat_encounters (
                encounter_id TEXT PRIMARY KEY,
                character_id INTEGER NOT NULL,
                pilot_name TEXT NOT NULL,
                target_object_id INTEGER NOT NULL,
                target_name TEXT NOT NULL,
                target_combat_level INTEGER NULL,
                started_at TEXT NOT NULL,
                last_event_at TEXT NOT NULL,
                ended_at TEXT NULL,
                outcome INTEGER NOT NULL,
                system_name TEXT NOT NULL,
                sector_name TEXT NOT NULL,
                starbase_name TEXT NOT NULL,
                nearest_nav_name TEXT NOT NULL,
                outgoing_damage REAL NOT NULL,
                incoming_damage REAL NOT NULL,
                outgoing_hit_count INTEGER NOT NULL,
                incoming_hit_count INTEGER NOT NULL,
                outgoing_critical_count INTEGER NOT NULL,
                incoming_critical_count INTEGER NOT NULL,
                largest_outgoing_hit REAL NOT NULL,
                largest_incoming_hit REAL NOT NULL
            );

            CREATE INDEX ix_combat_encounters_character_started
                ON combat_encounters(character_id, started_at DESC);

            CREATE TABLE combat_events (
                event_id INTEGER PRIMARY KEY AUTOINCREMENT,
                encounter_id TEXT NOT NULL,
                sequence INTEGER NOT NULL,
                occurred_at TEXT NOT NULL,
                direction INTEGER NOT NULL,
                damage REAL NOT NULL,
                unmodified_damage REAL NOT NULL,
                modifier REAL NOT NULL,
                damage_type TEXT NOT NULL,
                is_critical INTEGER NOT NULL,
                source_object_id INTEGER NOT NULL,
                source_name TEXT NOT NULL,
                victim_object_id INTEGER NOT NULL,
                victim_name TEXT NOT NULL,
                FOREIGN KEY(encounter_id)
                    REFERENCES combat_encounters(encounter_id)
                    ON DELETE CASCADE,
                UNIQUE(encounter_id, sequence)
            );

            CREATE INDEX ix_combat_events_encounter_time
                ON combat_events(encounter_id, occurred_at, sequence);

            PRAGMA user_version = 1;
            """;
        command.ExecuteNonQuery();
    }

    private static void UpsertEncounter(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CombatJournalEncounter encounter)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO combat_encounters (
                encounter_id, character_id, pilot_name,
                target_object_id, target_name, target_combat_level,
                started_at, last_event_at, ended_at, outcome,
                system_name, sector_name, starbase_name,
                nearest_nav_name, outgoing_damage, incoming_damage,
                outgoing_hit_count, incoming_hit_count,
                outgoing_critical_count, incoming_critical_count,
                largest_outgoing_hit, largest_incoming_hit)
            VALUES (
                @encounter_id, @character_id, @pilot_name,
                @target_object_id, @target_name, @target_combat_level,
                @started_at, @last_event_at, @ended_at, @outcome,
                @system_name, @sector_name, @starbase_name,
                @nearest_nav_name, @outgoing_damage, @incoming_damage,
                @outgoing_hit_count, @incoming_hit_count,
                @outgoing_critical_count, @incoming_critical_count,
                @largest_outgoing_hit, @largest_incoming_hit)
            ON CONFLICT(encounter_id) DO UPDATE SET
                pilot_name = excluded.pilot_name,
                target_name = excluded.target_name,
                target_combat_level = COALESCE(
                    excluded.target_combat_level,
                    combat_encounters.target_combat_level),
                last_event_at = excluded.last_event_at,
                ended_at = excluded.ended_at,
                outcome = excluded.outcome,
                system_name = excluded.system_name,
                sector_name = excluded.sector_name,
                starbase_name = excluded.starbase_name,
                nearest_nav_name = excluded.nearest_nav_name,
                outgoing_damage = excluded.outgoing_damage,
                incoming_damage = excluded.incoming_damage,
                outgoing_hit_count = excluded.outgoing_hit_count,
                incoming_hit_count = excluded.incoming_hit_count,
                outgoing_critical_count = excluded.outgoing_critical_count,
                incoming_critical_count = excluded.incoming_critical_count,
                largest_outgoing_hit = excluded.largest_outgoing_hit,
                largest_incoming_hit = excluded.largest_incoming_hit;
            """;
        AddEncounterParameters(command, encounter);
        command.ExecuteNonQuery();
    }

    private static void InsertEvent(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CombatJournalEvent combatEvent)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT OR IGNORE INTO combat_events (
                encounter_id, sequence, occurred_at, direction,
                damage, unmodified_damage, modifier, damage_type,
                is_critical, source_object_id, source_name,
                victim_object_id, victim_name)
            VALUES (
                @encounter_id, @sequence, @occurred_at, @direction,
                @damage, @unmodified_damage, @modifier, @damage_type,
                @is_critical, @source_object_id, @source_name,
                @victim_object_id, @victim_name);
            """;
        command.Parameters.AddWithValue(
            "@encounter_id",
            combatEvent.EncounterId);
        command.Parameters.AddWithValue("@sequence", combatEvent.Sequence);
        command.Parameters.AddWithValue(
            "@occurred_at",
            FormatTimestamp(combatEvent.OccurredAt));
        command.Parameters.AddWithValue(
            "@direction",
            (int)combatEvent.Direction);
        command.Parameters.AddWithValue("@damage", combatEvent.Damage);
        command.Parameters.AddWithValue(
            "@unmodified_damage",
            combatEvent.UnmodifiedDamage);
        command.Parameters.AddWithValue("@modifier", combatEvent.Modifier);
        command.Parameters.AddWithValue("@damage_type", combatEvent.DamageType);
        command.Parameters.AddWithValue(
            "@is_critical",
            combatEvent.IsCritical ? 1 : 0);
        command.Parameters.AddWithValue(
            "@source_object_id",
            (long)combatEvent.SourceObjectId);
        command.Parameters.AddWithValue("@source_name", combatEvent.SourceName);
        command.Parameters.AddWithValue(
            "@victim_object_id",
            (long)combatEvent.VictimObjectId);
        command.Parameters.AddWithValue("@victim_name", combatEvent.VictimName);
        command.ExecuteNonQuery();
    }

    private static void AddEncounterParameters(
        SqliteCommand command,
        CombatJournalEncounter encounter)
    {
        command.Parameters.AddWithValue("@encounter_id", encounter.EncounterId);
        command.Parameters.AddWithValue(
            "@character_id",
            (long)encounter.CharacterId);
        command.Parameters.AddWithValue("@pilot_name", encounter.PilotName);
        command.Parameters.AddWithValue(
            "@target_object_id",
            (long)encounter.TargetObjectId);
        command.Parameters.AddWithValue("@target_name", encounter.TargetName);
        command.Parameters.AddWithValue(
            "@target_combat_level",
            encounter.TargetCombatLevel.HasValue
                ? encounter.TargetCombatLevel.Value
                : DBNull.Value);
        command.Parameters.AddWithValue(
            "@started_at",
            FormatTimestamp(encounter.StartedAt));
        command.Parameters.AddWithValue(
            "@last_event_at",
            FormatTimestamp(encounter.LastEventAt));
        command.Parameters.AddWithValue(
            "@ended_at",
            encounter.EndedAt.HasValue
                ? FormatTimestamp(encounter.EndedAt.Value)
                : DBNull.Value);
        command.Parameters.AddWithValue("@outcome", (int)encounter.Outcome);
        command.Parameters.AddWithValue("@system_name", encounter.SystemName);
        command.Parameters.AddWithValue("@sector_name", encounter.SectorName);
        command.Parameters.AddWithValue("@starbase_name", encounter.StarbaseName);
        command.Parameters.AddWithValue(
            "@nearest_nav_name",
            encounter.NearestNavName);
        command.Parameters.AddWithValue(
            "@outgoing_damage",
            encounter.OutgoingDamage);
        command.Parameters.AddWithValue(
            "@incoming_damage",
            encounter.IncomingDamage);
        command.Parameters.AddWithValue(
            "@outgoing_hit_count",
            encounter.OutgoingHitCount);
        command.Parameters.AddWithValue(
            "@incoming_hit_count",
            encounter.IncomingHitCount);
        command.Parameters.AddWithValue(
            "@outgoing_critical_count",
            encounter.OutgoingCriticalCount);
        command.Parameters.AddWithValue(
            "@incoming_critical_count",
            encounter.IncomingCriticalCount);
        command.Parameters.AddWithValue(
            "@largest_outgoing_hit",
            encounter.LargestOutgoingHit);
        command.Parameters.AddWithValue(
            "@largest_incoming_hit",
            encounter.LargestIncomingHit);
    }

    private static CombatJournalEncounter ReadEncounter(SqliteDataReader reader)
    {
        return new CombatJournalEncounter
        {
            EncounterId = reader.GetString(0),
            CharacterId = checked((uint)reader.GetInt64(1)),
            PilotName = reader.GetString(2),
            TargetObjectId = checked((uint)reader.GetInt64(3)),
            TargetName = reader.GetString(4),
            TargetCombatLevel = reader.IsDBNull(5) ? null : reader.GetInt32(5),
            StartedAt = ParseTimestamp(reader.GetString(6)),
            LastEventAt = ParseTimestamp(reader.GetString(7)),
            EndedAt = reader.IsDBNull(8)
                ? null
                : ParseTimestamp(reader.GetString(8)),
            Outcome = (CombatJournalOutcome)reader.GetInt32(9),
            SystemName = reader.GetString(10),
            SectorName = reader.GetString(11),
            StarbaseName = reader.GetString(12),
            NearestNavName = reader.GetString(13),
            OutgoingDamage = reader.GetDouble(14),
            IncomingDamage = reader.GetDouble(15),
            OutgoingHitCount = reader.GetInt32(16),
            IncomingHitCount = reader.GetInt32(17),
            OutgoingCriticalCount = reader.GetInt32(18),
            IncomingCriticalCount = reader.GetInt32(19),
            LargestOutgoingHit = reader.GetFloat(20),
            LargestIncomingHit = reader.GetFloat(21),
        };
    }

    private static CombatJournalEvent ReadEvent(SqliteDataReader reader)
    {
        return new CombatJournalEvent
        {
            EventId = reader.GetInt64(0),
            EncounterId = reader.GetString(1),
            Sequence = reader.GetInt64(2),
            OccurredAt = ParseTimestamp(reader.GetString(3)),
            Direction = (CombatJournalDirection)reader.GetInt32(4),
            Damage = reader.GetFloat(5),
            UnmodifiedDamage = reader.GetFloat(6),
            Modifier = reader.GetFloat(7),
            DamageType = reader.GetString(8),
            IsCritical = reader.GetInt32(9) != 0,
            SourceObjectId = checked((uint)reader.GetInt64(10)),
            SourceName = reader.GetString(11),
            VictimObjectId = checked((uint)reader.GetInt64(12)),
            VictimName = reader.GetString(13),
        };
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(this.connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA foreign_keys = ON;
            PRAGMA busy_timeout = 5000;
            """;
        command.ExecuteNonQuery();
        return connection;
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
