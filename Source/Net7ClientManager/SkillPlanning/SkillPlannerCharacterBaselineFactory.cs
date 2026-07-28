namespace Net7ClientManager.SkillPlanning;

internal static class SkillPlannerCharacterBaselineFactory
{
    internal static bool TryResolveClientProfessionIndex(
        SkillPlannerCatalog catalog,
        int clientRace,
        int clientProfession,
        out int plannerProfessionIndex)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        // The client RPGInfo coordinates use the reverse ordering of the
        // Net-7 web planner for both race and profession. Resolve through the
        // stable profession tag rather than leaking either coordinate system
        // into the other.
        var professionTag = (clientRace, clientProfession) switch
        {
            (0, 0) => "je",
            (0, 1) => "js",
            (0, 2) => "jd",
            (1, 0) => "ps",
            (1, 1) => "pp",
            (1, 2) => "pw",
            (2, 0) => "ts",
            (2, 1) => "tt",
            (2, 2) => "te",
            _ => "",
        };

        if (professionTag.Length > 0 &&
            catalog.TryGetProfession(
                professionTag,
                out var profession))
        {
            plannerProfessionIndex = profession.Index;
            return true;
        }

        plannerProfessionIndex = -1;
        return false;
    }
}
