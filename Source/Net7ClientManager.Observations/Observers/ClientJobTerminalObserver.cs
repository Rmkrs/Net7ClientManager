using System.Globalization;
using Net7ClientManager.Observations.Models;

namespace Net7ClientManager.Observations.Observers;

internal sealed class ClientJobTerminalObserver
{
    private const uint ImageBase = 0x00400000;

    private const uint ClientContextStarbaseView = 0x135c;
    private const uint StarbaseViewJobsPanel = 0x0194;

    private const uint PanelResourceOwner = 0x0084;
    private const uint PanelOpenState = 0x00bc;
    private const uint PanelRequestState = 0x00c4;
    private const uint PanelSelectedFilteredIndex = 0x00cc;
    private const uint PanelSelectedJobId = 0x00d0;
    private const uint PanelSelectedCategory = 0x00d4;
    private const uint PanelDisplayedOffersBegin = 0x00e0;
    private const uint PanelDisplayedOffersEnd = 0x00e4;
    private const uint PanelMasterListSentinel = 0x00f0;
    private const uint PanelMasterOfferCount = 0x00f4;

    private const uint RenderObjectResourceRoot = 0x001c;

    private const uint FontManagerStatic = 0x00be67c4;
    private const uint FontManagerRva =
        FontManagerStatic - ImageBase;

    private const uint HashMapBucketsBegin = 0x08;
    private const uint HashMapBucketsEnd = 0x0c;
    private const uint HashNodeNext = 0x00;
    private const uint HashNodeKey = 0x04;
    private const uint HashNodeValue = 0x08;

    private const uint ResourceCollectionNamedObjects = 0x0c;
    private const uint ResourceNodeObject = 0x14;
    private const uint ResourceObjectNamePointer = 0xf4;
    private const uint ResourceObjectTextPointer = 0xfc;

    private const string JobTitleResourceName =
        "JOB_TITLE_JNEWS00";
    private const string JobDetailResourceName =
        "WRAP_DETAIL_JNEWS00";
    private const string JobRewardResourceName =
        "ITEM_NAME_JNEWS00";

    private const uint ListNodeNext = 0x00;
    private const uint ListNodePrevious = 0x04;
    private const uint ListNodeJobInfo = 0x08;

    private const uint JobInfoCategory = 0x00;
    private const uint JobInfoTypeTextPointer = 0x08;
    private const uint JobInfoLevelTextPointer = 0x18;
    private const uint JobInfoSponsorTextPointer = 0x28;
    private const uint JobInfoRewardTextPointer = 0x38;
    private const uint JobInfoJobId = 0x44;

    private const int MaximumOfferCount = 512;
    private const int MaximumCatalogueSnapshotAttempts = 3;
    private const int MaximumHashBucketCount = 4096;
    private const int MaximumHashChainLength = 256;
    private const int MaximumNamedResourceNodeCount = 4096;
    private const int MaximumResourceNameLength = 128;
    private const int MaximumJobTextLength = 16384;
    private const int DisplayedCatalogueSettleMilliseconds = 2000;
    private const int MasterCatalogueQuiescenceMilliseconds = 2000;
    private const int MinimumRefreshBaselineForDetection = 16;
    private const int MinimumReplacementCatalogueSize = 8;
    private const double CatalogueCollapseRatio = 0.35;
    private const double CatalogueReplacementOverlapRatio = 0.25;

    private readonly Dictionary<uint, ClientJobDescriptionObservation>
        descriptions = [];

    private HashSet<uint> refreshBaselineJobIds = [];
    private int refreshBaselineOfferCount;
    private string lastMasterCatalogueFingerprint = "";
    private DateTimeOffset? lastMasterCatalogueChangedAt;
    private string lastDisplayedCatalogueFingerprint = "";
    private DateTimeOffset? lastDisplayedCatalogueChangedAt;
    private bool wasOpen;
    private int catalogueGeneration;

    private uint cachedPanelAddress;
    private ClientJobDisplayResourceAddresses? cachedDisplayResources;
    private uint pendingDescriptionJobId;
    private bool pendingDescriptionRequestObserved;
    private uint stableDescriptionCandidateJobId;
    private string stableDescriptionCandidateFingerprint = "";
    private int stableDescriptionCandidateReadCount;

