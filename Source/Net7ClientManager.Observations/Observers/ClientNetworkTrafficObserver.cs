namespace Net7ClientManager.Observations.Observers;

using System.Globalization;
using Net7ClientManager.Observations.Models;

internal sealed class ClientNetworkTrafficObserver
{
    private const uint ImageBase = 0x00400000;

    private const uint TrafficMeterConnectionVTableStatic =
        0x00b08754;

    private const uint TrafficMeterConnectionVTableRva =
        TrafficMeterConnectionVTableStatic - ImageBase;

    private const uint ClientContextConnectionWrapperOffset =
        0x1124;

    private const uint ConnectionWrapperInnerConnectionOffset =
        0x08;

    private const uint TrafficMeterVTableOffset =
        0x00;

    private const uint ReceiveCurrentBucketBytesOffset =
        0x0c;

    private const uint ReceiveBucketStartedAtOffset =
        0x10;

    private const uint ReceiveHistoryAddressOffset =
        0x14;

    private const uint ReceiveBucketCountOffset =
        0x18;

    private const uint ReceiveWriteIndexOffset =
        0x1c;

    private const uint SendCurrentBucketBytesOffset =
        0x20;

    private const uint SendBucketStartedAtOffset =
        0x24;

    private const uint SendHistoryAddressOffset =
        0x28;

    private const uint SendBucketCountOffset =
        0x2c;

    private const uint SendWriteIndexOffset =
        0x30;

    public void Refresh(
        ProcessMemoryReader memory,
        ObservedClientState state)
    {
        if (!state.HasDirectClientState ||
            state.ModuleBaseAddress == 0 ||
            state.ClientContextAddress == 0)
        {
            state.NetworkTraffic =
                ClientNetworkTrafficObservation.Unavailable(
                    "Direct SClient state is unavailable");

            return;
        }

        if (!TryReadPointer(
                memory,
                state.ClientContextAddress,
                ClientContextConnectionWrapperOffset,
                "SClient.ConnectionWrapper",
                out var connectionWrapperAddress,
                out var error))
        {
            state.NetworkTraffic =
                ClientNetworkTrafficObservation.Unavailable(
                    error);

            return;
        }

        if (connectionWrapperAddress == 0)
        {
            state.NetworkTraffic =
                ClientNetworkTrafficObservation.Unavailable(
                    "SClient connection wrapper is null");

            return;
        }

        if (!TryReadPointer(
                memory,
                connectionWrapperAddress,
                ConnectionWrapperInnerConnectionOffset,
                "ConnectionWrapper.InnerConnection",
                out var trafficMeterAddress,
                out error))
        {
            state.NetworkTraffic =
                ClientNetworkTrafficObservation.Unavailable(
                    error,
                    connectionWrapperAddress);

            return;
        }

        if (trafficMeterAddress == 0)
        {
            state.NetworkTraffic =
                ClientNetworkTrafficObservation.Unavailable(
                    "Connection wrapper has no inner traffic meter",
                    connectionWrapperAddress);

            return;
        }

        if (!TryReadPointer(
                memory,
                trafficMeterAddress,
                TrafficMeterVTableOffset,
                "TrafficMeterConnection.VTable",
                out var trafficMeterVTableAddress,
                out error))
        {
            state.NetworkTraffic =
                ClientNetworkTrafficObservation.Unavailable(
                    error,
                    connectionWrapperAddress,
                    trafficMeterAddress);

            return;
        }

        uint expectedVTableAddress;

        try
        {
            expectedVTableAddress = checked(
                state.ModuleBaseAddress +
                TrafficMeterConnectionVTableRva);
        }
        catch (OverflowException)
        {
            state.NetworkTraffic =
                ClientNetworkTrafficObservation.Unavailable(
                    "Traffic-meter vtable address overflow",
                    connectionWrapperAddress,
                    trafficMeterAddress,
                    trafficMeterVTableAddress);

            return;
        }

        if (trafficMeterVTableAddress !=
            expectedVTableAddress)
        {
            state.NetworkTraffic =
                ClientNetworkTrafficObservation.Unavailable(
                        $"Unexpected traffic-meter vtable {FormatPointer(trafficMeterVTableAddress)}; expected {FormatPointer(expectedVTableAddress)}",
                    connectionWrapperAddress,
                    trafficMeterAddress,
                    trafficMeterVTableAddress);

            return;
        }

        if (!TryReadMeterDirection(
                memory,
                trafficMeterAddress,
                "Receive",
                ReceiveCurrentBucketBytesOffset,
                ReceiveBucketStartedAtOffset,
                ReceiveHistoryAddressOffset,
                ReceiveBucketCountOffset,
                ReceiveWriteIndexOffset,
                out var receive,
                out error) ||
            !TryReadMeterDirection(
                memory,
                trafficMeterAddress,
                "Send",
                SendCurrentBucketBytesOffset,
                SendBucketStartedAtOffset,
                SendHistoryAddressOffset,
                SendBucketCountOffset,
                SendWriteIndexOffset,
                out var send,
                out error))
        {
            state.NetworkTraffic =
                ClientNetworkTrafficObservation.Unavailable(
                    error,
                    connectionWrapperAddress,
                    trafficMeterAddress,
                    trafficMeterVTableAddress);

            return;
        }

        state.NetworkTraffic =
            new ClientNetworkTrafficObservation
            {
                IsAvailable = true,
                Status =
                    "Available; framed application bytes over four completed one-second buckets",
                ConnectionWrapperAddress =
                    connectionWrapperAddress,
                TrafficMeterAddress =
                    trafficMeterAddress,
                TrafficMeterVTableAddress =
                    trafficMeterVTableAddress,
                ReceiveBytesPerSecond =
                    CalculateAverage(receive.Buckets),
                SendBytesPerSecond =
                    CalculateAverage(send.Buckets),
                ReceiveCurrentBucketBytes =
                    receive.CurrentBucketBytes,
                SendCurrentBucketBytes =
                    send.CurrentBucketBytes,
                ReceiveBucketStartedAt =
                    receive.BucketStartedAt,
                SendBucketStartedAt =
                    send.BucketStartedAt,
                ReceiveHistoryAddress =
                    receive.HistoryAddress,
                SendHistoryAddress =
                    send.HistoryAddress,
                ReceiveWriteIndex =
                    receive.WriteIndex,
                SendWriteIndex =
                    send.WriteIndex,
                ReceiveBuckets =
                    receive.Buckets,
                SendBuckets =
                    send.Buckets,
            };
    }

