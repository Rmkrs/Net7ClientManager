namespace Net7ClientManager.Navigation;

using System.Globalization;
using System.Text.Json;

internal sealed class ForgeMissionCatalogStore
{
    private const int MaximumCatalogBytes = 32 * 1024 * 1024;
    private const int MaximumMissionCount = 100000;
    private readonly NavigationDataPathProvider paths = new();

    public ForgeMissionCatalogSnapshot Load()
    {
        try
        {
            if (!File.Exists(this.paths.MissionCatalogPath))
            {
                return ForgeMissionCatalogSnapshot.Unavailable();
            }

            var bytes = File.ReadAllBytes(this.paths.MissionCatalogPath);

            if (bytes.Length is <= 0 or > MaximumCatalogBytes)
            {
                return ForgeMissionCatalogSnapshot.Unavailable(
                    "The cached Forge mission catalogue has an invalid size.");
            }

            var response =
                JsonSerializer.Deserialize<ForgeMissionCatalogResponse>(
                    bytes,
                    ForgeNavigationDataJson.ReadOptions);

            return response == null
                ? ForgeMissionCatalogSnapshot.Unavailable(
                    "The cached Forge mission catalogue is empty.")
                : CreateSnapshot(response);
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                JsonException or
                InvalidOperationException)
        {
            return ForgeMissionCatalogSnapshot.Unavailable(
                $"The cached Forge mission catalogue could not be loaded: {exception.Message}");
        }
    }

