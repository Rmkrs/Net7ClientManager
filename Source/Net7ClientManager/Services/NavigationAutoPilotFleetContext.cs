namespace Net7ClientManager.Services;

using Net7ClientManager.Forms;
using Net7ClientManager.Models;

/// <summary>
/// Frozen managed-client membership for one Auto Pilot journey. Runtime
/// identity and group membership are discovered from live observations when
/// the journey starts; configured account or character data never participates.
/// </summary>
internal sealed class NavigationAutoPilotFleetContext : IDisposable
{
    private bool disposed;

    public NavigationAutoPilotFleetContext(
        NavigationAutoPilotFleetParticipant leader,
        IReadOnlyList<NavigationAutoPilotFleetParticipant> followers)
    {
        this.Leader = leader;
        this.Followers = followers;
        this.Participants = [leader, .. followers];
    }

    public NavigationAutoPilotFleetParticipant Leader { get; }

    public IReadOnlyList<NavigationAutoPilotFleetParticipant> Followers
    { get; }

    public IReadOnlyList<NavigationAutoPilotFleetParticipant> Participants
    { get; }

    public bool HasFollowers => this.Followers.Count > 0;

    public long PreparedLeaderGenerationSequence { get; set; }

    public bool ContainsProcess(int processId)
    {
        return this.Participants.Any(
            participant => participant.Client.ProcessId == processId);
    }

    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;

        foreach (var participant in this.Participants)
        {
            participant.Dispose();
        }
    }
}

internal sealed class NavigationAutoPilotFleetParticipant(
    ClientInstance client,
    ClientHostForm hostForm,
    string liveName,
    int groupSlot,
    IDisposable navigationObservationLease) : IDisposable
{
    private IDisposable? navigationObservationLeaseHandle =
        navigationObservationLease;

    public ClientInstance Client { get; } = client;

    public ClientHostForm HostForm { get; } = hostForm;

    public string LiveName { get; } = liveName;

    public int GroupSlot { get; } = groupSlot;

    public void Dispose()
    {
        Interlocked.Exchange(
                ref this.navigationObservationLeaseHandle,
                null)
            ?.Dispose();
    }
}
