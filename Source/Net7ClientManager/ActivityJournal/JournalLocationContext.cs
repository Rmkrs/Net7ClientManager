namespace Net7ClientManager.ActivityJournal;

using Net7ClientManager.Observations;
using Net7ClientManager.Observations.Models;

/// <summary>
/// Shared, conservative location context for durable journals. Future history
/// domains should use this resolver rather than inventing their own location
/// interpretation.
/// </summary>
internal sealed record JournalLocationContext(
    string SystemName,
    string SectorName,
    string StarbaseName,
    uint? NearestNavObjectId,
    string NearestNavName)
{
    public bool IsUsable =>
        !string.IsNullOrWhiteSpace(this.SectorName) ||
        !string.IsNullOrWhiteSpace(this.StarbaseName);

    public bool MatchesWorldState(JournalLocationContext? other)
    {
        return other != null &&
            Same(this.SystemName, other.SystemName) &&
            Same(this.SectorName, other.SectorName) &&
            Same(this.StarbaseName, other.StarbaseName);
    }

    private static bool Same(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}

internal static class JournalLocationResolver
{
    public static JournalLocationContext Capture(
        ClientObservationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var systemName = Normalize(snapshot.World.CurrentSystemName);
        var sectorName = Normalize(snapshot.World.CurrentSectorName);
        var starbaseName = Normalize(snapshot.World.CurrentStarbaseName);
        uint? nearestNavObjectId = null;
        var nearestNavName = "";

        var canResolveNearestNav =
            string.IsNullOrWhiteSpace(starbaseName) &&
            snapshot.World.Environment is
                (ClientWorldEnvironment.Space or
                 ClientWorldEnvironment.Planet or
                 ClientWorldEnvironment.GasGiant) &&
            snapshot.LocalPlayer.IsAvailable &&
            snapshot.LocalPlayer.Spatial.IsAvailable &&
            snapshot.Navigation.IsAvailable;

        if (canResolveNearestNav)
        {
            var position = snapshot.LocalPlayer.Spatial.Position;
            var nearest = snapshot.Navigation.Targets
                .Where(target =>
                    target.IsAvailable &&
                    target.ObjectId != 0 &&
                    target.Spatial.IsAvailable &&
                    !string.IsNullOrWhiteSpace(GetDisplayName(target)))
                .Select(target => new
                {
                    Target = target,
                    DistanceSquared = DistanceSquared(
                        position,
                        target.Spatial.Position),
                })
                .OrderBy(candidate => candidate.DistanceSquared)
                .FirstOrDefault();

            if (nearest != null)
            {
                nearestNavObjectId = nearest.Target.ObjectId;
                nearestNavName = GetDisplayName(nearest.Target);
            }
        }

        return new JournalLocationContext(
            systemName,
            sectorName,
            starbaseName,
            nearestNavObjectId,
            nearestNavName);
    }

    private static string GetDisplayName(
        ClientNavigationTargetObservation target)
    {
        return Normalize(
            string.IsNullOrWhiteSpace(target.MapDisplayName)
                ? target.Name
                : target.MapDisplayName);
    }

    private static double DistanceSquared(
        ClientSpatialPosition left,
        ClientSpatialPosition right)
    {
        var dx = (double)left.X - right.X;
        var dy = (double)left.Y - right.Y;
        var dz = (double)left.Z - right.Z;
        return (dx * dx) + (dy * dy) + (dz * dz);
    }

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
}
