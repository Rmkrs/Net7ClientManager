namespace Net7ClientManager.Addons.Contracts;

public sealed record AddonUiLabel
{
    public required string Text { get; init; }

    /// <summary>
    /// Optional window widget id. When present, anchor/x/y are resolved inside
    /// that window's content rectangle and the label follows window visibility
    /// and minimization.
    /// </summary>
    public string ParentWidgetId { get; init; } = "";

    public AddonUiAnchor Anchor { get; init; } =
        AddonUiAnchor.TopLeft;

    /// <summary>
    /// Pixel coordinates are resolved directly. GameCanvas coordinates use
    /// the game's 1280x720 reference canvas and scale with the hosted client.
    /// </summary>
    public AddonUiCoordinateSpace CoordinateSpace { get; init; } =
        AddonUiCoordinateSpace.Pixels;

    public int X { get; init; }

    public int Y { get; init; }

    /// <summary>
    /// Zero means the host should size the label from its text.
    /// </summary>
    public int Width { get; init; }

    /// <summary>
    /// Zero means the host should size the label from its text.
    /// </summary>
    public int Height { get; init; }

    public float FontSize { get; init; } = 10.0f;

    public bool IsBold { get; init; } = true;

    public AddonUiTextAlignment TextAlignment { get; init; } =
        AddonUiTextAlignment.Left;

    public int Padding { get; init; } = 8;

    public int CornerRadius { get; init; } = 5;

    public AddonUiColor TextColor { get; init; } =
        AddonUiColor.White;

    public AddonUiColor BackgroundColor { get; init; } =
        AddonUiColor.DefaultLabelBackground;

    public AddonUiColor BorderColor { get; init; } =
        AddonUiColor.DefaultLabelBorder;
}
