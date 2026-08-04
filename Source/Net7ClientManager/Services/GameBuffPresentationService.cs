namespace Net7ClientManager.Services;

using System.Globalization;
using Net7ClientManager.Models;
using Net7ClientManager.Observations;
using Net7ClientManager.Observations.Models;

internal sealed class GameBuffPresentationService(
    GameBuffDefinitionCatalogService buffDefinitionCatalogService)
{
    public GameBuffOverlayPresentation Build(
        ClientInstance client,
        ClientObservationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(snapshot);

        var buffs = snapshot.LocalPlayer.Buffs;

        if (!buffs.IsAvailable)
        {
            return new GameBuffOverlayPresentation
            {
                ObservedAt = snapshot.ObservedAt,
            };
        }

        var definitions = buffDefinitionCatalogService.GetCatalog(client);
        var entries = buffs.ActiveBuffs
            .Where(buff => buff.Slot is >= 0 and < 16)
            .OrderBy(buff => buff.Slot)
            .Select(buff => ResolveEntry(
                buff,
                definitions))
            .ToArray();

        return new GameBuffOverlayPresentation
        {
            ObservedAt = snapshot.ObservedAt,
            Buffs = entries,
        };
    }

    private static GameBuffOverlayEntry ResolveEntry(
        ClientBuffObservation buff,
        GameBuffDefinitionCatalog definitions)
    {
        var scrubName = Humanize(buff.ScrubTypeName);
        var rawType = Humanize(buff.BuffType);
        var fallbackName = FirstNonEmpty(
            scrubName,
            rawType,
            Humanize(buff.DisplayName),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Buff {buff.Slot + 1}"));
        var hasDefinition = definitions.TryResolve(
            [
                buff.ScrubTypeName,
                buff.BuffType,
                scrubName,
                rawType,
            ],
            out var definition);

        return new GameBuffOverlayEntry
        {
            Slot = buff.Slot,
            Identity = string.Create(
                CultureInfo.InvariantCulture,
                $"{buff.Slot}:{buff.BuffType}:{buff.ScrubTypeName}"),
            Name = hasDefinition
                ? definition.ToolTip
                : fallbackName,
            Description = hasDefinition
                ? definition.AlternateToolTip
                : "",
            IsGoodBuff = hasDefinition
                ? definition.IsGoodBuff
                : null,
            IsPermanent = buff.IsPermanent,
            RemainingMilliseconds = buff.NominalRemainingMilliseconds,
            RemovalAtClientTime = buff.NominalRemovalAtClientTime,
        };
    }

    private static string Humanize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        return string.Join(
            " ",
            value.Replace('_', ' ')
                .Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries));
    }

    private static string FirstNonEmpty(params string[] values)
    {
        return values.FirstOrDefault(value =>
            !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";
    }
}
