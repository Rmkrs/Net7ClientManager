namespace Net7ClientManager.Contributions;

using System.Globalization;
using System.Text;
using Net7ClientManager.Models;
using Net7ClientManager.Navigation;
using Net7ClientManager.Observations;
using Net7ClientManager.Observations.Models;

internal sealed partial class ForgeContributionCoordinator
{
    private readonly HashSet<string> inFlightJobOfferKeys =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> completedJobOfferKeys =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> sessionObservedJobOfferKeys =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> lifetimeObservedJobOfferKeys;
    private readonly Dictionary<string, DateTimeOffset> jobOfferRetryAllowedAt =
        new(StringComparer.Ordinal);

    private void ObserveJobOffers(ClientObservationSnapshot snapshot)
    {
        if (snapshot.LifecycleState != ClientLifecycleState.InGame ||
            snapshot.LoadingOrTransitionFlag != 0 ||
            !snapshot.World.IsAvailable ||
            snapshot.World.Environment != ClientWorldEnvironment.Starbase ||
            !snapshot.StarbaseContext.IsAvailable ||
            !snapshot.JobTerminal.IsAvailable ||
            !snapshot.JobTerminal.IsOpen ||
            snapshot.JobTerminal.CatalogueGeneration < 1 ||
            snapshot.JobTerminal.Descriptions.Count == 0)
        {
            return;
        }

        var identity = ClientLiveCharacterIdentityResolver.Resolve(snapshot);
        if (string.IsNullOrWhiteSpace(identity.Name))
        {
            return;
        }

        var starbaseId = snapshot.World.CurrentStarbaseId != 0
            ? snapshot.World.CurrentStarbaseId
            : snapshot.StarbaseContext.StarbaseId;
        if (starbaseId == 0 ||
            string.IsNullOrWhiteSpace(snapshot.World.CurrentStarbaseName) ||
            string.IsNullOrWhiteSpace(snapshot.World.CurrentSectorName))
        {
            return;
        }

        var facilitySlot = snapshot.StarbaseContext.Interaction.Kind ==
                ClientStarbaseInteractionKind.JobsTerminal
            ? snapshot.StarbaseContext.Interaction.FacilitySlot
            : -1;

        var offersById = snapshot.JobTerminal.Offers
            .Where(offer => offer.JobId != 0)
            .GroupBy(offer => offer.JobId)
            .ToDictionary(group => group.Key, group => group.First());
        List<ObservedJobOfferContribution> ready = [];
        var observedChanged = false;

        lock (this.stateLock)
        {
            foreach (var description in snapshot.JobTerminal.Descriptions)
            {
                if (!description.StillAvailable ||
                    description.JobId == 0 ||
                    string.IsNullOrWhiteSpace(description.Title) ||
                    string.IsNullOrWhiteSpace(description.Description) ||
                    string.IsNullOrWhiteSpace(description.Reward) ||
                    !offersById.TryGetValue(description.JobId, out var offer) ||
                    offer.Category == ClientJobCategory.Unknown ||
                    offer.Level is not > 0 ||
                    string.IsNullOrWhiteSpace(offer.Type) ||
                    string.IsNullOrWhiteSpace(offer.Sponsor) ||
                    !string.Equals(
                        NormalizeKey(description.Title),
                        NormalizeKey(offer.Type),
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        NormalizeKey(description.Reward),
                        NormalizeKey(offer.Reward),
                        StringComparison.Ordinal))
                {
                    continue;
                }

                var reward = description.Reward.Trim();
                var familyFingerprint = ComputeJobOfferFamilyFingerprint(
                    (int)offer.Category,
                    offer.Level.Value,
                    offer.Type,
                    offer.Sponsor,
                    description.Title,
                    reward);
                var semanticFingerprint = ComputeJobOfferSemanticFingerprint(
                    familyFingerprint,
                    description.Description);
                var terminalKey = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{starbaseId}|{facilitySlot}|{NormalizeKey(snapshot.World.CurrentStarbaseName)}");
                var factFingerprint = string.Concat(
                    terminalKey,
                    "|",
                    semanticFingerprint);

                observedChanged |=
                    this.RecordObservedJobOffer(factFingerprint);

                if (this.completedJobOfferKeys.Contains(factFingerprint) ||
                    this.inFlightJobOfferKeys.Contains(factFingerprint) ||
                    (this.jobOfferRetryAllowedAt.TryGetValue(
                         factFingerprint,
                         out var retryAllowedAt) &&
                     snapshot.ObservedAt < retryAllowedAt))
                {
                    continue;
                }

                ready.Add(new ObservedJobOfferContribution(
                    factFingerprint,
                    identity.Name,
                    snapshot.ObservedAt,
                    starbaseId,
                    snapshot.World.CurrentStarbaseName.Trim(),
                    snapshot.World.CurrentSectorName.Trim(),
                    snapshot.World.CurrentSystemName.Trim(),
                    snapshot.World.ActiveSectorNumber,
                    facilitySlot,
                    new ForgeJobOfferContributionItem
                    {
                        FamilyFingerprint = familyFingerprint,
                        SemanticFingerprint = semanticFingerprint,
                        ObservedJobId = offer.JobId,
                        CatalogueGeneration = snapshot.JobTerminal.CatalogueGeneration,
                        Category = (int)offer.Category,
                        Level = offer.Level.Value,
                        Type = offer.Type.Trim(),
                        Sponsor = offer.Sponsor.Trim(),
                        Title = description.Title.Trim(),
                        Description = description.Description.Trim(),
                        AdvertisedReward = reward,
                        ObjectiveSummary = BuildJobObjectiveSummary(
                            description.Description),
                        StillAvailable = true,
                    }));
            }
        }

        if (observedChanged)
        {
            this.saveSettings();
            this.RaiseStatisticsChanged();
        }

        foreach (var contribution in ready)
        {
            this.StartJobOfferSubmission(contribution);
        }
    }

