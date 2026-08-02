namespace Net7ClientManager.ControlPlane;

using System.Globalization;
using Net7ClientManager.Addons.Projection;
using Net7ClientManager.ControlPlane.Contracts;
using Net7ClientManager.MissionJournal;
using Net7ClientManager.Models;
using Net7ClientManager.Observations;
using Net7ClientManager.Observations.Models;

internal sealed partial class ClientManagerControlPlaneService
{
    private const string InvalidInventoryCollection = "__invalid__";

    private readonly GameSnapshotProjector automationSnapshotProjector = new();

    private ControlPlaneResponse GetInteraction(ControlPlaneRequest request)
    {
        var resolved = this.ResolveObservedQuery(request);

        if (resolved.Failure != null)
        {
            return resolved.Failure;
        }

        var snapshot = resolved.Snapshot!;

        if (!snapshot.Target.IsAvailable)
        {
            return this.Failure(
                request,
                ControlPlaneExitCode.Unavailable,
                "Target interaction information is not available yet.");
        }

        var target = this.GetPublicDomain(snapshot, "target");

        if (target == null)
        {
            return this.Failure(
                request,
                ControlPlaneExitCode.Unavailable,
                "Target interaction information is not available yet.");
        }

        var hasTarget = GetBoolean(target, "has_target") == true;

        if (!hasTarget)
        {
            return this.Success(
                request,
                "No target",
                "No target",
                CreateQueryData(
                    resolved,
                    new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["has_target"] = false,
                        ["available"] = true,
                        ["active"] = false,
                        ["verb"] = null,
                        ["actions"] = Array.Empty<object>(),
                    }));
        }

        if (!TryGetDictionary(target, "interaction", out var interaction) ||
            GetBoolean(interaction, "available") != true)
        {
            return this.Failure(
                request,
                ControlPlaneExitCode.Unavailable,
                "Interaction information for the current target is not available yet.");
        }

        var actions = GetDictionaries(interaction, "actions").ToArray();
        var primary = actions.FirstOrDefault(action =>
                          GetBoolean(action, "executable") == true) ??
                      actions.FirstOrDefault();
        var verb = "";
        var executable = false;

        if (primary != null)
        {
            _ = TryGetString(primary, "verb", out verb);
            executable = GetBoolean(primary, "executable") == true;
        }

