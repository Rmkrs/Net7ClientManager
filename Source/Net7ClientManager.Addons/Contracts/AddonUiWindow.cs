namespace Net7ClientManager.Addons.Contracts;

public sealed record AddonUiWindow
{
    public required string Title { get; init; }

    public AddonUiAnchor Anchor { get; init; } =
        AddonUiAnchor.TopLeft;

    public int X { get; init; }

    public int Y { get; init; }

    public required int Width { get; init; }

    public required int Height { get; init; }

    public bool IsVisible { get; init; } = true;

    public bool CanClose { get; init; } = true;

    public bool CanMinimize { get; init; } = true;

    public bool StartMinimized { get; init; }

    /// <summary>
    /// Optional compact width used while minimized. Zero preserves the full
    /// window width. The host always keeps enough room for title and chrome.
    /// </summary>
    public int MinimizedWidth { get; init; }

    public int ContentPadding { get; init; } = 12;

    public int CornerRadius { get; init; } = 8;

    public float TitleFontSize { get; init; } = 15.0f;

    public AddonUiColor BackgroundColor { get; init; } =
        new(255, 14, 20, 27);

    public AddonUiColor HeaderBackgroundColor { get; init; } =
        new(255, 18, 29, 39);

    public AddonUiColor BorderColor { get; init; } =
        new(255, 35, 139, 181);

    public AddonUiColor TitleColor { get; init; } =
        AddonUiColor.White;

    public AddonUiColor ChromeHoverColor { get; init; } =
        new(255, 53, 97, 124);
}
