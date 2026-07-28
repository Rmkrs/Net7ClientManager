namespace Net7ClientManager.Navigation;

internal sealed record ForgeNavigationFullPackage(
    string PackagePath,
    string PackageSha256,
    long PackageSize,
    ForgeNavigationDataPackageManifest Manifest,
    ForgeNavigationEntitySnapshotDocument Snapshot,
    ForgeNavigationDataDocument Document,
    byte[] SnapshotBytes);