    public void Save(ForgeMissionCatalogResponse response)
    {
        var normalized = NormalizeAndValidate(response);
        this.paths.EnsureDirectories();
        var bytes =
            ForgeNavigationDataJson.SerializeCanonical(normalized);

        if (bytes.Length > MaximumCatalogBytes)
        {
            throw new InvalidOperationException(
                "The Forge mission catalogue exceeds the supported size.");
        }

        var temporaryPath = string.Concat(
            this.paths.MissionCatalogPath,
            ".",
            Guid.NewGuid().ToString(
                "N",
                CultureInfo.InvariantCulture),
            ".tmp");

        try
        {
            File.WriteAllBytes(temporaryPath, bytes);
            File.Move(
                temporaryPath,
                this.paths.MissionCatalogPath,
                overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public static ForgeMissionCatalogSnapshot CreateSnapshot(
        ForgeMissionCatalogResponse response)
    {
        var normalized = NormalizeAndValidate(response);
        var canonicalBytes =
            ForgeNavigationDataJson.SerializeCanonical(normalized);

        return new ForgeMissionCatalogSnapshot
        {
            IsAvailable = true,
            Status =
                $"Forge mission catalogue revision {normalized.Revision} is available.",
            Revision = normalized.Revision,
            GeneratedAtUtc = normalized.GeneratedAtUtc,
            Sha256 = ForgeNavigationHash.ComputeSha256(canonicalBytes),
            Missions = normalized.Missions,
        };
    }

    private static ForgeMissionCatalogResponse NormalizeAndValidate(
        ForgeMissionCatalogResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        Validate(response);

        return response with
        {
            Missions = Array.AsReadOnly(
                response.Missions
                    .OrderBy(
                        mission => mission.Name,
                        StringComparer.OrdinalIgnoreCase)
                    .ThenBy(
                        mission => mission.Id,
                        StringComparer.Ordinal)
                    .Select(mission => mission with
                    {
                        Id = mission.Id.Trim(),
                        SemanticFingerprint =
                            mission.SemanticFingerprint
                                .Trim()
                                .ToLowerInvariant(),
                        Name = mission.Name.Trim(),
                        Summary = mission.Summary.Trim(),
                        RewardText = mission.RewardText.Trim(),
                        FailureConsequence =
                            mission.FailureConsequence.Trim(),
                        IssuingFaction =
                            mission.IssuingFaction.Trim(),
                        Stages = Array.AsReadOnly(
                            mission.Stages
                                .OrderBy(stage => stage.Index)
                                .Select(stage => stage with
                                {
                                    Text = stage.Text.Trim(),
                                })
                                .ToArray()),
                        IssuerNpcIds = NormalizeStrings(
                            mission.IssuerNpcIds),
                        CompletionNpcIds = NormalizeStrings(
                            mission.CompletionNpcIds),
                        CompletionLocations = Array.AsReadOnly(
                            mission.CompletionLocations
                                .Select(location => location with
                                {
                                    SectorName =
                                        location.SectorName.Trim(),
                                    StationName =
                                        location.StationName.Trim(),
                                })
                                .DistinctBy(
                                    location => string.Join(
                                        "|",
                                        location.SectorName,
                                        location.StationName),
                                    StringComparer.OrdinalIgnoreCase)
                                .OrderBy(
                                    location => location.SectorName,
                                    StringComparer.OrdinalIgnoreCase)
                                .ThenBy(
                                    location => location.StationName,
                                    StringComparer.OrdinalIgnoreCase)
                                .ToArray()),
                        Reward = mission.Reward == null
                            ? null
                            : mission.Reward with
                            {
                                Reputation = Array.AsReadOnly(
                                    mission.Reward.Reputation
                                        .OrderBy(
                                            reward => reward.FactionKey,
                                            StringComparer.Ordinal)
                                        .Select(reward => reward with
                                        {
                                            FactionKey =
                                                reward.FactionKey.Trim(),
                                            DisplayName =
                                                reward.DisplayName.Trim(),
                                        })
                                        .ToArray()),
                                Items = Array.AsReadOnly(
                                    mission.Reward.Items
                                        .OrderBy(
                                            reward => reward.ItemTemplateId)
                                        .ToArray()),
                            },
                        NamedReporters = NormalizeStrings(
                            mission.NamedReporters),
                    })
                    .ToArray()),
        };
    }

    private static IReadOnlyList<string> NormalizeStrings(
        IReadOnlyList<string> values)
    {
        return Array.AsReadOnly(
            values
                .Select(value => value.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray());
    }

    private static void Validate(
        ForgeMissionCatalogResponse response)
    {
        if (response.Revision < 0 ||
            response.GeneratedAtUtc == default ||
            response.Missions == null ||
            response.Missions.Count > MaximumMissionCount ||
            response.Missions.Any(mission => mission == null))
        {
            throw new InvalidOperationException(
                "The Forge mission catalogue is invalid.");
        }

        HashSet<string> identities = new(StringComparer.Ordinal);
        HashSet<string> fingerprints =
            new(StringComparer.OrdinalIgnoreCase);

        foreach (var mission in response.Missions)
        {
            if (string.IsNullOrWhiteSpace(mission.Id) ||
                mission.Id.Length > 160 ||
                string.IsNullOrWhiteSpace(
                    mission.SemanticFingerprint) ||
                !ForgeNavigationHash.IsSha256(
                    mission.SemanticFingerprint) ||
                string.IsNullOrWhiteSpace(mission.Name) ||
                mission.Name.Length > 256 ||
                mission.Summary == null ||
                mission.Summary.Length > 4096 ||
                mission.RewardText == null ||
                mission.RewardText.Length > 4096 ||
                mission.FailureConsequence == null ||
                mission.FailureConsequence.Length > 4096 ||
                mission.IssuingFaction == null ||
                mission.IssuingFaction.Length > 256 ||
                mission.StageCount is < 1 or > 20 ||
                mission.Stages == null ||
                mission.Stages.Count > 20 ||
                mission.Stages.Any(stage =>
                    stage == null ||
                    stage.Index is < 1 or > 20 ||
                    stage.Index > mission.StageCount ||
                    stage.Text == null ||
                    stage.Text.Length > 4096) ||
                mission.Stages
                    .Select(stage => stage.Index)
                    .Distinct()
                    .Count() != mission.Stages.Count ||
                mission.IssuerNpcIds == null ||
                mission.IssuerNpcIds.Any(value =>
                    string.IsNullOrWhiteSpace(value) ||
                    value.Length > 160) ||
                mission.CompletionNpcIds == null ||
                mission.CompletionNpcIds.Any(value =>
                    string.IsNullOrWhiteSpace(value) ||
                    value.Length > 160) ||
                mission.CompletionLocations == null ||
                mission.CompletionLocations.Any(location =>
                    location == null ||
                    location.SectorName == null ||
                    location.SectorName.Length > 128 ||
                    location.StationName == null ||
                    location.StationName.Length > 128) ||
                (!string.Equals(
                     mission.Confidence,
                     "observed",
                     StringComparison.Ordinal) &&
                 !string.Equals(
                     mission.Confidence,
                     "corroborated",
                     StringComparison.Ordinal)) ||
                mission.NamedReporters == null ||
                mission.NamedReporters.Any(name =>
                    string.IsNullOrWhiteSpace(name) ||
                    name.Length > 64 ||
                    name.Any(char.IsControl)) ||
                !identities.Add(mission.Id) ||
                !fingerprints.Add(mission.SemanticFingerprint))
            {
                throw new InvalidOperationException(
                    "The Forge mission catalogue contains an invalid mission.");
            }

            ValidateReward(mission.Reward);
        }
    }

    private static void ValidateReward(
        ForgeMissionReward? reward)
    {
        if (reward == null)
        {
            return;
        }

        if (reward.Credits < 0 ||
            reward.CombatExperience < 0 ||
            reward.ExploreExperience < 0 ||
            reward.TradeExperience < 0 ||
            reward.Reputation == null ||
            reward.Reputation.Count > 64 ||
            reward.Reputation.Any(item =>
                item == null ||
                string.IsNullOrWhiteSpace(item.FactionKey) ||
                item.FactionKey.Length > 128 ||
                item.DisplayName == null ||
                item.DisplayName.Length > 128 ||
                !float.IsFinite(item.ReactionDelta) ||
                item.ReactionDelta <= 0) ||
            reward.Items == null ||
            reward.Items.Count > 64 ||
            reward.Items.Any(item =>
                item == null ||
                item.ItemTemplateId <= 0 ||
                item.Quantity is < 1 or > 1000000) ||
            reward.Items
                .Select(item => item.ItemTemplateId)
                .Distinct()
                .Count() != reward.Items.Count)
        {
            throw new InvalidOperationException(
                "The Forge mission catalogue contains an invalid reward.");
        }
    }
}
