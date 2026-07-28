namespace Net7ClientManager.Addons.Runtime;

using System.Threading.Channels;

internal sealed class AddonWorkScheduler : IAsyncDisposable
{
    private readonly Channel<Func<CancellationToken, ValueTask>> channel =
        Channel.CreateUnbounded<Func<CancellationToken, ValueTask>>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false,
            });

    private readonly CancellationTokenSource cancellationTokenSource = new();
    private readonly Task workerTask;
    private bool disposed;

    public AddonWorkScheduler()
    {
        this.workerTask = Task.Run(this.RunAsync);
    }

    public Task EnqueueAsync(
        Func<CancellationToken, ValueTask> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        this.EnqueueCore(
            async schedulerToken =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    completion.TrySetCanceled(cancellationToken);
                    return;
                }

                try
                {
                    await action(schedulerToken).ConfigureAwait(false);
                    completion.TrySetResult(true);
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    completion.TrySetCanceled(
                        cancellationToken);
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            });

        return completion.Task;
    }

    public Task<T> EnqueueAsync<T>(
        Func<CancellationToken, ValueTask<T>> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        var completion = new TaskCompletionSource<T>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        this.EnqueueCore(
            async schedulerToken =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    completion.TrySetCanceled(cancellationToken);
                    return;
                }

                try
                {
                    var result = await action(schedulerToken)
                        .ConfigureAwait(false);

                    completion.TrySetResult(result);
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    completion.TrySetCanceled(
                        cancellationToken);
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            });

        return completion.Task;
    }

    public void EnqueueFireAndForget(
        Func<CancellationToken, ValueTask> action,
        Action<Exception>? errorHandler = null)
    {
        ArgumentNullException.ThrowIfNull(action);

        this.EnqueueCore(
            async schedulerToken =>
            {
                try
                {
                    await action(schedulerToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                    when (schedulerToken.IsCancellationRequested)
                {
                }
                catch (Exception ex)
                {
                    errorHandler?.Invoke(ex);
                }
            });
    }

    public async ValueTask DisposeAsync()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        this.channel.Writer.TryComplete();

        try
        {
            await this.workerTask.ConfigureAwait(false);
        }
        finally
        {
            this.cancellationTokenSource.Cancel();
            this.cancellationTokenSource.Dispose();
        }
    }

    private void EnqueueCore(
        Func<CancellationToken, ValueTask> action)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);

        if (!this.channel.Writer.TryWrite(action))
        {
            throw new InvalidOperationException(
                "The addon work scheduler is not accepting work.");
        }
    }

    private async Task RunAsync()
    {
        try
        {
            await foreach (var action in this.channel.Reader.ReadAllAsync(
                               this.cancellationTokenSource.Token))
            {
                await action(this.cancellationTokenSource.Token)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
            when (this.cancellationTokenSource.IsCancellationRequested)
        {
        }
    }
}

