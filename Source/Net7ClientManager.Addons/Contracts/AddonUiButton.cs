namespace Net7ClientManager.Addons.Contracts;

public sealed record AddonUiButton
{
    public required string Text { get; init; }

    /// <summary>
    /// Optional host-rendered tooltip shown after a short hover delay.
    /// Tooltips are presentation-only and do not create Lua callbacks or
    /// gesture scopes.
    /// </summary>
    public string Tooltip { get; init; } = "";

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
    /// Zero means the host should size the button from its text.
    /// </summary>
    public int Width { get; init; }

    /// <summary>
    /// Zero means the host should use its standard button height.
    /// </summary>
    public int Height { get; init; }

    public bool IsEnabled { get; init; } = true;

    public float FontSize { get; init; } = 10.0f;

    public bool IsBold { get; init; } = true;

    public AddonUiTextAlignment TextAlignment { get; init; } =
        AddonUiTextAlignment.Left;

    public int Padding { get; init; } = 8;

    public int CornerRadius { get; init; } = 5;

    public AddonUiColor TextColor { get; init; } =
        AddonUiColor.White;

    public AddonUiColor BackgroundColor { get; init; } =
        AddonUiColor.DefaultButtonBackground;

    public AddonUiColor HoverBackgroundColor { get; init; } =
        AddonUiColor.DefaultButtonHoverBackground;

    public AddonUiColor PressedBackgroundColor { get; init; } =
        AddonUiColor.DefaultButtonPressedBackground;

    public AddonUiColor DisabledBackgroundColor { get; init; } =
        AddonUiColor.DefaultButtonDisabledBackground;

    public AddonUiColor BorderColor { get; init; } =
        AddonUiColor.DefaultButtonBorder;

    public AddonUiColor DisabledBorderColor { get; init; } =
        AddonUiColor.DefaultButtonDisabledBorder;

    public AddonUiColor DisabledTextColor { get; init; } =
        AddonUiColor.DefaultButtonDisabledText;
}