    private bool RecordObservedJobOffer(string factFingerprint)
    {
        var changed = false;

        if (this.sessionObservedJobOfferKeys.Add(factFingerprint))
        {
            this.session.JobOffersObserved =
                this.sessionObservedJobOfferKeys.Count;
            changed = true;
        }

        if (this.lifetimeObservedJobOfferKeys.Add(factFingerprint))
        {
            this.settings.Lifetime.ObservedJobOfferKeys.Add(factFingerprint);
            this.settings.Lifetime.JobOffersObserved =
                this.lifetimeObservedJobOfferKeys.Count;
            changed = true;
        }

        return changed;
    }

    private void StartJobOfferSubmission(
        ObservedJobOfferContribution contribution)
    {
        CancellationToken participationToken;

        lock (this.stateLock)
        {
            if (this.completedJobOfferKeys.Contains(contribution.FactFingerprint) ||
                !this.inFlightJobOfferKeys.Add(contribution.FactFingerprint))
            {
                return;
            }

            participationToken = this.participationCancellation.Token;
            this.status = string.Create(
                CultureInfo.InvariantCulture,
                $"Sharing selected job offer {contribution.Item.Title} with Forge.");
        }

        var submissionCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                this.cancellation.Token,
                participationToken);
        var task = this.SubmitJobOfferAsync(
            contribution,
            submissionCancellation);
        this.Track(task);
    }

    private async Task SubmitJobOfferAsync(
        ObservedJobOfferContribution contribution,
        CancellationTokenSource submissionCancellation)
    {
        var cancellationToken = submissionCancellation.Token;

        try
        {
            var identity = await this.identityService.EnsureAsync(
                    contribution.LivePilotName,
                    cancellationToken)
                .ConfigureAwait(false);
            var unsignedRequest = new ForgeJobOfferContributionRequest
            {
                ContributorId = identity.ContributorId,
                RequestId = Guid.NewGuid().ToString(
                    "D",
                    CultureInfo.InvariantCulture),
                SubmittedAtUtc = DateTimeOffset.UtcNow,
                FirstObservedAtUtc = contribution.ObservedAtUtc,
                LastObservedAtUtc = contribution.ObservedAtUtc,
                ObservationCount = 1,
                ClientVersion = clientVersion,
                DatasetRevision = this.dataSet.AuthorityRevision,
                Attribution = this.settings.Attribution ==
                    ForgeContributionAttribution.LivePilotName
                        ? "live-pilot-name"
                        : "publicly-anonymous",
                LivePilotName = contribution.LivePilotName,
                StarbaseId = contribution.StarbaseId,
                StationName = contribution.StationName,
                SectorName = contribution.SectorName,
                SystemName = contribution.SystemName,
                ActiveSectorNumber = contribution.ActiveSectorNumber,
                FacilitySlot = contribution.FacilitySlot,
                Offers = [contribution.Item],
            };
            var request = unsignedRequest with
            {
                Signature = identity.Sign(unsignedRequest),
            };
            var response = await this.client.SubmitJobOffersAsync(
                    request,
                    cancellationToken)
                .ConfigureAwait(false);

            lock (this.stateLock)
            {
                this.completedJobOfferKeys.Add(contribution.FactFingerprint);
                this.jobOfferRetryAllowedAt.Remove(contribution.FactFingerprint);
                this.session.JobOfferFactsSubmitted += response.Received;
                this.session.JobOffersAlreadyCanonical += response.AlreadyCanonical;
                this.session.JobOfferEvidenceAccepted += response.EvidenceAccepted;
                this.session.JobOfferConflicts += response.Conflicts;
                this.session.JobOffersCreated += response.Created;
                this.session.JobOffersStrengthened += response.Strengthened;
                this.session.SuccessfulBatches++;
                this.session.LastSuccessfulContributionUtc = DateTimeOffset.UtcNow;

                var lifetime = this.settings.Lifetime;
                lifetime.JobOfferFactsSubmitted += response.Received;
                lifetime.JobOffersAlreadyCanonical += response.AlreadyCanonical;
                lifetime.JobOfferEvidenceAccepted += response.EvidenceAccepted;
                lifetime.JobOfferConflicts += response.Conflicts;
                lifetime.JobOffersCreated += response.Created;
                lifetime.JobOffersStrengthened += response.Strengthened;
                lifetime.SuccessfulBatches++;
                lifetime.LastSuccessfulContributionUtc = DateTimeOffset.UtcNow;

                this.status = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Forge accepted selected job offer {contribution.Item.Title}.");
            }

            this.saveSettings();
            this.RaiseStatisticsChanged();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            lock (this.stateLock)
            {
                this.jobOfferRetryAllowedAt[contribution.FactFingerprint] =
                    DateTimeOffset.UtcNow + FailedSubmissionRetryDelay;
                this.session.FailedBatches++;
                this.session.LastFailedContributionUtc = DateTimeOffset.UtcNow;
                this.settings.Lifetime.FailedBatches++;
                this.settings.Lifetime.LastFailedContributionUtc =
                    DateTimeOffset.UtcNow;
                System.Diagnostics.Debug.WriteLine(
                    string.Concat("[Forge jobs] ", exception));
                this.status = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Forge could not share selected job offer {contribution.Item.Title}. It will retry later without another click.");
            }

            this.saveSettings();
            this.RaiseStatisticsChanged();
        }
        finally
        {
            lock (this.stateLock)
            {
                this.inFlightJobOfferKeys.Remove(contribution.FactFingerprint);
            }

            submissionCancellation.Dispose();
        }
    }

    private static string BuildJobObjectiveSummary(string description)
    {
        var builder = new StringBuilder(description.Length);
        var pendingSpace = false;

        foreach (var character in description.Trim())
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(character);
            if (builder.Length >= 4096)
            {
                break;
            }
        }

        return builder.ToString();
    }

    private static string ComputeJobOfferFamilyFingerprint(
        int category,
        int level,
        string type,
        string sponsor,
        string title,
        string reward)
    {
        var source = string.Join(
            "|",
            category.ToString(CultureInfo.InvariantCulture),
            level.ToString(CultureInfo.InvariantCulture),
            NormalizeKey(type),
            NormalizeKey(sponsor),
            NormalizeKey(title),
            NormalizeKey(reward));
        return ForgeNavigationHash.ComputeSha256(
            Encoding.UTF8.GetBytes(source));
    }

    private static string ComputeJobOfferSemanticFingerprint(
        string familyFingerprint,
        string description)
    {
        var source = string.Join(
            "|",
            familyFingerprint.ToLowerInvariant(),
            NormalizeKey(description));
        return ForgeNavigationHash.ComputeSha256(
            Encoding.UTF8.GetBytes(source));
    }

    private sealed record ObservedJobOfferContribution(
        string FactFingerprint,
        string LivePilotName,
        DateTimeOffset ObservedAtUtc,
        uint StarbaseId,
        string StationName,
        string SectorName,
        string SystemName,
        uint ActiveSectorNumber,
        int FacilitySlot,
        ForgeJobOfferContributionItem Item);
}
