namespace Net7ClientManager.Navigation;

public sealed record NavigationTargetSelectionResult
{
    public bool Succeeded { get; init; }

    public bool IsTransientFailure { get; init; }

    public string Error { get; init; } = "";

    public string TargetName { get; init; } = "";

    public string TargetKind { get; init; } = "";

    public uint ObjectId { get; init; }

    public uint ActiveSectorNumber { get; init; }

    public string TargetCycleDirection { get; init; } = "";

    public int TargetCycleClickCount { get; init; }

    public IReadOnlyList<uint> TargetCycleObservedObjectIds
    { get; init; } = [];

    public bool ClearTargetWasSent { get; init; }

    public uint SelectedObjectId { get; init; }

    public static NavigationTargetSelectionResult Failure(
        string error,
        bool isTransientFailure = false)
    {
        return new NavigationTargetSelectionResult
        {
            Error = error,
            IsTransientFailure = isTransientFailure,
        };
    }
}
