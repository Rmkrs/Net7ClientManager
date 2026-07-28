namespace Net7ClientManager.SkillPlanning;

#if DEBUG
internal static class SkillBuildLocalStoreScenarios
{
    public static void Validate()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            string.Concat(
                "n7cm-build-store-",
                Guid.NewGuid().ToString("N")));
        var databasePath = Path.Combine(directory, "skill-builds.db");

        try
        {
            var store = new SkillBuildLocalStore(databasePath);
            store.Initialize();
            store.Initialize();

            var build = new SkillBuildDocument
            {
                BuildId = "local-scenario-build",
                ProfessionIndex = 6,
                Title = "Local scenario",
                Summary = "Persistence scenario",
                Notes = "Multi-line\r\nBuild notes",
                Equipment =
                [
                    new SkillBuildEquipmentRequirement
                    {
                        RequirementId = "weapon-a",
                        Kind = SkillBuildEquipmentKind.Weapon,
                        Order = 1,
                        Alternatives =
                        [
                            new SkillBuildEquipmentAlternative
                            {
                                ItemTemplateId = 1001,
                                ItemName = "Scenario weapon",
                            },
                        ],
                    },
                ],
                RecommendedSkills =
                [
                    new SkillBuildSkillRecommendation(40, 8),
                ],
            };

            const uint characterId = 9001;
            const uint otherCharacterId = 9002;
            store.SaveBuild(
                characterId,
                build,
                new DateTimeOffset(
                    2026,
                    7,
                    21,
                    12,
                    0,
                    0,
                    TimeSpan.Zero));

            var builds = store.GetBuildsForCharacter(characterId, 6);
            AssertEqual(1, builds.Count, "saved build count");
            AssertEqual(
                build.BuildId,
                builds[0].BuildId,
                "saved build identity");
            AssertEqual(
                1,
                builds[0].Equipment.Count,
                "saved equipment count");
            AssertEqual(
                8,
                builds[0].RecommendedSkills[0].TargetRank,
                "saved recommendation");
            AssertEqual(
                build.Notes,
                builds[0].Notes,
                "saved build notes");

            AssertEqual(
                0,
                store.GetBuildsForCharacter(otherCharacterId, 6).Count,
                "build is private to its pilot");

            store.SetActiveBuild(characterId, build.BuildId);
            AssertEqual(
                build.BuildId,
                store.GetActiveBuildId(characterId) ?? "",
                "active build");

            store.SaveBuild(
                characterId,
                build with { Title = "Updated local scenario" },
                new DateTimeOffset(
                    2026,
                    7,
                    21,
                    12,
                    30,
                    0,
                    TimeSpan.Zero));
            AssertEqual(
                "Updated local scenario",
                store.GetBuild(build.BuildId)?.Title ?? "",
                "mutable local build update");

            var disposableDraft = build with
            {
                BuildId = "local-disposable-draft",
                Title = "Disposable local draft",
            };
            store.SaveBuild(
                characterId,
                disposableDraft,
                new DateTimeOffset(
                    2026,
                    7,
                    21,
                    12,
                    45,
                    0,
                    TimeSpan.Zero));
            store.SetActiveBuild(characterId, disposableDraft.BuildId);
            AssertEqual(
                true,
                store.DeleteBuild(characterId, disposableDraft.BuildId),
                "delete unpublished local draft");
            AssertEqual(
                true,
                store.GetBuild(disposableDraft.BuildId) == null,
                "deleted local draft is cleaned up");
            AssertEqual(
                true,
                store.GetActiveBuildId(characterId) == null,
                "deleting active local draft clears active build");
            store.SetActiveBuild(characterId, build.BuildId);

            store.SetActiveBuild(otherCharacterId, build.BuildId);
            AssertEqual(
                1,
                store.GetBuildsForCharacter(otherCharacterId, 6).Count,
                "explicit use adds build to pilot library");

            var link = new SkillBuildForgeLink
            {
                LocalBuildId = build.BuildId,
                ForgeBuildId = "build_0123456789abcdef0123456789abcdef",
                Version = 2,
                ContentSha256 = new string('a', 64),
                PublisherPilotName = "ScenarioPilot",
                IsPublisherSource = true,
                LatestKnownVersion = 2,
                StarCount = 3,
                IsStarredByMe = false,
                IsOwnedByMe = true,
                UpdatedAtUtc = new DateTimeOffset(
                    2026,
                    7,
                    22,
                    18,
                    0,
                    0,
                    TimeSpan.Zero),
            };
            store.SaveForgeLink(link);
            var storedLink = store.GetForgeLink(build.BuildId) ??
                throw new InvalidOperationException(
                    "Build local-store scenario failed: Forge link missing.");
            AssertEqual(2, storedLink.Version, "Forge exact version");
            AssertEqual(3, storedLink.StarCount, "Forge star count");
            AssertEqual(true, storedLink.IsPublisherSource, "Forge source lineage");

            AssertEqual(
                false,
                store.DeleteBuild(characterId, build.BuildId),
                "published source build cannot be deleted");

            store.UpdateForgeKnowledge(
                link.ForgeBuildId,
                latestKnownVersion: 4,
                starCount: 9,
                isStarredByMe: true,
                isOwnedByMe: true);
            storedLink = store.GetForgeLink(build.BuildId) ??
                throw new InvalidOperationException(
                    "Build local-store scenario failed: updated Forge link missing.");
            AssertEqual(4, storedLink.LatestKnownVersion, "Forge latest version");
            AssertEqual(9, storedLink.StarCount, "Forge updated stars");
            AssertEqual(true, storedLink.IsStarredByMe, "Forge local star state");

            var downloadedV1 = build with
            {
                BuildId = "forge-scenario-v1",
                Title = "Forge scenario v1",
            };
            var downloadedV1Link = new SkillBuildForgeLink
            {
                LocalBuildId = downloadedV1.BuildId,
                ForgeBuildId = "build_feedfacefeedfacefeedfacefeedface",
                Version = 1,
                ContentSha256 = new string('b', 64),
                PublisherPilotName = "OtherPilot",
                IsPublisherSource = false,
                LatestKnownVersion = 4,
                StarCount = 12,
                IsStarredByMe = true,
                IsOwnedByMe = false,
                UpdatedAtUtc = new DateTimeOffset(
                    2026,
                    7,
                    22,
                    19,
                    0,
                    0,
                    TimeSpan.Zero),
            };
            store.SaveAndActivateForgeVersion(
                characterId,
                downloadedV1,
                downloadedV1Link,
                downloadedV1Link.UpdatedAtUtc);
            store.SaveAndActivateForgeVersion(
                otherCharacterId,
                downloadedV1,
                downloadedV1Link,
                downloadedV1Link.UpdatedAtUtc);

            var downloadedV4 = downloadedV1 with
            {
                BuildId = "forge-scenario-v4",
                Title = "Forge scenario v4",
            };
            var downloadedV4Link = downloadedV1Link with
            {
                LocalBuildId = downloadedV4.BuildId,
                Version = 4,
                ContentSha256 = new string('c', 64),
                UpdatedAtUtc = new DateTimeOffset(
                    2026,
                    7,
                    22,
                    20,
                    0,
                    0,
                    TimeSpan.Zero),
            };
            store.SaveAndActivateForgeVersion(
                characterId,
                downloadedV4,
                downloadedV4Link,
                downloadedV4Link.UpdatedAtUtc);

            var characterForgeBuilds = store
                .GetBuildsForCharacter(characterId, 6)
                .Where(candidate => candidate.BuildId.StartsWith(
                    "forge-scenario-",
                    StringComparison.Ordinal))
                .ToArray();
            AssertEqual(1, characterForgeBuilds.Length, "one Forge version per pilot");
            AssertEqual(
                downloadedV4.BuildId,
                characterForgeBuilds[0].BuildId,
                "new Forge version replaces old pilot-library version");
            AssertEqual(
                downloadedV4.BuildId,
                store.GetActiveBuildId(characterId) ?? "",
                "new Forge version becomes active");

            var otherForgeBuilds = store
                .GetBuildsForCharacter(otherCharacterId, 6)
                .Where(candidate => candidate.BuildId.StartsWith(
                    "forge-scenario-",
                    StringComparison.Ordinal))
                .ToArray();
            AssertEqual(1, otherForgeBuilds.Length, "other pilot keeps one Forge version");
            AssertEqual(
                downloadedV1.BuildId,
                otherForgeBuilds[0].BuildId,
                "other pilot remains pinned to exact version");

            AssertEqual(
                true,
                store.DeleteBuild(characterId, downloadedV4.BuildId),
                "delete downloaded Forge build");
            AssertEqual(
                0,
                store.GetBuildsForCharacter(characterId, 6)
                    .Count(candidate => candidate.BuildId.StartsWith(
                        "forge-scenario-",
                        StringComparison.Ordinal)),
                "deleted Forge build leaves pilot library");
            AssertEqual(
                true,
                store.GetActiveBuildId(characterId) == null,
                "deleting active Forge build clears active build");
            AssertEqual(
                true,
                store.GetBuild(downloadedV4.BuildId) == null,
                "unreferenced Forge download is cleaned up");
            AssertEqual(
                false,
                store.DeleteBuild(characterId, downloadedV4.BuildId),
                "deleting an absent build is harmless");

            otherForgeBuilds = store
                .GetBuildsForCharacter(otherCharacterId, 6)
                .Where(candidate => candidate.BuildId.StartsWith(
                    "forge-scenario-",
                    StringComparison.Ordinal))
                .ToArray();
            AssertEqual(
                1,
                otherForgeBuilds.Length,
                "deleting one pilot does not affect another");
            AssertEqual(
                downloadedV1.BuildId,
                otherForgeBuilds[0].BuildId,
                "other pilot keeps the pinned Forge version");
        }
        finally
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static void AssertEqual<T>(
        T expected,
        T actual,
        string scenario)
        where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"Build local-store scenario failed: {scenario}; expected '{expected}', got '{actual}'.");
        }
    }
}
#endif
