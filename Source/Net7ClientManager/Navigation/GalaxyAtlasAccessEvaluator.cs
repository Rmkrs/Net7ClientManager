namespace Net7ClientManager.Navigation;

public static class GalaxyAtlasAccessEvaluator
{
    public static GalaxyAtlasDepartureAccess Evaluate(
        GalaxySectorDefinition destination,
        GalaxyNavigationCatalogDeparture departure,
        string? pilotProfession)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(departure);

        if (string.IsNullOrWhiteSpace(departure.ToSectorKey))
        {
            return new GalaxyAtlasDepartureAccess
            {
                State = GalaxyAtlasAccessState.Unavailable,
                Description = "The atlas does not have a known destination for this transition.",
            };
        }

        if (departure.Status == GalaxyNavigationDepartureStatus.Inactive)
        {
            return new GalaxyAtlasDepartureAccess
            {
                State = GalaxyAtlasAccessState.Blocked,
                Description = !string.IsNullOrWhiteSpace(departure.Note)
                    ? departure.Note
                    : "This transition is known to be inactive.",
            };
        }

        if (!string.IsNullOrWhiteSpace(destination.RequiredProfession))
        {
            if (string.IsNullOrWhiteSpace(pilotProfession))
            {
                return new GalaxyAtlasDepartureAccess
                {
                    State = GalaxyAtlasAccessState.Conditional,
                    Description = string.Concat(
                        destination.GetAccessRequirementDescription(),
                        "; the live profession is unavailable."),
                };
            }

            if (!string.Equals(
                    destination.RequiredProfession,
                    pilotProfession,
                    StringComparison.OrdinalIgnoreCase))
            {
                return new GalaxyAtlasDepartureAccess
                {
                    State = GalaxyAtlasAccessState.Blocked,
                    Description = string.Concat(
                        destination.GetAccessRequirementDescription(),
                        "; selected pilot is ",
                        pilotProfession,
                        "."),
                };
            }
        }

        if (!string.IsNullOrWhiteSpace(destination.RequiredFaction))
        {
            return new GalaxyAtlasDepartureAccess
            {
                State = GalaxyAtlasAccessState.Conditional,
                Description = string.Concat(
                    destination.GetAccessRequirementDescription(),
                    "; live faction-threshold validation is not available yet."),
            };
        }

        if (!string.IsNullOrWhiteSpace(departure.AccessRequirement))
        {
            return new GalaxyAtlasDepartureAccess
            {
                State = GalaxyAtlasAccessState.Conditional,
                Description = departure.AccessRequirement,
            };
        }

        if (departure.Status == GalaxyNavigationDepartureStatus.Blocked)
        {
            return new GalaxyAtlasDepartureAccess
            {
                State = GalaxyAtlasAccessState.Conditional,
                Description = !string.IsNullOrWhiteSpace(departure.Note)
                    ? departure.Note
                    : "This transition was observed blocked, but the exact access rule is unknown.",
            };
        }

        return new GalaxyAtlasDepartureAccess
        {
            State = GalaxyAtlasAccessState.Accessible,
            Description = "Known accessible to the selected pilot.",
        };
    }
}
