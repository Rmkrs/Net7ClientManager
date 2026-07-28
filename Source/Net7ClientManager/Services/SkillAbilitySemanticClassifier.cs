namespace Net7ClientManager.Services;

using Net7ClientManager.Models;

internal static class SkillAbilitySemanticClassifier
{
    private static readonly HashSet<string> HostileFamilies = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "Befriend",
        "Biorepression",
        "Compulsory Contemplation",
        "Energy Leech",
        "Enrage",
        "Gravity Link",
        "Hacking",
        "Menace",
        "Quantum Flux",
        "Shield Leech",
        "Shield Sap",
    };

    private static readonly HashSet<string> DefensiveFamilies = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "Hull Patch",
        "Jumpstart",
        "Recharge Shields",
        "Repair Equipment",
    };

    private static readonly HashSet<string> BuffFamilies = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "Afterburn",
        "Cloak",
        "Environment Shield",
        "Maelstrom Resonance",
        "Nullfactor Field",
        "Psionic Shield",
        "Rally",
        "Reactor Optimization",
        "Repulsor Field",
        "Shield Charging",
    };

    private static readonly HashSet<string> UtilityFamilies = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "Create Wormhole",
        "Extended Wormhole",
        "Fold Space",
        "Power Down",
        "Summon",
    };

    public static SkillAbilitySemantics Classify(
        SkillAbilityDefinition ability)
    {
        var semanticText = CombineSemanticText(ability);
        var target = ResolveFamilyTarget(ability);
        var confidence = target == SkillTargetDisposition.Unknown
            ? SkillSemanticConfidence.Unknown
            : SkillSemanticConfidence.Curated;

        var explicitTarget = ResolveExplicitTarget(semanticText);

        if (explicitTarget != SkillTargetDisposition.Unknown)
        {
            target |= explicitTarget;
            confidence = SkillSemanticConfidence.Explicit;
        }

        if (target == SkillTargetDisposition.Unknown)
        {
            target = ResolveNameFallback(ability.Name);

            if (target != SkillTargetDisposition.Unknown)
            {
                confidence = SkillSemanticConfidence.Inferred;
            }
        }

        var scope = ResolveEffectScope(
            ability,
            target);

        return new SkillAbilitySemantics(
            ResolveCategory(ability, target),
            target,
            scope,
            confidence);
    }

    private static SkillTargetDisposition ResolveFamilyTarget(
        SkillAbilityDefinition ability)
    {
        var family = ability.SkillFamilyName;
        var name = ability.Name;

        if (HostileFamilies.Contains(family))
        {
            return SkillTargetDisposition.Hostile;
        }

        if (string.Equals(
                family,
                "Shield Inversion",
                StringComparison.OrdinalIgnoreCase))
        {
            return SkillTargetDisposition.Hostile;
        }

        if (string.Equals(
                family,
                "Self Destruct",
                StringComparison.OrdinalIgnoreCase))
        {
            return SkillTargetDisposition.Self;
        }

        if (string.Equals(
                family,
                "Fold Space",
                StringComparison.OrdinalIgnoreCase))
        {
            if (Contains(name, "Enemy"))
            {
                return SkillTargetDisposition.Hostile;
            }

            if (Contains(name, "Friend"))
            {
                return SkillTargetDisposition.Friendly;
            }

            if (Contains(name, "Directional"))
            {
                return SkillTargetDisposition.Unknown;
            }

            return SkillTargetDisposition.Self;
        }

        if (string.Equals(
                family,
                "Summon",
                StringComparison.OrdinalIgnoreCase))
        {
            if (Contains(name, "Enemy"))
            {
                return SkillTargetDisposition.Hostile;
            }

            if (Contains(name, "Friend", "Return"))
            {
                return SkillTargetDisposition.Friendly;
            }

            return SkillTargetDisposition.Self;
        }

        if (string.Equals(
                family,
                "Repair Equipment",
                StringComparison.OrdinalIgnoreCase))
        {
            return Contains(name, "Regenerate")
                ? SkillTargetDisposition.Self
                : SkillTargetDisposition.Self |
                  SkillTargetDisposition.Friendly;
        }

        if (string.Equals(
                family,
                "Hull Patch",
                StringComparison.OrdinalIgnoreCase))
        {
            return Contains(name, "Patch Hull")
                ? SkillTargetDisposition.Self
                : SkillTargetDisposition.Self |
                  SkillTargetDisposition.Friendly;
        }

        if (string.Equals(
                family,
                "Recharge Shields",
                StringComparison.OrdinalIgnoreCase))
        {
            return Contains(name, "Regenerate")
                ? SkillTargetDisposition.Self
                : SkillTargetDisposition.Self |
                  SkillTargetDisposition.Friendly;
        }

        if (string.Equals(
                family,
                "Reactor Optimization",
                StringComparison.OrdinalIgnoreCase))
        {
            return Contains(name, "Boost", "Surge")
                ? SkillTargetDisposition.Self
                : SkillTargetDisposition.Self |
                  SkillTargetDisposition.Friendly;
        }

        if (string.Equals(
                family,
                "Shield Charging",
                StringComparison.OrdinalIgnoreCase))
        {
            return Contains(name, "Target", "Megacharge")
                ? SkillTargetDisposition.Self |
                  SkillTargetDisposition.Friendly
                : SkillTargetDisposition.Self;
        }

        if (string.Equals(
                family,
                "Repulsor Field",
                StringComparison.OrdinalIgnoreCase))
        {
            return Contains(name, "Minor")
                ? SkillTargetDisposition.Self
                : SkillTargetDisposition.Self |
                  SkillTargetDisposition.Friendly;
        }

        if (string.Equals(
                family,
                "Environment Shield",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                family,
                "Psionic Shield",
                StringComparison.OrdinalIgnoreCase))
        {
            return SkillTargetDisposition.Self |
                   SkillTargetDisposition.Friendly;
        }

        if (string.Equals(
                family,
                "Jumpstart",
                StringComparison.OrdinalIgnoreCase))
        {
            return SkillTargetDisposition.Friendly;
        }

        if (string.Equals(
                family,
                "Afterburn",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                family,
                "Cloak",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                family,
                "Power Down",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                family,
                "Nullfactor Field",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                family,
                "Rally",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                family,
                "Create Wormhole",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                family,
                "Extended Wormhole",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                family,
                "Maelstrom Resonance",
                StringComparison.OrdinalIgnoreCase))
        {
            return SkillTargetDisposition.Self;
        }

        return SkillTargetDisposition.Unknown;
    }

    private static SkillTargetDisposition ResolveExplicitTarget(
        string description)
    {
        var target = SkillTargetDisposition.Unknown;

        if (Contains(
                description,
                "friendly target",
                "friendly ships",
                "any player target",
                "any player's",
                "another player",
                "another player's",
                "other starships",
                "other ships",
                "incapacitated player",
                "targeted group member"))
        {
            target |= SkillTargetDisposition.Friendly;
        }

        if (Contains(
                description,
                "target enemy",
                "enemy target",
                "enemy npc",
                "enemy formation",
                "all enemies",
                "all hostiles",
                "hostile target",
                "single hostile",
                "target npc",
                "npc group",
                "organic target",
                "frighten target enemy"))
        {
            target |= SkillTargetDisposition.Hostile;
        }

        if (Contains(
                description,
                "only be applied to skill user",
                "may only be applied to skill user",
                "teleports the ship",
                "repair the ship's hull",
                "on the ship"))
        {
            target |= SkillTargetDisposition.Self;
        }

        return target;
    }

    private static SkillTargetDisposition ResolveNameFallback(
        string name)
    {
        if (Contains(name, "Enemy", "Drain", "Leech", "Sap", "Hack"))
        {
            return SkillTargetDisposition.Hostile;
        }

        if (Contains(name, "Friend", "Recharge", "Repair", "Jumpstart"))
        {
            return SkillTargetDisposition.Friendly;
        }

        if (Contains(name, "Self", "Cloak", "Power Down"))
        {
            return SkillTargetDisposition.Self;
        }

        return SkillTargetDisposition.Unknown;
    }

    private static SkillEffectScope ResolveEffectScope(
        SkillAbilityDefinition ability,
        SkillTargetDisposition target)
    {
        var scope = SkillEffectScope.Unknown;
        var description = CombineSemanticText(ability);
        var name = ability.Name;

        if (target.HasFlag(SkillTargetDisposition.Self))
        {
            scope |= SkillEffectScope.Self;
        }

        if ((target & (
                 SkillTargetDisposition.Friendly |
                 SkillTargetDisposition.Hostile)) != 0)
        {
            scope |= SkillEffectScope.Target;
        }

        if (Contains(
                name,
                "Group") ||
            Contains(
                description,
                "all group members",
                "all members of a target group",
                "enemy formation",
                "group-wide",
                "group buff",
                "groupmates",
                "skill user's group",
                "player and group"))
        {
            scope |= SkillEffectScope.Group;
        }

        if (Contains(
                name,
                "Area ",
                " Sphere",
                "Nova") ||
            Contains(
                description,
                "radius of",
                "area effect",
                "all group members within",
                "all friendly ships within",
                "all enemies within",
                "all hostiles within",
                "all targets within"))
        {
            scope |= SkillEffectScope.Area;
        }

        if (scope == SkillEffectScope.Unknown &&
            target == SkillTargetDisposition.Unknown &&
            Contains(description, "target"))
        {
            scope = SkillEffectScope.Target;
        }

        if (scope == SkillEffectScope.Unknown)
        {
            scope = target == SkillTargetDisposition.Unknown
                ? SkillEffectScope.Unknown
                : SkillEffectScope.Target;
        }

        return scope;
    }

    private static GameShortcutActionCategory ResolveCategory(
        SkillAbilityDefinition ability,
        SkillTargetDisposition target)
    {
        var family = ability.SkillFamilyName;

        if (DefensiveFamilies.Contains(family))
        {
            return GameShortcutActionCategory.Defensive;
        }

        if (BuffFamilies.Contains(family))
        {
            return GameShortcutActionCategory.Buff;
        }

        if (UtilityFamilies.Contains(family))
        {
            return GameShortcutActionCategory.Utility;
        }

        if (HostileFamilies.Contains(family) ||
            string.Equals(
                family,
                "Shield Inversion",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                family,
                "Self Destruct",
                StringComparison.OrdinalIgnoreCase) ||
            target.HasFlag(SkillTargetDisposition.Hostile))
        {
            return GameShortcutActionCategory.Offensive;
        }

        var semanticText = CombineSemanticText(ability);

        if (Contains(
                semanticText,
                "repair",
                "recharge",
                "resuscitate"))
        {
            return GameShortcutActionCategory.Defensive;
        }

        if (Contains(
                semanticText,
                "increases",
                "creates a shield",
                "protects",
                "cloaks"))
        {
            return GameShortcutActionCategory.Buff;
        }

        return GameShortcutActionCategory.Utility;
    }


    private static string CombineSemanticText(
        SkillAbilityDefinition ability)
    {
        return string.Join(
            " ",
            ability.Description,
            ability.RankDescription,
            ability.SkillFamilyDescription);
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
