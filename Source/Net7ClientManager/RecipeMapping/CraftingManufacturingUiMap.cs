namespace Net7ClientManager.RecipeMapping;

using Net7ClientManager.Models;

internal static class CraftingManufacturingUiMap
{
    private const int BaseWidth = 1280;
    private const int BaseHeight = 720;

    private static readonly int[] primaryRowY = [290, 365];
    private static readonly int[] secondaryRowY = [293, 364, 437, 507, 582];
    private static readonly int[] leafRowY = [293, 363, 439, 506];

    public static InputActionDefinition TopLevel1 { get; } =
        Create("Crafting primary selector", 579, 182);

    public static InputActionDefinition TopLevel2 { get; } =
        Create("Crafting secondary selector", 683, 182);

    public static InputActionDefinition TopLevel3 { get; } =
        Create("Crafting category selector", 787, 182);

    public static InputActionDefinition AllTechLevels { get; } =
        Create("Crafting all tech levels", 804, 245);

    public static bool CanAddressCategory(
        int primaryIndex,
        int secondaryIndex,
        int leafIndex)
    {
        return primaryIndex >= 0 && primaryIndex < primaryRowY.Length &&
            secondaryIndex >= 0 && secondaryIndex < secondaryRowY.Length &&
            leafIndex >= 0 && leafIndex < leafRowY.Length;
    }

    public static InputActionDefinition GetPrimarySlot(int index)
    {
        return CreateIndexed(
            "Crafting primary slot",
            index,
            x: 573,
            primaryRowY);
    }

    public static InputActionDefinition GetSecondarySlot(int index)
    {
        return CreateIndexed(
            "Crafting secondary slot",
            index,
            x: 681,
            secondaryRowY);
    }

    public static InputActionDefinition GetLeafSlot(int index)
    {
        return CreateIndexed(
            "Crafting category slot",
            index,
            x: 790,
            leafRowY);
    }

    private static InputActionDefinition CreateIndexed(
        string name,
        int index,
        int x,
        IReadOnlyList<int> yCoordinates)
    {
        if (index < 0 || index >= yCoordinates.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return Create(
            string.Concat(name, " ", index),
            x,
            yCoordinates[index]);
    }

    private static InputActionDefinition Create(
        string name,
        int x,
        int y)
    {
        return new InputActionDefinition
        {
            Name = name,
            Kind = InputActionKind.MouseClick,
            BaseWidth = BaseWidth,
            BaseHeight = BaseHeight,
            BaseX = x,
            BaseY = y,
        };
    }
}
