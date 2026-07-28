namespace Net7ClientManager.Services;

using System.Text;
using Net7ClientManager.Observations.Models;

internal static class ItemActivatedTargetSemanticClassifier
{
    private const uint SelfTargetFlag = 0x01;
    private const uint GroupTargetFlag = 0x02;
    private const uint FriendlyTargetFlag = 0x10;
    private const uint HostileTargetFlag = 0x20;

    public static ItemActivatedTargetSemantics Classify(
        string? itemDescription,
        IReadOnlyList<ClientRuntimeItemEffectObservation> activatedEffects)
    {
        if (activatedEffects.Count == 0)
        {
            return ItemActivatedTargetSemantics.Unknown;
        }

        var targetFlags = activatedEffects.Aggregate(
            0u,
            static (current, effect) => current | effect.Field50);
        var disposition = ResolveStructuredDisposition(targetFlags);
        var confidence = disposition == SkillTargetDisposition.Unknown
            ? ItemTargetSemanticConfidence.Unknown
            : ItemTargetSemanticConfidence.Structured;
        var semanticText = BuildSemanticText(
            itemDescription,
            activatedEffects);

        if (HasHostileIntent(semanticText))
        {
            disposition |= SkillTargetDisposition.Hostile;
            confidence = ItemTargetSemanticConfidence.Explicit;
        }

        if (HasFriendlyTargetIntent(semanticText))
        {
            disposition |= SkillTargetDisposition.Self |
                           SkillTargetDisposition.Friendly;
            confidence = ItemTargetSemanticConfidence.Explicit;
        }

        if (HasSelfIntent(semanticText))
        {
            disposition |= SkillTargetDisposition.Self;
            confidence = ItemTargetSemanticConfidence.Explicit;
        }

        if (disposition != SkillTargetDisposition.Unknown)
        {
            return new ItemActivatedTargetSemantics(
                disposition,
                confidence);
        }

        if (LooksBeneficial(semanticText))
        {
            // Earth & Beyond applies friendly buffs to the acting pilot when
            // the current target cannot receive them. Keep both recipient
            // possibilities so the tooltip can explain the effective target.
            return new ItemActivatedTargetSemantics(
                SkillTargetDisposition.Self |
                SkillTargetDisposition.Friendly,
                ItemTargetSemanticConfidence.Inferred);
        }

        return ItemActivatedTargetSemantics.Unknown;
    }

    private static SkillTargetDisposition ResolveStructuredDisposition(
        uint targetFlags)
    {
        var disposition = SkillTargetDisposition.Unknown;

        if ((targetFlags & SelfTargetFlag) != 0)
        {
            disposition |= SkillTargetDisposition.Self;
        }

        if ((targetFlags & (FriendlyTargetFlag | GroupTargetFlag)) != 0)
        {
            // A friendly-capable device can also fall back to self when the
            // selected target is not a valid friendly recipient.
            disposition |= SkillTargetDisposition.Self |
                           SkillTargetDisposition.Friendly;
        }

        if ((targetFlags & HostileTargetFlag) != 0)
        {
            disposition |= SkillTargetDisposition.Hostile;
        }

        return disposition;
    }

    private static string BuildSemanticText(
        string? itemDescription,
        IReadOnlyList<ClientRuntimeItemEffectObservation> activatedEffects)
    {
        var builder = new StringBuilder();

        Append(builder, itemDescription);

        foreach (var effect in activatedEffects)
        {
            Append(builder, effect.SourceOrIdentity);
            Append(builder, effect.NameFormat);
            Append(builder, effect.DescriptionFormat);
        }

        return builder.ToString();
    }

    private static void Append(
        StringBuilder builder,
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (builder.Length > 0)
        {
            builder.Append(' ');
        }

        builder.Append(value);
    }

    private static bool HasFriendlyTargetIntent(string value)
    {
        return Contains(
            value,
            "friendly target",
            "targeted friendly",
            "targeted group member",
            "target group member",
            "group member when activated",
            "another player",
            "another player's ship",
            "to target when activated",
            "to the target when activated");
    }

    private static bool HasSelfIntent(string value)
    {
        return Contains(
            value,
            "self only",
            "only affects self",
            "your own ship",
            "the activating ship",
            "the user's ship");
    }

    private static bool HasHostileIntent(string value)
    {
        return Contains(
            value,
            "enemy target",
            "target enemy",
            "hostile target",
            "damage to target",
            "damages target",
            "drain target",
            "drains target",
            "reduce target's",
            "reduces target's",
            "decrease target's",
            "decreases target's",
            "lower target's",
            "lowers target's",
            "slowing it down",
            "slows the target",
            "weaken target",
            "disrupt target",
            "disable target",
            "leech",
            "shield sap");
    }

    private static bool LooksBeneficial(string value)
    {
        return Contains(
            value,
            "increase ",
            "increases ",
            "boost ",
            "boosts ",
            "improve ",
            "improves ",
            "enhance ",
            "enhances ",
            "recharge ",
            "regenerate ",
            "repair ",
            "restore ",
            "protect ",
            "turbo ",
            "warp speed",
            "combat speed",
            "reduce weapon delay",
            "reduces weapon delay");
    }

    private static bool Contains(
        string value,
        params string[] needles)
    {
        return needles.Any(needle =>
            value.Contains(
                needle,
                StringComparison.OrdinalIgnoreCase));
    }
}

internal sealed record ItemActivatedTargetSemantics(
    SkillTargetDisposition TargetDisposition,
    ItemTargetSemanticConfidence Confidence)
{
    public static ItemActivatedTargetSemantics Unknown { get; } =
        new(
            SkillTargetDisposition.Unknown,
            ItemTargetSemanticConfidence.Unknown);
}

internal enum ItemTargetSemanticConfidence
{
    Unknown,
    Inferred,
    Explicit,
    Structured,
}
