namespace Net7ClientManager.Services;

/// <summary>
/// Process-wide foreground-input gate shared by all deterministic game input
/// services. Observation remains concurrent; only the short phase that owns
/// focus, keyboard or mouse input is serialized.
/// </summary>
internal sealed class ForegroundInputCoordinator : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private bool disposed;

    public async Task<IDisposable> AcquireAsync(
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(
            this.disposed,
            this);

        await this.gate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        return new Releaser(this.gate);
    }

    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        this.gate.Dispose();
    }

    private sealed class Releaser(
        SemaphoreSlim gate)
        : IDisposable
    {
        private SemaphoreSlim? gate = gate;

        public void Dispose()
        {
            Interlocked.Exchange(
                    ref this.gate,
                    value: null)
                ?.Release();
        }
    }
}
