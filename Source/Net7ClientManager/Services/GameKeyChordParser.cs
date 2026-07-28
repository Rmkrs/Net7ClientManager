namespace Net7ClientManager.Services;

using System.Globalization;
using Net7ClientManager.Models;

internal static class GameKeyChordParser
{
    private static readonly IReadOnlyDictionary<string, KeyToken> namedKeys =
        new Dictionary<string, KeyToken>(StringComparer.OrdinalIgnoreCase)
        {
            ["Alt"] = new(Keys.Menu, "Alt"),
            ["L Alt"] = new(Keys.LMenu, "Left Alt"),
            ["Left Alt"] = new(Keys.LMenu, "Left Alt"),
            ["R Alt"] = new(Keys.RMenu, "Right Alt"),
            ["Right Alt"] = new(Keys.RMenu, "Right Alt"),
            ["Ctrl"] = new(Keys.ControlKey, "Ctrl"),
            ["Control"] = new(Keys.ControlKey, "Ctrl"),
            ["L Ctrl"] = new(Keys.LControlKey, "Left Ctrl"),
            ["Left Ctrl"] = new(Keys.LControlKey, "Left Ctrl"),
            ["R Ctrl"] = new(Keys.RControlKey, "Right Ctrl"),
            ["Right Ctrl"] = new(Keys.RControlKey, "Right Ctrl"),
            ["Shift"] = new(Keys.ShiftKey, "Shift"),
            ["L Shift"] = new(Keys.LShiftKey, "Left Shift"),
            ["Left Shift"] = new(Keys.LShiftKey, "Left Shift"),
            ["R Shift"] = new(Keys.RShiftKey, "Right Shift"),
            ["Right Shift"] = new(Keys.RShiftKey, "Right Shift"),
            ["Backspace"] = new(Keys.Back, "Backspace"),
            ["Caps"] = new(Keys.CapsLock, "Caps Lock"),
            ["Del"] = new(Keys.Delete, "Delete", IsExtended: true),
            ["Down"] = new(Keys.Down, "Down", IsExtended: true),
            ["End"] = new(Keys.End, "End", IsExtended: true),
            ["Home"] = new(Keys.Home, "Home", IsExtended: true),
            ["Ins"] = new(Keys.Insert, "Insert", IsExtended: true),
            ["Left"] = new(Keys.Left, "Left", IsExtended: true),
            ["Num Lock"] = new(Keys.NumLock, "Num Lock", IsExtended: true),
            ["Pause"] = new(Keys.Pause, "Pause"),
            ["PgDn"] = new(Keys.PageDown, "Page Down", IsExtended: true),
            ["PgUp"] = new(Keys.PageUp, "Page Up", IsExtended: true),
            ["Return"] = new(Keys.Enter, "Enter"),
            ["Right"] = new(Keys.Right, "Right", IsExtended: true),
            ["Scroll Lock"] = new(Keys.Scroll, "Scroll Lock"),
            ["Semicolon"] = new(Keys.OemSemicolon, ";"),
            ["Space"] = new(Keys.Space, "Space"),
            ["Tab"] = new(Keys.Tab, "Tab"),
            ["Up"] = new(Keys.Up, "Up", IsExtended: true),
            ["Keypad ."] = new(Keys.Decimal, "Keypad ."),
            ["Keypad -"] = new(Keys.Subtract, "Keypad -"),
            ["Keypad *"] = new(Keys.Multiply, "Keypad *"),
            ["Keypad /"] = new(Keys.Divide, "Keypad /", IsExtended: true),
            ["Keypad +"] = new(Keys.Add, "Keypad +"),
            ["Keypad Enter"] = new(Keys.Enter, "Keypad Enter", IsExtended: true),
            ["'"] = new(Keys.OemQuotes, "'"),
            ["-"] = new(Keys.OemMinus, "-"),
            [","] = new(Keys.Oemcomma, ","),
            ["."] = new(Keys.OemPeriod, "."),
            ["/"] = new(Keys.OemQuestion, "/"),
            ["["] = new(Keys.OemOpenBrackets, "["),
            ["\\"] = new(Keys.OemPipe, "\\"),
            ["]"] = new(Keys.OemCloseBrackets, "]"),
            ["`"] = new(Keys.Oemtilde, "`"),
            ["="] = new(Keys.Oemplus, "="),
        };

