namespace Net7ClientManager.Services;

using System.Globalization;
using System.Text;
using Net7ClientManager.Observations.Models;

internal sealed record ItemShortcutDetails
{
    public int ItemTemplateId { get; init; }

    public int SourceSlotIndex { get; init; }

    public GameShortcutKind Kind { get; init; }

    public string Name { get; init; } = "";

    public string Description { get; init; } = "";

    public string Manufacturer { get; init; } = "";

    public string TypeDisplayName { get; init; } = "";

    public uint? TechLevel { get; init; }

    public int? StackCount { get; init; }

    public bool? IsNativeAction { get; init; }

    public bool IsBusy { get; init; }

    public bool IsOperationallyReady { get; init; }

    public bool IsInPostDeadlineBusyTail { get; init; }

    public long? NominalRemainingMilliseconds { get; init; }

    public float? Range { get; init; }

    public ItemActivatedTargetSemantics ActivatedTargetSemantics
    { get; init; } = ItemActivatedTargetSemantics.Unknown;

    public IReadOnlyList<ItemShortcutEffectDetails> ActivatedEffects
    { get; init; } = [];

    public IReadOnlyList<ItemShortcutEffectDetails> EquippedEffects
    { get; init; } = [];

    public string LayoutSignature
    {
        get
        {
            var builder = new StringBuilder();
            builder.Append(this.ItemTemplateId.ToString(CultureInfo.InvariantCulture));
            builder.Append(':');
            builder.Append(this.IsNativeAction switch
            {
                true => '1',
                false => '0',
                null => '?',
            });
            builder.Append(':');
            builder.Append(this.TechLevel?.ToString(CultureInfo.InvariantCulture) ?? "");
            builder.Append(':');
            builder.Append(this.TypeDisplayName);
            builder.Append(':');
            builder.Append(this.ActivatedTargetSemantics.TargetDisposition);
            builder.Append(':');
            builder.Append(this.ActivatedTargetSemantics.Confidence);

            AppendEffectSignature(
                builder,
                this.ActivatedEffects);
            AppendEffectSignature(
                builder,
                this.EquippedEffects);

            return builder.ToString();
        }
    }

    public static ItemShortcutDetails? Create(
        GameShortcutKind kind,
        ClientInventoryItemObservation? item)
    {
        if (item?.IsOccupied != true ||
            item.ItemTemplateId is not > 0)
        {
            return null;
        }

        var template = item.Template;
        var activatedEffects = ResolveEffects(
            template?.ActivatedEffects ?? [],
            item.InstanceActivatedEffectInfo);
        var equippedEffects = ResolveEffects(
            template?.EquippedEffects ?? [],
            item.InstanceEquipEffectInfo);
        var operational = item.Operational;
        var activatedTargetSemantics =
            ItemActivatedTargetSemanticClassifier.Classify(
                template?.Description,
                template?.ActivatedEffects ?? []);

        return new ItemShortcutDetails
        {
            ItemTemplateId = item.ItemTemplateId.Value,
            SourceSlotIndex = item.Slot,
            Kind = kind,
            Name = FirstNonEmpty(
                template?.Name,
                ClientItemTemplateNameResolver.GetKnownName(
                    item.ItemTemplateId),
                kind == GameShortcutKind.Equipment
                    ? string.Create(
                        CultureInfo.InvariantCulture,
                        $"Equipment slot {item.Slot}")
                    : string.Create(
                        CultureInfo.InvariantCulture,
                        $"Cargo slot {item.Slot}")),
            Description = template?.Description ?? "",
            Manufacturer = template?.Manufacturer ?? "",
            TypeDisplayName = ItemTypeDisplayNameResolver.Resolve(
                template,
                kind == GameShortcutKind.Equipment
                    ? "Equipment"
                    : "Item"),
            TechLevel = template?.TechLevel,
            StackCount = item.StackCount,
            IsNativeAction = kind == GameShortcutKind.Equipment
                ? operational.IsNativeAction
                : null,
            IsBusy = operational.IsBusy,
            IsOperationallyReady =
                operational.IsOperationallyReady,
            IsInPostDeadlineBusyTail =
                operational.IsInPostDeadlineBusyTail,
            NominalRemainingMilliseconds =
                operational.NominalRemainingMilliseconds,
            Range = operational.EffectRange is > 0
                ? operational.EffectRange
                : operational.TargetRange is > 0
                    ? operational.TargetRange
                    : null,
            ActivatedTargetSemantics = activatedTargetSemantics,
            ActivatedEffects = activatedEffects,
            EquippedEffects = equippedEffects,
        };
    }

