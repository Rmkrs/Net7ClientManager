namespace Net7ClientManager.Navigation;

using System.IO.Compression;
using System.Text.Json;

internal sealed class ForgeNavigationUpdatePackageReader
{
    private const long MaximumPackageBytes = 64L * 1024 * 1024;
    private const int MaximumManifestBytes = 64 * 1024;
    private const int MaximumPayloadBytes = 60 * 1024 * 1024;

    public ForgeNavigationUpdatePackage Read(
        string packagePath,
        ForgeNavigationUpdateResponse expected)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        ArgumentNullException.ThrowIfNull(expected);

        if (!ForgeNavigationHash.IsSha256(expected.PackageSha256) ||
            !ForgeNavigationHash.IsSha256(expected.ResultSnapshotSha256) ||
            expected.PackageSize <= 0 ||
            expected.PackageSize > MaximumPackageBytes)
        {
            throw new InvalidOperationException(
                "Forge returned invalid navigation update metadata.");
        }

        if (string.Equals(expected.Mode, "full", StringComparison.Ordinal))
        {
            var full = ForgeNavigationDataPackageLoader.LoadFull(
                packagePath,
                expected.PackageSha256,
                expected.PackageSize);
            ValidateExpectedManifest(full.Manifest, expected);

            return new ForgeNavigationUpdatePackage(
                packagePath,
                full.PackageSha256,
                full.PackageSize,
                full.Manifest,
                full.SnapshotBytes,
                full.Snapshot,
                null);
        }

        if (!string.Equals(expected.Mode, "delta", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Unsupported Forge navigation update mode '{expected.Mode}'.");
        }

        var packageInfo = new FileInfo(packagePath);

        if (!packageInfo.Exists ||
            packageInfo.Length != expected.PackageSize ||
            packageInfo.Length > MaximumPackageBytes)
        {
            throw new InvalidOperationException(
                "Downloaded navigation update has an invalid size.");
        }

        var packageSha256 = ForgeNavigationHash.ComputeFileSha256(packagePath);

        if (!string.Equals(
                packageSha256,
                expected.PackageSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Downloaded navigation update has an invalid hash.");
        }

        using var stream = new FileStream(
            packagePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        using var archive = new ZipArchive(
            stream,
            ZipArchiveMode.Read,
            leaveOpen: false);
        var manifestBytes = ForgeNavigationDataPackageLoader.ReadEntry(
            archive,
            "net7forge.json",
            MaximumManifestBytes);
        var manifest = JsonSerializer.Deserialize<ForgeNavigationDataPackageManifest>(
                manifestBytes,
                ForgeNavigationDataJson.DistributionReadOptions) ??
            throw new InvalidOperationException(
                "Navigation update manifest is empty.");
        ForgeNavigationDataPackageLoader.ValidateManifest(
            manifest,
            "delta");
        ValidateExpectedManifest(manifest, expected);
        ForgeNavigationDataPackageLoader.ValidateArchiveEntries(
            archive,
            manifest.PayloadEntry);
        var payloadBytes = ForgeNavigationDataPackageLoader.ReadEntry(
            archive,
            manifest.PayloadEntry,
            MaximumPayloadBytes);
        var payloadSha256 = ForgeNavigationHash.ComputeSha256(payloadBytes);

        if (!string.Equals(
                payloadSha256,
                manifest.PayloadSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Navigation delta payload hash does not match its manifest.");
        }

        var delta = JsonSerializer.Deserialize<ForgeNavigationEntityDeltaDocument>(
                payloadBytes,
                ForgeNavigationDataJson.DistributionReadOptions) ??
            throw new InvalidOperationException(
                "Navigation delta payload is empty.");
        ForgeNavigationDataPackageLoader.ValidateDelta(delta);

        if (manifest.BaseRevision != delta.BaseRevision ||
            manifest.DataRevision != delta.TargetRevision ||
            manifest.ContractVersion != delta.ContractVersion ||
            !string.Equals(
                manifest.ResultSnapshotSha256,
                delta.ResultSnapshotSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Navigation delta manifest and payload disagree.");
        }

        return new ForgeNavigationUpdatePackage(
            packagePath,
            packageSha256,
            packageInfo.Length,
            manifest,
            payloadBytes,
            null,
            delta);
    }

    private static void ValidateExpectedManifest(
        ForgeNavigationDataPackageManifest manifest,
        ForgeNavigationUpdateResponse expected)
    {
        if (manifest.ContractVersion != expected.ContractVersion ||
            manifest.DataRevision != expected.ToRevision ||
            manifest.BaseRevision !=
                (string.Equals(expected.Mode, "delta", StringComparison.Ordinal)
                    ? expected.FromRevision
                    : null) ||
            !string.Equals(manifest.Mode, expected.Mode, StringComparison.Ordinal) ||
            !string.Equals(
                manifest.ResultSnapshotSha256,
                expected.ResultSnapshotSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Forge update response and package manifest disagree.");
        }
    }
}
