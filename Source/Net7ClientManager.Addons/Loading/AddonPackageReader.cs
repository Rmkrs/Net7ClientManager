namespace Net7ClientManager.Addons.Loading;

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Net7ClientManager.Addons.Contracts;

internal sealed class AddonPackageReader
{
    private const int MaximumEntryCount = 256;
    private const long MaximumPackageSize = 16 * 1024 * 1024;
    private const long MaximumEntrySize = 2 * 1024 * 1024;
    private const long MaximumExpandedPackageSize = 8 * 1024 * 1024;

    private static readonly JsonSerializerOptions jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public AddonPackageReadResult ReadArchive(string packagePath)
    {
        var fileName = Path.GetFileName(packagePath);

        try
        {
            var packageLength = new FileInfo(packagePath).Length;

            if (packageLength <= 0 ||
                packageLength > MaximumPackageSize)
            {
                return Invalid(
                    fileName,
                    packagePath,
                    "The addon package has an invalid size.");
            }

            var packageBytes = File.ReadAllBytes(packagePath);
            var packageSha256 = Convert.ToHexStringLower(
                SHA256.HashData(packageBytes));

            using var stream = new MemoryStream(
                packageBytes,
                writable: false);
            using var archive = new ZipArchive(
                stream,
                ZipArchiveMode.Read,
                leaveOpen: false);

            var entries = ReadArchiveEntries(archive);
            var manifest = DeserializeManifest(
                entries.GetValueOrDefault("addon.json"));
            var validationError = AddonManifestValidator.Validate(manifest);

            if (validationError != null)
            {
                return Invalid(
                    fileName,
                    packagePath,
                    validationError,
                    manifest);
            }

            var release = DeserializeRelease(
                entries.GetValueOrDefault("net7forge.json"));
            var releaseError = ValidateRelease(
                manifest!,
                release);

            if (releaseError != null)
            {
                return Invalid(
                    fileName,
                    packagePath,
                    releaseError,
                    manifest);
            }

            return CreatePackage(
                fileName,
                packagePath,
                manifest!,
                entries,
                packageSha256,
                release,
                isDevelopment: false);
        }
        catch (Exception ex)
            when (ex is IOException or
                  UnauthorizedAccessException or
                  InvalidDataException or
                  JsonException or
                  InvalidOperationException)
        {
            return Invalid(
                fileName,
                packagePath,
                string.Concat(
                    "Could not load addon package: ",
                    ex.Message));
        }
    }

    public AddonPackageReadResult ReadDirectory(string directory)
    {
        var directoryName = Path.GetFileName(directory);
        var manifestPath = Path.Combine(directory, "addon.json");

        if (!File.Exists(manifestPath))
        {
            return Invalid(
                directoryName,
                directory,
                "addon.json is missing.");
        }

        try
        {
            var manifest = JsonSerializer.Deserialize<AddonManifest>(
                File.ReadAllText(manifestPath),
                jsonOptions);
            var validationError = AddonManifestValidator.Validate(manifest);

            if (validationError != null)
            {
                return Invalid(
                    directoryName,
                    directory,
                    validationError,
                    manifest);
            }

            var entries = ReadDirectoryEntries(directory);
            return CreatePackage(
                directoryName,
                directory,
                manifest!,
                entries,
                packageSha256: "",
                release: null,
                isDevelopment: true);
        }
        catch (Exception ex)
            when (ex is IOException or
                  UnauthorizedAccessException or
                  JsonException or
                  InvalidOperationException)
        {
            return Invalid(
                directoryName,
                directory,
                string.Concat(
                    "Could not load addon source: ",
                    ex.Message));
        }
    }

