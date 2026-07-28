namespace Net7ClientManager.Observations.Observers;

internal static class ClientProgressionRules
{
    public const int MaximumLevel = 50;

    private const int PostMaximumExperienceRequirement =
        422_500;

    // Current level -> XP required for the next Net-7 progression step.
    private static readonly int[] experienceToNextLevel =
    [
        10_000,
        12_500,
        15_000,
        17_500,
        20_000,
        22_500,
        27_500,
        32_500,
        37_500,
        42_500,
        47_500,
        52_500,
        57_500,
        62_500,
        67_500,
        72_500,
        77_500,
        82_500,
        87_500,
        92_500,
        97_500,
        102_500,
        102_500,
        102_500,
        102_500,
        102_500,
        102_500,
        102_500,
        102_500,
        102_500,
        102_500,
        112_500,
        112_500,
        122_500,
        132_500,
        142_500,
        152_500,
        162_500,
        182_500,
        202_500,
        222_500,
        242_500,
        262_500,
        282_500,
        302_500,
        322_500,
        342_500,
        362_500,
        382_500,
        402_500,
        PostMaximumExperienceRequirement,
    ];

    // Generic hull-stage thresholds shared by the original profession data.
    // Live samples validated raw stages 3 and 5 against overall levels 70 and 126.
    private static readonly int[] hullUpgradeOverallLevels =
    [
        0,
        10,
        30,
        50,
        75,
        100,
        135,
    ];

    public static bool TryGetExperienceToNextLevel(
        int level,
        out int experience)
    {
        experience = 0;

        if (level < 0)
        {
            return false;
        }

        experience = level <= MaximumLevel
            ? experienceToNextLevel[level]
            : PostMaximumExperienceRequirement;

        return true;
    }

    public static bool TryGetHullUpgradeStage(
        int rawHullUpgradeLevel,
        out int tier,
        out int currentOverallLevel,
        out int? nextOverallLevel)
    {
        tier = 0;
        currentOverallLevel = 0;
        nextOverallLevel = null;

        if (rawHullUpgradeLevel < 0 ||
            rawHullUpgradeLevel >=
                hullUpgradeOverallLevels.Length)
        {
            return false;
        }

        tier = rawHullUpgradeLevel + 1;
        currentOverallLevel =
            hullUpgradeOverallLevels[
                rawHullUpgradeLevel];

        if (rawHullUpgradeLevel + 1 <
            hullUpgradeOverallLevels.Length)
        {
            nextOverallLevel =
                hullUpgradeOverallLevels[
                    rawHullUpgradeLevel + 1];
        }

        return true;
    }
}
