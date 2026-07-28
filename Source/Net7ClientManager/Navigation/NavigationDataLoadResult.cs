namespace Net7ClientManager.Navigation;

internal sealed record NavigationDataLoadResult(
    GalaxyDataSet DataSet,
    bool UsedFallback);
