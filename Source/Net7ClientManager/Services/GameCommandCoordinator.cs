namespace Net7ClientManager.Services;

using Net7ClientManager.Models;
using Net7ClientManager.Win32;

/// <summary>
/// Shared semantic command executor. Callers can request only catalogued game
/// actions; raw configurable input bindings remain inside the resolver.
/// </summary>
internal sealed class GameCommandCoordinator(
    GameKeyBindingResolver bindingResolver,
    ForegroundInputCoordinator foregroundInputCoordinator)
{
    public async Task<GameCommandExecutionResult> ExecuteAsync(
        ClientInstance client,
        GameCommand command,
        CancellationToken cancellationToken)
    {
        var startResult = await this.BeginSessionAsync(
                client,
                [command],
                cancellationToken)
            .ConfigureAwait(false);

        var session = startResult.Session;

        if (session == null)
        {
            return GameCommandExecutionResult.Failure(
                startResult.Error,
                startResult.Binding);
        }

        using (session)
        {
            return await session
                .ExecuteAsync(command, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async Task<GameCommandExecutionResult> ExecuteWithHeldCommandAsync(
        ClientInstance client,
        GameCommand tapCommand,
        GameCommand heldCommand,
        Func<CancellationToken, Task<bool>>? waitAfterHeldCommandAsync,
        CancellationToken cancellationToken)
    {
        var startResult = await this.BeginSessionAsync(
                client,
                [tapCommand, heldCommand],
                cancellationToken)
            .ConfigureAwait(false);

        var session = startResult.Session;

        if (session == null)
        {
            return GameCommandExecutionResult.Failure(
                startResult.Error,
                startResult.Binding);
        }

        using (session)
        {
            return await session
                .ExecuteWithHeldCommandAsync(
                    tapCommand,
                    heldCommand,
                    waitAfterHeldCommandAsync,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async Task<GameCommandSessionStartResult> BeginSessionAsync(
        ClientInstance client,
        IReadOnlyCollection<GameCommand> commands,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(commands);

        if (client.GameWindowHandle == IntPtr.Zero)
        {
            return GameCommandSessionStartResult.Failure(
                "The game window is unavailable.");
        }

        var requestedCommands = commands
            .Distinct()
            .ToArray();

        if (requestedCommands.Length == 0)
        {
            return GameCommandSessionStartResult.Failure(
                "No semantic game commands were requested.");
        }

        Dictionary<GameCommand, GameCommandBindingResolution> bindings = [];

        foreach (var command in requestedCommands)
        {
            var binding = await bindingResolver
                .ResolveAsync(
                    client,
                    command,
                    cancellationToken)
                .ConfigureAwait(false);

            if (!binding.Succeeded || binding.Preferred == null)
            {
                return GameCommandSessionStartResult.Failure(
                    binding.Error,
                    binding);
            }

            bindings[command] = binding;
        }

        var foregroundLease =
            await foregroundInputCoordinator
                .AcquireAsync(cancellationToken)
                .ConfigureAwait(false);

        if (client.GameWindowHandle == IntPtr.Zero)
        {
            foregroundLease.Dispose();

            return GameCommandSessionStartResult.Failure(
                "The game window became unavailable.");
        }

        return GameCommandSessionStartResult.Success(
            new GameCommandSession(
                client,
                bindings,
                foregroundLease));
    }

    internal sealed class GameCommandSession(
        ClientInstance client,
        IReadOnlyDictionary<GameCommand, GameCommandBindingResolution>
            bindings,
        IDisposable lease) : IDisposable
    {
        private IDisposable? foregroundLease = lease;

        public async Task<GameCommandExecutionResult> ExecuteAsync(
            GameCommand command,
            CancellationToken cancellationToken)
        {
            if (this.foregroundLease == null)
            {
                return GameCommandExecutionResult.Failure(
                    "The foreground input session is no longer active.");
            }

            if (!bindings.TryGetValue(command, out var binding) ||
                binding.Preferred == null)
            {
                return GameCommandExecutionResult.Failure(
                    $"The input session did not prepare '{command}'.");
            }

            if (client.GameWindowHandle == IntPtr.Zero)
            {
                return GameCommandExecutionResult.Failure(
                    "The game window became unavailable.",
                    binding);
            }

            var sent = await NativeMethods
                .TryForegroundTapChordAsync(
                    client.GameWindowHandle,
                    binding.Preferred,
                    cancellationToken)
                .ConfigureAwait(false);

            return sent
                ? GameCommandExecutionResult.Success(binding)
                : GameCommandExecutionResult.Failure(
                    $"Could not focus the game window to send {binding.Preferred.DisplayText} for '{binding.DefinitionName}'.",
                    binding);
        }

        public async Task<GameCommandExecutionResult> ExecuteWithHeldCommandAsync(
            GameCommand tapCommand,
            GameCommand heldCommand,
            Func<CancellationToken, Task<bool>>? waitAfterHeldCommandAsync,
            CancellationToken cancellationToken)
        {
            if (this.foregroundLease == null)
            {
                return GameCommandExecutionResult.Failure(
                    "The foreground input session is no longer active.");
            }

            if (!bindings.TryGetValue(tapCommand, out var tapBinding) ||
                tapBinding.Preferred == null)
            {
                return GameCommandExecutionResult.Failure(
                    $"The input session did not prepare '{tapCommand}'.");
            }

            if (!bindings.TryGetValue(heldCommand, out var heldBinding) ||
                heldBinding.Preferred == null)
            {
                return GameCommandExecutionResult.Failure(
                    $"The input session did not prepare '{heldCommand}'.",
                    heldBinding);
            }

            if (client.GameWindowHandle == IntPtr.Zero)
            {
                return GameCommandExecutionResult.Failure(
                    "The game window became unavailable.",
                    tapBinding);
            }

            var sent = await NativeMethods
                .TryForegroundTapChordWithHeldChordAsync(
                    client.GameWindowHandle,
                    tapBinding.Preferred,
                    heldBinding.Preferred,
                    waitAfterHeldCommandAsync,
                    cancellationToken)
                .ConfigureAwait(false);

            return sent
                ? GameCommandExecutionResult.Success(tapBinding)
                : GameCommandExecutionResult.Failure(
                    $"Could not focus the game window to hold {heldBinding.Preferred.DisplayText} and send {tapBinding.Preferred.DisplayText} for '{tapBinding.DefinitionName}'.",
                    tapBinding);
        }

        public void Dispose()
        {
            Interlocked.Exchange(
                    ref this.foregroundLease,
                    value: null)
                ?.Dispose();
        }
    }

    internal sealed record GameCommandSessionStartResult
    {
        public GameCommandSession? Session { get; init; }

        public string Error { get; init; } = "";

        public GameCommandBindingResolution? Binding { get; init; }

        public static GameCommandSessionStartResult Success(
            GameCommandSession session)
        {
            return new GameCommandSessionStartResult
            {
                Session = session,
            };
        }

        public static GameCommandSessionStartResult Failure(
            string error,
            GameCommandBindingResolution? binding = null)
        {
            return new GameCommandSessionStartResult
            {
                Error = error,
                Binding = binding,
            };
        }
    }
}
