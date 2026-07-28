namespace Net7ClientManager.Observations.Models;

public sealed record ClientNetworkTrafficObservation
{
    public const int ExpectedBucketCount = 4;

    public bool IsAvailable { get; init; }

    public string Status { get; init; } = "";

    public uint ConnectionWrapperAddress { get; init; }

    public uint TrafficMeterAddress { get; init; }

    public uint TrafficMeterVTableAddress { get; init; }

    public uint ReceiveBytesPerSecond { get; init; }

    public uint SendBytesPerSecond { get; init; }

    public uint ReceiveCurrentBucketBytes { get; init; }

    public uint SendCurrentBucketBytes { get; init; }

    public uint ReceiveBucketStartedAt { get; init; }

    public uint SendBucketStartedAt { get; init; }

    public uint ReceiveHistoryAddress { get; init; }

    public uint SendHistoryAddress { get; init; }

    public int ReceiveWriteIndex { get; init; }

    public int SendWriteIndex { get; init; }

    public IReadOnlyList<uint> ReceiveBuckets { get; init; } = [];

    public IReadOnlyList<uint> SendBuckets { get; init; } = [];

    public int AveragingWindowSeconds =>
        this.ReceiveBuckets.Count == this.SendBuckets.Count
            ? this.ReceiveBuckets.Count
            : 0;

    public static ClientNetworkTrafficObservation Unavailable(
        string status,
        uint connectionWrapperAddress = 0,
        uint trafficMeterAddress = 0,
        uint trafficMeterVTableAddress = 0)
    {
        return new ClientNetworkTrafficObservation
        {
            Status = status,
            ConnectionWrapperAddress = connectionWrapperAddress,
            TrafficMeterAddress = trafficMeterAddress,
            TrafficMeterVTableAddress = trafficMeterVTableAddress,
        };
    }
}
