namespace Net7ClientManager.Navigation;

internal sealed record ForgeNavigationUpdatePackage(
    string PackagePath,
    string PackageSha256,
    long PackageSize,
    ForgeNavigationDataPackageManifest Manifest,
    byte[] PayloadBytes,
    ForgeNavigationEntitySnapshotDocument? FullSnapshot,
    ForgeNavigationEntityDeltaDocument? DeltaDocument);
