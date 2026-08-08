namespace Net7ClientManager.RecipeMapping;

using System.Globalization;
using Microsoft.Data.Sqlite;
using Net7ClientManager.Observations.Models;

internal sealed class RecipeMappingStore
{
    // Crafting Recipes is still pre-release. Schema changes intentionally do
    // not carry migration code yet; testers can delete crafting.db and let the
    // feature recreate its own isolated store from scratch.
    private const int CurrentSchemaVersion = 2;

    private readonly string databasePath;
    private readonly string connectionString;
    private readonly Lock sync = new();

    public RecipeMappingStore()
        : this(Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData),
            "Net7ClientManager",
            "crafting.db"))
    {
    }

    internal RecipeMappingStore(string databasePath)
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
            var version = ExecuteScalarLong(connection, "PRAGMA user_version;");

            if (version != 0 && version != CurrentSchemaVersion)
            {
                throw new InvalidOperationException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Crafting schema {version} does not match pre-release schema {CurrentSchemaVersion}. Delete crafting.db to recreate only the unreleased crafting store."));
            }

            if (version == 0)
            {
                this.CreateSchema(connection);
            }
        }
    }

    public RecipeMappingDocument Load()
    {
        lock (this.sync)
        {
            using var connection = this.OpenConnection();
            Dictionary<uint, RecipeMappingCharacterRecord> characters = [];

            using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                    SELECT character_id, pilot_name,
                           baseline_observed_complete,
                           baseline_completed_at_utc,
                           updated_at_utc
                    FROM crafting_characters
                    ORDER BY character_id;
                    """;

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var characterId = checked((uint)reader.GetInt64(0));
                    characters[characterId] = new RecipeMappingCharacterRecord
                    {
                        CharacterId = characterId,
                        PilotName = reader.GetString(1),
                        BaselineObservedComplete = reader.GetInt64(2) != 0,
                        BaselineCompletedAtUtc = reader.IsDBNull(3)
                            ? null
                            : ParseTimestamp(reader.GetString(3)),
                        UpdatedAtUtc = reader.IsDBNull(4)
                            ? null
                            : ParseTimestamp(reader.GetString(4)),
                    };
                }
            }

            if (characters.Count == 0)
            {
                return new RecipeMappingDocument();
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                    SELECT character_id, skill_name, known_zero
                    FROM crafting_build_skill_baselines
                    ORDER BY character_id, skill_name COLLATE NOCASE;
                    """;

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var characterId = checked((uint)reader.GetInt64(0));
                    if (!characters.TryGetValue(characterId, out var character))
                    {
                        continue;
                    }

                    var skillName = reader.GetString(1);
                    character.BaselineInitializedBuildSkillNames.Add(skillName);
                    if (reader.GetInt64(2) != 0)
                    {
                        character.KnownZeroBuildSkillNames.Add(skillName);
                    }
                }
            }

            Dictionary<(uint CharacterId, int CategoryId), RecipeMappingCategoryRecord>
                categories = [];

            using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                    SELECT character_id, category_id,
                           primary_index, secondary_index, leaf_index,
                           path, is_visited, last_observed_at_utc
                    FROM crafting_categories
                    ORDER BY character_id, category_id;
                    """;

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var characterId = checked((uint)reader.GetInt64(0));
                    if (!characters.TryGetValue(characterId, out var character))
                    {
                        continue;
                    }

                    var category = new RecipeMappingCategoryRecord
                    {
                        CategoryId = reader.GetInt32(1),
                        PrimaryIndex = reader.GetInt32(2),
                        SecondaryIndex = reader.GetInt32(3),
                        LeafIndex = reader.GetInt32(4),
                        Path = reader.GetString(5),
                        IsVisited = reader.GetInt64(6) != 0,
                        LastObservedAtUtc = reader.IsDBNull(7)
                            ? null
                            : ParseTimestamp(reader.GetString(7)),
                    };
                    character.Categories.Add(category);
                    categories[(characterId, category.CategoryId)] = category;
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                    SELECT character_id, category_id, item_template_id
                    FROM crafting_category_formulas
                    ORDER BY character_id, category_id, item_template_id;
                    """;

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var key = (
                        checked((uint)reader.GetInt64(0)),
                        reader.GetInt32(1));
                    if (categories.TryGetValue(key, out var category))
                    {
                        category.FormulaItemTemplateIds.Add(reader.GetInt32(2));
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                    SELECT character_id, item_template_id
                    FROM crafting_known_recipes
                    ORDER BY character_id, item_template_id;
                    """;

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var characterId = checked((uint)reader.GetInt64(0));
                    if (characters.TryGetValue(characterId, out var character))
                    {
                        character.KnownRecipeItemTemplateIds.Add(reader.GetInt32(1));
                    }
                }
            }

            return new RecipeMappingDocument
            {
                Characters = characters.Values
                    .OrderBy(character => character.CharacterId)
                    .ToList(),
            };
        }
    }

    public void SaveCharacter(
        RecipeMappingCharacterRecord record,
        IReadOnlyList<RecipeMappingHistoryEventRecord>? historyEvents = null)
    {
        ArgumentNullException.ThrowIfNull(record);
        historyEvents ??= [];

        lock (this.sync)
        {
            using var connection = this.OpenConnection();
            using var transaction = connection.BeginTransaction();
            SaveCharacter(connection, transaction, record, historyEvents);
            transaction.Commit();
        }
    }

    public void SaveRecipeComponents(
        uint characterId,
        int outputItemTemplateId,
        IReadOnlyList<ClientProductionRecipeIngredientObservation> ingredients,
        DateTimeOffset observedAtUtc)
    {
        if (characterId is 0 or uint.MaxValue || outputItemTemplateId <= 0)
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(ingredients);

        lock (this.sync)
        {
            using var connection = this.OpenConnection();
            using var transaction = connection.BeginTransaction();

            using (var delete = connection.CreateCommand())
            {
                delete.Transaction = transaction;
                delete.CommandText = """
                    DELETE FROM crafting_recipe_components
                    WHERE character_id = @character_id
                      AND output_item_template_id = @output_item_template_id;
                    """;
                delete.Parameters.AddWithValue("@character_id", (long)characterId);
                delete.Parameters.AddWithValue(
                    "@output_item_template_id",
                    outputItemTemplateId);
                delete.ExecuteNonQuery();
            }

            foreach (var ingredient in ingredients
                         .Where(item => item.ItemTemplateId > 0 && item.Quantity > 0)
                         .GroupBy(item => item.ItemTemplateId)
                         .Select(group => new
                         {
                             ItemTemplateId = group.Key,
                             Quantity = group.Sum(item => item.Quantity),
                         }))
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO crafting_recipe_components (
                        character_id, output_item_template_id,
                        component_item_template_id, quantity, observed_at_utc)
                    VALUES (
                        @character_id, @output_item_template_id,
                        @component_item_template_id, @quantity, @observed_at_utc);
                    """;
                command.Parameters.AddWithValue("@character_id", (long)characterId);
                command.Parameters.AddWithValue(
                    "@output_item_template_id",
                    outputItemTemplateId);
                command.Parameters.AddWithValue(
                    "@component_item_template_id",
                    ingredient.ItemTemplateId);
                command.Parameters.AddWithValue("@quantity", ingredient.Quantity);
                command.Parameters.AddWithValue(
                    "@observed_at_utc",
                    FormatTimestamp(observedAtUtc));
                command.ExecuteNonQuery();
            }

            transaction.Commit();
        }
    }

    public RecipeMappingHistoryEventRecord AppendHistoryEvent(
        RecipeMappingHistoryEventRecord historyEvent)
    {
        ArgumentNullException.ThrowIfNull(historyEvent);

        lock (this.sync)
        {
            using var connection = this.OpenConnection();
            using var transaction = connection.BeginTransaction();
            var result = InsertHistoryEvent(connection, transaction, historyEvent);
            transaction.Commit();
            return result;
        }
    }

    public RecipeMappingRecipeDetailsPresentation GetRecipeDetails(
        uint characterId,
        int itemTemplateId)
    {
        if (characterId is 0 or uint.MaxValue || itemTemplateId <= 0)
        {
            return new RecipeMappingRecipeDetailsPresentation
            {
                ItemTemplateId = itemTemplateId,
            };
        }

        lock (this.sync)
        {
            using var connection = this.OpenConnection();
            DateTimeOffset? firstObservedAtUtc = null;
            var acquisitionSource = "";

            using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                    SELECT first_observed_at_utc, source
                    FROM crafting_known_recipes
                    WHERE character_id = @character_id
                      AND item_template_id = @item_template_id;
                    """;
                command.Parameters.AddWithValue("@character_id", (long)characterId);
                command.Parameters.AddWithValue("@item_template_id", itemTemplateId);
                using var reader = command.ExecuteReader();
                if (reader.Read())
                {
                    firstObservedAtUtc = ParseTimestamp(reader.GetString(0));
                    acquisitionSource = reader.GetString(1);
                }
            }

            List<RecipeMappingRecipeComponent> components = [];
            using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                    SELECT component_item_template_id, quantity
                    FROM crafting_recipe_components
                    WHERE character_id = @character_id
                      AND output_item_template_id = @item_template_id
                    ORDER BY component_item_template_id;
                    """;
                command.Parameters.AddWithValue("@character_id", (long)characterId);
                command.Parameters.AddWithValue("@item_template_id", itemTemplateId);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    components.Add(new RecipeMappingRecipeComponent
                    {
                        ItemTemplateId = reader.GetInt32(0),
                        Quantity = reader.GetInt32(1),
                    });
                }
            }

            var history = ReadHistory(connection, characterId, itemTemplateId);
            return new RecipeMappingRecipeDetailsPresentation
            {
                ItemTemplateId = itemTemplateId,
                FirstObservedAtUtc = firstObservedAtUtc,
                AcquisitionSource = acquisitionSource,
                Components = components,
                ComponentsSource = components.Count > 0 ? "observed" : "",
                History = history,
            };
        }
    }

    private static void SaveCharacter(
        SqliteConnection connection,
        SqliteTransaction transaction,
        RecipeMappingCharacterRecord record,
        IReadOnlyList<RecipeMappingHistoryEventRecord> historyEvents)
    {
        var now = record.UpdatedAtUtc ?? DateTimeOffset.UtcNow;

        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO crafting_characters (
                    character_id, pilot_name,
                    baseline_observed_complete,
                    baseline_completed_at_utc,
                    updated_at_utc)
                VALUES (
                    @character_id, @pilot_name,
                    @baseline_observed_complete,
                    @baseline_completed_at_utc,
                    @updated_at_utc)
                ON CONFLICT(character_id) DO UPDATE SET
                    pilot_name = excluded.pilot_name,
                    baseline_observed_complete = excluded.baseline_observed_complete,
                    baseline_completed_at_utc = excluded.baseline_completed_at_utc,
                    updated_at_utc = excluded.updated_at_utc;
                """;
            command.Parameters.AddWithValue("@character_id", (long)record.CharacterId);
            command.Parameters.AddWithValue("@pilot_name", record.PilotName ?? "");
            command.Parameters.AddWithValue(
                "@baseline_observed_complete",
                record.BaselineObservedComplete ? 1 : 0);
            command.Parameters.AddWithValue(
                "@baseline_completed_at_utc",
                record.BaselineCompletedAtUtc is { } completedAt
                    ? FormatTimestamp(completedAt)
                    : DBNull.Value);
            command.Parameters.AddWithValue("@updated_at_utc", FormatTimestamp(now));
            command.ExecuteNonQuery();
        }

        DeleteCharacterRows(
            connection,
            transaction,
            "crafting_build_skill_baselines",
            record.CharacterId);
        foreach (var skillName in record.BaselineInitializedBuildSkillNames
                     .Where(name => !string.IsNullOrWhiteSpace(name))
                     .Distinct(StringComparer.Ordinal))
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO crafting_build_skill_baselines (
                    character_id, skill_name, known_zero)
                VALUES (@character_id, @skill_name, @known_zero);
                """;
            command.Parameters.AddWithValue("@character_id", (long)record.CharacterId);
            command.Parameters.AddWithValue("@skill_name", skillName.Trim());
            command.Parameters.AddWithValue(
                "@known_zero",
                record.KnownZeroBuildSkillNames.Contains(
                    skillName,
                    StringComparer.Ordinal)
                    ? 1
                    : 0);
            command.ExecuteNonQuery();
        }

        DeleteCharacterRows(
            connection,
            transaction,
            "crafting_category_formulas",
            record.CharacterId);
        DeleteCharacterRows(
            connection,
            transaction,
            "crafting_categories",
            record.CharacterId);

        foreach (var category in record.Categories
                     .Where(category => category.CategoryId > 0)
                     .GroupBy(category => category.CategoryId)
                     .Select(group => group.Last()))
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO crafting_categories (
                        character_id, category_id,
                        primary_index, secondary_index, leaf_index,
                        path, is_visited, last_observed_at_utc)
                    VALUES (
                        @character_id, @category_id,
                        @primary_index, @secondary_index, @leaf_index,
                        @path, @is_visited, @last_observed_at_utc);
                    """;
                command.Parameters.AddWithValue("@character_id", (long)record.CharacterId);
                command.Parameters.AddWithValue("@category_id", category.CategoryId);
                command.Parameters.AddWithValue("@primary_index", category.PrimaryIndex);
                command.Parameters.AddWithValue("@secondary_index", category.SecondaryIndex);
                command.Parameters.AddWithValue("@leaf_index", category.LeafIndex);
                command.Parameters.AddWithValue("@path", category.Path ?? "");
                command.Parameters.AddWithValue("@is_visited", category.IsVisited ? 1 : 0);
                command.Parameters.AddWithValue(
                    "@last_observed_at_utc",
                    category.LastObservedAtUtc is { } observedAt
                        ? FormatTimestamp(observedAt)
                        : DBNull.Value);
                command.ExecuteNonQuery();
            }

            foreach (var itemTemplateId in category.FormulaItemTemplateIds
                         .Where(id => id > 0)
                         .Distinct())
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO crafting_category_formulas (
                        character_id, category_id, item_template_id)
                    VALUES (@character_id, @category_id, @item_template_id);
                    """;
                command.Parameters.AddWithValue("@character_id", (long)record.CharacterId);
                command.Parameters.AddWithValue("@category_id", category.CategoryId);
                command.Parameters.AddWithValue("@item_template_id", itemTemplateId);
                command.ExecuteNonQuery();
            }
        }

        var acquisitionEvents = historyEvents
            .Where(IsRecipeAcquisitionEvent)
            .Where(item => item.ItemTemplateId > 0)
            .GroupBy(item => item.ItemTemplateId)
            .ToDictionary(group => group.Key, group => group.First());

        foreach (var itemTemplateId in record.KnownRecipeItemTemplateIds
                     .Where(id => id > 0)
                     .Distinct())
        {
            acquisitionEvents.TryGetValue(itemTemplateId, out var acquisition);
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO crafting_known_recipes (
                    character_id, item_template_id,
                    first_observed_at_utc, source)
                VALUES (
                    @character_id, @item_template_id,
                    @first_observed_at_utc, @source)
                ON CONFLICT(character_id, item_template_id) DO NOTHING;
                """;
            command.Parameters.AddWithValue("@character_id", (long)record.CharacterId);
            command.Parameters.AddWithValue("@item_template_id", itemTemplateId);
            command.Parameters.AddWithValue(
                "@first_observed_at_utc",
                FormatTimestamp(acquisition?.ObservedAtUtc ?? now));
            command.Parameters.AddWithValue(
                "@source",
                acquisition?.Kind == RecipeMappingHistoryEventKind.RecipeLearnedByScan
                    ? "scan"
                    : acquisition != null
                        ? "analyze"
                        : "observed");
            command.ExecuteNonQuery();
        }

        foreach (var historyEvent in historyEvents)
        {
            InsertHistoryEvent(connection, transaction, historyEvent);
        }
    }

    private static RecipeMappingHistoryEventRecord InsertHistoryEvent(
        SqliteConnection connection,
        SqliteTransaction transaction,
        RecipeMappingHistoryEventRecord historyEvent)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO crafting_history_events (
                character_id, item_template_id, kind,
                result_validity, observed_at_utc,
                outcome_quality_percent, credits_spent,
                success_probability_percent,
                critical_success_probability_percent,
                output_quantity, recipe_count, new_recipe_count)
            VALUES (
                @character_id, @item_template_id, @kind,
                @result_validity, @observed_at_utc,
                @outcome_quality_percent, @credits_spent,
                @success_probability_percent,
                @critical_success_probability_percent,
                @output_quantity, @recipe_count, @new_recipe_count);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("@character_id", (long)historyEvent.CharacterId);
        command.Parameters.AddWithValue("@item_template_id", historyEvent.ItemTemplateId);
        command.Parameters.AddWithValue("@kind", (int)historyEvent.Kind);
        command.Parameters.AddWithValue("@result_validity", historyEvent.ResultValidity);
        command.Parameters.AddWithValue(
            "@observed_at_utc",
            FormatTimestamp(historyEvent.ObservedAtUtc));
        command.Parameters.AddWithValue(
            "@outcome_quality_percent",
            historyEvent.OutcomeQualityPercent.HasValue
                ? historyEvent.OutcomeQualityPercent.Value
                : DBNull.Value);
        command.Parameters.AddWithValue(
            "@credits_spent",
            historyEvent.CreditsSpent.HasValue
                ? historyEvent.CreditsSpent.Value
                : DBNull.Value);
        command.Parameters.AddWithValue(
            "@success_probability_percent",
            historyEvent.SuccessProbabilityPercent.HasValue
                ? historyEvent.SuccessProbabilityPercent.Value
                : DBNull.Value);
        command.Parameters.AddWithValue(
            "@critical_success_probability_percent",
            historyEvent.CriticalSuccessProbabilityPercent.HasValue
                ? historyEvent.CriticalSuccessProbabilityPercent.Value
                : DBNull.Value);
        command.Parameters.AddWithValue(
            "@output_quantity",
            historyEvent.OutputQuantity);
        command.Parameters.AddWithValue("@recipe_count", historyEvent.RecipeCount);
        command.Parameters.AddWithValue("@new_recipe_count", historyEvent.NewRecipeCount);
        var eventId = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);

        foreach (var item in historyEvent.ResultItems
                     .Where(item => item.ItemTemplateId > 0 && item.Quantity > 0))
        {
            using var itemCommand = connection.CreateCommand();
            itemCommand.Transaction = transaction;
            itemCommand.CommandText = """
                INSERT INTO crafting_history_items (
                    event_id, item_template_id, quantity, quality_percent)
                VALUES (
                    @event_id, @item_template_id, @quantity, @quality_percent);
                """;
            itemCommand.Parameters.AddWithValue("@event_id", eventId);
            itemCommand.Parameters.AddWithValue("@item_template_id", item.ItemTemplateId);
            itemCommand.Parameters.AddWithValue("@quantity", item.Quantity);
            itemCommand.Parameters.AddWithValue(
                "@quality_percent",
                item.QualityPercent.HasValue
                    ? item.QualityPercent.Value
                    : DBNull.Value);
            itemCommand.ExecuteNonQuery();
        }

        return historyEvent with { EventId = eventId };
    }

    private static IReadOnlyList<RecipeMappingHistoryEventRecord> ReadHistory(
        SqliteConnection connection,
        uint characterId,
        int itemTemplateId)
    {
        List<RecipeMappingHistoryEventRecord> events = [];
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT event_id, kind, result_validity, observed_at_utc,
                       outcome_quality_percent, credits_spent,
                       success_probability_percent,
                       critical_success_probability_percent,
                       output_quantity, recipe_count, new_recipe_count
                FROM crafting_history_events
                WHERE character_id = @character_id
                  AND item_template_id = @item_template_id
                ORDER BY observed_at_utc, event_id;
                """;
            command.Parameters.AddWithValue("@character_id", (long)characterId);
            command.Parameters.AddWithValue("@item_template_id", itemTemplateId);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                events.Add(new RecipeMappingHistoryEventRecord
                {
                    EventId = reader.GetInt64(0),
                    CharacterId = characterId,
                    ItemTemplateId = itemTemplateId,
                    Kind = (RecipeMappingHistoryEventKind)reader.GetInt32(1),
                    ResultValidity = reader.GetInt32(2),
                    ObservedAtUtc = ParseTimestamp(reader.GetString(3)),
                    OutcomeQualityPercent = reader.IsDBNull(4)
                        ? null
                        : reader.GetFloat(4),
                    CreditsSpent = reader.IsDBNull(5)
                        ? null
                        : reader.GetInt64(5),
                    SuccessProbabilityPercent = reader.IsDBNull(6)
                        ? null
                        : reader.GetFloat(6),
                    CriticalSuccessProbabilityPercent = reader.IsDBNull(7)
                        ? null
                        : reader.GetFloat(7),
                    OutputQuantity = reader.GetInt32(8),
                    RecipeCount = reader.GetInt32(9),
                    NewRecipeCount = reader.GetInt32(10),
                });
            }
        }

        if (events.Count == 0)
        {
            return events;
        }

        var byEventId = events.ToDictionary(item => item.EventId);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT event_id, item_template_id, quantity, quality_percent
                FROM crafting_history_items
                WHERE event_id IN (
                    SELECT event_id
                    FROM crafting_history_events
                    WHERE character_id = @character_id
                      AND item_template_id = @item_template_id)
                ORDER BY event_id, item_template_id;
                """;
            command.Parameters.AddWithValue("@character_id", (long)characterId);
            command.Parameters.AddWithValue("@item_template_id", itemTemplateId);
            Dictionary<long, List<RecipeMappingHistoryItem>> items = [];
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var eventId = reader.GetInt64(0);
                if (!byEventId.ContainsKey(eventId))
                {
                    continue;
                }

                if (!items.TryGetValue(eventId, out var list))
                {
                    list = [];
                    items[eventId] = list;
                }

                list.Add(new RecipeMappingHistoryItem
                {
                    ItemTemplateId = reader.GetInt32(1),
                    Quantity = reader.GetInt32(2),
                    QualityPercent = reader.IsDBNull(3)
                        ? null
                        : reader.GetFloat(3),
                });
            }

            events = events
                .Select(item => items.TryGetValue(item.EventId, out var resultItems)
                    ? item with { ResultItems = resultItems }
                    : item)
                .ToList();
        }

        return events;
    }

    private static bool IsRecipeAcquisitionEvent(
        RecipeMappingHistoryEventRecord historyEvent)
    {
        return historyEvent.Kind is
            RecipeMappingHistoryEventKind.RecipeLearnedByScan or
            RecipeMappingHistoryEventKind.AnalyzeSucceeded or
            RecipeMappingHistoryEventKind.AnalyzeCriticalSucceeded;
    }

    private static void DeleteCharacterRows(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tableName,
        uint characterId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = string.Concat(
            "DELETE FROM ",
            tableName,
            " WHERE character_id = @character_id;");
        command.Parameters.AddWithValue("@character_id", (long)characterId);
        command.ExecuteNonQuery();
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
            CREATE TABLE crafting_characters (
                character_id INTEGER PRIMARY KEY,
                pilot_name TEXT NOT NULL,
                baseline_observed_complete INTEGER NOT NULL,
                baseline_completed_at_utc TEXT NULL,
                updated_at_utc TEXT NULL
            );

            CREATE TABLE crafting_build_skill_baselines (
                character_id INTEGER NOT NULL,
                skill_name TEXT NOT NULL,
                known_zero INTEGER NOT NULL,
                PRIMARY KEY(character_id, skill_name),
                FOREIGN KEY(character_id) REFERENCES crafting_characters(character_id)
                    ON DELETE CASCADE
            );

            CREATE TABLE crafting_categories (
                character_id INTEGER NOT NULL,
                category_id INTEGER NOT NULL,
                primary_index INTEGER NOT NULL,
                secondary_index INTEGER NOT NULL,
                leaf_index INTEGER NOT NULL,
                path TEXT NOT NULL,
                is_visited INTEGER NOT NULL,
                last_observed_at_utc TEXT NULL,
                PRIMARY KEY(character_id, category_id),
                FOREIGN KEY(character_id) REFERENCES crafting_characters(character_id)
                    ON DELETE CASCADE
            );

            CREATE TABLE crafting_category_formulas (
                character_id INTEGER NOT NULL,
                category_id INTEGER NOT NULL,
                item_template_id INTEGER NOT NULL,
                PRIMARY KEY(character_id, category_id, item_template_id),
                FOREIGN KEY(character_id, category_id)
                    REFERENCES crafting_categories(character_id, category_id)
                    ON DELETE CASCADE
            );

            CREATE TABLE crafting_known_recipes (
                character_id INTEGER NOT NULL,
                item_template_id INTEGER NOT NULL,
                first_observed_at_utc TEXT NOT NULL,
                source TEXT NOT NULL,
                PRIMARY KEY(character_id, item_template_id),
                FOREIGN KEY(character_id) REFERENCES crafting_characters(character_id)
                    ON DELETE CASCADE
            );

            CREATE TABLE crafting_recipe_components (
                character_id INTEGER NOT NULL,
                output_item_template_id INTEGER NOT NULL,
                component_item_template_id INTEGER NOT NULL,
                quantity INTEGER NOT NULL,
                observed_at_utc TEXT NOT NULL,
                PRIMARY KEY(
                    character_id,
                    output_item_template_id,
                    component_item_template_id),
                FOREIGN KEY(character_id) REFERENCES crafting_characters(character_id)
                    ON DELETE CASCADE
            );

            CREATE TABLE crafting_history_events (
                event_id INTEGER PRIMARY KEY AUTOINCREMENT,
                character_id INTEGER NOT NULL,
                item_template_id INTEGER NOT NULL,
                kind INTEGER NOT NULL,
                result_validity INTEGER NOT NULL,
                observed_at_utc TEXT NOT NULL,
                outcome_quality_percent REAL NULL,
                credits_spent INTEGER NULL,
                success_probability_percent REAL NULL,
                critical_success_probability_percent REAL NULL,
                output_quantity INTEGER NOT NULL DEFAULT 0,
                recipe_count INTEGER NOT NULL DEFAULT 0,
                new_recipe_count INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY(character_id) REFERENCES crafting_characters(character_id)
                    ON DELETE CASCADE
            );

            CREATE TABLE crafting_history_items (
                event_id INTEGER NOT NULL,
                item_template_id INTEGER NOT NULL,
                quantity INTEGER NOT NULL,
                quality_percent REAL NULL,
                PRIMARY KEY(event_id, item_template_id),
                FOREIGN KEY(event_id) REFERENCES crafting_history_events(event_id)
                    ON DELETE CASCADE
            );

            CREATE INDEX idx_crafting_known_recipes_item
                ON crafting_known_recipes(item_template_id, character_id);
            CREATE INDEX idx_crafting_history_recipe
                ON crafting_history_events(
                    character_id, item_template_id, observed_at_utc, event_id);

            PRAGMA user_version = 2;
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
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

    private static string FormatTimestamp(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString(
            "O",
            CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset ParseTimestamp(string value)
    {
        return DateTimeOffset.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);
    }
}