    private static bool TryReadMeterDirection(
        ProcessMemoryReader memory,
        uint trafficMeterAddress,
        string directionName,
        uint currentBucketBytesOffset,
        uint bucketStartedAtOffset,
        uint historyAddressOffset,
        uint bucketCountOffset,
        uint writeIndexOffset,
        out MeterDirection direction,
        out string error)
    {
        direction = default;

        if (!TryReadUInt32(
                memory,
                trafficMeterAddress,
                currentBucketBytesOffset,
                $"TrafficMeterConnection.{directionName}CurrentBucketBytes",
                out var currentBucketBytes,
                out error) ||
            !TryReadUInt32(
                memory,
                trafficMeterAddress,
                bucketStartedAtOffset,
                $"TrafficMeterConnection.{directionName}BucketStartedAt",
                out var bucketStartedAt,
                out error) ||
            !TryReadPointer(
                memory,
                trafficMeterAddress,
                historyAddressOffset,
                $"TrafficMeterConnection.{directionName}History",
                out var historyAddress,
                out error) ||
            !TryReadUInt32(
                memory,
                trafficMeterAddress,
                bucketCountOffset,
                $"TrafficMeterConnection.{directionName}BucketCount",
                out var bucketCountRaw,
                out error) ||
            !TryReadUInt32(
                memory,
                trafficMeterAddress,
                writeIndexOffset,
                $"TrafficMeterConnection.{directionName}WriteIndex",
                out var writeIndexRaw,
                out error))
        {
            return false;
        }

        if (historyAddress == 0)
        {
            error =
                $"TrafficMeterConnection.{directionName}History is null";

            return false;
        }

        if (bucketCountRaw !=
            ClientNetworkTrafficObservation.ExpectedBucketCount)
        {
            error = string.Create(
                CultureInfo.InvariantCulture,
                $"Unexpected {directionName.ToLowerInvariant()} bucket count {bucketCountRaw}; expected {ClientNetworkTrafficObservation.ExpectedBucketCount}");

            return false;
        }

        if (writeIndexRaw >= bucketCountRaw)
        {
            error = $"Invalid {directionName.ToLowerInvariant()} write index {writeIndexRaw} for {bucketCountRaw} buckets";

            return false;
        }

        var bucketCount = checked((int)bucketCountRaw);
        var byteCount = checked(bucketCount * sizeof(uint));

        if (!memory.TryReadBytes(
                historyAddress,
                byteCount,
                out var historyBytes))
        {
            error = $"Could not read {directionName.ToLowerInvariant()} traffic buckets at {FormatPointer(historyAddress)}";

            return false;
        }

        var buckets = new uint[bucketCount];

        for (var index = 0;
             index < bucketCount;
             index++)
        {
            buckets[index] =
                BitConverter.ToUInt32(
                    historyBytes,
                    index * sizeof(uint));
        }

        direction =
            new MeterDirection(
                currentBucketBytes,
                bucketStartedAt,
                historyAddress,
                checked((int)writeIndexRaw),
                buckets);

        error = "";
        return true;
    }

    private static uint CalculateAverage(
        IReadOnlyList<uint> buckets)
    {
        ulong total = 0;

        foreach (var bucket in buckets)
        {
            total += bucket;
        }

        return checked(
            (uint)(total / (uint)buckets.Count));
    }

    private static bool TryReadPointer(
        ProcessMemoryReader memory,
        uint baseAddress,
        uint offset,
        string fieldName,
        out uint value,
        out string error)
    {
        return TryReadUInt32(
            memory,
            baseAddress,
            offset,
            fieldName,
            out value,
            out error);
    }

    private static bool TryReadUInt32(
        ProcessMemoryReader memory,
        uint baseAddress,
        uint offset,
        string fieldName,
        out uint value,
        out string error)
    {
        uint address;

        try
        {
            address = checked(
                baseAddress + offset);
        }
        catch (OverflowException)
        {
            value = 0;
            error =
                $"Address overflow while reading {fieldName}";

            return false;
        }

        if (!memory.TryReadUInt32(
                address,
                out value))
        {
            error = $"Could not read {fieldName} at {FormatPointer(address)}";

            return false;
        }

        error = "";
        return true;
    }

    private static string FormatPointer(
        uint address)
    {
        return address == 0
            ? "None"
            : $"0x{address:X8}";
    }

    private readonly record struct MeterDirection(
        uint CurrentBucketBytes,
        uint BucketStartedAt,
        uint HistoryAddress,
        int WriteIndex,
        IReadOnlyList<uint> Buckets);
}
