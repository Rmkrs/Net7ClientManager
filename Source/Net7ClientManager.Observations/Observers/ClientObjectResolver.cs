namespace Net7ClientManager.Observations.Observers;

using System.Globalization;
using Net7ClientManager.Observations.Models;

internal sealed class ClientObjectResolver
{
    private const uint ClientContextObjectRegistry = 0x38;
    private const uint ClientContextLocalPlayerObjectId = 0x112c;

    private const uint ObjectRegistryBucketCount = 0x101;
    private const uint ObjectRegistryNodeNext = 0x04;
    private const uint ObjectRegistryNodeClientObject = 0x10;
    private const uint ObjectRegistryNodeObjectId = 0x14;
    private const uint ObjectRegistryNodeFlags = 0x18;

    private const uint ClientObjectAuxData = 0x88;
    private const uint ClientObjectObjectId = 0x90;

    private const uint ShipAuxTargetGameId = 0x130c;

    private const int MaxObjectRegistryChainLength = 4096;

    public bool TryResolveLocalPlayerClientObject(
        ProcessMemoryReader memory,
        uint clientContextAddress,
        out uint clientObjectAddress,
        out string error,
        out uint errorAddress)
    {
        clientObjectAddress = 0;
        error = "";
        errorAddress = 0;

        uint localPlayerObjectIdAddress;

        try
        {
            localPlayerObjectIdAddress = checked(
                clientContextAddress +
                ClientContextLocalPlayerObjectId);
        }
        catch (OverflowException)
        {
            error = "Local player ObjectId address overflow";
            return false;
        }

        if (!memory.TryReadUInt32(
                localPlayerObjectIdAddress,
                out var localPlayerObjectId))
        {
            errorAddress =
                localPlayerObjectIdAddress;

            error = $"Could not read local player ObjectId at 0x{errorAddress:X8}";

            return false;
        }

        if (IsAbsentObjectId(
                localPlayerObjectId))
        {
            errorAddress =
                localPlayerObjectIdAddress;

            error =
                "Local player ObjectId is absent";

            return false;
        }

        return this.TryLookupClientObject(
            memory,
            clientContextAddress,
            localPlayerObjectId,
            out clientObjectAddress,
            out error,
            out errorAddress);
    }

    public bool TryReadCurrentTargetSource(
        ProcessMemoryReader memory,
        uint clientContextAddress,
        out CurrentTargetSource source,
        out string error,
        out uint errorAddress)
    {
        source = default;
        error = "";
        errorAddress = 0;

        if (!this.TryResolveLocalPlayerClientObject(
                memory,
                clientContextAddress,
                out var localClientObjectAddress,
                out error,
                out errorAddress))
        {
            return false;
        }

        uint shipAuxPointerAddress;

        try
        {
            shipAuxPointerAddress = checked(
                localClientObjectAddress +
                ClientObjectAuxData);
        }
        catch (OverflowException)
        {
            error = "Local ShipAuxData pointer address overflow";
            return false;
        }

        if (!memory.TryReadUInt32(
                shipAuxPointerAddress,
                out var localShipAuxDataAddress))
        {
            errorAddress = shipAuxPointerAddress;

            error = $"Could not read local ShipAuxData pointer at 0x{errorAddress:X8}";

            return false;
        }

        if (localShipAuxDataAddress == 0)
        {
            errorAddress = shipAuxPointerAddress;
            error = "Local ShipAuxData pointer was null";

            return false;
        }

        uint targetObjectIdAddress;

        try
        {
            targetObjectIdAddress = checked(
                localShipAuxDataAddress +
                ShipAuxTargetGameId);
        }
        catch (OverflowException)
        {
            error = "TargetGameID address overflow";
            return false;
        }

        if (!memory.TryReadUInt32(
                targetObjectIdAddress,
                out var targetObjectId))
        {
            errorAddress = targetObjectIdAddress;

            error = $"Could not read TargetGameID at 0x{errorAddress:X8}";

            return false;
        }

        source = new CurrentTargetSource(
            localClientObjectAddress,
            localShipAuxDataAddress,
            targetObjectIdAddress,
            targetObjectId);

        return true;
    }

