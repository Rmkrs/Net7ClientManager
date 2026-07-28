namespace Net7ClientManager.Addons.Runtime;

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

/// <summary>
/// Host-owned, addon- and owner-scoped persistent storage.
///
/// Lua never receives a path. The host derives one from the validated addon id
/// and an SHA-256 digest of the stable owner key, validates the complete JSON
/// document against a fixed quota and replaces files atomically.
/// </summary>
internal sealed class AddonStorageStore
{
    public const int MaximumDocumentBytes =
        16 * 1024 * 1024;

    private const int MaximumKeyCount = 64;

    private const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string rootDirectory;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> gates =
        new(StringComparer.Ordinal);

    public AddonStorageStore(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        this.rootDirectory = rootDirectory;
    }

    public async ValueTask<ReadResult> ReadAsync(
        string ownerKey,
        string addonId,
        string key,
        CancellationToken cancellationToken)
    {
        var scope = this.CreateScope(ownerKey, addonId);
        var gate = this.gates.GetOrAdd(
            scope.GateKey,
            static _ => new SemaphoreSlim(1, 1));

        await gate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            var loaded = await this.ReadDocumentCoreAsync(
                    scope,
                    cancellationToken)
                .ConfigureAwait(false);

            if (!loaded.Succeeded)
            {
                return ReadResult.Failure(loaded.Error);
            }

            if (loaded.Document == null ||
                !loaded.Document.Values.TryGetValue(
                    key,
                    out var value) ||
                value == null)
            {
                return ReadResult.Missing();
            }

            return ReadResult.Success(value.DeepClone());
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask<CommandResult> WriteAsync(
        string ownerKey,
        string addonId,
        string key,
        JsonNode value,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(value);

        var scope = this.CreateScope(ownerKey, addonId);
        var gate = this.gates.GetOrAdd(
            scope.GateKey,
            static _ => new SemaphoreSlim(1, 1));

        await gate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            var loaded = await this.ReadDocumentCoreAsync(
                    scope,
                    cancellationToken)
                .ConfigureAwait(false);

            if (!loaded.Succeeded)
            {
                return CommandResult.Failure(loaded.Error);
            }

            var document =
                loaded.Document ??
                CreateDocument(ownerKey, addonId);

            if (!document.Values.ContainsKey(key) &&
                document.Values.Count >= MaximumKeyCount)
            {
                return CommandResult.Failure(
                    $"persistent storage may contain at most {MaximumKeyCount} keys");
            }

            document.Values[key] = value.DeepClone();

            return await this.WriteDocumentCoreAsync(
                    scope,
                    document,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask<CommandResult> RemoveAsync(
        string ownerKey,
        string addonId,
        string key,
        CancellationToken cancellationToken)
    {
        var scope = this.CreateScope(ownerKey, addonId);
        var gate = this.gates.GetOrAdd(
            scope.GateKey,
            static _ => new SemaphoreSlim(1, 1));

        await gate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            var loaded = await this.ReadDocumentCoreAsync(
                    scope,
                    cancellationToken)
                .ConfigureAwait(false);

            if (!loaded.Succeeded)
            {
                return CommandResult.Failure(loaded.Error);
            }

            if (loaded.Document == null ||
                !loaded.Document.Values.Remove(key))
            {
                return CommandResult.Success();
            }

            if (loaded.Document.Values.Count == 0)
            {
                try
                {
                    if (File.Exists(scope.FilePath))
                    {
                        File.Delete(scope.FilePath);
                    }

                    return CommandResult.Success();
                }
                catch (Exception ex)
                    when (ex is IOException or
                          UnauthorizedAccessException)
                {
                    return CommandResult.Failure(
                        string.Concat(
                            "persistent storage key was removed, but the empty document could not be deleted: ",
                            ex.Message));
                }
            }

            return await this.WriteDocumentCoreAsync(
                    scope,
                    loaded.Document,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask<CommandResult> ClearAsync(
        string ownerKey,
        string addonId,
        CancellationToken cancellationToken)
    {
        var scope = this.CreateScope(ownerKey, addonId);
        var gate = this.gates.GetOrAdd(
            scope.GateKey,
            static _ => new SemaphoreSlim(1, 1));

        await gate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            if (File.Exists(scope.FilePath))
            {
                File.Delete(scope.FilePath);
            }

            return CommandResult.Success();
        }
        catch (Exception ex)
            when (ex is IOException or
                  UnauthorizedAccessException)
        {
            return CommandResult.Failure(
                string.Concat(
                    "persistent storage could not be cleared: ",
                    ex.Message));
        }
        finally
        {
            gate.Release();
        }
    }

    private async ValueTask<DocumentReadResult> ReadDocumentCoreAsync(
        StorageScope scope,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(scope.FilePath))
        {
            return DocumentReadResult.Success(document: null);
        }

        try
        {
            var fileInfo = new FileInfo(scope.FilePath);

            if (fileInfo.Length > MaximumDocumentBytes)
            {
                return DocumentReadResult.Failure(
                    $"persistent storage exceeds the {MaximumDocumentBytes} byte quota");
            }

            var bytes = await File.ReadAllBytesAsync(
                    scope.FilePath,
                    cancellationToken)
                .ConfigureAwait(false);

            var document = JsonSerializer.Deserialize<StorageDocument>(
                bytes,
                jsonOptions);

            if (document == null)
            {
                return DocumentReadResult.Failure(
                    "persistent storage did not contain a document");
            }

            if (document.SchemaVersion != CurrentSchemaVersion)
            {
                return DocumentReadResult.Failure(
                    string.Concat(
                        "persistent storage uses unsupported schema version ",
                        document.SchemaVersion,
                        " (expected ",
                        CurrentSchemaVersion,
                        ")"));
            }

            if (!string.Equals(
                    document.AddonId,
                    scope.AddonId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    document.OwnerKey,
                    scope.OwnerKey,
                    StringComparison.Ordinal))
            {
                return DocumentReadResult.Failure(
                    "persistent storage scope metadata does not match the current addon owner");
            }

            if (document.Values.Count > MaximumKeyCount)
            {
                return DocumentReadResult.Failure(
                    $"persistent storage contains more than {MaximumKeyCount} keys");
            }

            document.Values = new Dictionary<string, JsonNode?>(
                document.Values,
                StringComparer.Ordinal);

            return DocumentReadResult.Success(document);
        }
        catch (Exception ex)
            when (ex is IOException or
                  UnauthorizedAccessException or
                  JsonException or
                  NotSupportedException)
        {
            return DocumentReadResult.Failure(
                string.Concat(
                    "persistent storage could not be read: ",
                    ex.Message));
        }
    }

    private async ValueTask<CommandResult> WriteDocumentCoreAsync(
        StorageScope scope,
        StorageDocument document,
        CancellationToken cancellationToken)
    {
        byte[] bytes;

        try
        {
            bytes = JsonSerializer.SerializeToUtf8Bytes(
                document,
                jsonOptions);
        }
        catch (Exception ex)
            when (ex is JsonException or
                  NotSupportedException)
        {
            return CommandResult.Failure(
                string.Concat(
                    "persistent storage could not be serialized: ",
                    ex.Message));
        }

        if (bytes.Length > MaximumDocumentBytes)
        {
            return CommandResult.Failure(
                string.Concat(
                    "persistent storage requires ",
                    bytes.Length,
                    " bytes, exceeding the ",
                    MaximumDocumentBytes,
                    " byte quota"));
        }

        var directory = Path.GetDirectoryName(scope.FilePath)!;
        var temporaryPath = Path.Combine(
            directory,
            string.Concat(
                ".",
                Path.GetFileName(scope.FilePath),
                ".",
                Guid.NewGuid().ToString("N"),
                ".tmp"));

        try
        {
            Directory.CreateDirectory(directory);

            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 64 * 1024,
                             FileOptions.Asynchronous |
                             FileOptions.WriteThrough))
            {
                await stream.WriteAsync(
                        bytes,
                        cancellationToken)
                    .ConfigureAwait(false);

                await stream.FlushAsync(cancellationToken)
                    .ConfigureAwait(false);

                stream.Flush(flushToDisk: true);
            }

            File.Move(
                temporaryPath,
                scope.FilePath,
                overwrite: true);

            return CommandResult.Success();
        }
        catch (Exception ex)
            when (ex is IOException or
                  UnauthorizedAccessException)
        {
            return CommandResult.Failure(
                string.Concat(
                    "persistent storage could not be written: ",
                    ex.Message));
        }
        finally
        {
            DeleteIfPresent(temporaryPath);
        }
    }

    private StorageScope CreateScope(
        string ownerKey,
        string addonId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(addonId);

        var ownerHash = Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(ownerKey)))
            .ToLowerInvariant();

