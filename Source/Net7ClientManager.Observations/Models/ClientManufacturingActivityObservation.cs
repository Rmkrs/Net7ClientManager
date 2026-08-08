namespace Net7ClientManager.Observations.Models;

public sealed record ClientManufacturingActivityObservation
{
    public bool IsAvailable { get; init; }

    public string Status { get; init; } = "";

    public DateTimeOffset ObservedAt { get; init; }

    public uint ManufacturingObjectId { get; init; }

    public uint ClientObjectAddress { get; init; }

    public uint AuxDataAddress { get; init; }

    public bool IsAnalyzePanelActive { get; init; }

    public bool IsManufacturingPanelActive { get; init; }

    public int Mode { get; init; }

    public int Validity { get; init; }

    public int TargetItemTemplateId { get; init; }

    public ulong? NegotiatedCostCredits { get; init; }

    public float? SuccessProbabilityPercent { get; init; }

    public float? CriticalSuccessProbabilityPercent { get; init; }

    public bool IsAnalyzeUiPacingAvailable { get; init; }

    public bool IsAnalyzeUiAttemptInProgress { get; init; }

    public int AnalyzeUiDelayRemainingDeciseconds { get; init; }

    public bool IsManufactureOutputObservationAvailable { get; init; }

    public long ManufactureOutputSequence { get; init; }

    public DateTimeOffset? ManufactureOutputObservedAt { get; init; }

    public int ManufactureOutputItemTemplateId { get; init; }

    public int ManufactureOutputQuantity { get; init; }

    public float? ManufactureOutputQualityPercent { get; init; }

    public IReadOnlyList<int> ResultComponentItemTemplateIds { get; init; } = [];

    public static ClientManufacturingActivityObservation Unavailable(
        string status,
        DateTimeOffset? observedAt = null,
        uint manufacturingObjectId = 0,
        uint clientObjectAddress = 0,
        uint auxDataAddress = 0)
    {
        return new ClientManufacturingActivityObservation
        {
            Status = status,
            ObservedAt = observedAt ?? DateTimeOffset.MinValue,
            ManufacturingObjectId = manufacturingObjectId,
            ClientObjectAddress = clientObjectAddress,
            AuxDataAddress = auxDataAddress,
        };
    }
}
