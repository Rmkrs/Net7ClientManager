namespace Net7ClientManager.Observations.Observers;

using System.Collections.Concurrent;
using Net7ClientManager.Observations.Models;

/// <summary>
/// Resolves combat packet ObjectIds outside the high-frequency packet loop.
///
/// Unknown actors schedule one short-lived background lookup. Each lookup owns
/// its own read handle, so AuxData/string reconstruction cannot stall or share
/// the coordinator's hot ProcessMemoryReader. Resolved strings remain cached
/// for the active SClient/sector session after the native object despawns.
/// </summary>
internal sealed class ClientCombatIdentityResolver : IDisposable
{
    private const uint ClientObjectAuxData = 0x88;
    private const uint ClientObjectType = 0x94;

    private const int MaximumPendingIdentityCount = 2048;
    private const int MaximumResolutionAttempts = 5;
    private const int ResolutionRetryDelayMilliseconds = 25;

    private readonly Lock sessionLock = new();

    private readonly Dictionary<int, IdentityProcessSession> sessions = [];

    private readonly ClientObjectResolver objectResolver =
        new();

    private readonly ClientMapIdentityReader mapIdentityReader =
        new();

    private bool disposed;

    public void RefreshTarget(
        int processId,
        uint moduleBaseAddress,
        uint clientContextAddress,
        uint activeSectorNumber,
        bool isAvailable)
    {
        lock (this.sessionLock)
        {
            if (this.disposed)
            {
                return;
            }

            if (!isAvailable ||
                moduleBaseAddress == 0 ||
                clientContextAddress == 0)
            {
                this.RemoveSessionLocked(
                    processId);

                return;
            }

            if (this.sessions.TryGetValue(
                    processId,
                    out var existing) &&
                existing.Matches(
                    moduleBaseAddress,
                    clientContextAddress,
                    activeSectorNumber))
            {
                return;
            }

            this.RemoveSessionLocked(
                processId);

            this.sessions[processId] =
                new IdentityProcessSession(
                    processId,
                    moduleBaseAddress,
                    clientContextAddress,
                    activeSectorNumber);
        }
    }

    public void Request(
        int processId,
        params uint[] objectIds)
    {
        IdentityProcessSession? session;

        lock (this.sessionLock)
        {
            if (this.disposed ||
                !this.sessions.TryGetValue(
                    processId,
                    out session))
            {
                return;
            }
        }

        foreach (var objectId in objectIds)
        {
            if (ClientObjectResolver.IsAbsentObjectId(
                    objectId) ||
                session.Identities.ContainsKey(
                    objectId) ||
                session.PendingObjectIds.Count >=
                    MaximumPendingIdentityCount ||
                !session.PendingObjectIds.TryAdd(
                    objectId,
                    0))
            {
                continue;
            }

            try
            {
                _ = Task.Run(
                    () => this.Resolve(
                        session,
                        objectId),
                    session.CancellationToken);
            }
            catch
            {
                session.PendingObjectIds.TryRemove(
                    objectId,
                    out _);

                session.IncrementResolutionFailureCount();
            }
        }
    }

    public ClientCombatIdentityResolverSnapshot GetSnapshot(
        int processId)
    {
        IdentityProcessSession? session;

        lock (this.sessionLock)
        {
            if (this.disposed ||
                !this.sessions.TryGetValue(
                    processId,
                    out session))
            {
                return ClientCombatIdentityResolverSnapshot.Empty;
            }
        }

        return new ClientCombatIdentityResolverSnapshot(
            new Dictionary<uint, ClientCombatActorIdentityObservation>(
                session.Identities),
            session.PendingObjectIds.Count,
            session.ReadResolutionFailureCount());
    }

    public void RemoveProcess(
        int processId)
    {
        lock (this.sessionLock)
        {
            this.RemoveSessionLocked(
                processId);
        }
    }

    public void Dispose()
    {
        lock (this.sessionLock)
        {
            if (this.disposed)
            {
                return;
            }

            this.disposed = true;

            foreach (var session in this.sessions.Values)
            {
                session.Dispose();
            }

            this.sessions.Clear();
        }
    }