    public ClientJobTerminalObservation Observe(
        ProcessMemoryReader memory,
        uint clientContextAddress,
        uint moduleBaseAddress,
        DateTimeOffset observedAt)
    {
        if (clientContextAddress == 0)
        {
            this.ResetPanelLifetimeState();

            return ClientJobTerminalObservation.Unavailable(
                "SClient is unavailable");
        }

        if (!TryReadPointer(
                memory,
                clientContextAddress,
                ClientContextStarbaseView,
                out var starbaseViewAddress,
                out var error))
        {
            this.ResetPanelLifetimeState();

            return ClientJobTerminalObservation.Unavailable(
                error,
                clientContextAddress);
        }

        if (starbaseViewAddress == 0)
        {
            this.ResetPanelLifetimeState();

            return ClientJobTerminalObservation.Unavailable(
                "StarbaseView is unavailable; the client is probably not docked",
                clientContextAddress);
        }

        if (!TryReadPointer(
                memory,
                starbaseViewAddress,
                StarbaseViewJobsPanel,
                out var panelAddress,
                out error))
        {
            this.ResetPanelLifetimeState();

            return ClientJobTerminalObservation.Unavailable(
                error,
                clientContextAddress,
                starbaseViewAddress);
        }

        if (panelAddress == 0)
        {
            this.ResetPanelLifetimeState();

            return ClientJobTerminalObservation.Unavailable(
                "Jobs Terminal panel is unavailable",
                clientContextAddress,
                starbaseViewAddress);
        }

        if (this.cachedPanelAddress != panelAddress)
        {
            this.cachedPanelAddress = panelAddress;
            this.cachedDisplayResources = null;
            this.ResetPendingDescriptionCapture();
        }

        if (!TryReadUInt32(
                memory,
                panelAddress,
                PanelOpenState,
                out var openState,
                out error) ||
            !TryReadUInt32(
                memory,
                panelAddress,
                PanelRequestState,
                out var requestState,
                out error) ||
            !TryReadUInt32(
                memory,
                panelAddress,
                PanelSelectedFilteredIndex,
                out var selectedFilteredIndex,
                out error) ||
            !TryReadUInt32(
                memory,
                panelAddress,
                PanelSelectedJobId,
                out var selectedJobId,
                out error) ||
            !TryReadUInt32(
                memory,
                panelAddress,
                PanelSelectedCategory,
                out var selectedCategory,
                out error))
        {
            return ClientJobTerminalObservation.Unavailable(
                error,
                clientContextAddress,
                starbaseViewAddress,
                panelAddress);
        }

        var isOpen = openState != 0;

        if (!isOpen)
        {
            this.ResetPendingDescriptionCapture();
            this.cachedDisplayResources = null;
        }

        List<ClientJobOfferObservation> offers = [];
        List<string> errors = [];

        TryReadCatalogueSnapshot(
            memory,
            panelAddress,
            offers,
            errors,
            out var sentinelAddress,
            out var reportedOfferCount,
            out var traversedNodeCount);

        List<uint> displayedJobIds = [];
        List<string> displayedErrors = [];

        TryReadDisplayedCatalogueSnapshot(
            memory,
            panelAddress,
            displayedJobIds,
            displayedErrors,
            out var displayedVectorBeginAddress,
            out var displayedVectorEndAddress);

        var displayResources = this.cachedDisplayResources;
        var displayResourceStatus =
            "Stable display resources are not resolved";

        if (isOpen)
        {
            if (displayResources == null)
            {
                if (TryResolveDisplayResources(
                        memory,
                        panelAddress,
                        moduleBaseAddress,
                        out var resolved,
                        out displayResourceStatus))
                {
                    displayResources = resolved;
                    this.cachedDisplayResources = resolved;
                }
            }
            else
            {
                displayResourceStatus =
                    "Stable display resources cached for the current panel lifetime";
            }
        }

        if (isOpen && selectedJobId == 0)
        {
            this.ResetPendingDescriptionCapture();
        }
        else if (isOpen &&
                 requestState == 2 &&
                 selectedJobId != 0)
        {
            this.ArmDescriptionCapture(
                selectedJobId,
                requestObserved: true);
        }
        else if (isOpen &&
                 requestState == 0 &&
                 selectedJobId != 0 &&
                 !this.descriptions.ContainsKey(selectedJobId) &&
                 this.pendingDescriptionJobId != selectedJobId)
        {
            // The ordinary observation cadence can miss the brief request
            // state entirely. Fall back to a conservative stable-text read
            // whenever an uncaptured selected job is already settled.
            this.ArmDescriptionCapture(
                selectedJobId,
                requestObserved: false);
        }

        if (isOpen &&
            requestState == 0 &&
            this.pendingDescriptionJobId != 0 &&
            selectedJobId == this.pendingDescriptionJobId &&
            displayResources != null)
        {
            if (!TryValidateDisplayResources(
                    memory,
                    displayResources,
                    out var validationError))
            {
                this.cachedDisplayResources = null;
                displayResources = null;
                displayResourceStatus =
                    $"Stable display resource cache invalidated: {validationError}";
            }
            else if (TryReadDisplayedDescription(
                         memory,
                         displayResources,
                         selectedJobId,
                         observedAt,
                         out var description,
                         out var descriptionError))
            {
                if (this.pendingDescriptionRequestObserved ||
                    this.ObserveStableDescriptionCandidate(description))
                {
                    this.descriptions[selectedJobId] = description;
                    this.ResetPendingDescriptionCapture();
                    displayResourceStatus =
                        "Stable display resources captured the selected job text";
                }
                else
                {
                    displayResourceStatus =
                        "Stable display resources are settling the selected job text";
                }
            }
            else
            {
                displayResourceStatus =
                    $"Stable display resources resolved; description pending: {descriptionError}";
            }
        }

        var masterCatalogueFingerprint = string.Join(
            "\n",
            offers
                .OrderBy(offer => offer.JobId)
                .Select(offer => offer.Fingerprint));

        var displayedCatalogueFingerprint = string.Create(
            CultureInfo.InvariantCulture,
            $"{selectedCategory}|{string.Join(",", displayedJobIds)}");

        var currentJobIds = offers
            .Select(offer => offer.JobId)
            .ToHashSet();

        var generationChangeKind =
            ClientJobCatalogueGenerationChangeKind.None;
        var generationChangeReason = "";
        var generationBaselineOfferCount = 0;
        var generationBaselineOverlapCount = 0;
        var generationBaselineOverlapRatio = 0.0;

        if (isOpen)
        {
            CatalogueGenerationDecision generationDecision;

            if (!this.wasOpen)
            {
                generationDecision = new CatalogueGenerationDecision(
                    ClientJobCatalogueGenerationChangeKind.TerminalOpened,
                    "Jobs Terminal opened",
                    0,
                    0,
                    0.0);
            }
            else
            {
                generationDecision = DetectCatalogueGenerationChange(
                    this.refreshBaselineJobIds,
                    this.refreshBaselineOfferCount,
                    currentJobIds);
            }

            if (generationDecision.Kind !=
                ClientJobCatalogueGenerationChangeKind.None)
            {
                generationChangeKind = generationDecision.Kind;
                generationChangeReason = generationDecision.Reason;
                generationBaselineOfferCount =
                    generationDecision.BaselineOfferCount;
                generationBaselineOverlapCount =
                    generationDecision.BaselineOverlapCount;
                generationBaselineOverlapRatio =
                    generationDecision.BaselineOverlapRatio;

                this.StartCatalogueGeneration(observedAt);

                if (selectedJobId != 0 &&
                    !this.descriptions.ContainsKey(selectedJobId))
                {
                    this.ArmDescriptionCapture(
                        selectedJobId,
                        requestObserved: requestState == 2);
                }
            }

            if (!string.Equals(
                    this.lastMasterCatalogueFingerprint,
                    masterCatalogueFingerprint,
                    StringComparison.Ordinal))
            {
                this.lastMasterCatalogueFingerprint =
                    masterCatalogueFingerprint;
                this.lastMasterCatalogueChangedAt = observedAt;
            }

            if (!string.Equals(
                    this.lastDisplayedCatalogueFingerprint,
                    displayedCatalogueFingerprint,
                    StringComparison.Ordinal))
            {
                this.lastDisplayedCatalogueFingerprint =
                    displayedCatalogueFingerprint;
                this.lastDisplayedCatalogueChangedAt = observedAt;
            }
        }

        this.wasOpen = isOpen;

        var masterQuietMilliseconds =
            isOpen && this.lastMasterCatalogueChangedAt.HasValue
                ? Math.Max(
                    0,
                    (long)(observedAt -
                           this.lastMasterCatalogueChangedAt.Value)
                        .TotalMilliseconds)
                : 0;

        var displayedQuietMilliseconds =
            isOpen && this.lastDisplayedCatalogueChangedAt.HasValue
                ? Math.Max(
                    0,
                    (long)(observedAt -
                           this.lastDisplayedCatalogueChangedAt.Value)
                        .TotalMilliseconds)
                : 0;

        var isMasterCatalogueQuiescent =
            isOpen &&
            offers.Count > 0 &&
            errors.Count == 0 &&
            offers.Count == reportedOfferCount &&
            masterQuietMilliseconds >=
                MasterCatalogueQuiescenceMilliseconds;

        var isDisplayedCatalogueSettled =
            isOpen &&
            displayedErrors.Count == 0 &&
            (displayedJobIds.Count > 0 ||
             isMasterCatalogueQuiescent) &&
            displayedQuietMilliseconds >=
                DisplayedCatalogueSettleMilliseconds;

        // The master list is the harvesting surface, but the filtered vector
        // is what the player actually sees. Once the displayed board settles,
        // retain the latest internally consistent master snapshot as the
        // refresh baseline. It may continue reconciling without making the
        // visible board unsettled.
        if (isDisplayedCatalogueSettled &&
            errors.Count == 0 &&
            offers.Count == reportedOfferCount)
        {
            this.refreshBaselineOfferCount = offers.Count;
            this.refreshBaselineJobIds = currentJobIds;
        }

        var stableResourceSummary = displayResources != null
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"stable display resources 3/3 at collection 0x{displayResources.ResourceCollectionAddress:X8}")
            : displayResourceStatus;

