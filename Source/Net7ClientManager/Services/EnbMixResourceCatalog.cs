namespace Net7ClientManager.Services;

using System.Text;

/// <summary>
/// Read-only index over Earth & Beyond's MIX1 art archives. MIX1 stores a
/// directory and a parallel filename table, so resources can be addressed by
/// their client-authored names without a hash database or external extractor.
/// </summary>
internal sealed class EnbMixResourceCatalog
{
    private const uint Mix1Magic = 0x3158494D;
    private const int HeaderLength = 16;
    private const int DirectoryEntryLength = 12;
    private const int MaximumEntryCount = 1_000_000;

    private readonly Dictionary<string, EnbMixResourceLocation> resources;

    private EnbMixResourceCatalog(
        Dictionary<string, EnbMixResourceLocation> resources)
    {
        this.resources = resources;
    }

    public static EnbMixResourceCatalog? Load(string mixFilesDirectory)
    {
        if (string.IsNullOrWhiteSpace(mixFilesDirectory) ||
            !Directory.Exists(mixFilesDirectory))
        {
            return null;
        }

        var resources = new Dictionary<string, EnbMixResourceLocation>(
            StringComparer.OrdinalIgnoreCase);

        IEnumerable<string> archivePaths;

        try
        {
            archivePaths = Directory
                .EnumerateFiles(
                    mixFilesDirectory,
                    "mixfile_art_*.mix",
                    SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            System.Security.SecurityException)
        {
            return null;
        }

        foreach (var archivePath in archivePaths)
        {
            try
            {
                foreach (var resource in ReadArchiveIndex(archivePath))
                {
                    // Later art archives win. This mirrors the normal patch
                    // layering expectation while remaining deterministic.
                    resources[resource.Name] = resource;
                }
            }
            catch (Exception ex) when (
                ex is IOException or
                UnauthorizedAccessException)
            {
                // One malformed or inaccessible archive must not suppress
                // icons from every other valid art archive.
            }
        }

        return resources.Count == 0
            ? null
            : new EnbMixResourceCatalog(resources);
    }

    public bool TryReadResource(
        string logicalResourceName,
        out byte[] bytes,
        out string resolvedResourceName)
    {
        bytes = [];
        resolvedResourceName = "";

        foreach (var candidate in GetResourceNameCandidates(logicalResourceName))
        {
            if (!this.resources.TryGetValue(candidate, out var resource))
            {
                continue;
            }

            try
            {
                using var stream = new FileStream(
                    resource.ArchivePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);

                if (resource.DataOffset < 0 ||
                    resource.DataLength <= 0 ||
                    resource.DataOffset > stream.Length - resource.DataLength ||
                    resource.DataLength > int.MaxValue)
                {
                    continue;
                }

                stream.Position = resource.DataOffset;
                bytes = new byte[(int)resource.DataLength];
                stream.ReadExactly(bytes);
                resolvedResourceName = resource.Name;
                return true;
            }
            catch (Exception ex) when (
                ex is IOException or
                UnauthorizedAccessException)
            {
                // Try the next compatible name, if one exists.
            }
        }

        return false;
    }

    private static IEnumerable<string> GetResourceNameCandidates(
        string logicalResourceName)
    {
        var trimmed = logicalResourceName.Trim();

        if (trimmed.Length == 0)
        {
            yield break;
        }

        yield return trimmed;

        if (string.Equals(
                Path.GetExtension(trimmed),
                ".tga",
                StringComparison.OrdinalIgnoreCase))
        {
            yield return Path.ChangeExtension(trimmed, ".dds");
        }
    }

    private static IReadOnlyList<EnbMixResourceLocation> ReadArchiveIndex(
        string archivePath)
    {
        using var stream = new FileStream(
            archivePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);

        if (stream.Length < HeaderLength ||
            reader.ReadUInt32() != Mix1Magic)
        {
            throw new InvalidDataException("The archive is not an Earth & Beyond MIX1 file.");
        }

        var directoryOffset = reader.ReadUInt32();
        var filenameTableOffset = reader.ReadUInt32();
        _ = reader.ReadUInt32();

        if (directoryOffset > stream.Length - sizeof(uint) ||
            filenameTableOffset > stream.Length - sizeof(uint))
        {
            throw new InvalidDataException("The MIX1 index offsets are outside the archive.");
        }

        stream.Position = directoryOffset;
        var entryCount = reader.ReadUInt32();

        if (entryCount > MaximumEntryCount ||
            (long)directoryOffset + sizeof(uint) + (long)entryCount * DirectoryEntryLength > stream.Length)
        {
            throw new InvalidDataException("The MIX1 directory count is invalid.");
        }

        var entries = new EnbMixDirectoryEntry[checked((int)entryCount)];

        for (var index = 0; index < entries.Length; index++)
        {
            _ = reader.ReadUInt32(); // Entry ID is not needed because names are explicit.
            var dataOffset = reader.ReadUInt32();
            var dataLength = reader.ReadUInt32();
            entries[index] = new EnbMixDirectoryEntry(dataOffset, dataLength);
        }

        stream.Position = filenameTableOffset;
        var filenameCount = reader.ReadUInt32();

        if (filenameCount != entryCount)
        {
            throw new InvalidDataException("The MIX1 filename table does not match its directory.");
        }

        var results = new List<EnbMixResourceLocation>(entries.Length);

        for (var index = 0; index < entries.Length; index++)
        {
            var storedLength = reader.ReadByte();

            if (storedLength == 0)
            {
                throw new InvalidDataException("A MIX1 filename has zero length.");
            }

            var storedName = reader.ReadBytes(storedLength);

            if (storedName.Length != storedLength)
            {
                throw new EndOfStreamException("The MIX1 filename table ended unexpectedly.");
            }

            var nullIndex = Array.IndexOf(storedName, (byte)0);
            var nameLength = nullIndex >= 0
                ? nullIndex
                : storedName.Length;
            var name = Encoding.ASCII.GetString(storedName, 0, nameLength);
            var entry = entries[index];

            if (name.Length == 0 ||
                entry.DataLength == 0 ||
                entry.DataOffset > stream.Length - entry.DataLength)
            {
                continue;
            }

            results.Add(
                new EnbMixResourceLocation(
                    name,
                    archivePath,
                    entry.DataOffset,
                    entry.DataLength));
        }

        return results;
    }

    private readonly record struct EnbMixDirectoryEntry(
        long DataOffset,
        long DataLength);

    private sealed record EnbMixResourceLocation(
        string Name,
        string ArchivePath,
        long DataOffset,
        long DataLength);
}