    public bool TryLookupClientObject(
        ProcessMemoryReader memory,
        uint clientContextAddress,
        uint objectId,
        out uint clientObjectAddress,
        out string error,
        out uint errorAddress)
    {
        clientObjectAddress = 0;
        error = "";
        errorAddress = 0;

        if (IsAbsentObjectId(objectId))
        {
            error = "ObjectId is absent";
            return false;
        }

        var bucketIndex =
            objectId %
            ObjectRegistryBucketCount;

        uint bucketAddress;

        try
        {
            bucketAddress = checked(
                clientContextAddress +
                ClientContextObjectRegistry +
                bucketIndex * sizeof(uint));
        }
        catch (OverflowException)
        {
            error = $"Object registry bucket address overflow for ObjectId {objectId}";

            return false;
        }

        if (!memory.TryReadUInt32(
                bucketAddress,
                out var nodeAddress))
        {
            errorAddress = bucketAddress;

            error = $"Could not read object registry bucket {bucketIndex} at 0x{bucketAddress:X8}";

            return false;
        }

        if (nodeAddress == 0)
        {
            errorAddress = bucketAddress;

            error = $"Object {objectId} was not present in empty registry bucket {bucketIndex}";

            return false;
        }

        var visited = new HashSet<uint>();

        for (var index = 0;
             index < MaxObjectRegistryChainLength &&
             nodeAddress != 0;
             index++)
        {
            if (!visited.Add(nodeAddress))
            {
                errorAddress = nodeAddress;

                error = $"Cycle detected in object registry bucket {bucketIndex} at 0x{nodeAddress:X8}";

                return false;
            }

            if (!TryReadObjectRegistryNode(
                    memory,
                    nodeAddress,
                    out var node))
            {
                errorAddress = nodeAddress;

                error = $"Could not read object registry node at 0x{nodeAddress:X8}";

                return false;
            }

            if (node.ObjectId == objectId)
            {
                if (node.ClientObjectAddress == 0)
                {
                    errorAddress = checked(
                        nodeAddress +
                        ObjectRegistryNodeClientObject);

                    error = $"Registry node for object {objectId} contained a null ClientObject";

                    return false;
                }

                var clientObjectIdAddress = checked(
                    node.ClientObjectAddress +
                    ClientObjectObjectId);

                if (!memory.TryReadUInt32(
                        clientObjectIdAddress,
                        out var clientObjectId))
                {
                    errorAddress =
                        clientObjectIdAddress;

                    error = $"Could not validate ClientObject ObjectId at 0x{clientObjectIdAddress:X8}";

                    return false;
                }

                if (clientObjectId != objectId)
                {
                    errorAddress =
                        clientObjectIdAddress;

                    error = $"Registry node ID {objectId} resolved to ClientObject ID {clientObjectId}";

                    return false;
                }

                clientObjectAddress =
                    node.ClientObjectAddress;

                return true;
            }

            nodeAddress = node.NextAddress;
        }

        if (nodeAddress != 0)
        {
            errorAddress = nodeAddress;

            error = string.Create(
                CultureInfo.InvariantCulture,
                $"Object registry bucket {bucketIndex} exceeded {MaxObjectRegistryChainLength} nodes");

            return false;
        }

        errorAddress = bucketAddress;

        error = $"Object {objectId} was not found in registry bucket {bucketIndex}";

        return false;
    }

    public static bool IsAbsentObjectId(
        uint objectId)
    {
        return objectId is 0 or uint.MaxValue;
    }

    private static bool TryReadObjectRegistryNode(
        ProcessMemoryReader memory,
        uint nodeAddress,
        out ObjectRegistryNode node)
    {
        node = default;

        if (!memory.TryReadUInt32(
                nodeAddress +
                ObjectRegistryNodeNext,
                out var nextAddress) ||
            !memory.TryReadUInt32(
                nodeAddress +
                ObjectRegistryNodeClientObject,
                out var clientObjectAddress) ||
            !memory.TryReadUInt32(
                nodeAddress +
                ObjectRegistryNodeObjectId,
                out var objectId) ||
            !memory.TryReadUInt32(
                nodeAddress +
                ObjectRegistryNodeFlags,
                out var flags))
        {
            return false;
        }

        node = new ObjectRegistryNode(
            nextAddress,
            clientObjectAddress,
            objectId,
            flags);

        return true;
    }
}
