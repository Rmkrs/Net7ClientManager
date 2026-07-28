namespace Net7ClientManager.Models;

/// <summary>
/// One keyboard chord parsed from the game's keymap.ini syntax.
/// </summary>
internal sealed record GameKeyChord(
    Keys KeyData,
    string SourceText,
    string DisplayText,
    bool IsExtendedKey)
{
    public Keys KeyCode => this.KeyData & Keys.KeyCode;

    public Keys Modifiers => this.KeyData & Keys.Modifiers;
}