    public static bool TryParse(
        string sourceText,
        out GameKeyChord? chord,
        out string error)
    {
        chord = null;
        error = "";

        var value = sourceText.Trim();

        if (value.Length == 0)
        {
            return true;
        }

        var modifiers = Keys.None;
        var index = 0;

        while (index < value.Length)
        {
            switch (value[index])
            {
                case '@':
                    modifiers |= Keys.Alt;
                    index++;
                    continue;

                case '^':
                    modifiers |= Keys.Control;
                    index++;
                    continue;

                case '$':
                    modifiers |= Keys.Shift;
                    index++;
                    continue;
            }

            break;
        }

        var keyText = value[index..].Trim();

        if (keyText.Length == 0)
        {
            error = $"Binding '{sourceText}' contains modifiers but no key.";
            return false;
        }

        if (string.Equals(
                keyText,
                "M Mouse",
                StringComparison.OrdinalIgnoreCase))
        {
            error =
                "Middle-mouse bindings are not supported by the host input executor yet.";

            return false;
        }

        if (!TryResolveKeyToken(keyText, out var keyToken))
        {
            error = $"Binding key '{keyText}' is not recognized.";
            return false;
        }

        chord = new GameKeyChord(
            KeyData: keyToken.Key | modifiers,
            SourceText: value,
            DisplayText: BuildDisplayText(
                modifiers,
                keyToken.DisplayText),
            IsExtendedKey: keyToken.IsExtended);

        return true;
    }

    private static string BuildDisplayText(
        Keys modifiers,
        string keyDisplayText)
    {
        var displayParts = new List<string>(4);

        if ((modifiers & Keys.Control) == Keys.Control)
        {
            displayParts.Add("Ctrl");
        }

        if ((modifiers & Keys.Shift) == Keys.Shift)
        {
            displayParts.Add("Shift");
        }

        if ((modifiers & Keys.Alt) == Keys.Alt)
        {
            displayParts.Add("Alt");
        }

        displayParts.Add(keyDisplayText);

        return string.Join("+", displayParts);
    }

    private static bool TryResolveKeyToken(
        string keyText,
        out KeyToken keyToken)
    {
        if (namedKeys.TryGetValue(keyText, out keyToken))
        {
            return true;
        }

        if (keyText.Length == 1)
        {
            var character = char.ToUpperInvariant(keyText[0]);

            if (character is >= 'A' and <= 'Z')
            {
                keyToken = new KeyToken(
                    (Keys)((int)Keys.A + character - 'A'),
                    character.ToString());

                return true;
            }

            if (character is >= '0' and <= '9')
            {
                keyToken = new KeyToken(
                    (Keys)((int)Keys.D0 + character - '0'),
                    character.ToString());

                return true;
            }
        }

        if (keyText.StartsWith("F", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(
                keyText.AsSpan(1),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var functionKeyNumber) &&
            functionKeyNumber is >= 1 and <= 12)
        {
            keyToken = new KeyToken(
                (Keys)((int)Keys.F1 + functionKeyNumber - 1),
                string.Concat(
                    "F",
                    functionKeyNumber.ToString(
                        CultureInfo.InvariantCulture)));

            return true;
        }

        const string keypadPrefix = "Keypad ";

        if (keyText.StartsWith(keypadPrefix, StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(
                keyText.AsSpan(keypadPrefix.Length),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var keypadNumber) &&
            keypadNumber is >= 0 and <= 9)
        {
            keyToken = new KeyToken(
                (Keys)((int)Keys.NumPad0 + keypadNumber),
                string.Concat(
                    "Keypad ",
                    keypadNumber.ToString(
                        CultureInfo.InvariantCulture)));

            return true;
        }

        keyToken = default;
        return false;
    }

    private readonly record struct KeyToken(
        Keys Key,
        string DisplayText,
        bool IsExtended = false);
}