    public string CalculateArchiveContentFingerprint(
        string packagePath)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var entries = ReadArchiveEntries(archive);
        entries.Remove("net7forge.json");
        return CalculateContentFingerprint(entries);
    }

    public string CalculateDirectoryContentFingerprint(
        string directory)
    {
        return CalculateContentFingerprint(
            ReadDirectoryEntries(directory));
    }

    private static AddonPackageReadResult CreatePackage(
        string sourceName,
        string sourcePath,
        AddonManifest manifest,
        IReadOnlyDictionary<string, byte[]> entries,
        string packageSha256,
        AddonPackageReleaseDocument? release,
        bool isDevelopment)
    {
        if (!TryNormalizeRelativePath(
                manifest.EntryPoint,
                out var entryPointName) ||
            !string.Equals(
                Path.GetExtension(entryPointName),
                ".lua",
                StringComparison.OrdinalIgnoreCase))
        {
            return Invalid(
                sourceName,
                sourcePath,
                "entryPoint must resolve to a .lua file inside the addon package.",
                manifest);
        }

        if (!entries.TryGetValue(
                entryPointName,
                out var entryPointBytes))
        {
            return Invalid(
                sourceName,
                sourcePath,
                string.Concat(
                    "Entry point '",
                    manifest.EntryPoint,
                    "' does not exist."),
                manifest);
        }

        Dictionary<string, string> modules = new(
            StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            if (!entry.Key.StartsWith(
                    "lib/",
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    Path.GetExtension(entry.Key),
                    ".lua",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relative = entry.Key["lib/".Length..];
            var moduleName = Path.ChangeExtension(
                    relative,
                    extension: null)
                .Replace('/', '.');

            if (string.IsNullOrWhiteSpace(moduleName) ||
                !modules.TryAdd(
                    moduleName,
                    DecodeText(entry.Value)))
            {
                return Invalid(
                    sourceName,
                    sourcePath,
                    string.Concat(
                        "Duplicate or invalid module name '",
                        moduleName,
                        "'."),
                    manifest);
            }
        }

        var descriptor = new AddonDescriptor
        {
            DirectoryName = sourceName,
            DirectoryPath = sourcePath,
            Manifest = manifest,
            IsValid = true,
            IsDevelopment = isDevelopment,
            PackageSha256 = packageSha256,
            PublisherId = release?.PublisherId ?? "",
            PublisherName = release?.PublisherName ?? "",
        };

        return new AddonPackageReadResult(
            descriptor,
            new AddonPackage
            {
                Descriptor = descriptor,
                Manifest = manifest,
                EntryPointSourceName = string.Concat(
                    "@addon/",
                    manifest.Id,
                    "/",
                    entryPointName),
                EntryPointSource = DecodeText(entryPointBytes),
                Modules = modules,
                PackageSha256 = packageSha256,
                Release = release,
                IsDevelopment = isDevelopment,
            });
    }

    private static Dictionary<string, byte[]> ReadArchiveEntries(
        ZipArchive archive)
    {
        if (archive.Entries.Count == 0 ||
            archive.Entries.Count > MaximumEntryCount)
        {
            throw new InvalidDataException(
                "The package has an invalid entry count.");
        }

        Dictionary<string, byte[]> entries = new(
            StringComparer.OrdinalIgnoreCase);
        long expandedSize = 0;

        foreach (var entry in archive.Entries)
        {
            if (!TryNormalizeRelativePath(
                    entry.FullName,
                    out var entryName) ||
                entries.ContainsKey(entryName))
            {
                throw new InvalidDataException(
                    string.Concat(
                        "Invalid or duplicate package entry '",
                        entry.FullName,
                        "'."));
            }

            var extension = Path.GetExtension(entryName);

            if (!string.Equals(
                    extension,
                    ".json",
                    StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(
                    extension,
                    ".lua",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    string.Concat(
                        "Package entry type is not allowed: ",
                        entryName));
            }

            if (entry.Length > MaximumEntrySize)
            {
                throw new InvalidDataException(
                    string.Concat(
                        "Package entry is too large: ",
                        entryName));
            }

            expandedSize += entry.Length;

            if (expandedSize > MaximumExpandedPackageSize)
            {
                throw new InvalidDataException(
                    "The expanded package is too large.");
            }

            using var source = entry.Open();
            using var target = new MemoryStream();
            source.CopyTo(target);
            entries.Add(entryName, target.ToArray());
        }

        return entries;
    }

    private static Dictionary<string, byte[]> ReadDirectoryEntries(
        string directory)
    {
        string[] files =
        [
            .. Directory.EnumerateFiles(
                    directory,
                    "*",
                    SearchOption.AllDirectories)
                .Where(file => !IsDevelopmentToolingPath(
                    directory,
                    file)),
        ];

        if (files.Length == 0 ||
            files.Length > MaximumEntryCount)
        {
            throw new InvalidOperationException(
                "The addon source has an invalid file count.");
        }

        Dictionary<string, byte[]> entries = new(
            StringComparer.OrdinalIgnoreCase);
        long expandedSize = 0;

        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(directory, file);

            if (!TryNormalizeRelativePath(
                    relative,
                    out var entryName))
            {
                throw new InvalidOperationException(
                    string.Concat(
                        "Invalid addon source path '",
                        relative,
                        "'."));
            }

            var fileLength = new FileInfo(file).Length;

            if (fileLength > MaximumEntrySize)
            {
                throw new InvalidOperationException(
                    string.Concat(
                        "Addon source file is too large: ",
                        entryName));
            }

            expandedSize += fileLength;

            if (expandedSize > MaximumExpandedPackageSize)
            {
                throw new InvalidOperationException(
                    "The addon source is too large.");
            }

            if (!entries.TryAdd(
                    entryName,
                    File.ReadAllBytes(file)))
            {
                throw new InvalidOperationException(
                    string.Concat(
                        "Duplicate addon source path '",
                        entryName,
                        "'."));
            }
        }

        return entries;
    }

    private static AddonManifest? DeserializeManifest(byte[]? bytes)
    {
        return bytes == null
            ? null
            : JsonSerializer.Deserialize<AddonManifest>(
                bytes,
                jsonOptions);
    }

    private static AddonPackageReleaseDocument? DeserializeRelease(
        byte[]? bytes)
    {
        return bytes == null
            ? null
            : JsonSerializer.Deserialize<AddonPackageReleaseDocument>(
                bytes,
                jsonOptions);
    }

    private static string? ValidateRelease(
        AddonManifest manifest,
        AddonPackageReleaseDocument? release)
    {
        if (release == null)
        {
            return "net7forge.json is missing or invalid.";
        }

        if (release.FormatVersion !=
            AddonPackageReleaseDocument.CurrentFormatVersion)
        {
            return string.Concat(
                "Unsupported addon package format ",
                release.FormatVersion,
                ".");
        }

        if (!string.Equals(
                release.AddonId,
                manifest.Id,
                StringComparison.Ordinal) ||
            !string.Equals(
                release.Version,
                manifest.Version,
                StringComparison.Ordinal))
        {
            return "net7forge.json does not match addon.json.";
        }

        if (string.IsNullOrWhiteSpace(release.PublisherId) ||
            string.IsNullOrWhiteSpace(release.PublisherName) ||
            release.PublishedAt == default)
        {
            return "net7forge.json contains incomplete publisher metadata.";
        }

        return null;
    }

    private static bool IsDevelopmentToolingPath(
        string directory,
        string file)
    {
        var relative = Path.GetRelativePath(directory, file)
            .Replace('\\', '/');
        return relative.StartsWith(
            ".net7-editor/",
            StringComparison.OrdinalIgnoreCase);
    }

    private static string CalculateContentFingerprint(
        IReadOnlyDictionary<string, byte[]> entries)
    {
        using var hash = IncrementalHash.CreateHash(
            HashAlgorithmName.SHA256);

        Span<byte> length = stackalloc byte[sizeof(int)];

        foreach (var entry in entries.OrderBy(
                     item => item.Key,
                     StringComparer.Ordinal))
        {
            var normalizedBytes = NormalizeTextBytes(
                entry.Key,
                entry.Value);
            var nameBytes = Encoding.UTF8.GetBytes(entry.Key);

            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(
                length,
                nameBytes.Length);
            hash.AppendData(length);
            hash.AppendData(nameBytes);

            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(
                length,
                normalizedBytes.Length);
            hash.AppendData(length);
            hash.AppendData(normalizedBytes);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static byte[] NormalizeTextBytes(
        string entryName,
        byte[] value)
    {
        var extension = Path.GetExtension(entryName);

        if (!string.Equals(
                extension,
                ".json",
                StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(
                extension,
                ".lua",
                StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        var normalized = DecodeText(value)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .TrimEnd('\n');

        return Encoding.UTF8.GetBytes(
            string.Concat(normalized, "\n"));
    }

    private static string DecodeText(byte[] value)
    {
        return Encoding.UTF8.GetString(value)
            .TrimStart('\uFEFF');
    }

    private static bool TryNormalizeRelativePath(
        string value,
        out string normalized)
    {
        normalized = value.Replace('\\', '/').Trim('/');

        if (string.IsNullOrWhiteSpace(normalized) ||
            Path.IsPathRooted(value))
        {
            return false;
        }

        return normalized
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .All(segment => segment is not "." and not "..");
    }

    private static AddonPackageReadResult Invalid(
        string sourceName,
        string sourcePath,
        string error,
        AddonManifest? manifest = null)
    {
        return new AddonPackageReadResult(
            new AddonDescriptor
            {
                DirectoryName = sourceName,
                DirectoryPath = sourcePath,
                Manifest = manifest,
                IsValid = false,
                Error = error,
                IsDevelopment = Directory.Exists(sourcePath),
            },
            Package: null);
    }
}