        var directory = Path.Combine(
            this.rootDirectory,
            addonId);

        return new StorageScope(
            ownerKey,
            addonId,
            Path.Combine(directory, ownerHash + ".json"),
            string.Concat(addonId, "\n", ownerHash));
    }

    private static StorageDocument CreateDocument(
        string ownerKey,
        string addonId)
    {
        return new StorageDocument
        {
            SchemaVersion = CurrentSchemaVersion,
            AddonId = addonId,
            OwnerKey = ownerKey,
            Values = new Dictionary<string, JsonNode?>(
                StringComparer.Ordinal),
        };
    }

    private static void DeleteIfPresent(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    internal sealed record ReadResult
    {
        public required bool Succeeded { get; init; }

        public bool Found { get; init; }

        public JsonNode? Value { get; init; }

        public string Error { get; init; } = "";

        public static ReadResult Success(JsonNode value)
        {
            return new ReadResult
            {
                Succeeded = true,
                Found = true,
                Value = value,
            };
        }

        public static ReadResult Missing()
        {
            return new ReadResult
            {
                Succeeded = true,
            };
        }

        public static ReadResult Failure(string error)
        {
            return new ReadResult
            {
                Succeeded = false,
                Error = error,
            };
        }
    }

    internal sealed record CommandResult
    {
        public required bool Succeeded { get; init; }

        public string Error { get; init; } = "";

        public static CommandResult Success()
        {
            return new CommandResult
            {
                Succeeded = true,
            };
        }

        public static CommandResult Failure(string error)
        {
            return new CommandResult
            {
                Succeeded = false,
                Error = error,
            };
        }
    }

    private sealed record DocumentReadResult
    {
        public required bool Succeeded { get; init; }

        public StorageDocument? Document { get; init; }

        public string Error { get; init; } = "";

        public static DocumentReadResult Success(
            StorageDocument? document)
        {
            return new DocumentReadResult
            {
                Succeeded = true,
                Document = document,
            };
        }

        public static DocumentReadResult Failure(string error)
        {
            return new DocumentReadResult
            {
                Succeeded = false,
                Error = error,
            };
        }
    }

    private sealed class StorageDocument
    {
        public int SchemaVersion { get; set; }

        public string AddonId { get; set; } = "";

        public string OwnerKey { get; set; } = "";

        public Dictionary<string, JsonNode?> Values { get; set; } =
            new(StringComparer.Ordinal);
    }

    private sealed record StorageScope(
        string OwnerKey,
        string AddonId,
        string FilePath,
        string GateKey);
}
