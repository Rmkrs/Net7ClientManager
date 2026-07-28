// ReSharper disable IdentifierTypo
namespace Net7ClientManager.Observations.Models;

public enum ClientSpatialStateKind
{
    Static,
    Keyframed,
    Interpolated,
    SimplePositionalUpdateTransition,
    ParentRelative,
    PlanetOrbital,
}
