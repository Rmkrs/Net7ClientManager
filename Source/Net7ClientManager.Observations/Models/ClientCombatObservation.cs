namespace Net7ClientManager.Observations.Models;

public sealed record ClientCombatObservation
{
    public bool IsAvailable { get; init; }

    public string Status { get; init; } = "";

    public DateTimeOffset? ObservedAt { get; init; }

    public uint ClientContextAddress { get; init; }

    public uint ConnectionWrapperAddress { get; init; }

    public uint MusicManagerAddress { get; init; }

    public int CurrentMusicTypeRaw { get; init; } = -1;

    public int PreviousMusicTypeRaw { get; init; } = -1;

    public bool IsCombatMusicContext =>
        this.CurrentMusicTypeRaw ==
        (int)ClientMusicType.Combat;

    public ClientCombatPollingMode PollingMode { get; init; }

    public ClientCombatArmingSource ArmingSource { get; init; }

    public int PacketPollingIntervalMicroseconds { get; init; }

    public int ContextSamplingIntervalMilliseconds { get; init; }

    public int CoolingRemainingMilliseconds { get; init; }

    public int WeaponArmRemainingMilliseconds { get; init; }

    public int TrackedWeaponCount { get; init; }

    public long WeaponStateSampleCount { get; init; }

    public long WeaponActivationCount { get; init; }

    public long WeaponActivationPhaseCount { get; init; }

    public long WeaponBusyTransitionCount { get; init; }

    public long WeaponStateReadFailureCount { get; init; }

    public DateTimeOffset? LastWeaponActivationObservedAt { get; init; }

    public long PacketPollCount { get; init; }

    public long IdlePacketPollCount { get; init; }

    public long ArmedPacketPollCount { get; init; }

    public long BurstPacketPollCount { get; init; }

    public long ContextSampleCount { get; init; }

    public long CachedPacketObservationCount { get; init; }

    public long UniquePacketCount { get; init; }

    public long DamagePacketCount { get; init; }

    public long DuplicatePacketObservationCount { get; init; }

    public long PacketReadFailureCount { get; init; }

    public long ContextReadFailureCount { get; init; }

    public int ResolvedIdentityCount { get; init; }

    public int PendingIdentityCount { get; init; }

    public long IdentityResolutionFailureCount { get; init; }

    public DateTimeOffset? LastDamageObservedAt { get; init; }

    public IReadOnlyList<ClientCombatEventObservation> RecentEvents { get; init; } = [];

    public static ClientCombatObservation Unavailable(
        string status,
        uint clientContextAddress = 0)
    {
        return new ClientCombatObservation
        {
            Status = status,
            ClientContextAddress = clientContextAddress,
            PollingMode = ClientCombatPollingMode.Dormant,
        };
    }
}
