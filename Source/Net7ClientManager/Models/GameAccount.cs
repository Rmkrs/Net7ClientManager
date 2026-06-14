namespace Net7ClientManager.Models;

public sealed class GameAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public int SortOrder { get; set; }

    public string DisplayName { get; set; } = "";

    public string LoginName { get; set; } = "";

    public string? ProtectedPassword { get; set; }

    public List<GameCharacter> Characters { get; set; } = [];

    public override string ToString()
    {
        return string.IsNullOrWhiteSpace(this.DisplayName)
            ? this.LoginName
            : this.DisplayName;
    }
}
