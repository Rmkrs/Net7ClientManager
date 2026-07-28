namespace Net7ClientManager.PilotArchive;

using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Net7ClientManager.Addons.Contracts;
using Net7ClientManager.Observations.Models;

public sealed class PilotArchiveStore
{
    private const int CurrentSchemaVersion = 3;

    private readonly string databasePath;
    private readonly string connectionString;
    private readonly Lock sync = new();

    public PilotArchiveStore()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Net7ClientManager");
        this.databasePath = Path.Combine(directory, "pilot-archive.db");
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
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Pilot Archive schema {version} is newer than supported schema {CurrentSchemaVersion}."));
            }

            if (version == 0)
            {
                this.CreateSchema(connection);
            }
            else
            {
                if (version == 1)
                {
                    this.MigrateToVersion2(connection);
                    version = 2;
                }

                if (version == 2)
                {
                    this.MigrateToVersion3(connection);
                }
            }
        }
    }

    internal bool Apply(
        PilotArchiveCapture capture,
        ISet<string> refreshUnchangedSections)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(refreshUnchangedSections);

        lock (this.sync)
        {
            using var connection = this.OpenConnection();
            using var transaction = connection.BeginTransaction();
            var timestamp = FormatTimestamp(capture.ObservedAt);

            var pilotInserted = false;

            using (var pilotCommand = connection.CreateCommand())
            {
                pilotCommand.Transaction = transaction;
                pilotCommand.CommandText = """
                    INSERT INTO pilots (
                        character_id, name, first_observed_at, last_observed_at)
                    VALUES (@character_id, @name, @observed_at, @observed_at)
                    ON CONFLICT(character_id) DO NOTHING;
                    """;
                pilotCommand.Parameters.AddWithValue("@character_id", (long)capture.CharacterId);
                pilotCommand.Parameters.AddWithValue("@name", capture.Name);
                pilotCommand.Parameters.AddWithValue("@observed_at", timestamp);
                pilotInserted = pilotCommand.ExecuteNonQuery() != 0;
            }

            var changed = pilotInserted;
            var touched = pilotInserted;

            foreach (var section in capture.Sections)
            {
                var previousFingerprint = this.GetSectionFingerprint(
                    connection,
                    transaction,
                    capture.CharacterId,
                    section.Section);
                var contentChanged = !string.Equals(
                    previousFingerprint,
                    section.Fingerprint,
                    StringComparison.Ordinal);
                var refreshTimestamp =
                    contentChanged ||
                    refreshUnchangedSections.Contains(section.Section);

                if (!contentChanged && !refreshTimestamp)
                {
                    continue;
                }

                if (contentChanged)
                {
                    this.ReplaceSection(
                        connection,
                        transaction,
                        capture.CharacterId,
                        capture.Name,
                        section);
                    changed = true;
                }

                this.UpsertSectionMetadata(
                    connection,
                    transaction,
                    capture.CharacterId,
                    section.Section,
                    section.Fingerprint,
                    timestamp);
                touched = true;
            }

            if (touched)
            {
                using var pilotCommand = connection.CreateCommand();
                pilotCommand.Transaction = transaction;
                pilotCommand.CommandText = """
                    UPDATE pilots SET
                        name = @name,
                        last_observed_at = @observed_at
                    WHERE character_id = @character_id;
                    """;
                pilotCommand.Parameters.AddWithValue("@name", capture.Name);
                pilotCommand.Parameters.AddWithValue("@observed_at", timestamp);
                pilotCommand.Parameters.AddWithValue("@character_id", (long)capture.CharacterId);
                pilotCommand.ExecuteNonQuery();
            }

            transaction.Commit();
            return changed;
        }
    }

    public IReadOnlyList<PilotArchivePilotSnapshot> GetPilots()
    {
        lock (this.sync)
        {
            using var connection = this.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT character_id, name, race, profession, profession_code,
                       affiliation, guild_name, guild_rank,
                       combat_level, explore_level, trade_level, overall_level,
                       available_skill_points, hull_tier,
                       current_system, current_sector, current_starbase,
                       registration_starbase, registration_sector, credits,
                       first_observed_at, last_observed_at
                FROM pilots
                ORDER BY name COLLATE NOCASE, character_id;
                """;

            using var reader = command.ExecuteReader();
            List<PilotArchivePilotSnapshot> result = [];

            while (reader.Read())
            {
                result.Add(ReadPilot(reader));
            }

            return result;
        }
    }

    public PilotArchivePilotDetails? GetPilot(uint characterId)
    {
        lock (this.sync)
        {
            using var connection = this.OpenConnection();
            var pilot = this.GetPilot(connection, characterId);

            if (pilot == null)
            {
                return null;
            }

            return new PilotArchivePilotDetails
            {
                Pilot = pilot,
                SectionObservedAt = this.ReadSectionTimes(connection, characterId),
                CargoSlots = this.ReadInventorySlots(connection, characterId, "cargo"),
                EquipmentSlots = this.ReadInventorySlots(connection, characterId, "equipped"),
                AmmoSlots = this.ReadInventorySlots(connection, characterId, "ammo"),
                VaultSlots = this.ReadInventorySlots(connection, characterId, "secure"),
                Skills = this.ReadSkills(connection, characterId),
                Missions = this.ReadMissions(connection, characterId),
                Reputations = this.ReadReputations(connection, characterId),
            };
        }
    }

    public IReadOnlyList<PilotArchiveSearchResult> Search(
        string query,
        int maximumResults = 500)
    {
        query = query?.Trim() ?? "";

        if (query.Length == 0)
        {
            return [];
        }

        lock (this.sync)
        {
            using var connection = this.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT s.character_id, p.name, s.category, s.result_text,
                       s.target_section, s.target_entity_key
                FROM search_entries AS s
                INNER JOIN pilots AS p ON p.character_id = s.character_id
                WHERE instr(s.search_text, @query) > 0
                ORDER BY p.name COLLATE NOCASE,
                         s.category COLLATE NOCASE,
                         s.result_text COLLATE NOCASE
                LIMIT @maximum_results;
                """;
            command.Parameters.AddWithValue(
                "@query",
                NormalizeSearchText(query));
            command.Parameters.AddWithValue(
                "@maximum_results",
                Math.Clamp(maximumResults, 1, 5000));

            using var reader = command.ExecuteReader();
            List<PilotArchiveSearchResult> result = [];

            while (reader.Read())
            {
                result.Add(new PilotArchiveSearchResult
                {
                    CharacterId = checked((uint)reader.GetInt64(0)),
                    PilotName = reader.GetString(1),
                    Category = reader.GetString(2),
                    Result = reader.GetString(3),
                    TargetSection = reader.GetString(4),
                    TargetEntityKey = reader.GetString(5),
                });
            }

            return result;
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
            CREATE TABLE pilots (
                character_id INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                race TEXT NULL,
                profession TEXT NULL,
                profession_code TEXT NULL,
                affiliation TEXT NULL,
                guild_name TEXT NULL,
                guild_rank TEXT NULL,
                combat_level INTEGER NULL,
                explore_level INTEGER NULL,
                trade_level INTEGER NULL,
                overall_level INTEGER NULL,
                available_skill_points INTEGER NULL,
                hull_tier INTEGER NULL,
                current_system TEXT NULL,
                current_sector TEXT NULL,
                current_starbase TEXT NULL,
                registration_starbase TEXT NULL,
                registration_sector TEXT NULL,
                credits INTEGER NULL,
                first_observed_at TEXT NOT NULL,
                last_observed_at TEXT NOT NULL
            );

            CREATE TABLE pilot_sections (
                character_id INTEGER NOT NULL,
                section TEXT NOT NULL,
                fingerprint TEXT NOT NULL,
                last_observed_at TEXT NOT NULL,
                PRIMARY KEY(character_id, section),
                FOREIGN KEY(character_id) REFERENCES pilots(character_id)
                    ON DELETE CASCADE
            );

            CREATE TABLE inventory_slots (
                character_id INTEGER NOT NULL,
                container TEXT NOT NULL,
                slot INTEGER NOT NULL,
                state TEXT NOT NULL,
                is_usable INTEGER NOT NULL,
                template_id INTEGER NULL,
                item_name TEXT NULL,
                stack_count INTEGER NULL,
                quality_percent REAL NULL,
                structure_percent REAL NULL,
                average_cost REAL NULL,
                builder_name TEXT NULL,
                instance_info TEXT NULL,
                activated_effect_info TEXT NULL,
                equip_effect_info TEXT NULL,
                mount_bone_name TEXT NULL,
                price_low INTEGER NULL,
                price_high INTEGER NULL,
                equipment_kind TEXT NULL,
                equipment_ordinal INTEGER NULL,
                PRIMARY KEY(character_id, container, slot),
                FOREIGN KEY(character_id) REFERENCES pilots(character_id)
                    ON DELETE CASCADE
            );

            CREATE TABLE skills (
                character_id INTEGER NOT NULL,
                skill_index INTEGER NOT NULL,
                name TEXT NOT NULL,
                category TEXT NOT NULL,
                is_active INTEGER NOT NULL,
                current_rank INTEGER NOT NULL,
                maximum_rank INTEGER NOT NULL,
                quest_only_levels INTEGER NOT NULL,
                spent_skill_points INTEGER NOT NULL,
                PRIMARY KEY(character_id, skill_index),
                FOREIGN KEY(character_id) REFERENCES pilots(character_id)
                    ON DELETE CASCADE
            );

            CREATE TABLE missions (
                character_id INTEGER NOT NULL,
                slot INTEGER NOT NULL,
                raw_id INTEGER NULL,
                name TEXT NOT NULL,
                summary TEXT NOT NULL,
                reward TEXT NOT NULL,
                failure_consequence TEXT NOT NULL,
                issuing_faction TEXT NOT NULL,
                stage INTEGER NULL,
                stage_count INTEGER NULL,
                is_timed INTEGER NULL,
                is_forfeitable INTEGER NULL,
                is_complete INTEGER NULL,
                is_failed INTEGER NULL,
                is_expired INTEGER NULL,
                current_stage_text TEXT NOT NULL,
                stages_json TEXT NOT NULL,
                PRIMARY KEY(character_id, slot),
                FOREIGN KEY(character_id) REFERENCES pilots(character_id)
                    ON DELETE CASCADE
            );

            CREATE TABLE reputations (
                character_id INTEGER NOT NULL,
                slot INTEGER NOT NULL,
                faction_key TEXT NOT NULL,
                display_name TEXT NOT NULL,
                description TEXT NOT NULL,
                reaction REAL NULL,
                normalized_reaction REAL NULL,
                display_order INTEGER NULL,
                PRIMARY KEY(character_id, slot),
                FOREIGN KEY(character_id) REFERENCES pilots(character_id)
                    ON DELETE CASCADE
            );

            CREATE TABLE search_entries (
                character_id INTEGER NOT NULL,
                category TEXT NOT NULL,
                entity_key TEXT NOT NULL,
                result_text TEXT NOT NULL,
                search_text TEXT NOT NULL,
                target_section TEXT NOT NULL,
                target_entity_key TEXT NOT NULL,
                PRIMARY KEY(character_id, category, entity_key),
                FOREIGN KEY(character_id) REFERENCES pilots(character_id)
                    ON DELETE CASCADE
            );

            CREATE INDEX ix_search_entries_text
                ON search_entries(search_text);
            CREATE INDEX ix_inventory_slots_item_name
                ON inventory_slots(item_name);

            CREATE TABLE IF NOT EXISTS item_templates (
                template_id INTEGER PRIMARY KEY,
                fingerprint TEXT NOT NULL,
                payload_json TEXT NOT NULL,
                last_observed_at TEXT NOT NULL
            );

            PRAGMA user_version = 3;
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private void MigrateToVersion2(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS item_templates (
                template_id INTEGER PRIMARY KEY,
                fingerprint TEXT NOT NULL,
                payload_json TEXT NOT NULL,
                last_observed_at TEXT NOT NULL
            );

            PRAGMA user_version = 2;
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private void MigrateToVersion3(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            ALTER TABLE pilots
                ADD COLUMN available_skill_points INTEGER NULL;

            ALTER TABLE pilots
                ADD COLUMN hull_tier INTEGER NULL;

            PRAGMA user_version = 3;
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    internal bool UpsertItemTemplates(
        IEnumerable<ClientRuntimeItemTemplateObservation> templates,
        DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(templates);

        var distinct = templates
            .Where(template =>
                template.IsAvailable &&
                template.ItemTemplateId > 0)
            .GroupBy(template => template.ItemTemplateId)
            .Select(group => group.First())
            .ToArray();

        if (distinct.Length == 0)
        {
            return false;
        }

        lock (this.sync)
        {
            using var connection = this.OpenConnection();
            using var transaction = connection.BeginTransaction();
            var changed = false;
            var timestamp = FormatTimestamp(observedAt);

            foreach (var template in distinct)
            {
                var payload = JsonSerializer.Serialize(template);
                var fingerprint = Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(
                        System.Text.Encoding.UTF8.GetBytes(payload)));

                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO item_templates (
                        template_id, fingerprint, payload_json, last_observed_at)
                    VALUES (
                        @template_id, @fingerprint, @payload_json, @observed_at)
                    ON CONFLICT(template_id) DO UPDATE SET
                        fingerprint = excluded.fingerprint,
                        payload_json = excluded.payload_json,
                        last_observed_at = excluded.last_observed_at
                    WHERE item_templates.fingerprint <> excluded.fingerprint;
                    """;
                command.Parameters.AddWithValue(
                    "@template_id",
                    template.ItemTemplateId);
                command.Parameters.AddWithValue(
                    "@fingerprint",
                    fingerprint);
                command.Parameters.AddWithValue(
                    "@payload_json",
                    payload);
                command.Parameters.AddWithValue(
                    "@observed_at",
                    timestamp);
                changed |= command.ExecuteNonQuery() != 0;
            }

            transaction.Commit();
            return changed;
        }
    }

    public ClientRuntimeItemTemplateObservation? GetItemTemplate(
        int itemTemplateId)
    {
        if (itemTemplateId <= 0)
        {
            return null;
        }

        lock (this.sync)
        {
            using var connection = this.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT payload_json
                FROM item_templates
                WHERE template_id = @template_id;
                """;
            command.Parameters.AddWithValue(
                "@template_id",
                itemTemplateId);

            return command.ExecuteScalar() is string payload
                ? JsonSerializer.Deserialize<ClientRuntimeItemTemplateObservation>(
                    payload)
                : null;
        }
    }

    private string? GetSectionFingerprint(
        SqliteConnection connection,
        SqliteTransaction transaction,
        uint characterId,
        string section)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT fingerprint
            FROM pilot_sections
            WHERE character_id = @character_id AND section = @section;
            """;
        command.Parameters.AddWithValue("@character_id", (long)characterId);
        command.Parameters.AddWithValue("@section", section);
        return command.ExecuteScalar() as string;
    }

    private void UpsertSectionMetadata(
        SqliteConnection connection,
        SqliteTransaction transaction,
        uint characterId,
        string section,
        string fingerprint,
        string observedAt)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO pilot_sections (
                character_id, section, fingerprint, last_observed_at)
            VALUES (@character_id, @section, @fingerprint, @observed_at)
            ON CONFLICT(character_id, section) DO UPDATE SET
                fingerprint = excluded.fingerprint,
                last_observed_at = excluded.last_observed_at;
            """;
        command.Parameters.AddWithValue("@character_id", (long)characterId);
        command.Parameters.AddWithValue("@section", section);
        command.Parameters.AddWithValue("@fingerprint", fingerprint);
        command.Parameters.AddWithValue("@observed_at", observedAt);
        command.ExecuteNonQuery();
    }

    private void ReplaceSection(
        SqliteConnection connection,
        SqliteTransaction transaction,
        uint characterId,
        string pilotName,
        PilotArchiveSectionCapture section)
    {
        switch (section.Section)
        {
            case PilotArchiveSections.Overview:
                this.ReplaceOverview(
                    connection,
                    transaction,
                    characterId,
                    pilotName,
                    (PilotArchiveOverviewCapture)section.Value);
                break;

            case PilotArchiveSections.Location:
                this.ReplaceLocation(
                    connection,
                    transaction,
                    characterId,
                    (PilotArchiveLocationCapture)section.Value);
                break;

            case PilotArchiveSections.Progression:
                this.ReplaceProgression(
                    connection,
                    transaction,
                    characterId,
                    (PilotArchiveProgressionCapture)section.Value);
                break;

            case PilotArchiveSections.Skills:
                this.ReplaceSkills(
                    connection,
                    transaction,
                    characterId,
                    (IReadOnlyList<PilotArchiveSkill>)section.Value);
                break;

            case PilotArchiveSections.Cargo:
                this.ReplaceInventory(
                    connection,
                    transaction,
                    characterId,
                    "cargo",
                    "Cargo",
                    PilotArchiveSections.Cargo,
                    (IReadOnlyList<AddonInventorySlotSnapshot>)section.Value);
                break;

            case PilotArchiveSections.Equipment:
                var equipment = (PilotArchiveEquipmentCapture)section.Value;
                this.ReplaceInventory(
                    connection,
                    transaction,
                    characterId,
                    "equipped",
                    "Equipment",
                    PilotArchiveSections.Equipment,
                    equipment.EquipmentSlots);
                this.ReplaceInventory(
                    connection,
                    transaction,
                    characterId,
                    "ammo",
                    "Equipped ammo",
                    PilotArchiveSections.Equipment,
                    equipment.AmmoSlots,
                    clearSectionSearch: false);
                break;

            case PilotArchiveSections.Vault:
                this.ReplaceInventory(
                    connection,
                    transaction,
                    characterId,
                    "secure",
                    "Vault",
                    PilotArchiveSections.Vault,
                    (IReadOnlyList<AddonInventorySlotSnapshot>)section.Value);
                break;

            case PilotArchiveSections.Missions:
                this.ReplaceMissions(
                    connection,
                    transaction,
                    characterId,
                    (IReadOnlyList<PilotArchiveMission>)section.Value);
                break;

            case PilotArchiveSections.Reputations:
                this.ReplaceReputations(
                    connection,
                    transaction,
                    characterId,
                    (IReadOnlyList<PilotArchiveReputation>)section.Value);
                break;
        }
    }

    private void ReplaceOverview(
        SqliteConnection connection,
        SqliteTransaction transaction,
        uint characterId,
        string pilotName,
        PilotArchiveOverviewCapture value)
    {
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE pilots SET
                    race = @race,
                    profession = @profession,
                    profession_code = @profession_code,
                    affiliation = @affiliation,
                    guild_name = @guild_name,
                    guild_rank = @guild_rank
                WHERE character_id = @character_id;
                """;
            AddParameter(command, "@race", value.Race);
            AddParameter(command, "@profession", value.Profession);
            AddParameter(command, "@profession_code", value.ProfessionCode);
            AddParameter(command, "@affiliation", value.Affiliation);
            AddParameter(command, "@guild_name", value.GuildName);
            AddParameter(command, "@guild_rank", value.GuildRank);
            command.Parameters.AddWithValue("@character_id", (long)characterId);
            command.ExecuteNonQuery();
        }

        this.DeleteSearchForSection(
            connection,
            transaction,
            characterId,
            PilotArchiveSections.Overview);

        var descriptor = string.Join(
            " · ",
            new[] { value.Race, value.Profession, value.Affiliation }
                .Where(item => !string.IsNullOrWhiteSpace(item)));
        this.InsertSearch(
            connection,
            transaction,
            characterId,
            "Character",
            "character",
            string.IsNullOrWhiteSpace(descriptor)
                ? pilotName
                : descriptor,
            string.Join(" ", pilotName, descriptor, value.ProfessionCode),
            PilotArchiveSections.Overview,
            "character");

        if (!string.IsNullOrWhiteSpace(value.GuildName))
        {
            this.InsertSearch(
                connection,
                transaction,
                characterId,
                "Guild",
                "guild",
                string.Join(" · ",
                    new[] { value.GuildName, value.GuildRank }
                        .Where(item => !string.IsNullOrWhiteSpace(item))),
                string.Join(" ", value.GuildName, value.GuildRank),
                PilotArchiveSections.Overview,
                "guild");
        }
    }

    private void ReplaceLocation(
        SqliteConnection connection,
        SqliteTransaction transaction,
        uint characterId,
        PilotArchiveLocationCapture value)
    {
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE pilots SET
                    current_system = @current_system,
                    current_sector = @current_sector,
                    current_starbase = @current_starbase,
                    registration_starbase = @registration_starbase,
                    registration_sector = @registration_sector
                WHERE character_id = @character_id;
                """;
            AddParameter(command, "@current_system", value.CurrentSystem);
            AddParameter(command, "@current_sector", value.CurrentSector);
            AddParameter(command, "@current_starbase", value.CurrentStarbase);
            AddParameter(command, "@registration_starbase", value.RegistrationStarbase);
            AddParameter(command, "@registration_sector", value.RegistrationSector);
            command.Parameters.AddWithValue("@character_id", (long)characterId);
            command.ExecuteNonQuery();
        }

        this.DeleteSearchForSection(
            connection,
            transaction,
            characterId,
            PilotArchiveSections.Location);

        var current = string.Join(
            " · ",
            new[] { value.CurrentSystem, value.CurrentSector, value.CurrentStarbase }
                .Where(item => !string.IsNullOrWhiteSpace(item)));
        this.InsertSearch(
            connection,
            transaction,
            characterId,
            "Location",
            "current",
            current,
            current,
            PilotArchiveSections.Location,
            "current");

        var registered = string.Join(
            " · ",
            new[] { value.RegistrationSector, value.RegistrationStarbase }
                .Where(item => !string.IsNullOrWhiteSpace(item)));

        if (!string.IsNullOrWhiteSpace(registered))
        {
            this.InsertSearch(
                connection,
                transaction,
                characterId,
                "Registration",
                "registration",
                registered,
                registered,
                PilotArchiveSections.Location,
                "registration");
        }
    }

    private void ReplaceProgression(
        SqliteConnection connection,
        SqliteTransaction transaction,
        uint characterId,
        PilotArchiveProgressionCapture value)
    {
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE pilots SET
                    combat_level = @combat_level,
                    explore_level = @explore_level,
                    trade_level = @trade_level,
                    overall_level = @overall_level,
                    available_skill_points = @available_skill_points,
                    hull_tier = @hull_tier,
                    credits = @credits
                WHERE character_id = @character_id;
                """;
            AddParameter(command, "@combat_level", value.CombatLevel);
            AddParameter(command, "@explore_level", value.ExploreLevel);
            AddParameter(command, "@trade_level", value.TradeLevel);
            AddParameter(command, "@overall_level", value.OverallLevel);
            AddParameter(
                command,
                "@available_skill_points",
                value.AvailableSkillPoints);
            AddParameter(command, "@hull_tier", value.HullTier);
            AddParameter(
                command,
                "@credits",
                value.Credits.HasValue
                    ? checked((long)Math.Min(value.Credits.Value, (ulong)long.MaxValue))
                    : null);
            command.Parameters.AddWithValue("@character_id", (long)characterId);
            command.ExecuteNonQuery();
        }

        this.DeleteSearchForSection(
            connection,
            transaction,
            characterId,
            PilotArchiveSections.Progression);

        var levels = string.Join(
            " · ",
            string.Concat(
                "Overall ",
                FormatSearchValue(value.OverallLevel)),
            string.Concat(
                "Combat ",
                FormatSearchValue(value.CombatLevel)),
            string.Concat(
                "Explore ",
                FormatSearchValue(value.ExploreLevel)),
            string.Concat(
                "Trade ",
                FormatSearchValue(value.TradeLevel)));
        var credits = value.Credits.HasValue
            ? value.Credits.Value.ToString("N0", CultureInfo.InvariantCulture)
            : "unknown";

        this.InsertSearch(
            connection,
            transaction,
            characterId,
            "Progression",
            "levels",
            levels,
            string.Create(
                CultureInfo.InvariantCulture,
                $"{levels} level levels combat explore trade"),
            PilotArchiveSections.Progression,
            "levels");
        this.InsertSearch(
            connection,
            transaction,
            characterId,
            "Credits",
            "credits",
            string.Create(
                CultureInfo.InvariantCulture,
                $"{credits} credits"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"{credits} credit credits currency"),
            PilotArchiveSections.Progression,
            "credits");
    }

    private void ReplaceSkills(
        SqliteConnection connection,
        SqliteTransaction transaction,
        uint characterId,
        IReadOnlyList<PilotArchiveSkill> skills)
    {
        this.DeleteRows(connection, transaction, "skills", characterId);
        this.DeleteSearchForSection(
            connection,
            transaction,
            characterId,
            PilotArchiveSections.Skills);

        foreach (var skill in skills)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO skills (
                    character_id, skill_index, name, category, is_active,
                    current_rank, maximum_rank, quest_only_levels,
                    spent_skill_points)
                VALUES (
                    @character_id, @skill_index, @name, @category, @is_active,
                    @current_rank, @maximum_rank, @quest_only_levels,
                    @spent_skill_points);
                """;
            command.Parameters.AddWithValue("@character_id", (long)characterId);
            command.Parameters.AddWithValue("@skill_index", skill.Index);
            command.Parameters.AddWithValue("@name", skill.Name);
            command.Parameters.AddWithValue("@category", skill.Category);
            command.Parameters.AddWithValue("@is_active", skill.IsActive ? 1 : 0);
            command.Parameters.AddWithValue("@current_rank", skill.CurrentRank);
            command.Parameters.AddWithValue("@maximum_rank", skill.MaximumRank);
            command.Parameters.AddWithValue("@quest_only_levels", skill.QuestOnlyLevels);
            command.Parameters.AddWithValue("@spent_skill_points", skill.SpentSkillPoints);
            command.ExecuteNonQuery();

            if (skill.CurrentRank == 0 &&
                skill.MaximumRank == 0)
            {
                continue;
            }

            this.InsertSearch(
                connection,
                transaction,
                characterId,
                "Skill",
                skill.Index.ToString(CultureInfo.InvariantCulture),
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{skill.Name} {skill.CurrentRank}/{skill.MaximumRank}"),
                string.Join(" ", skill.Name, skill.Category),
                PilotArchiveSections.Skills,
                skill.Index.ToString(CultureInfo.InvariantCulture));
        }
    }

    private void ReplaceInventory(
        SqliteConnection connection,
        SqliteTransaction transaction,
        uint characterId,
        string container,
        string category,
        string targetSection,
        IReadOnlyList<AddonInventorySlotSnapshot> slots,
        bool clearSectionSearch = true)
    {
        using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = """
                DELETE FROM inventory_slots
                WHERE character_id = @character_id AND container = @container;
                """;
            delete.Parameters.AddWithValue("@character_id", (long)characterId);
            delete.Parameters.AddWithValue("@container", container);
            delete.ExecuteNonQuery();
        }

        if (clearSectionSearch)
        {
            this.DeleteSearchForSection(
                connection,
                transaction,
                characterId,
                targetSection);
        }

        foreach (var slot in slots)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO inventory_slots (
                    character_id, container, slot, state, is_usable,
                    template_id, item_name, stack_count, quality_percent,
                    structure_percent, average_cost, builder_name,
                    instance_info, activated_effect_info, equip_effect_info,
                    mount_bone_name, price_low, price_high,
                    equipment_kind, equipment_ordinal)
                VALUES (
                    @character_id, @container, @slot, @state, @is_usable,
                    @template_id, @item_name, @stack_count, @quality_percent,
                    @structure_percent, @average_cost, @builder_name,
                    @instance_info, @activated_effect_info, @equip_effect_info,
                    @mount_bone_name, @price_low, @price_high,
                    @equipment_kind, @equipment_ordinal);
                """;
            command.Parameters.AddWithValue("@character_id", (long)characterId);
            command.Parameters.AddWithValue("@container", container);
            command.Parameters.AddWithValue("@slot", slot.Slot);
            command.Parameters.AddWithValue("@state", slot.State);
            command.Parameters.AddWithValue("@is_usable", slot.IsUsable ? 1 : 0);
            AddParameter(command, "@template_id", slot.TemplateId);
            AddParameter(command, "@item_name", slot.Name);
            AddParameter(command, "@stack_count", slot.StackCount);
            AddParameter(command, "@quality_percent", slot.QualityPercent);
            AddParameter(command, "@structure_percent", slot.StructurePercent);
            AddParameter(command, "@average_cost", slot.AverageCost);
            AddParameter(command, "@builder_name", slot.BuilderName);
            AddParameter(command, "@instance_info", slot.InstanceInfo);
            AddParameter(command, "@activated_effect_info", slot.ActivatedEffectInfo);
            AddParameter(command, "@equip_effect_info", slot.EquipEffectInfo);
            AddParameter(command, "@mount_bone_name", slot.MountBoneName);
            AddParameter(command, "@price_low", slot.PriceLow);
            AddParameter(command, "@price_high", slot.PriceHigh);
            AddParameter(command, "@equipment_kind", slot.EquipmentKind);
            AddParameter(command, "@equipment_ordinal", slot.EquipmentOrdinal);
            command.ExecuteNonQuery();

            if (!slot.IsOccupied || string.IsNullOrWhiteSpace(slot.Name))
            {
                continue;
            }

            var count = slot.StackCount.GetValueOrDefault(1);
            var result = count > 1
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"{count}× {slot.Name} · slot {slot.Slot + 1}")
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"{slot.Name} · slot {slot.Slot + 1}");
            var entityKey = string.Create(
                CultureInfo.InvariantCulture,
                $"{container}:{slot.Slot}");

            this.InsertSearch(
                connection,
                transaction,
                characterId,
                category,
                entityKey,
                result,
                string.Join(
                    " ",
                    category,
                    container,
                    slot.Name,
                    slot.BuilderName,
                    slot.InstanceInfo,
                    slot.EquipmentKind,
                    slot.TemplateId?.ToString(CultureInfo.InvariantCulture),
                    count.ToString(CultureInfo.InvariantCulture)),
                targetSection,
                entityKey);
        }
    }

    private void ReplaceMissions(
        SqliteConnection connection,
        SqliteTransaction transaction,
        uint characterId,
        IReadOnlyList<PilotArchiveMission> missions)
    {
        this.DeleteRows(connection, transaction, "missions", characterId);
        this.DeleteSearchForSection(
            connection,
            transaction,
            characterId,
            PilotArchiveSections.Missions);

        foreach (var mission in missions)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO missions (
                    character_id, slot, raw_id, name, summary, reward,
                    failure_consequence, issuing_faction, stage, stage_count,
                    is_timed, is_forfeitable, is_complete, is_failed,
                    is_expired, current_stage_text, stages_json)
                VALUES (
                    @character_id, @slot, @raw_id, @name, @summary, @reward,
                    @failure_consequence, @issuing_faction, @stage, @stage_count,
                    @is_timed, @is_forfeitable, @is_complete, @is_failed,
                    @is_expired, @current_stage_text, @stages_json);
                """;
            command.Parameters.AddWithValue("@character_id", (long)characterId);
            command.Parameters.AddWithValue("@slot", mission.Slot);
            AddParameter(command, "@raw_id", mission.RawId);
            command.Parameters.AddWithValue("@name", mission.Name);
            command.Parameters.AddWithValue("@summary", mission.Summary);
            command.Parameters.AddWithValue("@reward", mission.Reward);
            command.Parameters.AddWithValue("@failure_consequence", mission.FailureConsequence);
            command.Parameters.AddWithValue("@issuing_faction", mission.IssuingFaction);
            AddParameter(command, "@stage", mission.Stage);
            AddParameter(command, "@stage_count", mission.StageCount);
            AddParameter(command, "@is_timed", ToDatabaseBoolean(mission.IsTimed));
            AddParameter(command, "@is_forfeitable", ToDatabaseBoolean(mission.IsForfeitable));
            AddParameter(command, "@is_complete", ToDatabaseBoolean(mission.IsComplete));
            AddParameter(command, "@is_failed", ToDatabaseBoolean(mission.IsFailed));
            AddParameter(command, "@is_expired", ToDatabaseBoolean(mission.IsExpired));
            command.Parameters.AddWithValue("@current_stage_text", mission.CurrentStageText);
            command.Parameters.AddWithValue("@stages_json", JsonSerializer.Serialize(mission.Stages));
            command.ExecuteNonQuery();

            var stageText = mission.Stage.HasValue && mission.StageCount.HasValue
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"stage {mission.Stage}/{mission.StageCount}")
                : "active";
            this.InsertSearch(
                connection,
                transaction,
                characterId,
                "Mission",
                mission.Slot.ToString(CultureInfo.InvariantCulture),
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{mission.Name} · {stageText}"),
                string.Join(
                    " ",
                    mission.Name,
                    mission.Summary,
                    mission.CurrentStageText,
                    string.Join(" ", mission.Stages),
                    mission.IssuingFaction,
                    mission.Reward),
                PilotArchiveSections.Missions,
                mission.Slot.ToString(CultureInfo.InvariantCulture));
        }
    }

    private void ReplaceReputations(
        SqliteConnection connection,
        SqliteTransaction transaction,
        uint characterId,
        IReadOnlyList<PilotArchiveReputation> reputations)
    {
        this.DeleteRows(connection, transaction, "reputations", characterId);
        this.DeleteSearchForSection(
            connection,
            transaction,
            characterId,
            PilotArchiveSections.Reputations);

        foreach (var reputation in reputations)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO reputations (
                    character_id, slot, faction_key, display_name, description,
                    reaction, normalized_reaction, display_order)
                VALUES (
                    @character_id, @slot, @faction_key, @display_name, @description,
                    @reaction, @normalized_reaction, @display_order);
                """;
            command.Parameters.AddWithValue("@character_id", (long)characterId);
            command.Parameters.AddWithValue("@slot", reputation.Slot);
            command.Parameters.AddWithValue("@faction_key", reputation.FactionKey);
            command.Parameters.AddWithValue("@display_name", reputation.DisplayName);
            command.Parameters.AddWithValue("@description", reputation.Description);
            AddParameter(command, "@reaction", reputation.Reaction);
            AddParameter(command, "@normalized_reaction", reputation.NormalizedReaction);
            AddParameter(command, "@display_order", reputation.Order);
            command.ExecuteNonQuery();

            var value = reputation.Reaction.HasValue
                ? reputation.Reaction.Value.ToString("0", CultureInfo.InvariantCulture)
                : "unknown";
            this.InsertSearch(
                connection,
                transaction,
                characterId,
                "Reputation",
                reputation.Slot.ToString(CultureInfo.InvariantCulture),
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{reputation.DisplayName}: {value}"),
                string.Join(
                    " ",
                    reputation.DisplayName,
                    reputation.FactionKey,
                    reputation.Description,
                    value),
                PilotArchiveSections.Reputations,
                reputation.Slot.ToString(CultureInfo.InvariantCulture));
        }
    }

    private void DeleteRows(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        uint characterId)
    {
        var commandText = table switch
        {
            "skills" =>
                "DELETE FROM skills WHERE character_id = @character_id;",
            "missions" =>
                "DELETE FROM missions WHERE character_id = @character_id;",
            "reputations" =>
                "DELETE FROM reputations WHERE character_id = @character_id;",
            _ => throw new ArgumentOutOfRangeException(
                nameof(table),
                table,
                "Unsupported Pilot Archive table."),
        };

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        command.Parameters.AddWithValue("@character_id", (long)characterId);
        command.ExecuteNonQuery();
    }

    private void DeleteSearchForSection(
        SqliteConnection connection,
        SqliteTransaction transaction,
        uint characterId,
        string targetSection)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM search_entries
            WHERE character_id = @character_id
              AND target_section = @target_section;
            """;
        command.Parameters.AddWithValue("@character_id", (long)characterId);
        command.Parameters.AddWithValue("@target_section", targetSection);
        command.ExecuteNonQuery();
    }

    private void InsertSearch(
        SqliteConnection connection,
        SqliteTransaction transaction,
        uint characterId,
        string category,
        string entityKey,
        string resultText,
        string searchText,
        string targetSection,
        string targetEntityKey)
    {
        if (string.IsNullOrWhiteSpace(resultText))
        {
            return;
        }

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO search_entries (
                character_id, category, entity_key, result_text, search_text,
                target_section, target_entity_key)
            VALUES (
                @character_id, @category, @entity_key, @result_text, @search_text,
                @target_section, @target_entity_key);
            """;
        command.Parameters.AddWithValue("@character_id", (long)characterId);
        command.Parameters.AddWithValue("@category", category);
        command.Parameters.AddWithValue("@entity_key", entityKey);
        command.Parameters.AddWithValue("@result_text", resultText);
        command.Parameters.AddWithValue("@search_text", NormalizeSearchText(searchText));
        command.Parameters.AddWithValue("@target_section", targetSection);
        command.Parameters.AddWithValue("@target_entity_key", targetEntityKey);
        command.ExecuteNonQuery();
    }

    private PilotArchivePilotSnapshot? GetPilot(
        SqliteConnection connection,
        uint characterId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT character_id, name, race, profession, profession_code,
                   affiliation, guild_name, guild_rank,
                   combat_level, explore_level, trade_level, overall_level,
                   available_skill_points, hull_tier,
                   current_system, current_sector, current_starbase,
                   registration_starbase, registration_sector, credits,
                   first_observed_at, last_observed_at
            FROM pilots
            WHERE character_id = @character_id;
            """;
        command.Parameters.AddWithValue("@character_id", (long)characterId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadPilot(reader) : null;
    }

    private IReadOnlyDictionary<string, DateTimeOffset> ReadSectionTimes(
        SqliteConnection connection,
        uint characterId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT section, last_observed_at
            FROM pilot_sections
            WHERE character_id = @character_id;
            """;
        command.Parameters.AddWithValue("@character_id", (long)characterId);
        using var reader = command.ExecuteReader();
        Dictionary<string, DateTimeOffset> result = new(StringComparer.Ordinal);

        while (reader.Read())
        {
            result[reader.GetString(0)] = ParseTimestamp(reader.GetString(1));
        }

        return result;
    }

    private IReadOnlyList<AddonInventorySlotSnapshot> ReadInventorySlots(
        SqliteConnection connection,
        uint characterId,
        string container)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT slot, state, is_usable, template_id, item_name, stack_count,
                   quality_percent, structure_percent, average_cost,
                   builder_name, instance_info, activated_effect_info,
                   equip_effect_info, mount_bone_name, price_low, price_high,
                   equipment_kind, equipment_ordinal
            FROM inventory_slots
            WHERE character_id = @character_id AND container = @container
            ORDER BY slot;
            """;
        command.Parameters.AddWithValue("@character_id", (long)characterId);
        command.Parameters.AddWithValue("@container", container);
        using var reader = command.ExecuteReader();
        List<AddonInventorySlotSnapshot> result = [];

        while (reader.Read())
        {
            result.Add(new AddonInventorySlotSnapshot
            {
                Collection = container,
                Slot = reader.GetInt32(0),
                State = reader.GetString(1),
                IsUsable = reader.GetInt32(2) != 0,
                TemplateId = GetNullableInt32(reader, 3),
                Name = GetNullableString(reader, 4),
                StackCount = GetNullableInt32(reader, 5),
                QualityPercent = GetNullableFloat(reader, 6),
                StructurePercent = GetNullableFloat(reader, 7),
                AverageCost = GetNullableFloat(reader, 8),
                BuilderName = GetNullableString(reader, 9),
                InstanceInfo = GetNullableString(reader, 10),
                ActivatedEffectInfo = GetNullableString(reader, 11),
                EquipEffectInfo = GetNullableString(reader, 12),
                MountBoneName = GetNullableString(reader, 13),
                PriceLow = GetNullableUInt32(reader, 14),
                PriceHigh = GetNullableUInt32(reader, 15),
                EquipmentKind = GetNullableString(reader, 16),
                EquipmentOrdinal = GetNullableInt32(reader, 17),
            });
        }

        return result;
    }

    private IReadOnlyList<PilotArchiveSkill> ReadSkills(
        SqliteConnection connection,
        uint characterId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT skill_index, name, category, is_active, current_rank,
                   maximum_rank, quest_only_levels, spent_skill_points
            FROM skills
            WHERE character_id = @character_id
            ORDER BY category COLLATE NOCASE, name COLLATE NOCASE;
            """;
        command.Parameters.AddWithValue("@character_id", (long)characterId);
        using var reader = command.ExecuteReader();
        List<PilotArchiveSkill> result = [];

        while (reader.Read())
        {
            result.Add(new PilotArchiveSkill
            {
                Index = reader.GetInt32(0),
                Name = reader.GetString(1),
                Category = reader.GetString(2),
                IsActive = reader.GetInt32(3) != 0,
                CurrentRank = reader.GetInt32(4),
                MaximumRank = reader.GetInt32(5),
                QuestOnlyLevels = reader.GetInt32(6),
                SpentSkillPoints = reader.GetInt32(7),
            });
        }

        return result;
    }

    private IReadOnlyList<PilotArchiveMission> ReadMissions(
        SqliteConnection connection,
        uint characterId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT slot, raw_id, name, summary, reward, failure_consequence,
                   issuing_faction, stage, stage_count, is_timed,
                   is_forfeitable, is_complete, is_failed, is_expired,
                   current_stage_text, stages_json
            FROM missions
            WHERE character_id = @character_id
            ORDER BY slot;
            """;
        command.Parameters.AddWithValue("@character_id", (long)characterId);
        using var reader = command.ExecuteReader();
        List<PilotArchiveMission> result = [];

        while (reader.Read())
        {
            result.Add(new PilotArchiveMission
            {
                Slot = reader.GetInt32(0),
                RawId = GetNullableInt32(reader, 1),
                Name = reader.GetString(2),
                Summary = reader.GetString(3),
                Reward = reader.GetString(4),
                FailureConsequence = reader.GetString(5),
                IssuingFaction = reader.GetString(6),
                Stage = GetNullableInt32(reader, 7),
                StageCount = GetNullableInt32(reader, 8),
                IsTimed = GetNullableBoolean(reader, 9),
                IsForfeitable = GetNullableBoolean(reader, 10),
                IsComplete = GetNullableBoolean(reader, 11),
                IsFailed = GetNullableBoolean(reader, 12),
                IsExpired = GetNullableBoolean(reader, 13),
                CurrentStageText = reader.GetString(14),
                Stages = JsonSerializer.Deserialize<string[]>(reader.GetString(15)) ?? [],
            });
        }

        return result;
    }

    private IReadOnlyList<PilotArchiveReputation> ReadReputations(
        SqliteConnection connection,
        uint characterId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT slot, faction_key, display_name, description, reaction,
                   normalized_reaction, display_order
            FROM reputations
            WHERE character_id = @character_id
            ORDER BY COALESCE(display_order, 2147483647), display_name COLLATE NOCASE;
            """;
        command.Parameters.AddWithValue("@character_id", (long)characterId);
        using var reader = command.ExecuteReader();
        List<PilotArchiveReputation> result = [];

        while (reader.Read())
        {
            result.Add(new PilotArchiveReputation
            {
                Slot = reader.GetInt32(0),
                FactionKey = reader.GetString(1),
                DisplayName = reader.GetString(2),
                Description = reader.GetString(3),
                Reaction = GetNullableFloat(reader, 4),
                NormalizedReaction = GetNullableFloat(reader, 5),
                Order = GetNullableInt32(reader, 6),
            });
        }

        return result;
    }

    private static PilotArchivePilotSnapshot ReadPilot(SqliteDataReader reader)
    {
        return new PilotArchivePilotSnapshot
        {
            CharacterId = checked((uint)reader.GetInt64(0)),
            Name = reader.GetString(1),
            Race = GetNullableString(reader, 2),
            Profession = GetNullableString(reader, 3),
            ProfessionCode = GetNullableString(reader, 4),
            Affiliation = GetNullableString(reader, 5),
            GuildName = GetNullableString(reader, 6),
            GuildRank = GetNullableString(reader, 7),
            CombatLevel = GetNullableInt32(reader, 8),
            ExploreLevel = GetNullableInt32(reader, 9),
            TradeLevel = GetNullableInt32(reader, 10),
            OverallLevel = GetNullableInt32(reader, 11),
            AvailableSkillPoints = GetNullableInt32(reader, 12),
            HullTier = GetNullableInt32(reader, 13),
            CurrentSystem = GetNullableString(reader, 14),
            CurrentSector = GetNullableString(reader, 15),
            CurrentStarbase = GetNullableString(reader, 16),
            RegistrationStarbase = GetNullableString(reader, 17),
            RegistrationSector = GetNullableString(reader, 18),
            Credits = reader.IsDBNull(19)
                ? null
                : checked((ulong)Math.Max(0L, reader.GetInt64(19))),
            FirstObservedAt = ParseTimestamp(reader.GetString(20)),
            LastObservedAt = ParseTimestamp(reader.GetString(21)),
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
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static string FormatSearchValue(int? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? "unknown";

    private static string FormatTimestamp(DateTimeOffset timestamp) =>
        timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);

    private static string NormalizeSearchText(string value) =>
        value.Trim().ToUpperInvariant();

    private static int? GetNullableInt32(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);

    private static uint? GetNullableUInt32(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : checked((uint)reader.GetInt64(ordinal));

    private static float? GetNullableFloat(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : (float)reader.GetDouble(ordinal);

    private static string? GetNullableString(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static bool? GetNullableBoolean(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal) != 0;

    private static int? ToDatabaseBoolean(bool? value) =>
        value.HasValue ? value.Value ? 1 : 0 : null;
}
