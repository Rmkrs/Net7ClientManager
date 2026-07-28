namespace Net7ClientManager.Services;

using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Win32;

internal sealed class GameRenderResolutionOverrideCoordinator
{
    private const string RegistrySubKey =
        @"SOFTWARE\Westwood Studios\Earth and Beyond\Render";

    private const string WidthValueName = "RenderDeviceWidth";
    private const string HeightValueName = "RenderDeviceHeight";

    private static readonly JsonSerializerOptions jsonSerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string journalPath;
    private RenderResolutionOverrideJournal? activeJournal;

    public GameRenderResolutionOverrideCoordinator()
    {
        var settingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Net7ClientManager");

        this.journalPath = Path.Combine(
            settingsDirectory,
            "pending-render-registry-restore.json");

        if (!this.TryRestore(out var recoveryStatus))
        {
            this.LastStatus = recoveryStatus;
            Debug.WriteLine(string.Concat(
                "[RenderResolution] Startup recovery failed: ",
                recoveryStatus));
        }
        else if (!string.IsNullOrWhiteSpace(recoveryStatus))
        {
            this.LastStatus = recoveryStatus;
            Debug.WriteLine(string.Concat(
                "[RenderResolution] ",
                recoveryStatus));
        }
    }

    public bool HasActiveOverride =>
        this.activeJournal != null || File.Exists(this.journalPath);

    public string? LastStatus { get; private set; }

