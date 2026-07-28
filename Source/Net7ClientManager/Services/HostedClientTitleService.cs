namespace Net7ClientManager.Services;

using System.Globalization;
using Net7ClientManager.Models;

public sealed class HostedClientTitleService
{
    public HostedClientTitlePresentation BuildPresentation(
        ClientInstance client,
        string? managerSlotName,
        string? statusText,
        bool showStatus)
    {
        ArgumentNullException.ThrowIfNull(client);

        List<HostedClientTitleSegment> segments =
        [
            new()
            {
                Kind = HostedClientTitleSegmentKind.ProcessId,
                Label = "PID",
                Value = client.ProcessId.ToString(
                    CultureInfo.InvariantCulture),
                Priority = 30,
            },
            BuildSlotSegment(managerSlotName),
        ];

        if (client.AutoLoginProvenance is
            { IsConfirmed: true } provenance &&
            !string.IsNullOrWhiteSpace(provenance.LoginName))
        {
            segments.Add(new HostedClientTitleSegment
            {
                Kind = HostedClientTitleSegmentKind.Account,
                Label = "Account",
                Value = UiObfuscationMode.AccountName(
                    provenance.LoginName.Trim()),
                Priority = 50,
            });
        }

        if (client.InGameSince.HasValue)
        {
            var localSince = client.InGameSince.Value.ToLocalTime();

            segments.Add(new HostedClientTitleSegment
            {
                Kind = HostedClientTitleSegmentKind.Since,
                Label = "Since",
                Value = localSince.ToString(
                    "yyyy-MM-dd HH:mm:ss",
                    CultureInfo.InvariantCulture),
                CompactValue = localSince.ToString(
                    "HH:mm:ss",
                    CultureInfo.InvariantCulture),
                Priority = 40,
            });
        }

        if (!string.IsNullOrWhiteSpace(
                client.LiveCharacterIdentity.Profession))
        {
            segments.Add(new HostedClientTitleSegment
            {
                Kind = HostedClientTitleSegmentKind.Profession,
                Label = "Class",
                Value = client.LiveCharacterIdentity.Profession.Trim(),
                Priority = 60,
            });
        }

        if (!string.IsNullOrWhiteSpace(
                client.LiveCharacterIdentity.Name))
        {
            segments.Add(new HostedClientTitleSegment
            {
                Kind = HostedClientTitleSegmentKind.CharacterName,
                Label = "Name",
                Value = client.LiveCharacterIdentity.Name.Trim(),
                Priority = 100,
                CanHide = false,
                IsEmphasized = true,
            });
        }

        if (showStatus && !string.IsNullOrWhiteSpace(statusText))
        {
            segments.Add(new HostedClientTitleSegment
            {
                Kind = HostedClientTitleSegmentKind.Status,
                Label = "Status",
                Value = statusText.Trim(),
                PlainText = statusText.Trim(),
                Priority = 90,
                CanHide = false,
                IsEmphasized = true,
            });
        }

        return new HostedClientTitlePresentation
        {
            WindowTitle = string.Join(
                " - ",
                segments.Select(FormatPlainText)),
            Segments = segments,
        };
    }

    private static HostedClientTitleSegment BuildSlotSegment(
        string? managerSlotName)
    {
        if (string.IsNullOrWhiteSpace(managerSlotName))
        {
            return new HostedClientTitleSegment
            {
                Kind = HostedClientTitleSegmentKind.Slot,
                Label = "Slot",
                Value = "Unassigned",
                PlainText = "Unassigned",
                Priority = 95,
                CanHide = false,
            };
        }

        var slotName = managerSlotName.Trim();

        return new HostedClientTitleSegment
        {
            Kind = HostedClientTitleSegmentKind.Slot,
            Label = "Slot",
            Value = slotName,
            PlainText = slotName,
            Priority = 95,
            CanHide = false,
        };
    }

    private static string FormatPlainText(
        HostedClientTitleSegment segment)
    {
        return segment.PlainText ?? string.Concat(
            segment.Label,
            ": ",
            segment.Value);
    }
}
