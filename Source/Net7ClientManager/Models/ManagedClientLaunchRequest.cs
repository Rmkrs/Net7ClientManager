namespace Net7ClientManager.Models;

public sealed class ManagedClientLaunchRequest
{
    public ManagedClientLaunchKind Kind { get; init; }

    public ManagedClientPlacementPolicy PlacementPolicy { get; init; }

    public Guid? ProfileId { get; init; }

    public Guid? TargetSlotId { get; init; }

    public Guid? AccountId { get; init; }

    public Guid? CharacterId { get; init; }

    public bool SkipIntro { get; init; } = true;

    public bool AutoLogin { get; init; }

    public bool AutoEnterGame { get; init; }

    public int? HostWidth { get; init; }

    public int? HostHeight { get; init; }

    public bool HasHostSize =>
        this.HostWidth > 0 &&
        this.HostHeight > 0;

    public int? GameResolutionWidth { get; init; }

    public int? GameResolutionHeight { get; init; }

    public bool HasGameResolution =>
        this.GameResolutionWidth > 0 &&
        this.GameResolutionHeight > 0;

    public string DisplayName { get; init; } = "Managed client";
}
