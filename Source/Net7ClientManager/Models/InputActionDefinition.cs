namespace Net7ClientManager.Models;

public sealed class InputActionDefinition
{
    public string Name { get; set; } = "";

    public InputActionKind Kind { get; set; }

    public Keys Key { get; set; }

    public int BaseWidth { get; set; } = 1280;

    public int BaseHeight { get; set; } = 720;

    public double BaseX { get; set; }

    public double BaseY { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public override string ToString()
    {
        return this.Kind switch
        {
            InputActionKind.KeyTap => $"{this.Name} ({this.Key})",
            InputActionKind.MouseClick => $"{this.Name} ({this.BaseX:0.0}, {this.BaseY:0.0})",
            _ => this.Name,
        };
    }
}
