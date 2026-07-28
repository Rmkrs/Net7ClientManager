namespace Net7ClientManager.Services;

using System.Text;
using Net7ClientManager.Models;

internal sealed class CommandPaletteHotKeyValidator(
    GameKeyMapLocator keyMapLocator)
{
    private const int ReadAttemptCount = 4;
    private static readonly TimeSpan readRetryDelay =
        TimeSpan.FromMilliseconds(25);

    public async Task<string?> ValidateAsync(
        IReadOnlyCollection<ClientInstance> clients,
        Keys hotKey,
        CancellationToken cancellationToken)
    {
        var keyCode = hotKey & Keys.KeyCode;

        if (keyCode == Keys.None || IsModifierKey(keyCode))
        {
            return "Choose a shortcut that includes a non-modifier key.";
        }

        var clientsByKeyMapPath =
            new Dictionary<string, List<ClientInstance>>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var client in clients)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var path = keyMapLocator.Locate(client);

            if (path == null)
            {
                return string.Concat(
                    "Could not locate keymap.ini for ",
                    GetClientDisplayName(client),
                    ". The shortcut was not changed.");
            }

            if (!clientsByKeyMapPath.TryGetValue(path, out var pathClients))
            {
                pathClients = [];
                clientsByKeyMapPath.Add(path, pathClients);
            }

            pathClients.Add(client);
        }

        if (clientsByKeyMapPath.Count == 0)
        {
            return "Start a game client before changing the Command Palette shortcut so its game controls can be checked.";
        }

        List<string> conflicts = [];

        foreach (var entry in clientsByKeyMapPath)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var readResult = await ReadKeyMapAsync(
                    entry.Key,
                    cancellationToken)
                .ConfigureAwait(false);

            if (!readResult.Succeeded)
            {
                return string.Concat(
                    readResult.Error,
                    " The shortcut was not changed.");
            }

            if (!GameKeyMapDocument.TryParse(
                    readResult.Text,
                    out var document,
                    out var parseError) ||
                document == null)
            {
                return string.Concat(
                    "Could not verify game controls in ",
                    entry.Key,
                    ": ",
                    parseError,
                    " The shortcut was not changed.");
            }

            foreach (var binding in document.GetBindings())
            {
                if (!GameKeyChordParser.TryParse(
                        binding.SourceText,
                        out var chord,
                        out var bindingError))
                {
                    if (LooksLikeMouseBinding(binding.SourceText))
                    {
                        continue;
                    }

                    return string.Concat(
                        "Could not verify game action '",
                        binding.DefinitionName,
                        "' in ",
                        entry.Key,
                        ": ",
                        bindingError,
                        " The shortcut was not changed.");
                }

                if (chord == null ||
                    Normalize(chord.KeyData) != Normalize(hotKey))
                {
                    continue;
                }

                var pilots = string.Join(
                    ", ",
                    entry.Value
                        .Select(GetClientDisplayName)
                        .Distinct(StringComparer.OrdinalIgnoreCase));

                conflicts.Add(
                    string.Concat(
                        "'",
                        binding.DefinitionName,
                        "' (",
                        binding.Slot == 1 ? "primary" : "alternate",
                        ") for ",
                        pilots));
            }
        }

        if (conflicts.Count == 0)
        {
            return null;
        }

        return string.Concat(
            FormatHotKey(hotKey),
            " conflicts with game controls: ",
            string.Join("; ", conflicts.Distinct(StringComparer.OrdinalIgnoreCase)),
            ". Choose another shortcut.");
    }

    public static string FormatHotKey(Keys hotKey)
    {
        List<string> parts = [];

        if ((hotKey & Keys.Control) == Keys.Control)
        {
            parts.Add("Ctrl");
        }

        if ((hotKey & Keys.Shift) == Keys.Shift)
        {
            parts.Add("Shift");
        }

        if ((hotKey & Keys.Alt) == Keys.Alt)
        {
            parts.Add("Alt");
        }

        var keyCode = hotKey & Keys.KeyCode;
        parts.Add(FormatKeyCode(keyCode));

        return string.Join("+", parts);
    }

    private static Keys Normalize(Keys keys)
    {
        return (keys & Keys.KeyCode) |
               (keys & (Keys.Control | Keys.Shift | Keys.Alt));
    }

    private static bool IsModifierKey(Keys keyCode)
    {
        return keyCode is
            Keys.ControlKey or
            Keys.LControlKey or
            Keys.RControlKey or
            Keys.ShiftKey or
            Keys.LShiftKey or
            Keys.RShiftKey or
            Keys.Menu or
            Keys.LMenu or
            Keys.RMenu or
            Keys.LWin or
            Keys.RWin;
    }

    private static string FormatKeyCode(Keys keyCode)
    {
        if ((int)keyCode >= (int)Keys.D0 &&
            (int)keyCode <= (int)Keys.D9)
        {
            return ((int)keyCode - (int)Keys.D0)
                .ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if ((int)keyCode >= (int)Keys.NumPad0 &&
            (int)keyCode <= (int)Keys.NumPad9)
        {
            return string.Concat(
                "Keypad ",
                ((int)keyCode - (int)Keys.NumPad0)
                    .ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return keyCode switch
        {
            Keys.OemSemicolon => ";",
            Keys.OemQuotes => "'",
            Keys.Oemcomma => ",",
            Keys.OemMinus => "-",
            Keys.OemPeriod => ".",
            Keys.OemQuestion => "/",
            Keys.OemOpenBrackets => "[",
            Keys.OemPipe => "\\",
            Keys.OemCloseBrackets => "]",
            Keys.Oemtilde => "`",
            Keys.Oemplus => "=",
            Keys.Return => "Enter",
            Keys.Escape => "Esc",
            Keys.Capital => "Caps Lock",
            Keys.PageUp => "Page Up",
            Keys.PageDown => "Page Down",
            _ => keyCode.ToString(),
        };
    }

    private static bool LooksLikeMouseBinding(string sourceText)
    {
        return sourceText.Contains(
            "Mouse",
            StringComparison.OrdinalIgnoreCase);
    }

    private static string GetClientDisplayName(ClientInstance client)
    {
        return string.IsNullOrWhiteSpace(client.LiveCharacterIdentity.Name)
            ? string.Concat(
                "client ",
                client.ProcessId.ToString(
                    System.Globalization.CultureInfo.InvariantCulture))
            : client.LiveCharacterIdentity.Name.Trim();
    }

    private static async Task<KeyMapReadResult> ReadKeyMapAsync(
        string path,
        CancellationToken cancellationToken)
    {
        Exception? lastException = null;

        for (var attempt = 0; attempt < ReadAttemptCount; attempt++)
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
                ex is IOException or UnauthorizedAccessException)
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
