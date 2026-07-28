namespace Net7ClientManager.Addons.Runtime;

using Net7ClientManager.Addons.Contracts;

public interface ILuaRuntime : IAsyncDisposable
{
    event EventHandler<AddonLogEntryEventArgs>? LogEntryWritten;

    event EventHandler<AddonUiCommandEventArgs>? UiCommandEmitted;

    void UpdateGameSnapshot(AddonGameSnapshot snapshot);

    ValueTask<LuaExecutionResult> StartAsync(
        string source,
        string chunkName,
        TimeSpan executionTimeout,
        CancellationToken cancellationToken = default);

    ValueTask<LuaExecutionResult> StopAsync(
        TimeSpan executionTimeout,
        CancellationToken cancellationToken = default);

    ValueTask<LuaExecutionResult> ExecuteAsync(
        string source,
        string chunkName,
        TimeSpan executionTimeout,
        CancellationToken cancellationToken = default);

    ValueTask<LuaExecutionResult> RaiseEventAsync(
        AddonGameEvent gameEvent,
        TimeSpan executionTimeout,
        CancellationToken cancellationToken = default);

    ValueTask<LuaExecutionResult> RaiseUiInteractionAsync(
        AddonUiInteraction interaction,
        TimeSpan executionTimeout,
        CancellationToken cancellationToken = default);
}


