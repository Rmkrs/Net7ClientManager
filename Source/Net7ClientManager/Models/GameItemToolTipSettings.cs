namespace Net7ClientManager.Models;

public sealed class GameItemToolTipSettings
{
    public const int MinimumOffset = -200;
    public const int MaximumOffset = 200;

    public bool Enabled { get; set; }

    public int HorizontalOffset { get; set; }

    public int VerticalOffset { get; set; }

    public void EnsureDefaults()
    {
        this.HorizontalOffset = Math.Clamp(
            this.HorizontalOffset,
            MinimumOffset,
            MaximumOffset);
        this.VerticalOffset = Math.Clamp(
            this.VerticalOffset,
            MinimumOffset,
            MaximumOffset);
    }
}
