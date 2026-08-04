namespace Net7ClientManager.Observations.Models;

using System.Globalization;

public sealed record ClientBuffObservation
{
    public int Slot { get; init; }

    public string PropertyPrefix { get; init; } = "";

    public bool IsPresent { get; init; }

    public string Status { get; init; } = "";

    public uint CurrentClientTime { get; init; }

    public uint RecordAddress { get; init; }

    public uint BuffTypePropertyAddress { get; init; }

    public bool BuffTypeValid { get; init; }

    /*
     * BuffType is the authoritative occupancy signal for the fixed
     * sixteen-slot BuffArray. Empty records remain allocated and valid,
     * but expose an empty BuffType string.
     */
    public string? BuffType { get; init; }

    public uint ScrubTypeNamePropertyAddress { get; init; }

    public bool ScrubTypeNameValid { get; init; }

    public string? ScrubTypeName { get; init; }

    public uint IsPermanentPropertyAddress { get; init; }

    public bool IsPermanentValid { get; init; }

    public bool? IsPermanent { get; init; }

    public uint BuffRemovalTimePropertyAddress { get; init; }

    public uint BuffRemovalTimeValidState { get; init; }

    /*
     * BuffRemovalTime uses the same UInt64-style AuxData time property as
     * equipped-item ReadyTime. The low word lives at +0x88 and the high
     * word at +0x8C. Observed timed buffs store an absolute client-time
     * deadline; the observed permanent Combat Trance buff used zero.
     */
    public ulong? NominalRemovalAtClientTime { get; init; }

    public bool IsOccupied =>
        this.BuffTypeValid &&
        !string.IsNullOrWhiteSpace(
            this.BuffType);

    public string DisplayName
    {
        get
        {
            var buffType = this.BuffType;

            return string.IsNullOrWhiteSpace(
                    buffType)
                ? string.Create(CultureInfo.InvariantCulture, $"Buff slot {this.Slot}")
                : buffType.Replace(
                    '_',
                    ' ');
        }
    }

    public bool IsTimed =>
        this.IsOccupied &&
        this.IsPermanent == false;

    public long? NominalRemainingMilliseconds
    {
        get
        {
            if (!this.IsTimed ||
                !this.NominalRemovalAtClientTime.HasValue)
            {
                return null;
            }

            var removalAt =
                this.NominalRemovalAtClientTime.Value;

            if (removalAt <= this.CurrentClientTime)
            {
                return 0;
            }

            var remaining =
                removalAt - this.CurrentClientTime;

            return remaining > long.MaxValue
                ? long.MaxValue
                : checked((long)remaining);
        }
    }

    /*
     * Occupancy remains authoritative until BuffType is cleared. We have
     * proved the nominal deadline but have not yet measured whether buff
     * cleanup has a short post-deadline tail, so this is deliberately not
     * named IsExpired.
     */
    public bool IsNominallyExpired =>
        this.IsTimed &&
        this.NominalRemainingMilliseconds == 0;
}
