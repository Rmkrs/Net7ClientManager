namespace Net7ClientManager.Addons.Loading;

using Net7ClientManager.Addons.Contracts;

internal sealed record AddonPackageReadResult(
    AddonDescriptor Descriptor,
    AddonPackage? Package);
