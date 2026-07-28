namespace Net7ClientManager.Contributions;

using System.Globalization;
using System.Text;
using Net7ClientManager.Models;
using Net7ClientManager.Navigation;
using Net7ClientManager.Observations;
using Net7ClientManager.Observations.Models;

internal sealed partial class ForgeContributionCoordinator
{
    private const int GravityWellMessageChannel = 17;
    private const string GravityWellEnterMessage = "You are now in a Gravity Well.";
    private const string GravityWellLeaveMessage = "You are now leaving the Gravity Well.";
    private const string GravityWellObservationSource = "packet-001d-message-string";

    private readonly HashSet<string> inFlightGravityWellKeys =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> completedGravityWellKeys =
        new(StringComparer.Ordinal);
    private readonly Dictionary<int, bool?> gravityWellInsideStateByProcessId = [];

    public void Observe(ClientChatMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!this.settings.Enabled ||
            !this.settings.Categories.NavigationObjects ||
            message.IsSnapshot ||
            message.Channel != GravityWellMessageChannel)
        {
            return;
        }

        if (!TryParseGravityWellBoundaryMessage(
                message.Text,
                out var direction,
                out var normalizedMessage))
        {
            return;
        }

        if (!this.TryCreateGravityWellObservation(
                message,
                direction,
                normalizedMessage,
                out var observation,
                out var unavailableReason))
        {
            if (!string.IsNullOrWhiteSpace(unavailableReason))
            {
                lock (this.stateLock)
                {
                    this.status = unavailableReason;
                }

                this.RaiseStatisticsChanged();
            }

            return;
        }

        string submissionKey;
        CancellationToken participationToken;

        lock (this.stateLock)
        {
            this.gravityWellInsideStateByProcessId[message.ProcessId] =
                direction == GravityWellBoundaryDirection.Entering;
            submissionKey = CreateGravityWellSubmissionKey(observation);

            if (this.completedGravityWellKeys.Contains(submissionKey) ||
                !this.inFlightGravityWellKeys.Add(submissionKey))
            {
                return;
            }

            participationToken = this.participationCancellation.Token;
            this.status = string.Create(
                CultureInfo.InvariantCulture,
                $"Preparing gravity-well boundary observation from {observation.SectorName}.");
        }

        this.RaiseStatisticsChanged();

