namespace Net7ClientManager.Contributions;

using System.Globalization;
using Net7ClientManager.Models;
using Net7ClientManager.Observations;
using Net7ClientManager.Observations.Models;

internal sealed partial class ForgeContributionCoordinator
{
    private readonly Dictionary<int, ProductionRecipeProcessState>
        productionRecipeProcessStates = [];
    private readonly HashSet<string> inFlightProductionRecipeKeys =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> completedProductionRecipeKeys =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> sessionObservedProductionRecipeKeys =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> lifetimeObservedProductionRecipeKeys;

    private void ObserveProductionRecipe(ClientObservationSnapshot snapshot)
    {
        var recipe = snapshot.ProductionRecipe;

        if (snapshot.LifecycleState != ClientLifecycleState.InGame ||
            snapshot.LoadingOrTransitionFlag != 0 ||
            !recipe.IsAvailable)
        {
            lock (this.stateLock)
            {
                this.productionRecipeProcessStates.Remove(snapshot.ProcessId);
            }

            return;
        }

        var identity = ClientLiveCharacterIdentityResolver.Resolve(snapshot);

        if (string.IsNullOrWhiteSpace(identity.Name))
        {
            lock (this.stateLock)
            {
                this.productionRecipeProcessStates.Remove(snapshot.ProcessId);
            }

            return;
        }

        var livePilotName = identity.Name.Trim();
        var stateFingerprint = string.Concat(
            NormalizeKey(livePilotName),
            "|",
            recipe.RecipeFingerprint);
        ObservedProductionRecipe? observation = null;
        string? submissionKey = null;
        CancellationToken participationToken = default;
        var settingsChanged = false;

        lock (this.stateLock)
        {
            if (!this.productionRecipeProcessStates.TryGetValue(
                    snapshot.ProcessId,
                    out var state) ||
                !string.Equals(
                    state.StateFingerprint,
                    stateFingerprint,
                    StringComparison.Ordinal))
            {
                this.productionRecipeProcessStates[snapshot.ProcessId] =
                    new ProductionRecipeProcessState(
                        stateFingerprint,
                        recipe.RecipeFingerprint,
                        snapshot.ObservedAt);
                this.status = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Checking the selected {FormatProductionKind(recipe.Kind)} recipe before sharing it.");
                return;
            }

            state.ObservationCount++;

            if (state.ObservationCount < 2 ||
                snapshot.ObservedAt < state.NextAttemptAllowedAt ||
                string.Equals(
                    state.AttemptedRecipeFingerprint,
                    recipe.RecipeFingerprint,
                    StringComparison.Ordinal))
            {
                return;
            }

            state.AttemptedRecipeFingerprint = recipe.RecipeFingerprint;
            state.NextAttemptAllowedAt = DateTimeOffset.MaxValue;
            observation = new ObservedProductionRecipe(
                snapshot.ProcessId,
                livePilotName,
                recipe.Kind,
                recipe.OutputItemTemplateId,
                recipe.Ingredients,
                recipe.RecipeFingerprint,
                state.StableSince,
                snapshot.ObservedAt,
                state.ObservationCount);
            settingsChanged = this.RecordObservedProductionRecipe(observation);
            submissionKey = this.CreateProductionRecipeSubmissionKey(observation);

            if (this.completedProductionRecipeKeys.Contains(submissionKey) ||
                !this.inFlightProductionRecipeKeys.Add(submissionKey))
            {
                this.status = string.Create(
                    CultureInfo.InvariantCulture,
                    $"This {FormatProductionKind(recipe.Kind)} recipe has already been shared during this session.");
                submissionKey = null;
            }
            else
            {
                this.status = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Sharing the selected {FormatProductionKind(recipe.Kind)} recipe with Forge.");
                participationToken = this.participationCancellation.Token;
            }
        }

        if (settingsChanged)
        {
            this.saveSettings();
        }

        this.RaiseStatisticsChanged();

        if (submissionKey == null || observation == null)
        {
            return;
        }

        var submissionCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                this.cancellation.Token,
                participationToken);
        var task = this.SubmitProductionRecipeAsync(
            observation,
            submissionKey,
            submissionCancellation);
        this.Track(task);
    }

    private async Task SubmitProductionRecipeAsync(
        ObservedProductionRecipe observation,
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
            var unsignedRequest = new ForgeProductionRecipeContributionRequest
            {
                ContributorId = identity.ContributorId,
                RequestId = Guid.NewGuid().ToString(
                    "D",
                    CultureInfo.InvariantCulture),
                SubmittedAtUtc = DateTimeOffset.UtcNow,
                FirstObservedAtUtc = observation.FirstObservedAtUtc,
                LastObservedAtUtc = observation.LastObservedAtUtc,
                ObservationCount = observation.ObservationCount,
                ClientVersion = clientVersion,
                DatasetRevision = this.dataSet.AuthorityRevision,
                Attribution = this.settings.Attribution ==
                    ForgeContributionAttribution.LivePilotName
                        ? "live-pilot-name"
                        : "publicly-anonymous",
                LivePilotName = observation.LivePilotName,
                Recipes =
                [
                    new ForgeProductionRecipeContributionItem
                    {
                        Kind = (int)observation.Kind,
                        OutputItemTemplateId =
                            observation.OutputItemTemplateId,
                        RecipeFingerprint = observation.RecipeFingerprint,
                        Ingredients =
                        [
                            .. observation.Ingredients.Select(ingredient =>
                                new ForgeProductionRecipeIngredient
                                {
                                    ItemTemplateId = ingredient.ItemTemplateId,
                                    Quantity = ingredient.Quantity,
                                }),
                        ],
                    },
                ],
            };
            var request = unsignedRequest with
            {
                Signature = identity.Sign(unsignedRequest),
            };
            var response = await this.client.SubmitProductionRecipesAsync(
                    request,
                    cancellationToken)
                .ConfigureAwait(false);

            lock (this.stateLock)
            {
                this.completedProductionRecipeKeys.Add(submissionKey);
                this.session.ProductionRecipeFactsSubmitted += response.Received;
                this.session.ProductionRecipesAlreadyCanonical +=
                    response.AlreadyCanonical;
                this.session.ProductionRecipeEvidenceAccepted +=
                    response.EvidenceAccepted;
                this.session.ProductionRecipeConflicts += response.Conflicts;
                this.session.ProductionRecipesCreated += response.Created;
                this.session.SuccessfulBatches++;
                this.session.LastSuccessfulContributionUtc =
                    DateTimeOffset.UtcNow;

                var lifetime = this.settings.Lifetime;
                lifetime.ProductionRecipeFactsSubmitted += response.Received;
                lifetime.ProductionRecipesAlreadyCanonical +=
                    response.AlreadyCanonical;
                lifetime.ProductionRecipeEvidenceAccepted +=
                    response.EvidenceAccepted;
                lifetime.ProductionRecipeConflicts += response.Conflicts;
                lifetime.ProductionRecipesCreated += response.Created;
                lifetime.SuccessfulBatches++;
                lifetime.LastSuccessfulContributionUtc =
                    DateTimeOffset.UtcNow;

                this.status = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Forge accepted the {FormatProductionKind(observation.Kind)} recipe.");
            }

            this.saveSettings();
            this.RaiseStatisticsChanged();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Application shutdown or participation changes abandon best-effort work.
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
                System.Diagnostics.Debug.WriteLine(
                    string.Concat(
                        "[Forge production recipe] ",
                        exception));
                this.status = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Forge could not share the selected {FormatProductionKind(observation.Kind)} recipe. It can be retried later this session.");

                if (this.productionRecipeProcessStates.TryGetValue(
                        observation.ProcessId,
                        out var state) &&
                    string.Equals(
                        state.RecipeFingerprint,
                        observation.RecipeFingerprint,
                        StringComparison.Ordinal))
                {
                    state.AttemptedRecipeFingerprint = null;
                    state.NextAttemptAllowedAt =
                        DateTimeOffset.UtcNow + FailedSubmissionRetryDelay;
                }
            }

            this.saveSettings();
            this.RaiseStatisticsChanged();
        }
        finally
        {
            lock (this.stateLock)
            {
                this.inFlightProductionRecipeKeys.Remove(submissionKey);
            }

            submissionCancellation.Dispose();
        }
    }

    private bool RecordObservedProductionRecipe(
        ObservedProductionRecipe observation)
    {
        var observationKey = string.Concat(
            ((int)observation.Kind).ToString(CultureInfo.InvariantCulture),
            "|",
            observation.OutputItemTemplateId.ToString(
                CultureInfo.InvariantCulture),
            "|",
            observation.RecipeFingerprint);
        var changed = false;

        if (this.sessionObservedProductionRecipeKeys.Add(observationKey))
        {
            this.session.ProductionRecipesObserved =
                this.sessionObservedProductionRecipeKeys.Count;
        }

        if (this.lifetimeObservedProductionRecipeKeys.Add(observationKey))
        {
            this.settings.Lifetime.ObservedProductionRecipeKeys.Add(
                observationKey);
            this.settings.Lifetime.ProductionRecipesObserved =
                this.lifetimeObservedProductionRecipeKeys.Count;
            changed = true;
        }

        return changed;
    }

    private string CreateProductionRecipeSubmissionKey(
        ObservedProductionRecipe observation)
    {
        var attributionIdentity = this.settings.Attribution ==
            ForgeContributionAttribution.LivePilotName
                ? NormalizeKey(observation.LivePilotName)
                : "anonymous";
        return string.Concat(
            this.dataSet.AuthorityRevision.ToString(CultureInfo.InvariantCulture),
            "|",
            attributionIdentity,
            "|",
            observation.RecipeFingerprint);
    }

    private static string FormatProductionKind(
        ClientProductionRecipeKind kind)
    {
        return kind == ClientProductionRecipeKind.Refine
            ? "refining"
            : "manufacturing";
    }

    private sealed class ProductionRecipeProcessState
    {
        public ProductionRecipeProcessState(
            string stateFingerprint,
            string recipeFingerprint,
            DateTimeOffset stableSince)
        {
            this.StateFingerprint = stateFingerprint;
            this.RecipeFingerprint = recipeFingerprint;
            this.StableSince = stableSince;
        }

        public string StateFingerprint { get; }

        public string RecipeFingerprint { get; }

        public DateTimeOffset StableSince { get; }

        public int ObservationCount { get; set; } = 1;

        public string? AttemptedRecipeFingerprint { get; set; }

        public DateTimeOffset NextAttemptAllowedAt { get; set; } =
            DateTimeOffset.MinValue;
    }

    private sealed record ObservedProductionRecipe(
        int ProcessId,
        string LivePilotName,
        ClientProductionRecipeKind Kind,
        int OutputItemTemplateId,
        IReadOnlyList<ClientProductionRecipeIngredientObservation> Ingredients,
        string RecipeFingerprint,
        DateTimeOffset FirstObservedAtUtc,
        DateTimeOffset LastObservedAtUtc,
        int ObservationCount);
}
