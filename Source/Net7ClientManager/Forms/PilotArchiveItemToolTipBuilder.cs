namespace Net7ClientManager.Forms;

using System.Globalization;
using Net7ClientManager.Addons.Contracts;
using Net7ClientManager.Observations.Models;
using Net7ClientManager.RecipeMapping;
using Net7ClientManager.SkillPlanning;
using Net7ClientManager.Services;

internal static class PilotArchiveItemToolTipBuilder
{
    public static ActionToolTipContent Build(
        AddonInventorySlotSnapshot slot,
        ClientRuntimeItemTemplateObservation? template,
        Image? icon,
        RecipeMappingItemPresentation? recipeMapping = null)
    {
        ArgumentNullException.ThrowIfNull(slot);

        List<ActionToolTipParagraph> paragraphs = [];
        var typeName = ItemTypeDisplayNameResolver.Resolve(
            template,
            ResolveFallbackTypeName(slot),
            string.Equals(
                slot.Collection,
                "ammo",
                StringComparison.Ordinal));
        var itemName = FirstNonEmpty(
            template?.Name,
            slot.Name,
            slot.TemplateId.HasValue
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"Item #{slot.TemplateId.Value}")
                : "Item");
        var headerPrefix = template is { TechLevel: > 0 } resolvedTemplate
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"Level {resolvedTemplate.TechLevel} {typeName}: ")
            : string.Concat(typeName, ": ");

        paragraphs.Add(
            new ActionToolTipParagraph(
                [
                    new(headerPrefix),
                    new(
                        itemName,
                        ActionToolTipTextRole.Accent,
                        Bold: true),
                ],
                ActionToolTipParagraphStyle.Header,
                SpaceAfter: 7));

        var conditionRuns = BuildConditionRuns(slot);

        if (conditionRuns.Count != 0)
        {
            paragraphs.Add(
                new ActionToolTipParagraph(
                    conditionRuns,
                    SpaceAfter: 5));
        }

        AppendItemRestrictions(paragraphs, template, recipeMapping);
        AppendRecipeMapping(paragraphs, recipeMapping);

        if (template != null)
        {
            var attributes = ResolveAttributes(
                template.Attributes,
                slot.InstanceInfo);
            var attributeRows = attributes
                .Select(TryFormatAttribute)
                .Where(row => row != null)
                .Cast<ItemAttributeRow>()
                .ToArray();

            if (attributeRows.Length != 0)
            {
                AppendSectionHeading(paragraphs, "Attributes");

                foreach (var row in attributeRows)
                {
                    paragraphs.Add(
                        new ActionToolTipParagraph(
                            [
                                new(string.Concat(row.Label, ": ")),
                                new(
                                    row.Value,
                                    ActionToolTipTextRole.Accent,
                                    Bold: true),
                            ],
                            SpaceAfter: 1));
                }
            }

            AppendEffects(
                paragraphs,
                "Activated",
                ResolveEffects(
                    template.ActivatedEffects,
                    slot.ActivatedEffectInfo));
            AppendEffects(
                paragraphs,
                "Equipped",
                ResolveEffects(
                    template.EquippedEffects,
                    slot.EquipEffectInfo));
        }

        var description = NormalizeText(template?.Description);

        if (!string.IsNullOrWhiteSpace(description))
        {
            paragraphs.Add(
                new ActionToolTipParagraph(
                    [new(description)],
                    SpaceBefore: 7,
                    SpaceAfter: 2));
        }

        var builder = FirstNonEmpty(slot.BuilderName, template?.Manufacturer);

        if (!string.IsNullOrWhiteSpace(builder))
        {
            paragraphs.Add(
                new ActionToolTipParagraph(
                    [
                        new("Manufactured by: ", ActionToolTipTextRole.Muted),
                        new(builder, ActionToolTipTextRole.Normal),
                    ],
                    SpaceBefore: description.Length == 0 ? 7 : 2));
        }

        return new ActionToolTipContent(paragraphs, icon);
    }

    public static ActionToolTipContent BuildCatalogItem(
        int itemTemplateId,
        string itemName,
        ClientRuntimeItemTemplateObservation? template,
        Image? icon,
        RecipeMappingItemPresentation? recipeMapping = null)
    {
        List<ActionToolTipParagraph> paragraphs = [];
        var typeName = ItemTypeDisplayNameResolver.Resolve(
            template,
            "Equipment");
        var resolvedName = FirstNonEmpty(
            template?.Name,
            itemName,
            itemTemplateId > 0
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"Item #{itemTemplateId}")
                : "Equipment");
        var headerPrefix = template is { TechLevel: > 0 } resolvedTemplate
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"Level {resolvedTemplate.TechLevel} {typeName}: ")
            : string.Concat(typeName, ": ");

        paragraphs.Add(
            new ActionToolTipParagraph(
                [
                    new(headerPrefix),
                    new(
                        resolvedName,
                        ActionToolTipTextRole.Accent,
                        Bold: true),
                ],
                ActionToolTipParagraphStyle.Header,
                SpaceAfter: 7));

        if (recipeMapping != null)
        {
            AppendItemRestrictions(paragraphs, template, recipeMapping);
            AppendRecipeMapping(paragraphs, recipeMapping);
        }

        if (template != null)
        {
            var attributeRows = ResolveAttributes(
                    template.Attributes,
                    instanceInfo: "")
                .Select(TryFormatAttribute)
                .Where(row => row != null)
                .Cast<ItemAttributeRow>()
                .ToArray();

            if (attributeRows.Length != 0)
            {
                AppendSectionHeading(paragraphs, "Attributes");

                foreach (var row in attributeRows)
                {
                    paragraphs.Add(
                        new ActionToolTipParagraph(
                            [
                                new(string.Concat(row.Label, ": ")),
                                new(
                                    row.Value,
                                    ActionToolTipTextRole.Accent,
                                    Bold: true),
                            ],
                            SpaceAfter: 1));
                }
            }

            AppendEffects(
                paragraphs,
                "Activated",
                ResolveEffects(
                    template.ActivatedEffects,
                    overlay: ""));
            AppendEffects(
                paragraphs,
                "Equipped",
                ResolveEffects(
                    template.EquippedEffects,
                    overlay: ""));
        }

        var description = NormalizeText(template?.Description);
        if (!string.IsNullOrWhiteSpace(description))
        {
            paragraphs.Add(
                new ActionToolTipParagraph(
                    [new(description)],
                    SpaceBefore: 7,
                    SpaceAfter: 2));
        }

        var builder = NormalizeText(template?.Manufacturer);
        if (!string.IsNullOrWhiteSpace(builder))
        {
            paragraphs.Add(
                new ActionToolTipParagraph(
                    [
                        new("Manufactured by: ", ActionToolTipTextRole.Muted),
                        new(builder, ActionToolTipTextRole.Normal),
                    ],
                    SpaceBefore: description.Length == 0 ? 7 : 2));
        }

        return new ActionToolTipContent(paragraphs, icon);
    }

    private static IReadOnlyList<ActionToolTipRun> BuildConditionRuns(
        AddonInventorySlotSnapshot slot)
    {
        List<ActionToolTipRun> runs = [];

        AppendConditionValue(
            runs,
            "Quality",
            slot.QualityPercent,
            "%");
        AppendConditionValue(
            runs,
            "Structure",
            slot.StructurePercent,
            "%");

        if (slot.StackCount is > 1)
        {
            AppendSeparator(runs);
            runs.Add(new("Stack: "));
            runs.Add(
                new(
                    slot.StackCount.Value.ToString(
                        CultureInfo.CurrentCulture),
                    ActionToolTipTextRole.Accent,
                    Bold: true));
        }

        return runs;
    }

    private static void AppendConditionValue(
        ICollection<ActionToolTipRun> runs,
        string label,
        float? value,
        string suffix)
    {
        if (!value.HasValue)
        {
            return;
        }

        AppendSeparator(runs);
        runs.Add(new(string.Concat(label, ": ")));
        runs.Add(
            new(
                string.Concat(
                    FormatNumber(value.Value),
                    suffix),
                ActionToolTipTextRole.Accent,
                Bold: true));
    }


    private static void AppendItemRestrictions(
        ICollection<ActionToolTipParagraph> paragraphs,
        ClientRuntimeItemTemplateObservation? template,
        RecipeMappingItemPresentation? recipeMapping)
    {
        IReadOnlyList<RecipeMappingRestrictionLine> restrictions;

        if (recipeMapping?.RestrictionLines.Count > 0)
        {
            restrictions = recipeMapping.RestrictionLines;
        }
        else
        {
            restrictions = ItemTemplateRestrictionEvaluator
                .Evaluate(template, profession: null)
                .Lines
                .Select(line => new RecipeMappingRestrictionLine
                {
                    Text = line.Text,
                    IsCharacterEligible = null,
                })
                .ToArray();
        }

        if (restrictions.Count == 0)
        {
            return;
        }

        for (var index = 0; index < restrictions.Count; index++)
        {
            var restriction = restrictions[index];
            var isViolated = restriction.IsCharacterEligible == false;
            paragraphs.Add(
                new ActionToolTipParagraph(
                    [
                        new(
                            restriction.Text,
                            isViolated
                                ? ActionToolTipTextRole.Danger
                                : ActionToolTipTextRole.Normal,
                            Bold: isViolated),
                    ],
                    SpaceBefore: index == 0 ? 2 : 0,
                    SpaceAfter: index == restrictions.Count - 1 ? 4 : 1));
        }
    }

    private static void AppendRecipeMapping(
        ICollection<ActionToolTipParagraph> paragraphs,
        RecipeMappingItemPresentation? recipeMapping)
    {
        if (recipeMapping == null)
        {
            return;
        }

        var mapping = recipeMapping;
        var hasRecipeStatus =
            mapping.Knowledge != RecipeMappingItemKnowledge.NotApplicable;
        var hasMappedPilots = mapping.MappedOnPilotNames.Count != 0;

        if (!hasRecipeStatus && !hasMappedPilots)
        {
            return;
        }

        AppendSectionHeading(paragraphs, "Manufacturing");

        if (hasRecipeStatus)
        {
            var role = mapping.Knowledge switch
            {
                RecipeMappingItemKnowledge.Mapped =>
                    ActionToolTipTextRole.Success,
                RecipeMappingItemKnowledge.Missing =>
                    ActionToolTipTextRole.Danger,
                _ => ActionToolTipTextRole.Normal,
            };
            var prefix = mapping.Knowledge ==
                         RecipeMappingItemKnowledge.NotManufacturable
                ? ""
                : "Recipe: ";

            paragraphs.Add(
                new ActionToolTipParagraph(
                    [
                        new(prefix),
                        new(
                            mapping.Text,
                            role,
                            Bold: mapping.Knowledge is
                                RecipeMappingItemKnowledge.Mapped or
                                RecipeMappingItemKnowledge.Missing),
                    ],
                    SpaceAfter: string.IsNullOrWhiteSpace(mapping.Detail) &&
                                !hasMappedPilots
                        ? 1
                        : 2));

            if (!string.IsNullOrWhiteSpace(mapping.Detail))
            {
                paragraphs.Add(
                    new ActionToolTipParagraph(
                        [new(mapping.Detail, ActionToolTipTextRole.Muted)],
                        SpaceAfter: hasMappedPilots ? 2 : 1));
            }
        }

        if (hasMappedPilots)
        {
            paragraphs.Add(
                new ActionToolTipParagraph(
                    [
                        new("Mapped on: "),
                        new(
                            string.Join(", ", mapping.MappedOnPilotNames),
                            ActionToolTipTextRole.Normal),
                    ],
                    SpaceAfter: 1));
        }
    }

    private static void AppendSeparator(
        ICollection<ActionToolTipRun> runs)
    {
        if (runs.Count != 0)
        {
            runs.Add(new("   ·   ", ActionToolTipTextRole.Muted));
        }
    }

    private static void AppendSectionHeading(
        ICollection<ActionToolTipParagraph> paragraphs,
        string text)
    {
        paragraphs.Add(
            new ActionToolTipParagraph(
                [new(text, ActionToolTipTextRole.Success, Bold: true)],
                SpaceBefore: 7,
                SpaceAfter: 3));
    }

    private static void AppendEffects(
        ICollection<ActionToolTipParagraph> paragraphs,
        string heading,
        IReadOnlyList<ResolvedItemEffect> effects)
    {
        var visible = effects
            .Select(effect => new ResolvedItemEffect(
                ItemEffectTextFormatter.Normalize(effect.Name),
                ItemEffectTextFormatter.Normalize(effect.Description)))
            .Where(effect =>
                !string.IsNullOrWhiteSpace(effect.Name) ||
                !string.IsNullOrWhiteSpace(effect.Description))
            .ToArray();

        if (visible.Length == 0)
        {
            return;
        }

        AppendSectionHeading(paragraphs, heading);

        foreach (var effect in visible)
        {
            List<ActionToolTipRun> runs = [];

            if (string.IsNullOrWhiteSpace(effect.Description))
            {
                runs.Add(
                    new(
                        effect.Name,
                        ActionToolTipTextRole.Accent,
                        Bold: true));
            }
            else if (string.IsNullOrWhiteSpace(effect.Name) ||
                     string.Equals(
                         effect.Name,
                         effect.Description,
                         StringComparison.OrdinalIgnoreCase))
            {
                runs.Add(
                    new(
                        effect.Description,
                        ActionToolTipTextRole.Normal));
            }
            else
            {
                runs.Add(
                    new(
                        string.Concat(effect.Name, ": "),
                        ActionToolTipTextRole.Accent,
                        Bold: true));
                runs.Add(
                    new(
                        effect.Description,
                        ActionToolTipTextRole.Normal));
            }

            paragraphs.Add(
                new ActionToolTipParagraph(
                    runs,
                    SpaceAfter: 2));
        }
    }

    private static IReadOnlyList<ClientRuntimeItemAttributeObservation>
        ResolveAttributes(
            IReadOnlyList<ClientRuntimeItemAttributeObservation> attributes,
            string? instanceInfo)
    {
        if (attributes.Count == 0 ||
            string.IsNullOrWhiteSpace(instanceInfo))
        {
            return attributes;
        }

        var overrides = ParseAttributeOverrides(
            instanceInfo,
            attributes);

        if (overrides.Count == 0)
        {
            return attributes;
        }

        return attributes
            .Select(attribute =>
            {
                if (!overrides.TryGetValue(
                        attribute.ItemInfoId,
                        out var value) ||
                    value.TypeCode != attribute.TypeCode)
                {
                    return attribute;
                }

                return attribute with
                {
                    RawValue = value.RawValue,
                    Int32Value = value.Int32Value,
                    FloatValue = value.FloatValue,
                    StringValue = value.StringValue,
                };
            })
            .ToArray();
    }

    private static IReadOnlyDictionary<uint, AttributeOverride>
        ParseAttributeOverrides(
            string instanceInfo,
            IReadOnlyList<ClientRuntimeItemAttributeObservation> attributes)
    {
        Dictionary<uint, AttributeOverride> result = [];
        var attributeTypes = attributes
            .GroupBy(attribute => attribute.ItemInfoId)
            .ToDictionary(
                group => group.Key,
                group => group.First().TypeCode);
        foreach (var segment in instanceInfo.Split(
                     new[] { '^', '/' },
                     StringSplitOptions.RemoveEmptyEntries |
                     StringSplitOptions.TrimEntries))
        {
            var parts = segment.Split(':', 3);

            if (parts.Length < 2 ||
                !uint.TryParse(
                    parts[0],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var itemInfoId) ||
                !attributeTypes.TryGetValue(
                    itemInfoId,
                    out var templateTypeCode))
            {
                continue;
            }

            uint typeCode;
            string encodedValue;

            if (parts.Length == 3 &&
                uint.TryParse(
                    parts[1],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var explicitTypeCode))
            {
                typeCode = explicitTypeCode;
                encodedValue = parts[2];
            }
            else
            {
                typeCode = templateTypeCode;
                encodedValue = parts[1];
            }

            if (typeCode != templateTypeCode)
            {
                continue;
            }

            switch (typeCode)
            {
                case 1 when TryParseIntegerOverride(
                    encodedValue,
                    out var intValue):
                    result[itemInfoId] = new AttributeOverride(
                        typeCode,
                        unchecked((uint)intValue),
                        intValue,
                        null,
                        "");
                    break;

                case 2 when float.TryParse(
                    encodedValue,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var floatValue) &&
                    float.IsFinite(floatValue):
                    result[itemInfoId] = new AttributeOverride(
                        typeCode,
                        unchecked((uint)BitConverter.SingleToInt32Bits(floatValue)),
                        null,
                        floatValue,
                        "");
                    break;

                case 4:
                    result[itemInfoId] = new AttributeOverride(
                        typeCode,
                        0,
                        null,
                        null,
                        encodedValue);
                    break;
            }
        }

        return result;
    }

    private static bool TryParseIntegerOverride(
        string encodedValue,
        out int value)
    {
        if (int.TryParse(
                encodedValue,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out value))
        {
            return true;
        }

        if (!double.TryParse(
                encodedValue,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var interpolatedValue) ||
            !double.IsFinite(interpolatedValue) ||
            interpolatedValue < int.MinValue ||
            interpolatedValue > int.MaxValue)
        {
            value = 0;
            return false;
        }

        value = (int)interpolatedValue;
        return true;
    }

    private static ItemAttributeRow? TryFormatAttribute(
        ClientRuntimeItemAttributeObservation attribute)
    {
        return attribute.ItemInfoId switch
        {
            0x01 => FormatString(attribute, "Ammo"),
            0x04 => FormatInteger(attribute, "Requires Combat level"),
            0x05 => FormatNumber(attribute, "Damage", " units"),
            0x07 => FormatNumber(attribute, "Range", " units"),
            0x08 => FormatNumber(attribute, "Radius", " units"),
            0x09 => FormatNumber(attribute, "Energy Use", " units"),
            0x0a => FormatNumber(attribute, "Energy Drain", " units/sec"),
            0x0c => FormatInteger(attribute, "Requires Explore level"),
            0x0e => FormatInteger(attribute, "Requires Overall level"),
            0x10 => FormatNumber(attribute, "Max Power", " units"),
            0x13 => FormatNumber(attribute, "Targeting Range", " units"),
            0x14 => FormatNumber(attribute, "Recharge", " units/sec"),
            0x15 => FormatNumber(attribute, "Reload Time", " seconds"),
            0x16 => FormatInteger(attribute, "Rounds Fired", " rounds"),
            0x17 => FormatNumber(attribute, "Shield Use", " units"),
            0x18 => FormatNumber(attribute, "Capacity", " units"),
            0x19 => FormatNumber(attribute, "Shield Drain", " units/sec"),
            0x1a => FormatNumber(attribute, "Recharge", " units/sec"),
            0x1d => FormatNumber(attribute, "Signature", " units"),
            0x1f => FormatNumber(attribute, "Thrust", " units"),
            0x20 => FormatInteger(attribute, "Requires Trade level"),
            0x21 => FormatNumber(attribute, "Warp", " units"),
            0x22 => FormatNumber(attribute, "Warp Drain", " units/sec"),
            0x25 => FormatPercentage(attribute, "Critical Chance"),
            _ => null,
        };
    }

    private static ItemAttributeRow? FormatString(
        ClientRuntimeItemAttributeObservation attribute,
        string label)
    {
        return string.IsNullOrWhiteSpace(attribute.StringValue)
            ? null
            : new ItemAttributeRow(label, attribute.StringValue.Trim());
    }

    private static ItemAttributeRow? FormatInteger(
        ClientRuntimeItemAttributeObservation attribute,
        string label,
        string suffix = "")
    {
        return attribute.Int32Value.HasValue
            ? new ItemAttributeRow(
                label,
                string.Concat(
                    attribute.Int32Value.Value.ToString(
                        "N0",
                        CultureInfo.CurrentCulture),
                    suffix))
            : null;
    }

    private static ItemAttributeRow? FormatNumber(
        ClientRuntimeItemAttributeObservation attribute,
        string label,
        string suffix)
    {
        var value = GetNumericValue(attribute);

        return value.HasValue
            ? new ItemAttributeRow(
                label,
                string.Concat(
                    FormatNumber(value.Value),
                    suffix))
            : null;
    }

    private static ItemAttributeRow? FormatPercentage(
        ClientRuntimeItemAttributeObservation attribute,
        string label)
    {
        var value = GetNumericValue(attribute);

        return value.HasValue
            ? new ItemAttributeRow(
                label,
                string.Concat(FormatNumber(value.Value), "%"))
            : null;
    }

    private static float? GetNumericValue(
        ClientRuntimeItemAttributeObservation attribute)
    {
        if (attribute.FloatValue is { } floatValue &&
            float.IsFinite(floatValue))
        {
            return floatValue;
        }

        return attribute.Int32Value.HasValue
            ? attribute.Int32Value.Value
            : null;
    }

    private static IReadOnlyList<ResolvedItemEffect> ResolveEffects(
        IReadOnlyList<ClientRuntimeItemEffectObservation> effects,
        string? overlay)
    {
        if (effects.Count == 0)
        {
            return [];
        }

        var overlays = ParseEffectOverlay(overlay, effects.Count);
        List<ResolvedItemEffect> result = [];

        for (var index = 0; index < effects.Count; index++)
        {
            var effect = effects[index];
            var values = index < overlays.Count
                ? overlays[index]
                : null;
            var resolved = ItemEffectTextFormatter.Resolve(
                effect.NameFormat,
                values?.NameValues ?? effect.NameValues,
                effect.DescriptionFormat,
                values?.DescriptionValues ?? effect.DescriptionValues);
            result.Add(
                new ResolvedItemEffect(
                    resolved.Name,
                    resolved.Description));
        }

        return result;
    }

    private static IReadOnlyList<EffectOverlayValues?> ParseEffectOverlay(
        string? overlay,
        int expectedEffectCount)
    {
        if (string.IsNullOrWhiteSpace(overlay))
        {
            return Enumerable
                .Repeat<EffectOverlayValues?>(null, expectedEffectCount)
                .ToArray();
        }

        var segments = overlay.Split(
            '#',
            StringSplitOptions.RemoveEmptyEntries |
            StringSplitOptions.TrimEntries);
        List<EffectOverlayValues?> result = [];

        for (var index = 0; index < expectedEffectCount; index++)
        {
            if (index >= segments.Length)
            {
                result.Add(null);
                continue;
            }

            var parts = segments[index].Split('^');

            if (parts.Length != 2 ||
                !TryParseFloatList(parts[0], out var nameValues) ||
                !TryParseFloatList(parts[1], out var descriptionValues))
            {
                result.Add(null);
                continue;
            }

            result.Add(
                new EffectOverlayValues(nameValues, descriptionValues));
        }

        return result;
    }

    private static bool TryParseFloatList(
        string text,
        out IReadOnlyList<float> values)
    {
        List<float> result = [];

        foreach (var token in text.Split(
                     ',',
                     StringSplitOptions.RemoveEmptyEntries |
                     StringSplitOptions.TrimEntries))
        {
            if (!float.TryParse(
                    token,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var value) ||
                !float.IsFinite(value))
            {
                values = [];
                return false;
            }

            result.Add(value);
        }

        values = result;
        return true;
    }

    private static string ResolveFallbackTypeName(
        AddonInventorySlotSnapshot slot)
    {
        if (!string.IsNullOrWhiteSpace(slot.EquipmentKind))
        {
            return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(
                slot.EquipmentKind.Replace('_', ' '));
        }

        return slot.Collection switch
        {
            "cargo" => "Cargo Item",
            "secure" or "vault" => "Vault Item",
            "ammo" => "Ammo",
            _ => "Equipment",
        };
    }

    private static string NormalizeText(string? text)
    {
        return string.IsNullOrWhiteSpace(text)
            ? ""
            : text
                .Replace("\\n", "\n", StringComparison.Ordinal)
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Trim();
    }

    private static string FormatNumber(float value)
    {
        var rounded = MathF.Round(value);

        return MathF.Abs(value - rounded) < 0.005f
            ? rounded.ToString("N0", CultureInfo.CurrentCulture)
            : value.ToString("0.##", CultureInfo.CurrentCulture);
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value =>
            !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";
    }

    private sealed record ItemAttributeRow(string Label, string Value);

    private sealed record ResolvedItemEffect(
        string Name,
        string Description);

    private sealed record EffectOverlayValues(
        IReadOnlyList<float> NameValues,
        IReadOnlyList<float> DescriptionValues);

    private sealed record AttributeOverride(
        uint TypeCode,
        uint RawValue,
        int? Int32Value,
        float? FloatValue,
        string StringValue);
}