        var submissionCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                this.cancellation.Token,
                participationToken);
        var task = this.SubmitGravityWellBoundaryAsync(
            observation,
            submissionKey,
            submissionCancellation);
        this.Track(task);
    }

    private bool TryCreateGravityWellObservation(
        ClientChatMessage message,
        GravityWellBoundaryDirection direction,
        string normalizedMessage,
        out ObservedGravityWellBoundary observation,
        out string unavailableReason)
    {
        observation = null!;
        unavailableReason = "";

        ClientObservationSnapshot? snapshot;

        lock (this.stateLock)
        {
            this.latestSnapshotsByProcessId.TryGetValue(
                message.ProcessId,
                out snapshot);
        }

        if (snapshot == null)
        {
            unavailableReason =
                "Waiting for a current client snapshot before contributing gravity-well boundary observations.";
            return false;
        }

        if (snapshot.LifecycleState != ClientLifecycleState.InGame ||
            snapshot.LoadingOrTransitionFlag != 0 ||
            !snapshot.World.IsAvailable ||
            snapshot.World.Environment != ClientWorldEnvironment.Space ||
            snapshot.World.ActiveSectorNumber == 0)
        {
            return false;
        }

        var identity = ClientLiveCharacterIdentityResolver.Resolve(snapshot);

        if (string.IsNullOrWhiteSpace(identity.Name))
        {
            return false;
        }

        var sector = this.dataSet.Document.Sectors.SingleOrDefault(candidate =>
            candidate.ActiveSectorNumber == snapshot.World.ActiveSectorNumber);

        if (sector == null)
        {
            unavailableReason = string.Create(
                CultureInfo.InvariantCulture,
                $"Waiting for active sector {snapshot.World.ActiveSectorNumber} to exist in the Forge dataset before contributing gravity-well boundary observations.");
            return false;
        }

        if (!TryGetGravityWellBoundaryPosition(
                snapshot,
                out var position,
                out unavailableReason))
        {
            return false;
        }

        observation = new ObservedGravityWellBoundary(
            snapshot.ProcessId,
            sector.Id,
            sector.Key,
            sector.Name,
            sector.SystemName,
            sector.ActiveSectorNumber,
            identity.Name.Trim(),
            direction,
            position.X,
            position.Y,
            position.Z,
            message.Channel,
            normalizedMessage,
            message.ObservedAt);
        return true;
    }

    private static bool TryGetGravityWellBoundaryPosition(
        ClientObservationSnapshot snapshot,
        out ClientSpatialPosition position,
        out string unavailableReason)
    {
        if (snapshot.Target.Distance.Local.IsAvailable &&
            IsFinite(snapshot.Target.Distance.Local.Position))
        {
            position = snapshot.Target.Distance.Local.Position;
            unavailableReason = "";
            return true;
        }

        if (snapshot.LocalPlayer.Spatial.IsAvailable &&
            IsFinite(snapshot.LocalPlayer.Spatial.Position))
        {
            position = snapshot.LocalPlayer.Spatial.Position;
            unavailableReason = "";
            return true;
        }

        position = default;
        unavailableReason =
            "Waiting for local-player spatial state before contributing gravity-well boundary observations.";
        return false;
    }

    private async Task SubmitGravityWellBoundaryAsync(
        ObservedGravityWellBoundary observation,
        string submissionKey,
        CancellationTokenSource submissionCancellation)
    {
        var cancellationToken = submissionCancellation.Token;

        try
        {
            var identity = await this.identityService.EnsureAsync(
                    observation.LivePilotName,
                    cancellationToken)
                .ConfigureAwait(false);
            var unsignedRequest = new ForgeGravityWellBoundaryContributionRequest
            {
                ContributorId = identity.ContributorId,
                RequestId = Guid.NewGuid().ToString("D", CultureInfo.InvariantCulture),
                SubmittedAtUtc = DateTimeOffset.UtcNow,
                ClientVersion = clientVersion,
                DatasetRevision = this.dataSet.AuthorityRevision,
                Attribution = this.settings.Attribution ==
                    ForgeContributionAttribution.LivePilotName
                        ? "live-pilot-name"
                        : "publicly-anonymous",
                LivePilotName = observation.LivePilotName,
                SectorId = observation.SectorId,
                SectorKey = observation.SectorKey,
                SectorName = observation.SectorName,
                SystemName = observation.SystemName,
                ActiveSectorNumber = observation.ActiveSectorNumber,
                Observations =
                [
                    new ForgeGravityWellBoundaryContributionItem
                    {
                        ObservedAtUtc = observation.ObservedAtUtc,
                        Direction = observation.Direction ==
                            GravityWellBoundaryDirection.Entering
                                ? "entering"
                                : "leaving",
                        X = observation.X,
                        Y = observation.Y,
                        Z = observation.Z,
                        MessageChannel = observation.MessageChannel,
                        RawMessage = observation.RawMessage,
                        Source = GravityWellObservationSource,
                    },
                ],
            };
            var request = unsignedRequest with
            {
                Signature = identity.Sign(unsignedRequest),
            };
            var response = await this.client.SubmitGravityWellBoundariesAsync(
                    request,
                    cancellationToken)
                .ConfigureAwait(false);

            lock (this.stateLock)
            {
                this.completedGravityWellKeys.Add(submissionKey);
                this.session.SuccessfulBatches++;
                this.session.LastSuccessfulContributionUtc =
                    DateTimeOffset.UtcNow;
                this.settings.Lifetime.SuccessfulBatches++;
                this.settings.Lifetime.LastSuccessfulContributionUtc =
                    DateTimeOffset.UtcNow;
                this.status = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Forge accepted the gravity-well boundary observation: {response.EvidenceAccepted} evidence facts, {response.AlreadyKnown} already known, {response.RollupsCreated} shapes created, {response.RollupsUpdated} shapes updated.");
            }

            this.saveSettings();
            this.RaiseStatisticsChanged();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Application shutdown abandons best-effort contribution work.
        }
        catch (Exception exception)
        {
            lock (this.stateLock)
            {
                this.session.FailedBatches++;
                this.session.LastFailedContributionUtc = DateTimeOffset.UtcNow;
                this.settings.Lifetime.FailedBatches++;
                this.settings.Lifetime.LastFailedContributionUtc =
                    DateTimeOffset.UtcNow;
                this.status = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Gravity-well boundary contribution failed: {exception.Message}");
            }

            this.saveSettings();
            this.RaiseStatisticsChanged();
        }
        finally
        {
            lock (this.stateLock)
            {
                this.inFlightGravityWellKeys.Remove(submissionKey);
            }

            submissionCancellation.Dispose();
        }
    }

    private static bool TryParseGravityWellBoundaryMessage(
        string text,
        out GravityWellBoundaryDirection direction,
        out string normalizedMessage)
    {
        normalizedMessage = NormalizeComputerMessage(text);

        if (string.Equals(
                normalizedMessage,
                GravityWellEnterMessage,
                StringComparison.Ordinal))
        {
            direction = GravityWellBoundaryDirection.Entering;
            return true;
        }

        if (string.Equals(
                normalizedMessage,
                GravityWellLeaveMessage,
                StringComparison.Ordinal))
        {
            direction = GravityWellBoundaryDirection.Leaving;
            return true;
        }

        direction = default;
        return false;
    }

    private static string NormalizeComputerMessage(string text)
    {
        var normalized = (text ?? string.Empty).Trim();

        return normalized.StartsWith(
                "COMPUTER:",
                StringComparison.OrdinalIgnoreCase)
            ? normalized["COMPUTER:".Length..].Trim()
            : normalized;
    }

    private string CreateGravityWellSubmissionKey(
        ObservedGravityWellBoundary observation)
    {
        var attributionIdentity = this.settings.Attribution ==
            ForgeContributionAttribution.LivePilotName
                ? NormalizeKey(observation.LivePilotName)
                : "anonymous";
        var roundedX = MathF.Round(observation.X / 25.0f) * 25.0f;
        var roundedY = MathF.Round(observation.Y / 25.0f) * 25.0f;
        var roundedZ = MathF.Round(observation.Z / 25.0f) * 25.0f;
        var source = string.Create(
            CultureInfo.InvariantCulture,
            $"{this.dataSet.AuthorityRevision}|{attributionIdentity}|gravity-well|{observation.SectorId}|{observation.Direction}|{roundedX:R}|{roundedY:R}|{roundedZ:R}|{observation.RawMessage}");
        return ForgeNavigationHash.ComputeSha256(
            Encoding.UTF8.GetBytes(source));
    }

    private static bool IsFinite(ClientSpatialPosition position)
    {
        return float.IsFinite(position.X) &&
            float.IsFinite(position.Y) &&
            float.IsFinite(position.Z);
    }

    private enum GravityWellBoundaryDirection
    {
        Entering = 1,
        Leaving = 2,
    }

    private sealed record ObservedGravityWellBoundary(
        int ProcessId,
        string SectorId,
        string SectorKey,
        string SectorName,
        string SystemName,
        uint ActiveSectorNumber,
        string LivePilotName,
        GravityWellBoundaryDirection Direction,
        float X,
        float Y,
        float Z,
        int MessageChannel,
        string RawMessage,
        DateTimeOffset ObservedAtUtc);
}
