namespace Net7ClientManager.Navigation;

internal static class FactionStandingBandResolver
{
    public const float MinimumReaction = -10000.0f;

    public const float MaximumReaction = 10000.0f;

    public const float DangerUpperExclusive = -1000.0f;

    public const float SafeLowerInclusive = 2000.0f;

    public static GalaxyAtlasSafetyBand ResolveStandard(float reaction)
    {
        if (reaction < DangerUpperExclusive)
        {
            return GalaxyAtlasSafetyBand.Danger;
        }

        if (reaction >= SafeLowerInclusive)
        {
            return GalaxyAtlasSafetyBand.Safe;
        }

        return GalaxyAtlasSafetyBand.Neutral;
    }

    public static float Normalize(float reaction) =>
        Math.Clamp(
            (reaction - MinimumReaction) /
            (MaximumReaction - MinimumReaction),
            0.0f,
            1.0f);
}
