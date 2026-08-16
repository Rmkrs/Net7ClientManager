namespace Net7ClientManager.Services;

using System.Text;
using Net7ClientManager.Models;

/// <summary>
/// Resolves semantic game commands through the active user section in the
/// client-authored keymap.ini. The file is read for every invocation so an
/// applied Control Options change is visible without restarting the manager.
/// </summary>
internal sealed class GameKeyBindingResolver(
    GameKeyMapLocator keyMapLocator)
{
    private const int ReadAttemptCount = 4;
    private static readonly TimeSpan readRetryDelay =
        TimeSpan.FromMilliseconds(25);

    public async Task<GameCommandBindingResolution> ResolveAsync(
        ClientInstance client,
        GameCommand command,
        CancellationToken cancellationToken)
    {
        var definitionNames = GameCommandCatalog.GetDefinitionNames(command);
        var canonicalDefinitionName = definitionNames[0];

        if (TryResolveHardcodedBinding(
                command,
                canonicalDefinitionName,
                out var hardcodedResolution))
        {
            return hardcodedResolution;
        }

        var keyMapPath = keyMapLocator.Locate(client);

        if (keyMapPath == null)
        {
            return GameCommandBindingResolution.Failure(
                command,
                canonicalDefinitionName,
                @"Could not find ..\Data\client\output\keymap.ini relative to the running client.exe.");
        }

        // A pristine client does not materialize keymap.ini until the player
        // changes/saves Control Options. In that specific case the client is
        // still using its original keyboard defaults, so use the matching
        // built-in bindings. Once keymap.ini exists it remains authoritative:
        // absent entries there are intentional user unbinds and must not fall
        // back to defaults.
        if (!File.Exists(keyMapPath) &&
            TryResolveDefaultClientBinding(
                command,
                canonicalDefinitionName,
                keyMapPath,
                out var defaultResolution))
        {
            return defaultResolution;
        }

        GameKeyMapDocument? document = null;
        var parseError = "";

        for (var attempt = 0;
             attempt < ReadAttemptCount;
             attempt++)
        {
            var readResult = await ReadKeyMapAsync(
                    keyMapPath,
                    cancellationToken)
                .ConfigureAwait(false);

            if (!readResult.Succeeded)
            {
                return GameCommandBindingResolution.Failure(
                    command,
                    canonicalDefinitionName,
                    readResult.Error,
                    keyMapPath);
            }

            if (GameKeyMapDocument.TryParse(
                    readResult.Text,
                    out document,
                    out parseError) &&
                document != null)
            {
                break;
            }

            if (attempt + 1 < ReadAttemptCount)
            {
                await Task.Delay(
                        readRetryDelay,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        if (document == null)
        {
            return GameCommandBindingResolution.Failure(
                command,
                canonicalDefinitionName,
                parseError,
                keyMapPath);
        }

        // keymap.ini is the client-authored materialized user map. A missing
        // slot is intentionally unbound; never resurrect a control_keys.ini
        // default for an absent entry.
        List<string> candidateErrors = [];

        foreach (var definitionName in definitionNames)
        {
            var primaryResult = ParseBindingSlot(
                document.GetBinding(definitionName, slot: 1),
                slotName: "primary");

            var alternateResult = ParseBindingSlot(
                document.GetBinding(definitionName, slot: 2),
                slotName: "alternate");

            if (primaryResult.Chord != null ||
                alternateResult.Chord != null)
            {
                return new GameCommandBindingResolution
                {
                    Command = command,
                    DefinitionName = definitionName,
                    Succeeded = true,
                    KeyMapPath = keyMapPath,
                    UserProfile = document.ActiveUserProfile,
                    Primary = primaryResult.Chord,
                    Alternate = alternateResult.Chord,
                };
            }

            var parseErrors = new[]
                {
                    primaryResult,
                    alternateResult,
                }
                .Where(result => !result.Succeeded)
                .Select(result =>
                    $"{result.SlotName}: {result.Error}")
                .ToList();

            candidateErrors.Add(parseErrors.Count == 0
                ? $"'{definitionName}' is unbound"
                : string.Concat(
                    $"'{definitionName}' has no executable binding: ",
                    string.Join("; ", parseErrors)));
        }

        var error = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"No executable input binding was found for {command} in active keymap profile [{document.ActiveUserProfile}]. Tried: {string.Join("; ", candidateErrors)}.");

        return GameCommandBindingResolution.Failure(
            command,
            canonicalDefinitionName,
            error,
            keyMapPath,
            document.ActiveUserProfile);
    }

    public void ForgetProcess(int processId)
    {
        keyMapLocator.ForgetProcess(processId);
    }

    private static bool TryResolveDefaultClientBinding(
        GameCommand command,
        string definitionName,
        string keyMapPath,
        out GameCommandBindingResolution resolution)
    {
        resolution = default!;

        // Original Earth & Beyond keyboard defaults. These are only consulted
        // when keymap.ini itself does not exist, which represents an untouched
        // Control Options setup. Do not add uncertain mappings here.
        var sourceText = command switch
        {
            GameCommand.FireAll => "F",
            GameCommand.TargetNearestNavigation => "W",
            GameCommand.TargetNearestObject => "X",
            GameCommand.NextContextTarget => "D",
            GameCommand.PreviousContextTarget => "C",
            GameCommand.NextTarget => "N",
            GameCommand.PreviousTarget => "P",
            GameCommand.Warp => "Q",
            GameCommand.Formation => "T",
            GameCommand.FireActivateSlot1 => "1",
            GameCommand.FireActivateSlot2 => "2",
            GameCommand.FireActivateSlot3 => "3",
            GameCommand.FireActivateSlot4 => "4",
            GameCommand.FireActivateSlot5 => "5",
            GameCommand.FireActivateSlot6 => "6",
            _ => null,
        };

        if (sourceText == null)
        {
            return false;
        }

        if (!GameKeyChordParser.TryParse(
                sourceText,
                out var chord,
                out var error) ||
            chord == null)
        {
            resolution = GameCommandBindingResolution.Failure(
                command,
                definitionName,
                string.Concat(
                    "Could not resolve the built-in client default binding: ",
                    error),
                keyMapPath,
                "Built-in defaults");
            return true;
        }

        resolution = new GameCommandBindingResolution
        {
            Command = command,
            DefinitionName = definitionName,
            Succeeded = true,
            KeyMapPath = keyMapPath,
            UserProfile = "Built-in defaults",
            Primary = chord,
        };
        return true;
    }

    private static bool TryResolveHardcodedBinding(
        GameCommand command,
        string definitionName,
        out GameCommandBindingResolution resolution)
    {
        resolution = default!;

        // control_keys.ini explicitly marks Shift Shortcuts as non-definable
        // and hard-codes it to Alt. It is therefore absent from the active
        // user profile in keymap.ini and must not be treated as unbound.
        if (command != GameCommand.SwapShortcutBanks)
        {
            return false;
        }

        if (!GameKeyChordParser.TryParse(
                "Alt",
                out var chord,
                out var error) ||
            chord == null)
        {
            resolution = GameCommandBindingResolution.Failure(
                command,
                definitionName,
                string.Concat(
                    "Could not resolve the hard-coded shortcut-bank modifier: ",
                    error));
            return true;
        }

        resolution = new GameCommandBindingResolution
        {
            Command = command,
            DefinitionName = definitionName,
            Succeeded = true,
            Primary = chord,
        };
        return true;
    }

    private static BindingSlotParseResult ParseBindingSlot(
        string? sourceText,
        string slotName)
    {
        if (string.IsNullOrWhiteSpace(sourceText))
        {
            return BindingSlotParseResult.Success(
                slotName,
                chord: null);
        }

        return GameKeyChordParser.TryParse(
                sourceText,
                out var chord,
                out var error)
            ? BindingSlotParseResult.Success(
                slotName,
                chord)
            : BindingSlotParseResult.Failure(
                slotName,
                error);
    }

    private static async Task<KeyMapReadResult> ReadKeyMapAsync(
        string path,
        CancellationToken cancellationToken)
    {
        Exception? lastException = null;

        for (var attempt = 0;
             attempt < ReadAttemptCount;
             attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    bufferSize: 4096,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);

                using var reader = new StreamReader(
                    stream,
                    Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: true);

                var text = await reader
                    .ReadToEndAsync(cancellationToken)
                    .ConfigureAwait(false);

                return KeyMapReadResult.Success(text);
            }
            catch (Exception ex) when (
                ex is IOException or
                UnauthorizedAccessException)
            {
                lastException = ex;

                if (attempt + 1 < ReadAttemptCount)
                {
                    await Task.Delay(
                            readRetryDelay,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }

        return KeyMapReadResult.Failure(
            string.Concat(
                "Could not read keymap.ini: ",
                lastException?.Message ?? "unknown file error"));
    }

    private sealed record BindingSlotParseResult(
        bool Succeeded,
        string SlotName,
        GameKeyChord? Chord,
        string Error)
    {
        public static BindingSlotParseResult Success(
            string slotName,
            GameKeyChord? chord)
        {
            return new BindingSlotParseResult(
                Succeeded: true,
                SlotName: slotName,
                Chord: chord,
                Error: "");
        }

        public static BindingSlotParseResult Failure(
            string slotName,
            string error)
        {
            return new BindingSlotParseResult(
                Succeeded: false,
                SlotName: slotName,
                Chord: null,
                Error: error);
        }
    }

    private sealed record KeyMapReadResult(
        bool Succeeded,
        string Text,
        string Error)
    {
        public static KeyMapReadResult Success(string text)
        {
            return new KeyMapReadResult(
                Succeeded: true,
                Text: text,
                Error: "");
        }

        public static KeyMapReadResult Failure(string error)
        {
            return new KeyMapReadResult(
                Succeeded: false,
                Text: "",
                Error: error);
        }
    }
}