    private void Resolve(
        IdentityProcessSession session,
        uint objectId)
    {
        ClientCombatActorIdentityObservation? identity = null;
        var lastError = "Identity could not be resolved";

        try
        {
            using var memory = ProcessMemoryReader.Open(
                session.ProcessId);

            var separator =
                this.mapIdentityReader
                    .ReadMapDisplayNameSeparator(
                        memory,
                        session.ModuleBaseAddress);

            for (var attempt = 1;
                 attempt <= MaximumResolutionAttempts;
                 attempt++)
            {
                session.CancellationToken.ThrowIfCancellationRequested();

                if (this.TryResolve(
                        memory,
                        session,
                        objectId,
                        separator,
                        out identity,
                        out lastError))
                {
                    break;
                }

                if (attempt < MaximumResolutionAttempts &&
                    session.CancellationToken.WaitHandle.WaitOne(
                        ResolutionRetryDelayMilliseconds))
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            lastError = ex.Message;
        }

        lock (this.sessionLock)
        {
            if (this.disposed ||
                !this.sessions.TryGetValue(
                    session.ProcessId,
                    out var current) ||
                !ReferenceEquals(
                    current,
                    session))
            {
                return;
            }

            session.Identities[objectId] =
                identity ??
                ClientCombatActorIdentityObservation.Unresolved(
                    objectId,
                    lastError);

            session.PendingObjectIds.TryRemove(
                objectId,
                out _);

            if (identity == null)
            {
                session.IncrementResolutionFailureCount();
            }
        }
    }

    private bool TryResolve(
        ProcessMemoryReader memory,
        IdentityProcessSession session,
        uint objectId,
        string separator,
        out ClientCombatActorIdentityObservation? identity,
        out string error)
    {
        identity = null;
        error = "";

        if (!this.objectResolver.TryLookupClientObject(
                memory,
                session.ClientContextAddress,
                objectId,
                out var clientObjectAddress,
                out error,
                out _))
        {
            return false;
        }

        if (!memory.TryReadUInt32(
                checked(
                    clientObjectAddress +
                    ClientObjectAuxData),
                out var auxDataAddress) ||
            auxDataAddress == 0)
        {
            error = $"Object {objectId} has no readable ObjectAuxData";

            return false;
        }

        if (!memory.TryReadBytes(
                checked(
                    clientObjectAddress +
                    ClientObjectType),
                1,
                out var objectTypeBytes))
        {
            error = $"Could not read object type for {objectId}";

            return false;
        }

        var rawObjectType =
            objectTypeBytes[0];

        var result = this.mapIdentityReader.Read(
            memory,
            session.ModuleBaseAddress,
            auxDataAddress,
            rawObjectType,
            separator);

        var displayName =
            result.MapDisplayName.Trim();

        if (string.IsNullOrWhiteSpace(
                displayName) ||
            string.Equals(
                displayName,
                "Unknown",
                StringComparison.OrdinalIgnoreCase))
        {
            error = $"Object {objectId} identity was not ready: {result.Status}";

            return false;
        }

        identity = new ClientCombatActorIdentityObservation
        {
            IsResolved = true,
            ObjectId = objectId,
            RawObjectType = rawObjectType,
            DisplayName = displayName,
            Name = result.Name,
            Owner = result.Owner,
            Title = result.Title,
            Rank = result.Rank,
            Status = result.Status,
        };

        return true;
    }

    private void RemoveSessionLocked(
        int processId)
    {
        if (!this.sessions.Remove(
                processId,
                out var session))
        {
            return;
        }

        session.Dispose();
    }

    private sealed class IdentityProcessSession(
        int processId,
        uint moduleBaseAddress,
        uint clientContextAddress,
        uint activeSectorNumber)
        : IDisposable
    {
        private readonly CancellationTokenSource
            cancellationTokenSource =
                new();

        public int ProcessId { get; } = processId;

        public uint ModuleBaseAddress { get; } = moduleBaseAddress;

        public uint ClientContextAddress { get; } = clientContextAddress;

        public uint ActiveSectorNumber { get; } = activeSectorNumber;

        public CancellationToken CancellationToken =>
            this.cancellationTokenSource.Token;

        public ConcurrentDictionary<uint, ClientCombatActorIdentityObservation>
            Identities
        { get; } = [];

        public ConcurrentDictionary<uint, byte> PendingObjectIds { get; } = [];

        private long resolutionFailureCount;

        public void IncrementResolutionFailureCount()
        {
            Interlocked.Increment(
                ref this.resolutionFailureCount);
        }

        public long ReadResolutionFailureCount()
        {
            return Interlocked.Read(
                ref this.resolutionFailureCount);
        }

        public bool Matches(
            uint moduleBaseAddress,
            uint clientContextAddress,
            uint activeSectorNumber)
        {
            return this.ModuleBaseAddress == moduleBaseAddress &&
                   this.ClientContextAddress == clientContextAddress &&
                   this.ActiveSectorNumber == activeSectorNumber;
        }

        public void Dispose()
        {
            this.cancellationTokenSource.Cancel();
            this.cancellationTokenSource.Dispose();
        }
    }
}