        var status = errors.Count == 0 && displayedErrors.Count == 0
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"Available; terminal {(isOpen ? "open" : "closed")}; generation {this.catalogueGeneration}; master {offers.Count}/{reportedOfferCount} readable ({(isMasterCatalogueQuiescent ? "quiescent" : "reconciling")}); displayed {displayedJobIds.Count} in {((ClientJobCategory)unchecked((int)selectedCategory))} ({(isDisplayedCatalogueSettled ? "settled" : "changing")}); {this.descriptions.Count} description(s) captured; {stableResourceSummary}")
            : string.Create(
                CultureInfo.InvariantCulture,
                $"Available with {errors.Count} master and {displayedErrors.Count} displayed catalogue read error(s); terminal {(isOpen ? "open" : "closed")}; generation {this.catalogueGeneration}; master {offers.Count}/{reportedOfferCount}; displayed {displayedJobIds.Count}; first: {(errors.Count > 0 ? errors[0] : displayedErrors[0])}; {stableResourceSummary}");

        return new ClientJobTerminalObservation
        {
            IsAvailable = true,
            Status = status,
            ClientContextAddress = clientContextAddress,
            StarbaseViewAddress = starbaseViewAddress,
            PanelAddress = panelAddress,
            IsOpen = isOpen,
            RequestState = unchecked((int)requestState),
            SelectedFilteredIndex =
                unchecked((int)selectedFilteredIndex),
            SelectedJobId = selectedJobId,
            SelectedRawCategory =
                unchecked((int)selectedCategory),
            ListSentinelAddress = sentinelAddress,
            ReportedOfferCount = reportedOfferCount,
            TraversedNodeCount = traversedNodeCount,
            ReadErrorCount = errors.Count,
            DisplayedVectorBeginAddress =
                displayedVectorBeginAddress,
            DisplayedVectorEndAddress =
                displayedVectorEndAddress,
            DisplayedOfferCount = displayedJobIds.Count,
            DisplayedReadErrorCount = displayedErrors.Count,
            CatalogueGeneration = this.catalogueGeneration,
            CatalogueGenerationChangeKind = generationChangeKind,
            CatalogueGenerationChangeReason = generationChangeReason,
            CatalogueBaselineOfferCount = generationBaselineOfferCount,
            CatalogueBaselineOverlapCount = generationBaselineOverlapCount,
            CatalogueBaselineOverlapRatio = generationBaselineOverlapRatio,
            IsDisplayedCatalogueSettled =
                isDisplayedCatalogueSettled,
            DisplayedCatalogueQuietMilliseconds =
                displayedQuietMilliseconds,
            IsMasterCatalogueQuiescent =
                isMasterCatalogueQuiescent,
            MasterCatalogueQuietMilliseconds =
                masterQuietMilliseconds,
            DisplayResourcesResolved = displayResources != null,
            DisplayResourceStatus = displayResourceStatus,
            ResourceOwnerAddress =
                displayResources?.ResourceOwnerAddress ?? 0,
            ResourceRootAddress =
                displayResources?.ResourceRootAddress ?? 0,
            ResourceCollectionAddress =
                displayResources?.ResourceCollectionAddress ?? 0,
            TitleResourceAddress =
                displayResources?.TitleResourceAddress ?? 0,
            DetailResourceAddress =
                displayResources?.DetailResourceAddress ?? 0,
            RewardResourceAddress =
                displayResources?.RewardResourceAddress ?? 0,
            Offers = offers,
            DisplayedJobIds = displayedJobIds,
            Descriptions = this.descriptions.Values
                .OrderBy(value => value.JobId)
                .ToArray(),
        };
    }

    private void ResetPanelLifetimeState()
    {
        this.wasOpen = false;
        this.cachedPanelAddress = 0;
        this.cachedDisplayResources = null;
        this.ResetPendingDescriptionCapture();
    }

    private void ArmDescriptionCapture(
        uint jobId,
        bool requestObserved)
    {
        if (jobId == 0)
        {
            this.ResetPendingDescriptionCapture();
            return;
        }

        if (this.pendingDescriptionJobId != jobId)
        {
            this.stableDescriptionCandidateJobId = 0;
            this.stableDescriptionCandidateFingerprint = "";
            this.stableDescriptionCandidateReadCount = 0;
            this.pendingDescriptionRequestObserved = false;
        }

        this.pendingDescriptionJobId = jobId;
        this.pendingDescriptionRequestObserved |= requestObserved;
    }

    private bool ObserveStableDescriptionCandidate(
        ClientJobDescriptionObservation description)
    {
        if (this.stableDescriptionCandidateJobId == description.JobId &&
            string.Equals(
                this.stableDescriptionCandidateFingerprint,
                description.Fingerprint,
                StringComparison.Ordinal))
        {
            this.stableDescriptionCandidateReadCount++;
        }
        else
        {
            this.stableDescriptionCandidateJobId = description.JobId;
            this.stableDescriptionCandidateFingerprint =
                description.Fingerprint;
            this.stableDescriptionCandidateReadCount = 1;
        }

        return this.stableDescriptionCandidateReadCount >= 2;
    }

    private void ResetPendingDescriptionCapture()
    {
        this.pendingDescriptionJobId = 0;
        this.pendingDescriptionRequestObserved = false;
        this.stableDescriptionCandidateJobId = 0;
        this.stableDescriptionCandidateFingerprint = "";
        this.stableDescriptionCandidateReadCount = 0;
    }

    private void StartCatalogueGeneration(
        DateTimeOffset observedAt)
    {
        if (this.catalogueGeneration > 0)
        {
            this.descriptions.Clear();
        }

        this.catalogueGeneration++;
        this.ResetPendingDescriptionCapture();
        this.lastMasterCatalogueFingerprint = "";
        this.lastMasterCatalogueChangedAt = observedAt;
        this.lastDisplayedCatalogueFingerprint = "";
        this.lastDisplayedCatalogueChangedAt = observedAt;
        this.refreshBaselineJobIds = [];
        this.refreshBaselineOfferCount = 0;
    }

    private static CatalogueGenerationDecision
        DetectCatalogueGenerationChange(
            IReadOnlySet<uint> baseline,
            int baselineOfferCount,
            IReadOnlySet<uint> current)
    {
        if (baselineOfferCount <
                MinimumRefreshBaselineForDetection ||
            baseline.Count == 0)
        {
            return CatalogueGenerationDecision.None;
        }

        var overlap = baseline.Count(current.Contains);
        var baselineOverlapRatio =
            overlap / (double)Math.Max(1, baseline.Count);
        var smallerSetOverlapRatio =
            overlap / (double)Math.Max(
                1,
                Math.Min(baseline.Count, current.Count));
        var collapsedThreshold = Math.Max(
            1,
            (int)Math.Floor(
                baselineOfferCount * CatalogueCollapseRatio));

        // Catalogue snapshots are already boundary-checked and retried.
        // A severe drop from the retained master baseline is therefore a real
        // in-place refresh phase, even when the remaining rows are old IDs.
        if (current.Count <= collapsedThreshold)
        {
            return new CatalogueGenerationDecision(
                ClientJobCatalogueGenerationChangeKind
                    .SettledCatalogueCollapsed,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"refresh baseline collapsed from {baselineOfferCount} to {current.Count} offer(s); {overlap} baseline JobID(s) remain"),
                baselineOfferCount,
                overlap,
                baselineOverlapRatio);
        }

        // Some servers may replace the board atomically instead of draining
        // it first. Low overlap catches that shape without requiring a drop.
        if (current.Count >= MinimumReplacementCatalogueSize &&
            smallerSetOverlapRatio <
                CatalogueReplacementOverlapRatio)
        {
            return new CatalogueGenerationDecision(
                ClientJobCatalogueGenerationChangeKind
                    .SettledCatalogueReplaced,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"refresh baseline was replaced; {overlap}/{Math.Min(baseline.Count, current.Count)} JobID(s) overlap"),
                baselineOfferCount,
                overlap,
                baselineOverlapRatio);
        }

        return CatalogueGenerationDecision.None;
    }

    private static bool TryReadDisplayedCatalogueSnapshot(
        ProcessMemoryReader memory,
        uint panelAddress,
        List<uint> displayedJobIds,
        List<string> errors,
        out uint vectorBeginAddress,
        out uint vectorEndAddress)
    {
        vectorBeginAddress = 0;
        vectorEndAddress = 0;

        for (var attempt = 1;
             attempt <= MaximumCatalogueSnapshotAttempts;
             attempt++)
        {
            displayedJobIds.Clear();
            errors.Clear();

            if (!TryReadUInt32(
                    memory,
                    panelAddress,
                    PanelDisplayedOffersBegin,
                    out var beginBefore,
                    out var error) ||
                !TryReadUInt32(
                    memory,
                    panelAddress,
                    PanelDisplayedOffersEnd,
                    out var endBefore,
                    out error))
            {
                errors.Add(error);
                return false;
            }

            if (!TryGetPointerVectorCount(
                    beginBefore,
                    endBefore,
                    "Jobs displayed vector",
                    out var countBefore,
                    out error))
            {
                errors.Add(error);
                return false;
            }

            for (var index = 0;
                 index < countBefore;
                 index++)
            {
                uint slotAddress;

                try
                {
                    slotAddress = checked(
                        beginBefore + (uint)index * 4);
                }
                catch (OverflowException)
                {
                    errors.Add(
                        "Jobs displayed vector slot address overflow");
                    break;
                }

                if (!memory.TryReadUInt32(
                        slotAddress,
                        out var jobInfoAddress) ||
                    jobInfoAddress == 0)
                {
                    errors.Add(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"Jobs displayed vector slot {index} has an unreadable or zero JobInfo pointer"));
                    break;
                }

                if (!TryReadUInt32(
                        memory,
                        jobInfoAddress,
                        JobInfoJobId,
                        out var jobId,
                        out error))
                {
                    errors.Add(error);
                    break;
                }

                displayedJobIds.Add(jobId);
            }

            if (!TryReadUInt32(
                    memory,
                    panelAddress,
                    PanelDisplayedOffersBegin,
                    out var beginAfter,
                    out error) ||
                !TryReadUInt32(
                    memory,
                    panelAddress,
                    PanelDisplayedOffersEnd,
                    out var endAfter,
                    out error))
            {
                errors.Add(error);
                return false;
            }

            vectorBeginAddress = beginAfter;
            vectorEndAddress = endAfter;

            if (beginBefore == beginAfter &&
                endBefore == endAfter &&
                errors.Count == 0 &&
                displayedJobIds.Count == countBefore)
            {
                return true;
            }

            if (attempt == MaximumCatalogueSnapshotAttempts)
            {
                if (beginBefore != beginAfter ||
                    endBefore != endAfter)
                {
                    errors.Add(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"Jobs displayed vector changed during all {MaximumCatalogueSnapshotAttempts} bounded snapshot attempt(s)"));
                }
                else if (displayedJobIds.Count != countBefore)
                {
                    errors.Add(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"Jobs displayed vector remained internally inconsistent after {MaximumCatalogueSnapshotAttempts} bounded snapshot attempt(s): {displayedJobIds.Count}/{countBefore} JobID(s)"));
                }
            }
        }

        return errors.Count == 0;
    }

    private static bool TryGetPointerVectorCount(
        uint beginAddress,
        uint endAddress,
        string label,
        out int count,
        out string error)
    {
        count = 0;
        error = "";

        if (beginAddress == 0 && endAddress == 0)
        {
            return true;
        }

        if (beginAddress == 0 || endAddress == 0)
        {
            error = $"{label} has one zero boundary";
            return false;
        }

        if (endAddress < beginAddress)
        {
            error = string.Create(
                CultureInfo.InvariantCulture,
                $"{label} end 0x{endAddress:X8} precedes begin 0x{beginAddress:X8}");
            return false;
        }

        var byteLength = endAddress - beginAddress;

        if ((byteLength & 3) != 0)
        {
            error = string.Create(
                CultureInfo.InvariantCulture,
                $"{label} byte length {byteLength} is not pointer-aligned");
            return false;
        }

        var rawCount = byteLength / 4;

        if (rawCount > MaximumOfferCount)
        {
            error = string.Create(
                CultureInfo.InvariantCulture,
                $"{label} count {rawCount} exceeds bounded limit {MaximumOfferCount}");
            return false;
        }

        count = unchecked((int)rawCount);
        return true;
    }

    private static bool TryReadCatalogueSnapshot(
        ProcessMemoryReader memory,
        uint panelAddress,
        List<ClientJobOfferObservation> offers,
        List<string> errors,
        out uint sentinelAddress,
        out int reportedOfferCount,
        out int traversedNodeCount)
    {
        sentinelAddress = 0;
        reportedOfferCount = 0;
        traversedNodeCount = 0;

        for (var attempt = 1;
             attempt <= MaximumCatalogueSnapshotAttempts;
             attempt++)
        {
            offers.Clear();
            errors.Clear();
            traversedNodeCount = 0;

            if (!TryReadUInt32(
                    memory,
                    panelAddress,
                    PanelMasterListSentinel,
                    out var sentinelBefore,
                    out var error) ||
                !TryReadUInt32(
                    memory,
                    panelAddress,
                    PanelMasterOfferCount,
                    out var countBeforeRaw,
                    out error))
            {
                errors.Add(error);
                return false;
            }

            var countBefore =
                countBeforeRaw <= int.MaxValue
                    ? (int)countBeforeRaw
                    : int.MaxValue;

            if (sentinelBefore != 0)
            {
                TryReadList(
                    memory,
                    sentinelBefore,
                    countBefore,
                    offers,
                    errors,
                    out traversedNodeCount);
            }

            if (!TryReadUInt32(
                    memory,
                    panelAddress,
                    PanelMasterListSentinel,
                    out var sentinelAfter,
                    out error) ||
                !TryReadUInt32(
                    memory,
                    panelAddress,
                    PanelMasterOfferCount,
                    out var countAfterRaw,
                    out error))
            {
                errors.Add(error);
                return false;
            }

            var countAfter =
                countAfterRaw <= int.MaxValue
                    ? (int)countAfterRaw
                    : int.MaxValue;

            sentinelAddress = sentinelAfter;
            reportedOfferCount = countAfter;

            if (sentinelBefore == sentinelAfter &&
                countBefore == countAfter &&
                errors.Count == 0 &&
                offers.Count == countAfter)
            {
                return true;
            }

            if (attempt == MaximumCatalogueSnapshotAttempts)
            {
                if (sentinelBefore != sentinelAfter ||
                    countBefore != countAfter)
                {
                    errors.Add(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"Jobs catalogue changed during all {MaximumCatalogueSnapshotAttempts} bounded snapshot attempt(s)"));
                }
                else if (offers.Count != countAfter)
                {
                    errors.Add(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"Jobs catalogue remained internally inconsistent after {MaximumCatalogueSnapshotAttempts} bounded snapshot attempt(s): {offers.Count}/{countAfter} offer(s)"));
                }
            }
        }

        return errors.Count == 0;
    }

    private static bool TryResolveDisplayResources(
        ProcessMemoryReader memory,
        uint panelAddress,
        uint moduleBaseAddress,
        out ClientJobDisplayResourceAddresses resources,
        out string status)
    {
        resources = default!;
        status = "";

        if (!TryReadUInt32(
                memory,
                panelAddress,
                PanelResourceOwner,
                out var resourceOwnerAddress,
                out var error) ||
            resourceOwnerAddress == 0)
        {
            status = error.Length > 0
                ? error
                : "Jobs panel resource owner is zero";
            return false;
        }

        if (!TryReadUInt32(
                memory,
                resourceOwnerAddress,
                RenderObjectResourceRoot,
                out var resourceRootAddress,
                out error) ||
            resourceRootAddress == 0)
        {
            status = error.Length > 0
                ? error
                : "Jobs panel resource root is zero";
            return false;
        }

        uint fontManagerGlobalAddress;

        try
        {
            fontManagerGlobalAddress = checked(
                moduleBaseAddress + FontManagerRva);
        }
        catch (OverflowException)
        {
            status = "Font manager global address overflow";
            return false;
        }

        if (!memory.TryReadUInt32(
                fontManagerGlobalAddress,
                out var fontManagerAddress) ||
            fontManagerAddress == 0)
        {
            status = string.Create(
                CultureInfo.InvariantCulture,
                $"Could not read FontManager pointer at 0x{fontManagerGlobalAddress:X8}");
            return false;
        }

        if (!TryFindHashMapValue(
                memory,
                fontManagerAddress,
                resourceRootAddress,
                out var resourceCollectionAddress,
                out error) ||
            resourceCollectionAddress == 0)
        {
            status = error.Length > 0
                ? error
                : "Resource collection lookup returned zero";
            return false;
        }

        if (!TryFindNamedResourceObjects(
                memory,
                resourceCollectionAddress,
                out var titleResourceAddress,
                out var detailResourceAddress,
                out var rewardResourceAddress,
                out error))
        {
            status = error;
            return false;
        }

        resources = new ClientJobDisplayResourceAddresses
        {
            ResourceOwnerAddress = resourceOwnerAddress,
            ResourceRootAddress = resourceRootAddress,
            FontManagerAddress = fontManagerAddress,
            ResourceCollectionAddress =
                resourceCollectionAddress,
            TitleResourceAddress =
                titleResourceAddress,
            DetailResourceAddress =
                detailResourceAddress,
            RewardResourceAddress =
                rewardResourceAddress,
        };

        status = "Stable display resources resolved by bounded map traversal";
        return true;
    }

    private static bool TryFindHashMapValue(
        ProcessMemoryReader memory,
        uint mapAddress,
        uint key,
        out uint value,
        out string error)
    {
        value = 0;

        if (!TryReadUInt32(
                memory,
                mapAddress,
                HashMapBucketsBegin,
                out var bucketsBegin,
                out error) ||
            !TryReadUInt32(
                memory,
                mapAddress,
                HashMapBucketsEnd,
                out var bucketsEnd,
                out error))
        {
            return false;
        }

        if (!TryGetBucketCount(
                bucketsBegin,
                bucketsEnd,
                out var bucketCount,
                out error))
        {
            return false;
        }

        uint bucketAddress;

        try
        {
            bucketAddress = checked(
                bucketsBegin +
                (key % (uint)bucketCount) * 4);
        }
        catch (OverflowException)
        {
            error = "Hash bucket address overflow";
            return false;
        }

        if (!memory.TryReadUInt32(
                bucketAddress,
                out var nodeAddress))
        {
            error = string.Create(
                CultureInfo.InvariantCulture,
                $"Could not read hash bucket at 0x{bucketAddress:X8}");
            return false;
        }

        HashSet<uint> visited = [];

        for (var chainLength = 0;
             nodeAddress != 0 &&
             chainLength < MaximumHashChainLength;
             chainLength++)
        {
            if (!visited.Add(nodeAddress))
            {
                error = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Hash chain looped at 0x{nodeAddress:X8}");
                return false;
            }

            if (!TryReadUInt32(
                    memory,
                    nodeAddress,
                    HashNodeKey,
                    out var nodeKey,
                    out error) ||
                !TryReadUInt32(
                    memory,
                    nodeAddress,
                    HashNodeValue,
                    out var nodeValue,
                    out error) ||
                !TryReadUInt32(
                    memory,
                    nodeAddress,
                    HashNodeNext,
                    out var nextNodeAddress,
                    out error))
            {
                return false;
            }

            if (nodeKey == key)
            {
                value = nodeValue;
                return true;
            }

            nodeAddress = nextNodeAddress;
        }

        error = nodeAddress != 0
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"Hash chain exceeded bounded length {MaximumHashChainLength}")
            : string.Create(
                CultureInfo.InvariantCulture,
                $"No resource collection entry matched root 0x{key:X8}");
        return false;
    }

    private static bool TryFindNamedResourceObjects(
        ProcessMemoryReader memory,
        uint resourceCollectionAddress,
        out uint titleResourceAddress,
        out uint detailResourceAddress,
        out uint rewardResourceAddress,
        out string error)
    {
        titleResourceAddress = 0;
        detailResourceAddress = 0;
        rewardResourceAddress = 0;

        uint namedMapAddress;

        try
        {
            namedMapAddress = checked(
                resourceCollectionAddress +
                ResourceCollectionNamedObjects);
        }
        catch (OverflowException)
        {
            error = "Named resource map address overflow";
            return false;
        }

        if (!TryReadUInt32(
                memory,
                namedMapAddress,
                HashMapBucketsBegin,
                out var bucketsBegin,
                out error) ||
            !TryReadUInt32(
                memory,
                namedMapAddress,
                HashMapBucketsEnd,
                out var bucketsEnd,
                out error) ||
            !TryGetBucketCount(
                bucketsBegin,
                bucketsEnd,
                out var bucketCount,
                out error))
        {
            return false;
        }

        HashSet<uint> visitedNodes = [];
        var visitedNodeCount = 0;

        for (var bucketIndex = 0;
             bucketIndex < bucketCount;
             bucketIndex++)
        {
            uint bucketAddress;

            try
            {
                bucketAddress = checked(
                    bucketsBegin +
                    (uint)bucketIndex * 4);
            }
            catch (OverflowException)
            {
                error = "Named resource bucket address overflow";
                return false;
            }

            if (!memory.TryReadUInt32(
                    bucketAddress,
                    out var nodeAddress))
            {
                error = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Could not read named resource bucket at 0x{bucketAddress:X8}");
                return false;
            }

            while (nodeAddress != 0)
            {
                if (visitedNodeCount >=
                    MaximumNamedResourceNodeCount)
                {
                    error = string.Create(
                        CultureInfo.InvariantCulture,
                        $"Named resource map exceeded bounded node count {MaximumNamedResourceNodeCount}");
                    return false;
                }

                if (!visitedNodes.Add(nodeAddress))
                {
                    error = string.Create(
                        CultureInfo.InvariantCulture,
                        $"Named resource map looped at 0x{nodeAddress:X8}");
                    return false;
                }

                visitedNodeCount++;

                if (!TryReadUInt32(
                        memory,
                        nodeAddress,
                        ResourceNodeObject,
                        out var resourceObjectAddress,
                        out error) ||
                    !TryReadUInt32(
                        memory,
                        nodeAddress,
                        HashNodeNext,
                        out var nextNodeAddress,
                        out error))
                {
                    return false;
                }

                if (resourceObjectAddress != 0 &&
                    TryReadUInt32(
                        memory,
                        resourceObjectAddress,
                        ResourceObjectNamePointer,
                        out var resourceNameAddress,
                        out _) &&
                    resourceNameAddress != 0 &&
                    memory.TryReadNullTerminatedLatin1String(
                        resourceNameAddress,
                        MaximumResourceNameLength,
                        out var resourceName))
                {
                    if (string.Equals(
                            resourceName,
                            JobTitleResourceName,
                            StringComparison.Ordinal))
                    {
                        titleResourceAddress =
                            resourceObjectAddress;
                    }
                    else if (string.Equals(
                                 resourceName,
                                 JobDetailResourceName,
                                 StringComparison.Ordinal))
                    {
                        detailResourceAddress =
                            resourceObjectAddress;
                    }
                    else if (string.Equals(
                                 resourceName,
                                 JobRewardResourceName,
                                 StringComparison.Ordinal))
                    {
                        rewardResourceAddress =
                            resourceObjectAddress;
                    }
                }

                if (titleResourceAddress != 0 &&
                    detailResourceAddress != 0 &&
                    rewardResourceAddress != 0)
                {
                    error = "";
                    return true;
                }

                nodeAddress = nextNodeAddress;
            }
        }

        error = string.Create(
            CultureInfo.InvariantCulture,
            $"Named resource map did not expose all Jobs text objects: title={(titleResourceAddress != 0 ? "yes" : "no")}, detail={(detailResourceAddress != 0 ? "yes" : "no")}, reward={(rewardResourceAddress != 0 ? "yes" : "no")}");
        return false;
    }

    private static bool TryGetBucketCount(
        uint bucketsBegin,
        uint bucketsEnd,
        out int bucketCount,
        out string error)
    {
        bucketCount = 0;
        error = "";

        if (bucketsBegin == 0 ||
            bucketsEnd <= bucketsBegin ||
            ((bucketsEnd - bucketsBegin) & 3) != 0)
        {
            error = string.Create(
                CultureInfo.InvariantCulture,
                $"Invalid hash bucket vector 0x{bucketsBegin:X8}..0x{bucketsEnd:X8}");
            return false;
        }

        var count = (bucketsEnd - bucketsBegin) / 4;

        if (count == 0 ||
            count > MaximumHashBucketCount)
        {
            error = string.Create(
                CultureInfo.InvariantCulture,
                $"Hash bucket count {count} is outside bounded range 1..{MaximumHashBucketCount}");
            return false;
        }

        bucketCount = checked((int)count);
        return true;
    }

    private static bool TryValidateDisplayResources(
        ProcessMemoryReader memory,
        ClientJobDisplayResourceAddresses resources,
        out string error)
    {
        return TryValidateNamedResource(
                   memory,
                   resources.TitleResourceAddress,
                   JobTitleResourceName,
                   out error) &&
               TryValidateNamedResource(
                   memory,
                   resources.DetailResourceAddress,
                   JobDetailResourceName,
                   out error) &&
               TryValidateNamedResource(
                   memory,
                   resources.RewardResourceAddress,
                   JobRewardResourceName,
                   out error);
    }

    private static bool TryValidateNamedResource(
        ProcessMemoryReader memory,
        uint resourceAddress,
        string expectedName,
        out string error)
    {
        error = "";

        if (!TryReadUInt32(
                memory,
                resourceAddress,
                ResourceObjectNamePointer,
                out var nameAddress,
                out error) ||
            nameAddress == 0 ||
            !memory.TryReadNullTerminatedLatin1String(
                nameAddress,
                MaximumResourceNameLength,
                out var actualName))
        {
            error = error.Length > 0
                ? error
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"Could not validate resource at 0x{resourceAddress:X8}");
            return false;
        }

        if (!string.Equals(
                actualName,
                expectedName,
                StringComparison.Ordinal))
        {
            error = string.Create(
                CultureInfo.InvariantCulture,
                $"Resource at 0x{resourceAddress:X8} is '{actualName}', expected '{expectedName}'");
            return false;
        }

        return true;
    }

    private static bool TryReadDisplayedDescription(
        ProcessMemoryReader memory,
        ClientJobDisplayResourceAddresses resources,
        uint jobId,
        DateTimeOffset observedAt,
        out ClientJobDescriptionObservation description,
        out string error)
    {
        description = default!;

        if (!TryReadResourceText(
                memory,
                resources.TitleResourceAddress,
                "job title",
                out var title,
                out error) ||
            !TryReadResourceText(
                memory,
                resources.DetailResourceAddress,
                "job detail",
                out var detail,
                out error) ||
            !TryReadResourceText(
                memory,
                resources.RewardResourceAddress,
                "job reward",
                out var reward,
                out error))
        {
            return false;
        }

        if (title.Length == 0 &&
            detail.Length == 0)
        {
            error =
                "Stable title and detail resources are both empty";
            return false;
        }

        description = new ClientJobDescriptionObservation
        {
            JobId = jobId,
            StillAvailable = !string.Equals(
                title,
                "Job no longer available",
                StringComparison.OrdinalIgnoreCase),
            Title = title,
            Description = detail,
            Reward = reward,
            Source = "Stable Jobs UI resources",
            TitleResourceAddress =
                resources.TitleResourceAddress,
            DescriptionResourceAddress =
                resources.DetailResourceAddress,
            RewardResourceAddress =
                resources.RewardResourceAddress,
            ObservedAt = observedAt,
        };

        error = "";
        return true;
    }

    private static bool TryReadResourceText(
        ProcessMemoryReader memory,
        uint resourceAddress,
        string label,
        out string value,
        out string error)
    {
        value = "";

        if (!TryReadUInt32(
                memory,
                resourceAddress,
                ResourceObjectTextPointer,
                out var textAddress,
                out error))
        {
            return false;
        }

        if (textAddress == 0)
        {
            error = string.Create(
                CultureInfo.InvariantCulture,
                $"Stable {label} pointer is zero at resource 0x{resourceAddress:X8}");
            return false;
        }

        if (!memory.TryReadNullTerminatedLatin1String(
                textAddress,
                MaximumJobTextLength,
                out value))
        {
            error = string.Create(
                CultureInfo.InvariantCulture,
                $"Could not read stable {label} text at 0x{textAddress:X8}");
            return false;
        }

        value = value.Trim();
        error = "";
        return true;
    }

    private static bool TryReadList(
        ProcessMemoryReader memory,
        uint sentinelAddress,
        int reportedOfferCount,
        List<ClientJobOfferObservation> offers,
        List<string> errors,
        out int traversedNodeCount)
    {
        traversedNodeCount = 0;

        if (!TryReadUInt32(
                memory,
                sentinelAddress,
                ListNodeNext,
                out var nodeAddress,
                out var error))
        {
            errors.Add(error);
            return false;
        }

        HashSet<uint> visited = [];
        var expectedTraversal =
            reportedOfferCount >= MaximumOfferCount - 8
                ? MaximumOfferCount
                : Math.Max(16, reportedOfferCount + 8);
        var maximumTraversal = Math.Min(
            MaximumOfferCount,
            expectedTraversal);

        while (nodeAddress != 0 &&
               nodeAddress != sentinelAddress &&
               traversedNodeCount < maximumTraversal)
        {
            if (!visited.Add(nodeAddress))
            {
                errors.Add(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Jobs master list looped at 0x{nodeAddress:X8}"));
                return false;
            }

            traversedNodeCount++;

            if (!TryReadUInt32(
                    memory,
                    nodeAddress,
                    ListNodePrevious,
                    out _,
                    out error) ||
                !TryReadUInt32(
                    memory,
                    nodeAddress,
                    ListNodeJobInfo,
                    out var jobInfoAddress,
                    out error))
            {
                errors.Add(error);
                return false;
            }

            if (jobInfoAddress == 0)
            {
                errors.Add(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Jobs list node 0x{nodeAddress:X8} has a zero JobInfo pointer"));
            }
            else if (TryReadOffer(
                         memory,
                         nodeAddress,
                         jobInfoAddress,
                         out var offer,
                         out error))
            {
                offers.Add(offer);
            }
            else
            {
                errors.Add(error);
            }

            if (!TryReadUInt32(
                    memory,
                    nodeAddress,
                    ListNodeNext,
                    out nodeAddress,
                    out error))
            {
                errors.Add(error);
                return false;
            }
        }

        if (traversedNodeCount >= maximumTraversal &&
            nodeAddress != sentinelAddress)
        {
            errors.Add(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Jobs list exceeded the bounded traversal limit of {maximumTraversal}"));
            return false;
        }

        return true;
    }

    private static bool TryReadOffer(
        ProcessMemoryReader memory,
        uint nodeAddress,
        uint jobInfoAddress,
        out ClientJobOfferObservation offer,
        out string error)
    {
        offer = default!;

        if (!TryReadUInt32(
                memory,
                jobInfoAddress,
                JobInfoCategory,
                out var rawCategory,
                out error) ||
            !TryReadUInt32(
                memory,
                jobInfoAddress,
                JobInfoJobId,
                out var jobId,
                out error) ||
            !TryReadIndirectString(
                memory,
                jobInfoAddress,
                JobInfoTypeTextPointer,
                "job type",
                out var type,
                out error) ||
            !TryReadIndirectString(
                memory,
                jobInfoAddress,
                JobInfoLevelTextPointer,
                "job level",
                out var levelText,
                out error) ||
            !TryReadIndirectString(
                memory,
                jobInfoAddress,
                JobInfoSponsorTextPointer,
                "job sponsor",
                out var sponsor,
                out error) ||
            !TryReadIndirectString(
                memory,
                jobInfoAddress,
                JobInfoRewardTextPointer,
                "job reward",
                out var reward,
                out error))
        {
            return false;
        }

        offer = new ClientJobOfferObservation
        {
            NodeAddress = nodeAddress,
            JobInfoAddress = jobInfoAddress,
            JobId = jobId,
            RawCategory = unchecked((int)rawCategory),
            Type = type,
            LevelText = levelText,
            Sponsor = sponsor,
            Reward = reward,
        };

        return true;
    }

    private static bool TryReadIndirectString(
        ProcessMemoryReader memory,
        uint baseAddress,
        uint pointerOffset,
        string label,
        out string value,
        out string error)
    {
        value = "";

        if (!TryReadUInt32(
                memory,
                baseAddress,
                pointerOffset,
                out var stringAddress,
                out error))
        {
            return false;
        }

        if (stringAddress == 0)
        {
            return true;
        }

        if (!memory.TryReadNullTerminatedLatin1String(
                stringAddress,
                MaximumJobTextLength,
                out value))
        {
            error = string.Create(
                CultureInfo.InvariantCulture,
                $"Could not read {label} at 0x{stringAddress:X8}");
            return false;
        }

        return true;
    }

    private static bool TryReadPointer(
        ProcessMemoryReader memory,
        uint baseAddress,
        uint offset,
        out uint value,
        out string error)
    {
        return TryReadUInt32(
            memory,
            baseAddress,
            offset,
            out value,
            out error);
    }

    private static bool TryReadByte(
        ProcessMemoryReader memory,
        uint baseAddress,
        uint offset,
        out byte value,
        out string error)
    {
        value = 0;
        error = "";

        uint address;

        try
        {
            address = checked(baseAddress + offset);
        }
        catch (OverflowException)
        {
            error = string.Create(
                CultureInfo.InvariantCulture,
                $"Address overflow for 0x{baseAddress:X8} + 0x{offset:X}");
            return false;
        }

        if (!memory.TryReadBytes(
                address,
                1,
                out var bytes))
        {
            error = string.Create(
                CultureInfo.InvariantCulture,
                $"Could not read byte at 0x{address:X8}");
            return false;
        }

        value = bytes[0];
        return true;
    }

    private static bool TryReadUInt32(
        ProcessMemoryReader memory,
        uint baseAddress,
        uint offset,
        out uint value,
        out string error)
    {
        value = 0;
        error = "";

        uint address;

        try
        {
            address = checked(baseAddress + offset);
        }
        catch (OverflowException)
        {
            error = string.Create(
                CultureInfo.InvariantCulture,
                $"Address overflow for 0x{baseAddress:X8} + 0x{offset:X}");
            return false;
        }

        if (!memory.TryReadUInt32(address, out value))
        {
            error = string.Create(
                CultureInfo.InvariantCulture,
                $"Could not read UInt32 at 0x{address:X8}");
            return false;
        }

        return true;
    }

    private sealed record ClientJobDisplayResourceAddresses
    {
        public uint ResourceOwnerAddress { get; init; }

        public uint ResourceRootAddress { get; init; }

        public uint FontManagerAddress { get; init; }

        public uint ResourceCollectionAddress { get; init; }

        public uint TitleResourceAddress { get; init; }

        public uint DetailResourceAddress { get; init; }

        public uint RewardResourceAddress { get; init; }
    }

    private readonly record struct CatalogueGenerationDecision(
        ClientJobCatalogueGenerationChangeKind Kind,
        string Reason,
        int BaselineOfferCount,
        int BaselineOverlapCount,
        double BaselineOverlapRatio)
    {
        public static CatalogueGenerationDecision None { get; } =
            new(
                ClientJobCatalogueGenerationChangeKind.None,
                "",
                0,
                0,
                0.0);
    }
}
