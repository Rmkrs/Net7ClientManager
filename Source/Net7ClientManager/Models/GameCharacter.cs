namespace Net7ClientManager.Models;

public sealed class GameCharacter
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public int CharacterSlotNumber { get; set; }

    public string Name { get; set; } = "";

    public string? Race { get; set; }

    public string? Profession { get; set; }

    // Legacy v0.2 dev-field. Kept only so old JSON can still migrate cleanly.
    public string? CharacterSelectClickActionName { get; set; }

    public override string ToString()
    {
        if (!string.IsNullOrWhiteSpace(this.Profession))
        {
            return $"{this.Name} ({this.Profession})";
        }

        return this.Name;
    }
}
