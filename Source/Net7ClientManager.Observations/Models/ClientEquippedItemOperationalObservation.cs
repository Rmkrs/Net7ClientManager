namespace Net7ClientManager.Observations.Models;

public sealed record ClientEquippedItemOperationalObservation
{
    private const uint BusyStateFlag = 0x00000080;
    private const uint ActivationPhaseStateFlag = 0x00000001;

    public bool IsAvailable { get; init; }

    public string Status { get; init; } = "";

    public bool IsOccupied { get; init; }

    public uint CurrentClientTime { get; init; }

    public uint ItemStatePropertyAddress { get; init; }

    public uint ItemStateValidState { get; init; }

    public uint? RawItemState { get; init; }

    public uint ReadyTimePropertyAddress { get; init; }

    public uint ReadyTimeValidState { get; init; }

    public ulong? NominalReadyAtClientTime { get; init; }

    public uint TargetRangePropertyAddress { get; init; }

    public float? TargetRange { get; init; }

    public uint EffectsObjectAddress { get; init; }

    public bool EffectsObjectIsValid { get; init; }

    public uint EffectRangeAddress { get; init; }

    public float? EffectRange { get; init; }

    public uint EffectUsageAddress { get; init; }

    public uint? EffectUsage { get; init; }

    public uint EffectTargetsAddress { get; init; }

    public uint? EffectTargets { get; init; }

    public uint EffectValidityAddress { get; init; }

    public uint? EffectValidity { get; init; }

    public bool? IsNativeAction =>
        this.EffectUsage.HasValue
            ? (this.EffectUsage.Value & 0x00000005) != 0
            : null;

    public bool HasItemState =>
        this.RawItemState.HasValue;

    /*
     * Bit 0x80 is the common operationally-unavailable flag. It was
     * observed during both activated-item cooldown and newly installed
     * equipment warm-up. The current snapshot alone therefore cannot
     * distinguish Installing from CoolingDown; that requires transition
     * history from an earlier occupied/empty or ready/busy state.
     */
    public bool IsBusy =>
        this.RawItemState.HasValue &&
        (this.RawItemState.Value & BusyStateFlag) != 0;

    public bool IsOperationallyReady =>
        this.IsOccupied &&
        this.RawItemState.HasValue &&
        !this.IsBusy;

    /*
     * Bit 0x01 was observed during the immediate weapon firing phase,
     * then cleared while bit 0x80 remained set. Its wider meaning across
     * other item categories is intentionally left tentative.
     */
    public bool HasActivationPhaseFlag =>
        this.RawItemState.HasValue &&
        (this.RawItemState.Value &
         ActivationPhaseStateFlag) != 0;

    public long? NominalRemainingMilliseconds
    {
        get
        {
            if (!this.NominalReadyAtClientTime.HasValue)
            {
                return null;
            }

            var readyAt =
                this.NominalReadyAtClientTime.Value;

            if (readyAt <= this.CurrentClientTime)
            {
                return 0;
            }

            var remaining =
                readyAt - this.CurrentClientTime;

            return remaining > long.MaxValue
                ? long.MaxValue
                : checked((long)remaining);
        }
    }

    /*
     * The client can keep bit 0x80 set for roughly one second after the
     * nominal deadline. ItemState is therefore authoritative for actual
     * readiness; ReadyTime is an ETA rather than a release guarantee.
     */
    public bool IsInPostDeadlineBusyTail =>
        this.IsOccupied &&
        this.IsBusy &&
        this.NominalRemainingMilliseconds == 0;

    public static ClientEquippedItemOperationalObservation Unavailable(
        string status)
    {
        return new ClientEquippedItemOperationalObservation
        {
            Status = status,
        };
    }
}