        var output = primary == null
            ? "No interaction"
            : FormatToken(verb);
        var data = CreateQueryData(resolved);
        data["has_target"] = true;
        data["target"] = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["name"] = GetValue(target, "name"),
            ["kind"] = GetValue(target, "kind"),
            ["relation"] = GetValue(target, "relation"),
        };

        foreach (var item in interaction)
        {
            data[item.Key] = item.Value;
        }

        data["verb"] = primary == null ? null : verb;
        data["executable"] = primary == null ? null : executable;

        return this.Success(request, output, output, data);
    }

    private ControlPlaneResponse GetGroup(ControlPlaneRequest request)
    {
        var resolved = this.ResolveObservedQuery(request);

        if (resolved.Failure != null)
        {
            return resolved.Failure;
        }

        if (!resolved.Snapshot!.Group.IsAvailable)
        {
            return this.Failure(
                request,
                ControlPlaneExitCode.Unavailable,
                "Group information is not available yet.");
        }

        var group = this.GetPublicDomain(resolved.Snapshot, "group");

        if (group == null)
        {
            return this.Failure(
                request,
                ControlPlaneExitCode.Unavailable,
                "Group information is not available yet.");
        }

        var members = GetDictionaries(group, "members").ToArray();
        var requestedMember = NormalizeText(GetArgument(request, "member"));

        if (requestedMember != null)
        {
            var member = members.FirstOrDefault(candidate =>
                TryGetString(candidate, "name", out var name) &&
                string.Equals(
                    name,
                    requestedMember,
                    StringComparison.OrdinalIgnoreCase));
            var present = member != null;
            var memberOutput = present ? "Present" : "Not present";
            var memberData = CreateQueryData(resolved);
            memberData["requested_member"] = requestedMember;
            memberData["present"] = present;
            memberData["member"] = member;

            return this.Success(
                request,
                memberOutput,
                memberOutput,
                memberData);
        }

        var inGroup = GetBoolean(group, "in_group") == true;
        var names = members
            .Select(member => TryGetString(member, "name", out var name)
                ? name
                : null)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();
        var output = !inGroup
            ? "Not grouped"
            : names.Length == 0
                ? "Grouped"
                : string.Join(", ", names);
        var fullData = CreateQueryData(resolved, group);
        fullData["member_count"] = members.Length;
        fullData["total_member_count"] = inGroup
            ? members.Length + 1
            : 0;

        return this.Success(request, output, output, fullData);
    }

    private ControlPlaneResponse GetMissions(ControlPlaneRequest request)
    {
        var resolved = this.ResolveObservedQuery(request);

        if (resolved.Failure != null)
        {
            return resolved.Failure;
        }

        var missionLog = resolved.Snapshot!.LocalPlayer.Missions;

        if (!missionLog.IsAvailable)
        {
            return this.Failure(
                request,
                ControlPlaneExitCode.Unavailable,
                "Mission information is not available yet.");
        }

        var stateFilter = NormalizeMissionStateFilter(
            GetArgument(request, "state"));
        var typeFilter = NormalizeMissionTypeFilter(
            GetArgument(request, "type"));

        if (stateFilter == null)
        {
            return this.Failure(
                request,
                ControlPlaneExitCode.InvalidArguments,
                "--state must be active, complete, failed, expired, terminal, or all.");
        }

        if (typeFilter == null)
        {
            return this.Failure(
                request,
                ControlPlaneExitCode.InvalidArguments,
                "--type must be mission, job, combat-job, trade-job, explore-job, or all.");
        }

        var domain = this.GetPublicDomain(resolved.Snapshot, "missions");

        if (domain == null)
        {
            return this.Failure(
                request,
                ControlPlaneExitCode.Unavailable,
                "Mission information is not available yet.");
        }

        var observedBySlot = missionLog.Missions.ToDictionary(
            mission => mission.Slot);
        var allMissions = GetDictionaries(domain, "items")
            .Select(item => EnrichMission(
                item,
                observedBySlot,
                resolved.Client!,
                this.clientManager))
            .ToArray();
        var nameFilter = NormalizeText(GetArgument(request, "name"));
        var matching = allMissions
            .Where(mission => nameFilter == null ||
                TryGetString(mission, "name", out var name) &&
                name.Contains(
                    nameFilter,
                    StringComparison.OrdinalIgnoreCase))
            .Where(mission => stateFilter == "all" ||
                MatchesMissionState(GetMissionState(mission), stateFilter))
            .Where(mission => typeFilter == "all" ||
                MatchesMissionType(GetMissionType(mission), typeFilter))
            .ToArray();
        var output = matching.Length == 0
            ? "No matching missions"
            : string.Join(
                Environment.NewLine,
                matching.Select(FormatMissionLine));
        var data = CreateQueryData(resolved, domain);
        data["count"] = matching.Length;
        data["total_count"] = allMissions.Length;
        data["active_count"] = allMissions.Count(mission =>
            GetMissionState(mission) == "active");
        data["complete_count"] = allMissions.Count(mission =>
            GetMissionState(mission) == "complete");
        data["failed_count"] = allMissions.Count(mission =>
            GetMissionState(mission) == "failed");
        data["expired_count"] = allMissions.Count(mission =>
            GetMissionState(mission) == "expired");
        data["terminal_count"] = allMissions.Count(mission =>
            GetMissionState(mission) is "complete" or "failed" or "expired");
        data["filters"] = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["name"] = nameFilter,
            ["state"] = stateFilter,
            ["type"] = typeFilter,
        };
        data["items"] = matching;

        return this.Success(
            request,
            $"Found {matching.Length} matching mission(s).",
            output,
            data);
    }

    private ControlPlaneResponse GetInventory(ControlPlaneRequest request)
    {
        var resolved = this.ResolveObservedQuery(request);

        if (resolved.Failure != null)
        {
            return resolved.Failure;
        }

        var snapshot = resolved.Snapshot!;
        var domain = this.GetPublicDomain(snapshot, "inventory");

        if (domain == null || !HasAnyInventoryCollectionAvailable(snapshot))
        {
            return this.Failure(
                request,
                ControlPlaneExitCode.Unavailable,
                "Inventory information is not available yet.");
        }

        var collection = NormalizeInventoryCollection(
            GetArgument(request, "collection"));
        var itemFilter = NormalizeText(GetArgument(request, "item"));

        if (collection == InvalidInventoryCollection)
        {
            return this.Failure(
                request,
                ControlPlaneExitCode.InvalidArguments,
                "--collection must be cargo, equipment, ammo, secure, " +
                "reward, overflow, or vendor.");
        }

        if (collection == null && itemFilter == null)
        {
            var summaryData = CreateQueryData(resolved, domain);
            var summaryOutput = FormatInventorySummary(domain);
            return this.Success(
                request,
                summaryOutput,
                summaryOutput,
                summaryData);
        }

        collection ??= "cargo";

        if (!IsInventoryCollectionAvailable(snapshot, collection) ||
            !TryGetDictionary(domain, collection, out var collectionData))
        {
            return this.Failure(
                request,
                ControlPlaneExitCode.Unavailable,
                $"{FormatToken(collection)} inventory is not available yet.");
        }

        var items = GetDictionaries(collectionData, "items").ToArray();
        var matching = itemFilter == null
            ? items
            : items.Where(item =>
                TryGetString(item, "name", out var name) &&
                string.Equals(
                    name,
                    itemFilter,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
        var quantity = matching.Sum(GetInventoryQuantity);
        var output = itemFilter != null
            ? quantity.ToString(CultureInfo.InvariantCulture)
            : matching.Length == 0
                ? $"No items in {collection}"
                : string.Join(
                    Environment.NewLine,
                    matching.Select(FormatInventoryItemLine));
        var data = CreateQueryData(resolved, collectionData);
        data["collection"] = collection;
        data["item"] = itemFilter;
        data["quantity"] = quantity;
        data["match_count"] = matching.Length;
        data["items"] = matching;

        return this.Success(
            request,
            itemFilter != null
                ? $"Found {quantity} {itemFilter} in {collection}."
                : $"Found {matching.Length} item(s) in {collection}.",
            output,
            data);
    }

    private ObservedQueryResolution ResolveObservedQuery(
        ControlPlaneRequest request)
    {
        var resolution = this.ResolveSlot(request, requireRunning: true);

        if (resolution.Failure != null)
        {
            return ObservedQueryResolution.Failed(resolution.Failure);
        }

        var snapshot = this.FindObservation(resolution.Client!.ProcessId);

        if (snapshot == null || !snapshot.IsAvailable)
        {
            return ObservedQueryResolution.Failed(this.Failure(
                request,
                ControlPlaneExitCode.Unavailable,
                "Client observation is not available yet."));
        }

        return new ObservedQueryResolution(
            resolution.Slot,
            resolution.Client,
            snapshot,
            null);
    }

    private IReadOnlyDictionary<string, object?>? GetPublicDomain(
        ClientObservationSnapshot snapshot,
        string name)
    {
        var projected = this.automationSnapshotProjector.Project(snapshot);

        return projected.PublicData.TryGetValue(name, out var value) &&
               value is IReadOnlyDictionary<string, object?> dictionary
            ? dictionary
            : null;
    }

    private static Dictionary<string, object?> CreateQueryData(
        ObservedQueryResolution resolved,
        IReadOnlyDictionary<string, object?>? source = null)
    {
        var data = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["slot"] = resolved.Slot!.Name,
            ["pilot"] = resolved.Client!.LiveCharacterIdentity.Name,
            ["observed_at"] = resolved.Snapshot!.ObservedAt,
        };

        if (source != null)
        {
            foreach (var item in source)
            {
                data[item.Key] = item.Value;
            }
        }

        return data;
    }

    private static IReadOnlyDictionary<string, object?> EnrichMission(
        IReadOnlyDictionary<string, object?> source,
        IReadOnlyDictionary<int, ClientMissionObservation> observedBySlot,
        ClientInstance client,
        Net7ClientManager.Core.ClientManager clientManager)
    {
        var result = new Dictionary<string, object?>(
            StringComparer.Ordinal);

        foreach (var item in source)
        {
            result[item.Key] = item.Value;
        }

        var type = "mission";
        var sourceKind = "mission";
        string? jobCategory = null;
        object? destination = null;

        if (GetInteger(source, "slot") is { } slot &&
            observedBySlot.TryGetValue(slot, out var observed))
        {
            var guidance = clientManager.ResolveMissionJobGuidance(
                client.ProcessId,
                observed);

            if (guidance != null)
            {
                sourceKind = "job_terminal";
                type = guidance.JobCategory switch
                {
                    MissionJournalJobCategory.Combat => "combat_job",
                    MissionJournalJobCategory.Trade => "trade_job",
                    MissionJournalJobCategory.Explore => "explore_job",
                    _ => "job",
                };
                jobCategory = guidance.JobCategory ==
                              MissionJournalJobCategory.Unknown
                    ? null
                    : NormalizeEnum(guidance.JobCategory);
                destination = guidance.Destination == null
                    ? null
                    : new
                    {
                        system = guidance.Destination.SystemName,
                        sector = guidance.Destination.SectorName,
                        route = MapDestination(
                            guidance.Destination.RouteDestination),
                    };
                result["accepted_at"] = guidance.AcceptedAt;
                result["accepted_system"] = NormalizeText(
                    guidance.AcceptedSystem);
                result["accepted_sector"] = NormalizeText(
                    guidance.AcceptedSector);
                result["accepted_starbase"] = NormalizeText(
                    guidance.AcceptedStarbase);
            }
        }

        result["state"] = GetMissionState(source);
        result["type"] = type;
        result["source"] = sourceKind;
        result["job_category"] = jobCategory;
        result["destination"] = destination;
        return result;
    }

    private static string GetMissionState(
        IReadOnlyDictionary<string, object?> mission)
    {
        if (GetBoolean(mission, "complete") == true)
        {
            return "complete";
        }

        if (GetBoolean(mission, "failed") == true)
        {
            return "failed";
        }

        return GetBoolean(mission, "expired") == true
            ? "expired"
            : "active";
    }

    private static string GetMissionType(
        IReadOnlyDictionary<string, object?> mission)
    {
        return TryGetString(mission, "type", out var type)
            ? type
            : "mission";
    }

    private static string FormatMissionLine(
        IReadOnlyDictionary<string, object?> mission)
    {
        _ = TryGetString(mission, "name", out var name);
        _ = TryGetString(
            mission,
            "current_stage_text",
            out var objective);
        var stage = GetInteger(mission, "stage");
        var stageCount = GetInteger(mission, "stage_count");
        var stageText = stage.HasValue
            ? stageCount.HasValue
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"Stage {stage}/{stageCount}")
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"Stage {stage}")
            : "";

        return string.Join(
            "\t",
            new[]
            {
                name,
                FormatToken(GetMissionType(mission)),
                FormatToken(GetMissionState(mission)),
                stageText,
                objective,
            });
    }

    private static string FormatInventorySummary(
        IReadOnlyDictionary<string, object?> domain)
    {
        var parts = new List<string>();

        if (GetBoolean(domain, "available") == true &&
            TryGetDictionary(domain, "cargo", out var cargo))
        {
            var used = GetInteger(cargo, "used") ?? 0;
            var capacity = GetInteger(cargo, "capacity");
            parts.Add(capacity.HasValue
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"Cargo {used}/{capacity}")
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"Cargo {used}"));

            foreach (var collection in new[] { "equipment", "ammo" })
            {
                if (TryGetDictionary(domain, collection, out var data))
                {
                    var itemCount = GetDictionaries(data, "items").Count();
                    parts.Add(string.Create(
                        CultureInfo.InvariantCulture,
                        $"{FormatToken(collection)} {itemCount}"));
                }
            }
        }

        foreach (var collection in new[]
                 {
                     "secure",
                     "reward",
                     "overflow",
                     "vendor",
                 })
        {
            if (TryGetDictionary(domain, collection, out var data) &&
                GetBoolean(data, "available") == true)
            {
                var itemCount = GetDictionaries(data, "items").Count();
                parts.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{FormatToken(collection)} {itemCount}"));
            }
        }

        return parts.Count == 0
            ? "Inventory available"
            : string.Join(", ", parts);
    }

    private static string FormatInventoryItemLine(
        IReadOnlyDictionary<string, object?> item)
    {
        _ = TryGetString(item, "name", out var name);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{name}\t{GetInventoryQuantity(item)}");
    }

    private static int GetInventoryQuantity(
        IReadOnlyDictionary<string, object?> item)
    {
        var stackCount = GetInteger(item, "stack_count");
        return stackCount is > 0 ? stackCount.Value : 1;
    }

    private static IEnumerable<IReadOnlyDictionary<string, object?>>
        GetDictionaries(
            IReadOnlyDictionary<string, object?> source,
            string name)
    {
        return source.TryGetValue(name, out var value) &&
               value is IEnumerable<object?> values
            ? values.OfType<IReadOnlyDictionary<string, object?>>()
            : [];
    }

    private static bool TryGetDictionary(
        IReadOnlyDictionary<string, object?> source,
        string name,
        out IReadOnlyDictionary<string, object?> result)
    {
        if (source.TryGetValue(name, out var value) &&
            value is IReadOnlyDictionary<string, object?> dictionary)
        {
            result = dictionary;
            return true;
        }

        result = new Dictionary<string, object?>();
        return false;
    }

    private static object? GetValue(
        IReadOnlyDictionary<string, object?> source,
        string name)
    {
        return source.TryGetValue(name, out var value) ? value : null;
    }

    private static bool TryGetString(
        IReadOnlyDictionary<string, object?> source,
        string name,
        out string result)
    {
        if (source.TryGetValue(name, out var value) && value is string text)
        {
            result = text;
            return true;
        }

        result = "";
        return false;
    }

    private static bool? GetBoolean(
        IReadOnlyDictionary<string, object?> source,
        string name)
    {
        return source.TryGetValue(name, out var value) && value is bool flag
            ? flag
            : null;
    }

    private static int? GetInteger(
        IReadOnlyDictionary<string, object?> source,
        string name)
    {
        if (!source.TryGetValue(name, out var value))
        {
            return null;
        }

        return value switch
        {
            int number => number,
            long number when number is >= int.MinValue and <= int.MaxValue =>
                (int)number,
            uint number when number <= int.MaxValue => (int)number,
            _ => null,
        };
    }

    private static string? GetArgument(
        ControlPlaneRequest request,
        string name)
    {
        return request.Arguments.TryGetValue(name, out var value)
            ? value
            : null;
    }

    private static bool MatchesMissionState(
        string state,
        string filter)
    {
        return filter == "terminal"
            ? state is "complete" or "failed" or "expired"
            : string.Equals(state, filter, StringComparison.Ordinal);
    }

    private static bool MatchesMissionType(
        string type,
        string filter)
    {
        return filter == "job"
            ? type is "job" or "combat_job" or "trade_job" or "explore_job"
            : string.Equals(type, filter, StringComparison.Ordinal);
    }

    private static string? NormalizeMissionStateFilter(string? value)
    {
        return NormalizeFilter(value) switch
        {
            "" or "all" => "all",
            "active" => "active",
            "complete" or "completed" => "complete",
            "failed" => "failed",
            "expired" => "expired",
            "terminal" => "terminal",
            _ => null,
        };
    }

    private static string? NormalizeMissionTypeFilter(string? value)
    {
        return NormalizeFilter(value) switch
        {
            "" or "all" => "all",
            "mission" or "missions" => "mission",
            "job" or "jobs" => "job",
            "combatjob" => "combat_job",
            "tradejob" => "trade_job",
            "explorejob" => "explore_job",
            _ => null,
        };
    }

    private static string? NormalizeInventoryCollection(string? value)
    {
        return NormalizeFilter(value) switch
        {
            "" => null,
            "cargo" => "cargo",
            "equipment" or "equipped" => "equipment",
            "ammo" or "ammunition" => "ammo",
            "secure" or "vault" => "secure",
            "reward" or "rewards" => "reward",
            "overflow" => "overflow",
            "vendor" => "vendor",
            _ => InvalidInventoryCollection,
        };
    }

    private static bool HasAnyInventoryCollectionAvailable(
        ClientObservationSnapshot snapshot)
    {
        return snapshot.LocalPlayer.Inventory.IsAvailable ||
               snapshot.LocalPlayer.SecureInventory.IsAvailable ||
               snapshot.LocalPlayer.RewardOverflowInventory.IsAvailable ||
               snapshot.LocalPlayer.VendorInventory.IsAvailable;
    }

    private static bool IsInventoryCollectionAvailable(
        ClientObservationSnapshot snapshot,
        string collection)
    {
        return collection switch
        {
            "cargo" or "equipment" or "ammo" =>
                snapshot.LocalPlayer.Inventory.IsAvailable,
            "secure" =>
                snapshot.LocalPlayer.SecureInventory.IsAvailable,
            "reward" or "overflow" =>
                snapshot.LocalPlayer.RewardOverflowInventory.IsAvailable,
            "vendor" =>
                snapshot.LocalPlayer.VendorInventory.IsAvailable,
            _ => false,
        };
    }

    private static string NormalizeFilter(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? ""
            : new string(value
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray());
    }

    private static string? NormalizeText(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static string FormatToken(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? ""
            : string.Join(
                ' ',
                value.Split(
                        ['_', '-'],
                        StringSplitOptions.RemoveEmptyEntries)
                    .Select(part => string.Concat(
                        char.ToUpperInvariant(part[0]),
                        part[1..])));
    }

    private sealed record ObservedQueryResolution(
        ClientSlot? Slot,
        ClientInstance? Client,
        ClientObservationSnapshot? Snapshot,
        ControlPlaneResponse? Failure)
    {
        public static ObservedQueryResolution Failed(
            ControlPlaneResponse failure) =>
            new(null, null, null, failure);
    }
}
