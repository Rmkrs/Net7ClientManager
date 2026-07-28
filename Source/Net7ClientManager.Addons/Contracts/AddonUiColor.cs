// ReSharper disable CommentTypo
namespace Net7ClientManager.Addons.Contracts;

using System.Runtime.InteropServices;

/// <summary>
/// Addon-safe ARGB color value. The Lua API accepts #RRGGBB, #AARRGGBB
/// and the literal "transparent" and converts those strings into this type.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct AddonUiColor(
    byte Alpha,
    byte Red,
    byte Green,
    byte Blue)
{
    public static AddonUiColor Transparent =>
        new(0, 0, 0, 0);

    public static AddonUiColor White =>
        new(255, 255, 255, 255);

    public static AddonUiColor DefaultLabelBackground =>
        new(255, 28, 35, 45);

    public static AddonUiColor DefaultLabelBorder =>
        new(255, 92, 106, 126);

    public static AddonUiColor DefaultButtonBackground =>
        new(255, 32, 66, 101);

    public static AddonUiColor DefaultButtonHoverBackground =>
        new(255, 39, 78, 119);

    public static AddonUiColor DefaultButtonPressedBackground =>
        new(255, 33, 83, 132);

    public static AddonUiColor DefaultButtonDisabledBackground =>
        new(255, 46, 50, 58);

    public static AddonUiColor DefaultButtonBorder =>
        new(255, 83, 151, 210);

    public static AddonUiColor DefaultButtonDisabledBorder =>
        new(255, 86, 90, 98);

    public static AddonUiColor DefaultButtonDisabledText =>
        new(255, 160, 164, 172);
}
