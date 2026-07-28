namespace Net7ClientManager.Models;

using Net7ClientManager.Observations.Models;
using Net7ClientManager.Services;

public sealed record GroupSkillsPilotCard(
    int ProcessId,
    string Name,
    IReadOnlyList<GroupSkillsTargetRow> Targets,
    IReadOnlyList<GameShortcutPaletteEntry> Actions,
    int? FireAllIconItemTemplateId,
    IReadOnlyList<GroupSkillsFireAllWeapon> FireAllWeapons,
    int TooltipDelayMilliseconds);

public sealed record GroupSkillsFireAllWeapon(
    int ItemTemplateId,
    string Name,
    uint? TechLevel,
    bool RequiresAmmo,
    string? AmmoName,
    uint? AmmoTechLevel,
    int? AmmoCount,
    bool IsBusy,
    bool IsOperationallyReady,
    bool IsInPostDeadlineBusyTail,
    long? NominalRemainingMilliseconds,
    float? Range);

public sealed record GroupSkillsTargetRow(
    int? ProcessId,
    int GroupSlot,
    string Name,
    string Detail,
    int? ShieldPercent,
    int? HullPercent,
    int? ShieldCurrent,
    int? ShieldMaximum,
    int? HullCurrent,
    int? HullMaximum,
    int? ReactorCurrent,
    int? ReactorMaximum,
    int? ReactorPercent,
    bool IsLeader,
    bool IsLeaderTarget,
    bool HasTarget,
    ClientTargetKind Kind,
    ClientTargetRelation Relation,
    uint ObjectId,
    float? SurfaceDistance,
    int? OverallLevel,
    int? CombatLevel,
    int? ExploreLevel,
    int? TradeLevel);

public sealed record GroupSkillsActionResult(
    bool Succeeded,
    bool NeedsTarget,
    string Message)
{
    public static GroupSkillsActionResult Success(string message) =>
        new(true, NeedsTarget: false, message);

    public static GroupSkillsActionResult TargetNeeded(string message) =>
        new(Succeeded: false, NeedsTarget: true, message);

    public static GroupSkillsActionResult Failure(string message) =>
        new(Succeeded: false, NeedsTarget: false, message);
}
