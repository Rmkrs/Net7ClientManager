namespace Net7ClientManager.SkillPlanning;

using System.Globalization;
using Microsoft.Data.Sqlite;

/// <summary>
/// Persists mutable local equipment-centred builds, exact immutable Forge
/// downloads, publication lineage, and the active build for each character.
/// </summary>
internal sealed class SkillBuildLocalStore
{
    private const int CurrentSchemaVersion = 6;

    private readonly string databasePath;
    private readonly string connectionString;
    private readonly Lock sync = new();

    public SkillBuildLocalStore()
        : this(
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ApplicationData),
                "Net7ClientManager",
                "skill-builds.db"))
    {
    }

    internal SkillBuildLocalStore(string databasePath)
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
            Directory.CreateDirectory(Path.GetDirectoryName(this.databasePath)!);
            using var connection = this.OpenConnection();
            var version = ExecuteScalarLong(connection, "PRAGMA user_version;");

            if (version > CurrentSchemaVersion)
            {
                throw new InvalidOperationException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Build schema {version} is newer than supported schema {CurrentSchemaVersion}."));
            }

            if (version == 0)
            {
                this.CreateSchema(connection);
            }
            else if (version is 1 or 2)
            {
                ResetUnreleasedBuildSchemas(connection);
                this.CreateSchema(connection);
            }
            else if (version == 3)
            {
                MigrateVersion3To4(connection);
                MigrateVersion4To5(connection);
                MigrateVersion5To6(connection);
            }
            else if (version == 4)
            {
                MigrateVersion4To5(connection);
                MigrateVersion5To6(connection);
            }
            else if (version == 5)
            {
                MigrateVersion5To6(connection);
            }
        }
    }

    public IReadOnlyList<SkillBuildDocument> GetBuildsForCharacter(
        uint characterId,
        int professionIndex)
    {
        if (characterId == 0)
        {
            return [];
        }

        lock (this.sync)
        {
            using var connection = this.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT lb.document_json
                FROM character_build_library cbl
                JOIN local_builds lb ON lb.build_id = cbl.build_id
                WHERE cbl.character_id = @character_id
                  AND lb.profession_index = @profession_index
                ORDER BY lb.title COLLATE NOCASE, lb.updated_at_utc DESC;
                """;
            command.Parameters.AddWithValue(
                "@character_id",
                (long)characterId);
            command.Parameters.AddWithValue(
                "@profession_index",
                professionIndex);

            List<SkillBuildDocument> builds = [];
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (SkillBuildDocumentCodec.TryDeserializeBuild(
                        reader.GetString(0),
                        out var build,
                        out _))
                {
                    builds.Add(build);
                }
            }

            return builds;
        }
    }

    public SkillBuildDocument? GetBuild(string buildId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(buildId);

        lock (this.sync)
        {
            using var connection = this.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT document_json
                FROM local_builds
                WHERE build_id = @build_id;
                """;
            command.Parameters.AddWithValue("@build_id", buildId.Trim());
            var json = command.ExecuteScalar() as string;

            return json != null &&
                   SkillBuildDocumentCodec.TryDeserializeBuild(
                       json,
                       out var build,
                       out _)
                ? build
                : null;
        }
    }

    public void SaveBuild(
        uint characterId,
        SkillBuildDocument build,
        DateTimeOffset updatedAtUtc)
    {
        if (characterId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(characterId));
        }
        ArgumentNullException.ThrowIfNull(build);
        ArgumentException.ThrowIfNullOrWhiteSpace(build.BuildId);

        var buildId = build.BuildId.Trim();
        var json = SkillBuildDocumentCodec.SerializeBuild(build);

        lock (this.sync)
        {
            using var connection = this.OpenConnection();
            using var transaction = connection.BeginTransaction();
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO local_builds (
                        build_id, profession_index, title,
                        updated_at_utc, document_json)
                    VALUES (
                        @build_id, @profession_index, @title,
                        @updated_at_utc, @document_json)
                    ON CONFLICT(build_id) DO UPDATE SET
                        profession_index = excluded.profession_index,
                        title = excluded.title,
                        updated_at_utc = excluded.updated_at_utc,
                        document_json = excluded.document_json;
                    """;
                command.Parameters.AddWithValue("@build_id", buildId);
                command.Parameters.AddWithValue(
                    "@profession_index",
                    build.ProfessionIndex);
                command.Parameters.AddWithValue("@title", build.Title.Trim());
                command.Parameters.AddWithValue(
                    "@updated_at_utc",
                    FormatTimestamp(updatedAtUtc));
                command.Parameters.AddWithValue("@document_json", json);
                command.ExecuteNonQuery();
            }

            AddBuildToCharacter(
                connection,
                transaction,
                characterId,
                buildId,
                updatedAtUtc);
            transaction.Commit();
        }
    }

    public SkillBuildForgeLink? GetForgeLink(string localBuildId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localBuildId);

        lock (this.sync)
        {
            using var connection = this.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                    local_build_id,
                    forge_build_id,
                    version,
                    content_sha256,
                    publisher_pilot_name,
                    is_publisher_source,
                    latest_known_version,
                    star_count,
                    is_starred_by_me,
                    is_owned_by_me,
                    updated_at_utc
                FROM forge_build_links
                WHERE local_build_id = @local_build_id;
                """;
            command.Parameters.AddWithValue(
                "@local_build_id",
                localBuildId.Trim());
            using var reader = command.ExecuteReader();
            return reader.Read()
                ? ReadForgeLink(reader)
                : null;
        }
    }

    public IReadOnlyDictionary<string, SkillBuildForgeLink> GetForgeLinks(
        IEnumerable<string> localBuildIds)
    {
        ArgumentNullException.ThrowIfNull(localBuildIds);
        var ids = localBuildIds
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<string, SkillBuildForgeLink>(
                StringComparer.OrdinalIgnoreCase);
        }

        lock (this.sync)
        {
            using var connection = this.OpenConnection();
            using var command = connection.CreateCommand();
            var parameters = ids.Select((_, index) => $"@id{index}").ToArray();
            command.CommandText = string.Concat(
                """
                SELECT
                    local_build_id,
                    forge_build_id,
                    version,
                    content_sha256,
                    publisher_pilot_name,
                    is_publisher_source,
                    latest_known_version,
                    star_count,
                    is_starred_by_me,
                    is_owned_by_me,
                    updated_at_utc
                FROM forge_build_links
                WHERE local_build_id IN (
                """,
                string.Join(", ", parameters),
                ");");
            for (var index = 0; index < ids.Length; index++)
            {
                command.Parameters.AddWithValue(parameters[index], ids[index]);
            }

            Dictionary<string, SkillBuildForgeLink> result =
                new(StringComparer.OrdinalIgnoreCase);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var link = ReadForgeLink(reader);
                result[link.LocalBuildId] = link;
            }
            return result;
        }
    }

    public void SaveForgeLink(SkillBuildForgeLink link)
    {
        ArgumentNullException.ThrowIfNull(link);
        ArgumentException.ThrowIfNullOrWhiteSpace(link.LocalBuildId);
        ArgumentException.ThrowIfNullOrWhiteSpace(link.ForgeBuildId);

        lock (this.sync)
        {
            using var connection = this.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO forge_build_links
                (
                    local_build_id,
                    forge_build_id,
                    version,
                    content_sha256,
                    publisher_pilot_name,
                    is_publisher_source,
                    latest_known_version,
                    star_count,
                    is_starred_by_me,
                    is_owned_by_me,
                    updated_at_utc
                )
                VALUES
                (
                    @local_build_id,
                    @forge_build_id,
                    @version,
                    @content_sha256,
                    @publisher_pilot_name,
                    @is_publisher_source,
                    @latest_known_version,
                    @star_count,
                    @is_starred_by_me,
                    @is_owned_by_me,
                    @updated_at_utc
                )
                ON CONFLICT(local_build_id) DO UPDATE SET
                    forge_build_id = excluded.forge_build_id,
                    version = excluded.version,
                    content_sha256 = excluded.content_sha256,
                    publisher_pilot_name = excluded.publisher_pilot_name,
                    is_publisher_source = excluded.is_publisher_source,
                    latest_known_version = excluded.latest_known_version,
                    star_count = excluded.star_count,
                    is_starred_by_me = excluded.is_starred_by_me,
                    is_owned_by_me = excluded.is_owned_by_me,
                    updated_at_utc = excluded.updated_at_utc;
                """;
            command.Parameters.AddWithValue(
                "@local_build_id",
                link.LocalBuildId.Trim());
            command.Parameters.AddWithValue(
                "@forge_build_id",
                link.ForgeBuildId.Trim());
            command.Parameters.AddWithValue("@version", link.Version);
            command.Parameters.AddWithValue(
                "@content_sha256",
                link.ContentSha256.Trim());
            command.Parameters.AddWithValue(
                "@publisher_pilot_name",
                link.PublisherPilotName.Trim());
            command.Parameters.AddWithValue(
                "@is_publisher_source",
                link.IsPublisherSource ? 1 : 0);
            command.Parameters.AddWithValue(
                "@latest_known_version",
                link.LatestKnownVersion);
            command.Parameters.AddWithValue("@star_count", link.StarCount);
            command.Parameters.AddWithValue(
                "@is_starred_by_me",
                link.IsStarredByMe ? 1 : 0);
            command.Parameters.AddWithValue(
                "@is_owned_by_me",
                link.IsOwnedByMe ? 1 : 0);
            command.Parameters.AddWithValue(
                "@updated_at_utc",
                FormatTimestamp(link.UpdatedAtUtc));
            command.ExecuteNonQuery();
        }
    }

    public void SaveAndActivateForgeVersion(
        uint characterId,
        SkillBuildDocument build,
        SkillBuildForgeLink link,
        DateTimeOffset updatedAtUtc)
    {
        if (characterId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(characterId));
        }
        ArgumentNullException.ThrowIfNull(build);
        ArgumentNullException.ThrowIfNull(link);
        ArgumentException.ThrowIfNullOrWhiteSpace(build.BuildId);
        ArgumentException.ThrowIfNullOrWhiteSpace(link.LocalBuildId);
        ArgumentException.ThrowIfNullOrWhiteSpace(link.ForgeBuildId);
        if (!string.Equals(
                build.BuildId.Trim(),
                link.LocalBuildId.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The Forge link must identify the supplied local build.",
                nameof(link));
        }
        if (link.IsPublisherSource)
        {
            throw new ArgumentException(
                "An installed Forge version cannot be a publisher source.",
                nameof(link));
        }

        var buildId = build.BuildId.Trim();
        var forgeBuildId = link.ForgeBuildId.Trim();
        var documentJson = SkillBuildDocumentCodec.SerializeBuild(build);

        lock (this.sync)
        {
            using var connection = this.OpenConnection();
            using var transaction = connection.BeginTransaction();

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO local_builds (
                        build_id, profession_index, title,
                        updated_at_utc, document_json)
                    VALUES (
                        @build_id, @profession_index, @title,
                        @updated_at_utc, @document_json)
                    ON CONFLICT(build_id) DO UPDATE SET
                        profession_index = excluded.profession_index,
                        title = excluded.title,
                        updated_at_utc = excluded.updated_at_utc,
                        document_json = excluded.document_json;
                    """;
                command.Parameters.AddWithValue("@build_id", buildId);
                command.Parameters.AddWithValue(
                    "@profession_index",
                    build.ProfessionIndex);
                command.Parameters.AddWithValue("@title", build.Title.Trim());
                command.Parameters.AddWithValue(
                    "@updated_at_utc",
                    FormatTimestamp(updatedAtUtc));
                command.Parameters.AddWithValue("@document_json", documentJson);
                command.ExecuteNonQuery();
            }

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO forge_build_links
                    (
                        local_build_id,
                        forge_build_id,
                        version,
                        content_sha256,
                        publisher_pilot_name,
                        is_publisher_source,
                        latest_known_version,
                        star_count,
                        is_starred_by_me,
                        is_owned_by_me,
                        updated_at_utc
                    )
                    VALUES
                    (
                        @local_build_id,
                        @forge_build_id,
                        @version,
                        @content_sha256,
                        @publisher_pilot_name,
                        0,
                        @latest_known_version,
                        @star_count,
                        @is_starred_by_me,
                        @is_owned_by_me,
                        @link_updated_at_utc
                    )
                    ON CONFLICT(local_build_id) DO UPDATE SET
                        forge_build_id = excluded.forge_build_id,
                        version = excluded.version,
                        content_sha256 = excluded.content_sha256,
                        publisher_pilot_name = excluded.publisher_pilot_name,
                        is_publisher_source = 0,
                        latest_known_version = excluded.latest_known_version,
                        star_count = excluded.star_count,
                        is_starred_by_me = excluded.is_starred_by_me,
                        is_owned_by_me = excluded.is_owned_by_me,
                        updated_at_utc = excluded.updated_at_utc;
                    """;
                command.Parameters.AddWithValue("@local_build_id", buildId);
                command.Parameters.AddWithValue("@forge_build_id", forgeBuildId);
                command.Parameters.AddWithValue("@version", link.Version);
                command.Parameters.AddWithValue(
                    "@content_sha256",
                    link.ContentSha256.Trim());
                command.Parameters.AddWithValue(
                    "@publisher_pilot_name",
                    link.PublisherPilotName.Trim());
                command.Parameters.AddWithValue(
                    "@latest_known_version",
                    link.LatestKnownVersion);
                command.Parameters.AddWithValue("@star_count", link.StarCount);
                command.Parameters.AddWithValue(
                    "@is_starred_by_me",
                    link.IsStarredByMe ? 1 : 0);
                command.Parameters.AddWithValue(
                    "@is_owned_by_me",
                    link.IsOwnedByMe ? 1 : 0);
                command.Parameters.AddWithValue(
                    "@link_updated_at_utc",
                    FormatTimestamp(link.UpdatedAtUtc));
                command.ExecuteNonQuery();
            }

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    DELETE FROM character_build_library
                    WHERE character_id = @character_id
                      AND build_id <> @local_build_id
                      AND build_id IN (
                          SELECT local_build_id
                          FROM forge_build_links
                          WHERE forge_build_id = @forge_build_id
                            AND is_publisher_source = 0
                      );
                    """;
                command.Parameters.AddWithValue(
                    "@character_id",
                    (long)characterId);
                command.Parameters.AddWithValue("@local_build_id", buildId);
                command.Parameters.AddWithValue("@forge_build_id", forgeBuildId);
                command.ExecuteNonQuery();
            }

            AddBuildToCharacter(
                connection,
                transaction,
                characterId,
                buildId,
                updatedAtUtc);

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO character_builds (
                        character_id, build_id, updated_at_utc)
                    VALUES (
                        @character_id, @build_id, @updated_at_utc)
                    ON CONFLICT(character_id) DO UPDATE SET
                        build_id = excluded.build_id,
                        updated_at_utc = excluded.updated_at_utc;
                    """;
                command.Parameters.AddWithValue(
                    "@character_id",
                    (long)characterId);
                command.Parameters.AddWithValue("@build_id", buildId);
                command.Parameters.AddWithValue(
                    "@updated_at_utc",
                    FormatTimestamp(updatedAtUtc));
                command.ExecuteNonQuery();
            }

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    UPDATE forge_build_links
                    SET latest_known_version = @latest_known_version,
                        star_count = @star_count,
                        is_starred_by_me = @is_starred_by_me,
                        is_owned_by_me = @is_owned_by_me,
                        updated_at_utc = @updated_at_utc
                    WHERE forge_build_id = @forge_build_id;
                    """;
                command.Parameters.AddWithValue(
                    "@latest_known_version",
                    link.LatestKnownVersion);
                command.Parameters.AddWithValue(
                    "@star_count",
                    Math.Max(0, link.StarCount));
                command.Parameters.AddWithValue(
                    "@is_starred_by_me",
                    link.IsStarredByMe ? 1 : 0);
                command.Parameters.AddWithValue(
                    "@is_owned_by_me",
                    link.IsOwnedByMe ? 1 : 0);
                command.Parameters.AddWithValue(
                    "@updated_at_utc",
                    FormatTimestamp(updatedAtUtc));
                command.Parameters.AddWithValue("@forge_build_id", forgeBuildId);
                command.ExecuteNonQuery();
            }

            transaction.Commit();
        }
    }

    public void UpdateForgeKnowledge(
        string forgeBuildId,
        int latestKnownVersion,
        int starCount,
        bool isStarredByMe,
        bool isOwnedByMe)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(forgeBuildId);
        if (latestKnownVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(latestKnownVersion));
        }

        lock (this.sync)
        {
            using var connection = this.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE forge_build_links
                SET latest_known_version = @latest_known_version,
                    star_count = @star_count,
                    is_starred_by_me = @is_starred_by_me,
                    is_owned_by_me = @is_owned_by_me,
                    updated_at_utc = @updated_at_utc
                WHERE forge_build_id = @forge_build_id;
                """;
            command.Parameters.AddWithValue(
                "@latest_known_version",
                latestKnownVersion);
            command.Parameters.AddWithValue("@star_count", Math.Max(0, starCount));
            command.Parameters.AddWithValue(
                "@is_starred_by_me",
                isStarredByMe ? 1 : 0);
            command.Parameters.AddWithValue(
                "@is_owned_by_me",
                isOwnedByMe ? 1 : 0);
            command.Parameters.AddWithValue(
                "@updated_at_utc",
                FormatTimestamp(DateTimeOffset.UtcNow));
            command.Parameters.AddWithValue(
                "@forge_build_id",
                forgeBuildId.Trim());
            command.ExecuteNonQuery();
        }
    }

    public string? GetActiveBuildId(uint characterId)
    {
        if (characterId == 0)
        {
            return null;
        }

        lock (this.sync)
        {
            using var connection = this.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT build_id
                FROM character_builds
                WHERE character_id = @character_id;
                """;
            command.Parameters.AddWithValue(
                "@character_id",
                (long)characterId);
            return command.ExecuteScalar() as string;
        }
    }

    public void SetActiveBuild(
        uint characterId,
        string buildId)
    {
        if (characterId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(characterId));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(buildId);

        lock (this.sync)
        {
            using var connection = this.OpenConnection();
            using var transaction = connection.BeginTransaction();
            var now = DateTimeOffset.UtcNow;
            var normalizedBuildId = buildId.Trim();
            AddBuildToCharacter(
                connection,
                transaction,
                characterId,
                normalizedBuildId,
                now);

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO character_builds (
                    character_id, build_id, updated_at_utc)
                VALUES (
                    @character_id, @build_id, @updated_at_utc)
                ON CONFLICT(character_id) DO UPDATE SET
                    build_id = excluded.build_id,
                    updated_at_utc = excluded.updated_at_utc;
                """;
            command.Parameters.AddWithValue(
                "@character_id",
                (long)characterId);
            command.Parameters.AddWithValue("@build_id", normalizedBuildId);
            command.Parameters.AddWithValue(
                "@updated_at_utc",
                FormatTimestamp(now));
            command.ExecuteNonQuery();
            transaction.Commit();
        }
    }

    public bool DeleteBuild(
        uint characterId,
        string buildId)
    {
        if (characterId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(characterId));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(buildId);

        lock (this.sync)
        {
            using var connection = this.OpenConnection();
            using var transaction = connection.BeginTransaction();
            var normalizedBuildId = buildId.Trim();
            var removed = false;

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    SELECT is_publisher_source
                    FROM forge_build_links
                    WHERE local_build_id = @build_id;
                    """;
                command.Parameters.AddWithValue(
                    "@build_id",
                    normalizedBuildId);
                if (command.ExecuteScalar() is long publisherSource &&
                    publisherSource != 0)
                {
                    transaction.Rollback();
                    return false;
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    DELETE FROM character_builds
                    WHERE character_id = @character_id
                      AND build_id = @build_id;
                    """;
                command.Parameters.AddWithValue(
                    "@character_id",
                    (long)characterId);
                command.Parameters.AddWithValue(
                    "@build_id",
                    normalizedBuildId);
                removed |= command.ExecuteNonQuery() != 0;
            }

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    DELETE FROM character_build_library
                    WHERE character_id = @character_id
                      AND build_id = @build_id;
                    """;
                command.Parameters.AddWithValue(
                    "@character_id",
                    (long)characterId);
                command.Parameters.AddWithValue(
                    "@build_id",
                    normalizedBuildId);
                removed |= command.ExecuteNonQuery() != 0;
            }

            if (removed)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    DELETE FROM local_builds
                    WHERE build_id = @build_id
                      AND NOT EXISTS (
                          SELECT 1
                          FROM character_build_library
                          WHERE build_id = @build_id
                      )
                      AND NOT EXISTS (
                          SELECT 1
                          FROM character_builds
                          WHERE build_id = @build_id
                      );
                    """;
                command.Parameters.AddWithValue(
                    "@build_id",
                    normalizedBuildId);
                command.ExecuteNonQuery();
            }

            transaction.Commit();
            return removed;
        }
    }

    private static void AddBuildToCharacter(
        SqliteConnection connection,
        SqliteTransaction transaction,
        uint characterId,
        string buildId,
        DateTimeOffset addedAtUtc)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO character_build_library
            (character_id, build_id, added_at_utc)
            VALUES (@character_id, @build_id, @added_at_utc)
            ON CONFLICT(character_id, build_id) DO NOTHING;
            """;
        command.Parameters.AddWithValue("@character_id", (long)characterId);
        command.Parameters.AddWithValue("@build_id", buildId);
        command.Parameters.AddWithValue(
            "@added_at_utc",
            FormatTimestamp(addedAtUtc));
        command.ExecuteNonQuery();
    }

    private static SkillBuildForgeLink ReadForgeLink(SqliteDataReader reader) =>
        new()
        {
            LocalBuildId = reader.GetString(0),
            ForgeBuildId = reader.GetString(1),
            Version = reader.GetInt32(2),
            ContentSha256 = reader.GetString(3),
            PublisherPilotName = reader.GetString(4),
            IsPublisherSource = reader.GetInt32(5) != 0,
            LatestKnownVersion = reader.GetInt32(6),
            StarCount = reader.GetInt32(7),
            IsStarredByMe = reader.GetInt32(8) != 0,
            IsOwnedByMe = reader.GetInt32(9) != 0,
            UpdatedAtUtc = ParseTimestamp(reader.GetString(10)),
        };

    private static void ResetUnreleasedBuildSchemas(
        SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DROP TABLE IF EXISTS forge_build_links;
            DROP TABLE IF EXISTS character_builds;
            DROP TABLE IF EXISTS character_build_library;
            DROP TABLE IF EXISTS local_builds;
            DROP TABLE IF EXISTS character_build_guides;
            DROP TABLE IF EXISTS local_build_guides;
            PRAGMA user_version = 0;
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private static void MigrateVersion3To4(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE forge_build_links (
                local_build_id TEXT PRIMARY KEY,
                forge_build_id TEXT NOT NULL,
                version INTEGER NOT NULL CHECK (version > 0),
                content_sha256 TEXT NOT NULL,
                publisher_pilot_name TEXT NOT NULL,
                is_publisher_source INTEGER NOT NULL CHECK (is_publisher_source IN (0, 1)),
                latest_known_version INTEGER NOT NULL CHECK (latest_known_version > 0),
                star_count INTEGER NOT NULL CHECK (star_count >= 0),
                is_starred_by_me INTEGER NOT NULL CHECK (is_starred_by_me IN (0, 1)),
                is_owned_by_me INTEGER NOT NULL CHECK (is_owned_by_me IN (0, 1)),
                updated_at_utc TEXT NOT NULL,
                FOREIGN KEY (local_build_id)
                    REFERENCES local_builds(build_id)
                    ON DELETE CASCADE
            );

            CREATE INDEX ix_forge_build_links_forge_version
                ON forge_build_links (forge_build_id, version);

            PRAGMA user_version = 4;
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private static void MigrateVersion4To5(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE character_build_library (
                character_id INTEGER NOT NULL,
                build_id TEXT NOT NULL,
                added_at_utc TEXT NOT NULL,
                PRIMARY KEY (character_id, build_id),
                FOREIGN KEY (build_id)
                    REFERENCES local_builds(build_id)
                    ON DELETE CASCADE
            );

            CREATE INDEX ix_character_build_library_build
                ON character_build_library (build_id, character_id);

            INSERT INTO character_build_library
                (character_id, build_id, added_at_utc)
            SELECT character_id, build_id, updated_at_utc
            FROM character_builds;

            PRAGMA user_version = 5;
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private static void MigrateVersion5To6(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM character_build_library
            WHERE rowid IN (
                SELECT rowid
                FROM (
                    SELECT
                        cbl.rowid AS rowid,
                        ROW_NUMBER() OVER (
                            PARTITION BY
                                cbl.character_id,
                                fbl.forge_build_id
                            ORDER BY
                                CASE
                                    WHEN cb.build_id = cbl.build_id THEN 0
                                    ELSE 1
                                END,
                                fbl.version DESC,
                                cbl.added_at_utc DESC,
                                cbl.build_id
                        ) AS version_rank
                    FROM character_build_library cbl
                    JOIN forge_build_links fbl
                      ON fbl.local_build_id = cbl.build_id
                     AND fbl.is_publisher_source = 0
                    LEFT JOIN character_builds cb
                      ON cb.character_id = cbl.character_id
                ) ranked_versions
                WHERE version_rank > 1
            );

            PRAGMA user_version = 6;
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private void CreateSchema(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE local_builds (
                build_id TEXT PRIMARY KEY,
                profession_index INTEGER NOT NULL,
                title TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL,
                document_json TEXT NOT NULL
            );

            CREATE INDEX ix_local_builds_profession_title
                ON local_builds (
                    profession_index,
                    title COLLATE NOCASE);

            CREATE TABLE character_build_library (
                character_id INTEGER NOT NULL,
                build_id TEXT NOT NULL,
                added_at_utc TEXT NOT NULL,
                PRIMARY KEY (character_id, build_id),
                FOREIGN KEY (build_id)
                    REFERENCES local_builds(build_id)
                    ON DELETE CASCADE
            );

            CREATE INDEX ix_character_build_library_build
                ON character_build_library (build_id, character_id);

            CREATE TABLE character_builds (
                character_id INTEGER PRIMARY KEY,
                build_id TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL,
                FOREIGN KEY (build_id)
                    REFERENCES local_builds(build_id)
                    ON DELETE CASCADE
            );

            CREATE TABLE forge_build_links (
                local_build_id TEXT PRIMARY KEY,
                forge_build_id TEXT NOT NULL,
                version INTEGER NOT NULL CHECK (version > 0),
                content_sha256 TEXT NOT NULL,
                publisher_pilot_name TEXT NOT NULL,
                is_publisher_source INTEGER NOT NULL CHECK (is_publisher_source IN (0, 1)),
                latest_known_version INTEGER NOT NULL CHECK (latest_known_version > 0),
                star_count INTEGER NOT NULL CHECK (star_count >= 0),
                is_starred_by_me INTEGER NOT NULL CHECK (is_starred_by_me IN (0, 1)),
                is_owned_by_me INTEGER NOT NULL CHECK (is_owned_by_me IN (0, 1)),
                updated_at_utc TEXT NOT NULL,
                FOREIGN KEY (local_build_id)
                    REFERENCES local_builds(build_id)
                    ON DELETE CASCADE
            );

            CREATE INDEX ix_forge_build_links_forge_version
                ON forge_build_links (forge_build_id, version);

            PRAGMA user_version = 6;
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(this.connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON;";
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
        value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
}
