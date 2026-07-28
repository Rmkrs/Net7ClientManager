namespace Net7ClientManager.Models;

internal sealed class GameCommandBindingResolution
{
    public required GameCommand Command { get; init; }

    public required string DefinitionName { get; init; }

    public bool Succeeded { get; init; }

    public string Error { get; init; } = "";

    public string? KeyMapPath { get; init; }

    public string? UserProfile { get; init; }

    public GameKeyChord? Primary { get; init; }

    public GameKeyChord? Alternate { get; init; }

    public GameKeyChord? Preferred => this.Primary ?? this.Alternate;

    public static GameCommandBindingResolution Failure(
        GameCommand command,
        string definitionName,
        string error,
        string? keyMapPath = null,
        string? userProfile = null)
    {
        return new GameCommandBindingResolution
        {
            Command = command,
            DefinitionName = definitionName,
            Error = error,
            KeyMapPath = keyMapPath,
            UserProfile = userProfile,
        };
    }
}
