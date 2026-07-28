namespace Net7ClientManager.Addons.Contracts;

public delegate ValueTask<AddonCommandResult> AddonActionDispatcher(
    AddonActionRequest request,
    CancellationToken cancellationToken);
