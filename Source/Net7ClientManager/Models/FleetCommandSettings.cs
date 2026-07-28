namespace Net7ClientManager.Models;

public sealed class FleetCommandSettings
{
    // Retained for migration from settings written before the three-state
    // presentation model existed. New code uses CommandMenuShowMode.
    public bool CommandMenuEnabled { get; set; } = true;

    public CommandPaletteShowMode? CommandMenuShowMode { get; set; }

    public Keys CommandMenuHotKey { get; set; } = Keys.Control | Keys.Q;

    public CommandPalettePlacementMode CommandMenuPlacement { get; set; } =
        CommandPalettePlacementMode.Cursor;

    public double CommandMenuFixedPositionX { get; set; } = 0.5;

    public double CommandMenuFixedPositionY { get; set; } = 0.5;

    public FleetFormationMode FormationMode { get; set; } =
        FleetFormationMode.Block;

    public bool ReturnFocusToMain { get; set; } = true;

    public CommandPaletteShowMode EffectiveCommandMenuShowMode =>
        this.CommandMenuShowMode ??
        (this.CommandMenuEnabled
            ? CommandPaletteShowMode.Keybinding
            : CommandPaletteShowMode.Never);

    public void EnsureDefaults()
    {
        if (this.CommandMenuShowMode is not { } showMode ||
            !Enum.IsDefined(showMode))
        {
            this.CommandMenuShowMode =
                this.CommandMenuEnabled
                    ? CommandPaletteShowMode.Keybinding
                    : CommandPaletteShowMode.Never;
        }

        this.CommandMenuEnabled =
            this.CommandMenuShowMode != CommandPaletteShowMode.Never;

        if (!Enum.IsDefined(this.CommandMenuPlacement))
        {
            this.CommandMenuPlacement = CommandPalettePlacementMode.Cursor;
        }

        this.CommandMenuFixedPositionX = Math.Clamp(
            this.CommandMenuFixedPositionX,
            0.0,
            1.0);

        this.CommandMenuFixedPositionY = Math.Clamp(
            this.CommandMenuFixedPositionY,
            0.0,
            1.0);
    }

    public void SetCommandMenuShowMode(CommandPaletteShowMode showMode)
    {
        this.CommandMenuShowMode = showMode;
        this.CommandMenuEnabled = showMode != CommandPaletteShowMode.Never;
    }
}
