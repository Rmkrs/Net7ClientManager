// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Net7ClientManager.Models;
using Net7ClientManager.Services;

public sealed partial class MainForm
{
    private void RecordCommandPaletteDiagnostic(
        string eventName,
        ClientInstance? client = null,
        string? details = null)
    {
        this.commandPaletteDiagnostics.Record(
            eventName,
            client,
            details);
    }

    private string CreateSupportDiagnosticsReport()
    {
        var builder = new StringBuilder(capacity: 4_096);
        var assembly = typeof(MainForm).Assembly;
        var version = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (string.IsNullOrWhiteSpace(version))
        {
            version = Application.ProductVersion;
        }

        var buildMetadataSeparator = version.IndexOf('+');
        if (buildMetadataSeparator >= 0)
        {
            version = version[..buildMetadataSeparator];
        }

        var commandSettings = this.clientManager.FleetCommandSettings;
        commandSettings.EnsureDefaults();

        builder.AppendLine("Net7 Client Manager diagnostics");
        builder.Append("Generated UTC: ")
            .AppendLine(DateTimeOffset.UtcNow.ToString("O"));
        builder.Append("Version: ").AppendLine(version);
        builder.Append("Runtime: ")
            .Append(RuntimeInformation.FrameworkDescription)
            .Append(" | ")
            .Append(RuntimeInformation.OSDescription)
            .Append(" | ")
            .Append(RuntimeInformation.ProcessArchitecture)
            .AppendLine();

        builder.Append("Command Palette: Show=")
            .Append(commandSettings.EffectiveCommandMenuShowMode)
            .Append(" | Placement=")
            .Append(commandSettings.CommandMenuPlacement)
            .Append(" | Shortcut=")
            .Append(CommandPaletteHotKeyValidator.FormatHotKey(
                commandSettings.CommandMenuHotKey))
            .Append(" | Hook=")
            .Append(this.commandPaletteKeyboardHook != null
                ? "Active"
                : "Inactive")
            .AppendLine();

        if (!string.IsNullOrWhiteSpace(this.commandPaletteKeyboardHookError))
        {
            builder.Append("Hook error: ")
                .AppendLine(this.commandPaletteKeyboardHookError);
        }

        builder.Append("Fleet Fire All: Shortcut=")
            .Append(commandSettings.FleetFireAllHotKey == Keys.None
                ? "Not set"
                : CommandPaletteHotKeyValidator.FormatHotKey(
                    commandSettings.FleetFireAllHotKey))
            .Append(" | Hook=")
            .Append(this.fleetFireAllKeyboardHook != null
                ? "Active"
                : "Inactive")
            .AppendLine();

        if (!string.IsNullOrWhiteSpace(this.fleetFireAllKeyboardHookError))
        {
            builder.Append("Fleet Fire All hook error: ")
                .AppendLine(this.fleetFireAllKeyboardHookError);
        }

        var clients = this.clientManager.Clients
            .OrderBy(client => client.ProcessId)
            .ToArray();

        builder.Append("Managed clients: ")
            .AppendLine(clients.Length.ToString());

        foreach (var client in clients)
        {
            var slotName = ResolveSlotName(
                this.clientManager.ActiveProfile,
                client.AssignedSlotId);
            var characterName = client.LiveCharacterIdentity.Name;

            builder.Append("  PID ").Append(client.ProcessId)
                .Append(" | ")
                .Append(string.IsNullOrWhiteSpace(slotName)
                    ? "(unassigned)"
                    : slotName)
                .Append(" | ")
                .Append(string.IsNullOrWhiteSpace(characterName)
                    ? "(unknown)"
                    : characterName)
                .Append(" | ")
                .Append(client.LifecycleState)
                .Append(" | ")
                .Append(client.State)
                .AppendLine();
        }

        builder.AppendLine("Recent Command Palette events (last 10):");

        var paletteEntries = this.commandPaletteDiagnostics.Snapshot();

        if (paletteEntries.Count == 0)
        {
            builder.AppendLine("  None.");
        }
        else
        {
            foreach (var entry in paletteEntries)
            {
                builder.Append("  ")
                    .Append(entry.TimestampUtc.ToString("O"))
                    .Append(" | ")
                    .Append(entry.EventName);

                if (entry.ProcessId is { } processId)
                {
                    builder.Append(" | PID ").Append(processId);
                }

                if (!string.IsNullOrWhiteSpace(entry.CharacterName))
                {
                    builder.Append(" | Character ")
                        .Append(entry.CharacterName);
                }

                if (!string.IsNullOrWhiteSpace(entry.Details))
                {
                    builder.Append(" | ").Append(entry.Details);
                }

                builder.AppendLine();
            }
        }

        return builder.ToString();
    }

    private static string? ResolveSlotName(
        LayoutProfile? profile,
        Guid? slotId)
    {
        if (profile == null || slotId == null)
        {
            return null;
        }

        return profile.Slots.FirstOrDefault(slot => slot.Id == slotId.Value)?.Name;
    }
}
