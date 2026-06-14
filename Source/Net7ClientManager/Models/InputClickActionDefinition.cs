namespace Net7ClientManager.Models;

public sealed class InputClickActionDefinition
{
    public string Name { get; set; } = "";

    public int BaseWidth { get; set; } = 1280;

    public int BaseHeight { get; set; } = 720;

    public double BaseX { get; set; }

    public double BaseY { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public override string ToString()
    {
        return $"{this.Name} ({this.BaseX:0.0}, {this.BaseY:0.0})";
    }
}
