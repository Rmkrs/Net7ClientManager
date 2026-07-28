namespace Net7ClientManager.Addons.Loading;

using Net7ClientManager.Addons.Contracts;

internal sealed record AddonPackageAcquisitionResult
{
    public required AddonCommandResult Result { get; init; }

    public string PackagePath { get; init; } = "";

    public static AddonPackageAcquisitionResult Success(string packagePath)
    {
        return new AddonPackageAcquisitionResult
        {
            Result = AddonCommandResult.Success(),
            PackagePath = packagePath,
        };
    }

    public static AddonPackageAcquisitionResult Failure(string error)
    {
        return new AddonPackageAcquisitionResult
        {
            Result = AddonCommandResult.Failure(error),
        };
    }
}
