using System.Globalization;

namespace Net7ClientManager.Observations.Models;

public enum ClientJobCategory
{
    Combat = 0,
    Trade = 1,
    Explore = 2,
    Unknown = -1,
}

public enum ClientJobCatalogueGenerationChangeKind
{
    None = 0,
    TerminalOpened = 1,
    SettledCatalogueCollapsed = 2,
    SettledCatalogueReplaced = 3,
}

public sealed record ClientJobOfferObservation
{
    public uint NodeAddress { get; init; }

    public uint JobInfoAddress { get; init; }

    public uint JobId { get; init; }

    public int RawCategory { get; init; }

    public ClientJobCategory Category =>
        this.RawCategory switch
        {
            0 => ClientJobCategory.Combat,
            1 => ClientJobCategory.Trade,
            2 => ClientJobCategory.Explore,
            _ => ClientJobCategory.Unknown,
        };

    public string Type { get; init; } = "";

    public string LevelText { get; init; } = "";

    public int? Level =>
        int.TryParse(
            this.LevelText,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var level)
            ? level
            : null;

    public string Sponsor { get; init; } = "";

    public string Reward { get; init; } = "";

    public string Fingerprint =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{this.JobId}|{this.RawCategory}|{this.LevelText}|{this.Type}|{this.Sponsor}|{this.Reward}");
}

public sealed record ClientJobDescriptionObservation
{
    public uint JobId { get; init; }

    public bool StillAvailable { get; init; }

    public string Title { get; init; } = "";

    public string Description { get; init; } = "";

    public string Reward { get; init; } = "";

    public string Source { get; init; } = "";

    public uint TitleResourceAddress { get; init; }

    public uint DescriptionResourceAddress { get; init; }

    public uint RewardResourceAddress { get; init; }

    public DateTimeOffset ObservedAt { get; init; }

    public string Fingerprint =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{this.JobId}|{this.StillAvailable}|{this.Title}|{this.Description}|{this.Reward}");
}

public sealed record ClientJobTerminalObservation
{
    public bool IsAvailable { get; init; }

    public string Status { get; init; } = "";

    public uint ClientContextAddress { get; init; }

    public uint StarbaseViewAddress { get; init; }

    public uint PanelAddress { get; init; }

    public bool IsOpen { get; init; }

    public int RequestState { get; init; }

    public int SelectedFilteredIndex { get; init; }

    public uint SelectedJobId { get; init; }

    public int SelectedRawCategory { get; init; }

    public ClientJobCategory SelectedCategory =>
        this.SelectedRawCategory switch
        {
            0 => ClientJobCategory.Combat,
            1 => ClientJobCategory.Trade,
            2 => ClientJobCategory.Explore,
            _ => ClientJobCategory.Unknown,
        };

    public uint ListSentinelAddress { get; init; }

    public int ReportedOfferCount { get; init; }

    public int TraversedNodeCount { get; init; }

    public int ReadErrorCount { get; init; }

    public uint DisplayedVectorBeginAddress { get; init; }

    public uint DisplayedVectorEndAddress { get; init; }

    public int DisplayedOfferCount { get; init; }

    public int DisplayedReadErrorCount { get; init; }

    public int CatalogueGeneration { get; init; }

    public ClientJobCatalogueGenerationChangeKind
        CatalogueGenerationChangeKind { get; init; }

    public string CatalogueGenerationChangeReason { get; init; } = "";

    public int CatalogueBaselineOfferCount { get; init; }

    public int CatalogueBaselineOverlapCount { get; init; }

    public double CatalogueBaselineOverlapRatio { get; init; }

    public bool IsDisplayedCatalogueSettled { get; init; }

    public bool IsCatalogueSettled =>
        this.IsDisplayedCatalogueSettled;

    public long DisplayedCatalogueQuietMilliseconds { get; init; }

    public long CatalogueQuietMilliseconds =>
        this.DisplayedCatalogueQuietMilliseconds;

    public bool IsMasterCatalogueQuiescent { get; init; }

    public long MasterCatalogueQuietMilliseconds { get; init; }

    public bool DisplayResourcesResolved { get; init; }

    public string DisplayResourceStatus { get; init; } = "";

    public uint ResourceOwnerAddress { get; init; }

    public uint ResourceRootAddress { get; init; }

    public uint ResourceCollectionAddress { get; init; }

    public uint TitleResourceAddress { get; init; }

    public uint DetailResourceAddress { get; init; }

    public uint RewardResourceAddress { get; init; }

    public IReadOnlyList<ClientJobOfferObservation> Offers
    { get; init; } = [];

    public IReadOnlyList<uint> DisplayedJobIds
    { get; init; } = [];

    public IReadOnlyList<ClientJobDescriptionObservation> Descriptions
    { get; init; } = [];

    public ClientJobOfferObservation? SelectedOffer =>
        this.SelectedJobId != 0
            ? this.Offers.FirstOrDefault(
                offer => offer.JobId == this.SelectedJobId)
            : null;

    public ClientJobDescriptionObservation? SelectedDescription =>
        this.SelectedJobId != 0
            ? this.Descriptions
                .Where(description =>
                    description.JobId == this.SelectedJobId)
                .OrderByDescending(description =>
                    description.ObservedAt)
                .FirstOrDefault()
            : null;

    public string CatalogueFingerprint =>
        string.Join(
            "\n",
            this.Offers
                .OrderBy(offer => offer.JobId)
                .Select(offer => offer.Fingerprint));

    public string DisplayedCatalogueFingerprint =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{this.SelectedRawCategory}|{string.Join(",", this.DisplayedJobIds)}");

    public string DescriptionFingerprint =>
        string.Join(
            "\n",
            this.Descriptions
                .OrderBy(description => description.JobId)
                .Select(description => description.Fingerprint));

    public string StateFingerprint =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{this.IsAvailable}|{this.IsOpen}|{this.PanelAddress:X8}|{this.RequestState}|{this.SelectedFilteredIndex}|{this.SelectedJobId}|{this.SelectedRawCategory}|{this.ReportedOfferCount}|{this.DisplayedOfferCount}|{this.CatalogueGeneration}|{this.CatalogueGenerationChangeKind}|{this.IsDisplayedCatalogueSettled}|{this.IsMasterCatalogueQuiescent}|{this.DisplayResourcesResolved}|{this.CatalogueFingerprint}|{this.DisplayedCatalogueFingerprint}|{this.DescriptionFingerprint}");

    public static ClientJobTerminalObservation Unavailable(
        string status,
        uint clientContextAddress = 0,
        uint starbaseViewAddress = 0,
        uint panelAddress = 0)
    {
        return new ClientJobTerminalObservation
        {
            Status = status,
            ClientContextAddress = clientContextAddress,
            StarbaseViewAddress = starbaseViewAddress,
            PanelAddress = panelAddress,
        };
    }
}