    private static IReadOnlyList<ItemShortcutEffectDetails> ResolveEffects(
        IReadOnlyList<ClientRuntimeItemEffectObservation> effects,
        string? overlay)
    {
        if (effects.Count == 0)
        {
            return [];
        }

        var overlays = ParseEffectOverlay(
            overlay,
            effects.Count);
        List<ItemShortcutEffectDetails> resolved = [];

        for (var index = 0; index < effects.Count; index++)
        {
            var effect = effects[index];
            var values = index < overlays.Count
                ? overlays[index]
                : null;
            var nameValues = values?.NameValues ??
                             effect.NameValues;
            var descriptionValues = values?.DescriptionValues ??
                                    effect.DescriptionValues;

            var display = ItemEffectTextFormatter.Resolve(
                effect.NameFormat,
                nameValues,
                effect.DescriptionFormat,
                descriptionValues);
            resolved.Add(
                new ItemShortcutEffectDetails(
                    display.Name,
                    display.Description,
                    effect.SourceOrIdentity));
        }

        return resolved;
    }

    private static IReadOnlyList<EffectOverlayValues?> ParseEffectOverlay(
        string? overlay,
        int expectedEffectCount)
    {
        if (string.IsNullOrWhiteSpace(overlay))
        {
            return Enumerable
                .Repeat<EffectOverlayValues?>(
                    null,
                    expectedEffectCount)
                .ToArray();
        }

        var segments = overlay.Split(
            '#',
            StringSplitOptions.RemoveEmptyEntries |
            StringSplitOptions.TrimEntries);
        List<EffectOverlayValues?> result = [];

        for (var index = 0;
             index < expectedEffectCount;
             index++)
        {
            if (index >= segments.Length)
            {
                result.Add(null);
                continue;
            }

            var parts = segments[index].Split('^');

            if (parts.Length != 2 ||
                !TryParseOverlayValues(
                    parts[0],
                    out var nameValues) ||
                !TryParseOverlayValues(
                    parts[1],
                    out var descriptionValues))
            {
                result.Add(null);
                continue;
            }

            result.Add(
                new EffectOverlayValues(
                    nameValues,
                    descriptionValues));
        }

        return result;
    }

    private static bool TryParseOverlayValues(
        string text,
        out IReadOnlyList<float> values)
    {
        List<float> parsed = [];

        foreach (var token in text.Split(
                     ',',
                     StringSplitOptions.RemoveEmptyEntries |
                     StringSplitOptions.TrimEntries))
        {
            if (!float.TryParse(
                    token,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var value))
            {
                values = [];
                return false;
            }

            parsed.Add(value);
        }

        values = parsed;
        return true;
    }

    private static string FirstNonEmpty(
        params string?[] values)
    {
        return values.FirstOrDefault(value =>
            !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";
    }

    private static void AppendEffectSignature(
        StringBuilder builder,
        IReadOnlyList<ItemShortcutEffectDetails> effects)
    {
        foreach (var effect in effects)
        {
            builder.Append('|');
            builder.Append(effect.SourceOrIdentity);
        }
    }

    private sealed record EffectOverlayValues(
        IReadOnlyList<float> NameValues,
        IReadOnlyList<float> DescriptionValues);
}

internal sealed record ItemShortcutEffectDetails(
    string Name,
    string Description,
    string SourceOrIdentity);