    public bool TryApply(
        int width,
        int height,
        out string status)
    {
        status = string.Empty;
        this.LastStatus = null;

        if (width <= 0 || height <= 0)
        {
            status = "The configured game resolution is invalid.";
            this.LastStatus = status;
            return false;
        }

        if (this.HasActiveOverride)
        {
            status =
                "A previous temporary game-resolution override still needs to be restored.";
            this.LastStatus = status;
            return false;
        }

        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(
                RegistryHive.LocalMachine,
                RegistryView.Registry32);
            using var renderKey = baseKey.OpenSubKey(
                RegistrySubKey,
                writable: true);

            if (renderKey == null)
            {
                status = string.Concat(
                    "Earth & Beyond's render registry key was not found: ",
                    RegistrySubKey,
                    ".");
                this.LastStatus = status;
                return false;
            }

            var journal = new RenderResolutionOverrideJournal
            {
                CreatedAtUtc = DateTimeOffset.UtcNow,
                TemporaryWidth = width,
                TemporaryHeight = height,
                OriginalWidth = ReadDwordState(
                    renderKey,
                    WidthValueName),
                OriginalHeight = ReadDwordState(
                    renderKey,
                    HeightValueName),
            };

            this.WriteJournal(journal);
            this.activeJournal = journal;

            try
            {
                renderKey.SetValue(
                    WidthValueName,
                    width,
                    RegistryValueKind.DWord);
                renderKey.SetValue(
                    HeightValueName,
                    height,
                    RegistryValueKind.DWord);

                var writtenWidth = ReadDwordState(
                    renderKey,
                    WidthValueName);
                var writtenHeight = ReadDwordState(
                    renderKey,
                    HeightValueName);

                if (!writtenWidth.Exists ||
                    writtenWidth.Value != width ||
                    !writtenHeight.Exists ||
                    writtenHeight.Value != height)
                {
                    throw new InvalidOperationException(
                        "The game-resolution registry values could not be verified after writing them.");
                }
            }
            catch
            {
                _ = this.TryRestore(out _);
                throw;
            }

            status = string.Create(
                CultureInfo.InvariantCulture,
                $"Temporarily configured Earth & Beyond for {width}×{height}.");
            this.LastStatus = status;
            return true;
        }
        catch (Exception exception)
        {
            status = string.Concat(
                "Earth & Beyond's game resolution could not be configured: ",
                exception.Message);
            this.LastStatus = status;
            return false;
        }
    }

    public bool TryRestore(out string status)
    {
        status = string.Empty;

        var journal = this.activeJournal ?? this.TryReadJournal();

        if (journal == null)
        {
            return true;
        }

        this.activeJournal = journal;

        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(
                RegistryHive.LocalMachine,
                RegistryView.Registry32);
            using var renderKey = baseKey.OpenSubKey(
                RegistrySubKey,
                writable: true);

            if (renderKey == null)
            {
                status = string.Concat(
                    "Earth & Beyond's render registry key was not found while restoring the previous resolution: ",
                    RegistrySubKey,
                    ".");
                this.LastStatus = status;
                return false;
            }

            var widthResult = RestoreValueIfStillTemporary(
                renderKey,
                WidthValueName,
                journal.TemporaryWidth,
                journal.OriginalWidth);
            var heightResult = RestoreValueIfStillTemporary(
                renderKey,
                HeightValueName,
                journal.TemporaryHeight,
                journal.OriginalHeight);

            this.DeleteJournal();
            this.activeJournal = null;

            if (widthResult == RestoreValueResult.SkippedNewerValue ||
                heightResult == RestoreValueResult.SkippedNewerValue)
            {
                status =
                    "The previous game resolution was restored where safe; newer registry changes were left untouched.";
            }
            else
            {
                status = "The previous Earth & Beyond game resolution was restored.";
            }

            this.LastStatus = status;
            return true;
        }
        catch (Exception exception)
        {
            status = string.Concat(
                "The previous Earth & Beyond game resolution could not be restored: ",
                exception.Message);
            this.LastStatus = status;
            return false;
        }
    }

    private RenderResolutionOverrideJournal? TryReadJournal()
    {
        if (!File.Exists(this.journalPath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(this.journalPath);
            return JsonSerializer.Deserialize<RenderResolutionOverrideJournal>(
                json,
                jsonSerializerOptions);
        }
        catch (Exception exception)
        {
            Debug.WriteLine(string.Concat(
                "[RenderResolution] Invalid recovery journal: ",
                exception.Message));

            try
            {
                var brokenPath = string.Concat(this.journalPath, ".broken");

                if (File.Exists(brokenPath))
                {
                    File.Delete(brokenPath);
                }

                File.Move(this.journalPath, brokenPath);
            }
            catch (Exception)
            {
            }

            return null;
        }
    }

    private void WriteJournal(RenderResolutionOverrideJournal journal)
    {
        var directory = Path.GetDirectoryName(this.journalPath)!;
        Directory.CreateDirectory(directory);

        var temporaryPath = string.Concat(this.journalPath, ".tmp");
        var json = JsonSerializer.Serialize(journal, jsonSerializerOptions);

        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, this.journalPath, overwrite: true);
    }

    private void DeleteJournal()
    {
        if (File.Exists(this.journalPath))
        {
            File.Delete(this.journalPath);
        }

        var temporaryPath = string.Concat(this.journalPath, ".tmp");

        if (File.Exists(temporaryPath))
        {
            File.Delete(temporaryPath);
        }
    }

    private static RegistryDwordState ReadDwordState(
        RegistryKey key,
        string valueName)
    {
        var value = key.GetValue(
            valueName,
            defaultValue: null,
            RegistryValueOptions.DoNotExpandEnvironmentNames);

        if (value == null)
        {
            return new RegistryDwordState();
        }

        if (key.GetValueKind(valueName) != RegistryValueKind.DWord)
        {
            throw new InvalidOperationException(string.Concat(
                "Registry value ",
                valueName,
                " is not a DWORD."));
        }

        return new RegistryDwordState
        {
            Exists = true,
            Value = Convert.ToInt32(value, CultureInfo.InvariantCulture),
        };
    }

    private static RestoreValueResult RestoreValueIfStillTemporary(
        RegistryKey key,
        string valueName,
        int temporaryValue,
        RegistryDwordState originalValue)
    {
        var currentValue = ReadDwordState(key, valueName);

        if (!currentValue.Exists || currentValue.Value != temporaryValue)
        {
            return RestoreValueResult.SkippedNewerValue;
        }

        if (originalValue.Exists)
        {
            key.SetValue(
                valueName,
                originalValue.Value,
                RegistryValueKind.DWord);
        }
        else
        {
            key.DeleteValue(valueName, throwOnMissingValue: false);
        }

        return RestoreValueResult.Restored;
    }

    private sealed class RenderResolutionOverrideJournal
    {
        public RenderResolutionOverrideJournal()
        {
        }

        public DateTimeOffset CreatedAtUtc { get; set; }

        public int TemporaryWidth { get; set; }

        public int TemporaryHeight { get; set; }

        public RegistryDwordState OriginalWidth { get; set; } = new();

        public RegistryDwordState OriginalHeight { get; set; } = new();
    }

    private sealed class RegistryDwordState
    {
        public RegistryDwordState()
        {
        }

        public bool Exists { get; set; }

        public int Value { get; set; }
    }

    private enum RestoreValueResult
    {
        Restored,
        SkippedNewerValue,
    }
}
