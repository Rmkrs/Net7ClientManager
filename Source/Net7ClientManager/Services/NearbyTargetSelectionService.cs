namespace Net7ClientManager.Services;

using Net7ClientManager.Addons.Contracts;
using Net7ClientManager.Forms;
using Net7ClientManager.Models;
using Net7ClientManager.Observations;
using Net7ClientManager.Observations.Models;
using Net7ClientManager.Win32;

internal sealed class NearbyTargetSelectionService(
    ClientObservationCoordinator observationCoordinator,
    ForegroundInputCoordinator foregroundInputCoordinator)
{
    private static readonly TimeSpan hoverTimeout =
        TimeSpan.FromMilliseconds(milliseconds: 500);

    private static readonly TimeSpan confirmationTimeout =
        TimeSpan.FromMilliseconds(milliseconds: 850);

    private static readonly TimeSpan pollInterval =
        TimeSpan.FromMilliseconds(milliseconds: 15);

    private static readonly TimeSpan clickHoldDuration =
        TimeSpan.FromMilliseconds(milliseconds: 35);

    private static readonly TimeSpan retryDelay =
        TimeSpan.FromMilliseconds(milliseconds: 40);

    private static readonly Point[] hoverProbeOffsets =
    [
        new(x: 0, y: 0),
        new(x: -2, y: 0),
        new(x: 2, y: 0),
        new(x: 0, y: -2),
        new(x: 0, y: 2),
        new(x: -2, y: -2),
        new(x: 2, y: -2),
        new(x: -2, y: 2),
        new(x: 2, y: 2),
        new(x: -4, y: 0),
        new(x: 4, y: 0),
        new(x: 0, y: -4),
        new(x: 0, y: 4),
    ];

    private const int CursorLeaseTolerancePixels = 4;
    private const int RequiredStableHoverSamples = 2;
    private const int MaximumClickAttempts = 2;

    public async ValueTask<AddonCommandResult> SelectAsync(
        ClientInstance client,
        ClientHostForm hostForm,
        AddonActionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(argument: client);
        ArgumentNullException.ThrowIfNull(argument: hostForm);
        ArgumentNullException.ThrowIfNull(argument: request);

        if (request.Kind != AddonActionKind.SelectNearbyTarget)
        {
            return AddonCommandResult.Failure(
                error: "Unsupported addon action");
        }

        if (request.ObjectId == 0)
        {
            return AddonCommandResult.Failure(
                error: "The target object id is invalid");
        }

        if (client.GameWindowHandle == IntPtr.Zero ||
            hostForm.IsDisposed ||
            hostForm.Disposing)
        {
            return AddonCommandResult.Failure(
                error: "The hosted game client is unavailable");
        }

        if (!this.TryReadActionState(
                processId: client.ProcessId,
                request: request,
                state: out var state,
                error: out var stateError))
        {
            return AddonCommandResult.Failure(error: stateError);
        }

        if (state.IsSelected)
        {
            return AddonCommandResult.Success();
        }

        if (!NativeMethods.TryGetClientSize(
                windowHandle: client.GameWindowHandle,
                size: out var clientSize) ||
            clientSize.Width <= 0 ||
            clientSize.Height <= 0)
        {
            return AddonCommandResult.Failure(
                error: "Could not resolve the hosted game viewport");
        }

        using var foregroundLease =
            await foregroundInputCoordinator
                .AcquireAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(continueOnCapturedContext: false);

        if (!NativeMethods.TryGetCursorScreenPosition(
                point: out var originalCursorPosition))
        {
            return AddonCommandResult.Failure(
                error: "Could not read the current mouse position");
        }

        var commandedCursorPosition = Point.Empty;
        var ownsCursorPosition = false;
        var mouseButtonDown = false;
        var playerMovedAfterClick = false;
        var verifiedClickCount = 0;
        var lastFailure =
            "The game did not confirm the requested target selection";

        await hostForm
            .SetAddonOverlayInputSuppressedAsync(suppressed: true)
            .ConfigureAwait(continueOnCapturedContext: false);

        try
        {
            NativeMethods.FocusWindow(windowHandle: client.GameWindowHandle);

            await Task.Delay(
                    delay: TimeSpan.FromMilliseconds(milliseconds: 20),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(continueOnCapturedContext: false);

            if (!CursorRemainsLeased(expectedPosition: originalCursorPosition))
            {
                return AddonCommandResult.Failure(
                    error: "Target selection was cancelled because the mouse moved");
            }

            for (var attempt = 1;
                 attempt <= MaximumClickAttempts;
                 attempt++)
            {
                var hover = await this.AcquireHoveredTargetAsync(
                        client: client,
                        request: request,
                        clientSize: clientSize,
                        currentCommandedPosition: commandedCursorPosition,
                        currentlyOwnsCursor: ownsCursorPosition,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(continueOnCapturedContext: false);

                commandedCursorPosition = hover.CursorPosition;
                ownsCursorPosition = hover.OwnsCursorPosition;

                if (hover.IsSelected)
                {
                    return AddonCommandResult.Success();
                }

                if (hover.UserMoved)
                {
                    ownsCursorPosition = false;

                    return AddonCommandResult.Failure(
                        error: "Target selection was cancelled because the mouse moved");
                }

                if (!hover.IsHovered)
                {
                    lastFailure = hover.Error;

                    if (!hover.CanRetry ||
                        attempt == MaximumClickAttempts)
                    {
                        return AddonCommandResult.Failure(error: lastFailure);
                    }

                    await Task.Delay(
                            delay: retryDelay,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(continueOnCapturedContext: false);

                    continue;
                }

                if (!CursorRemainsLeased(expectedPosition: commandedCursorPosition))
                {
                    ownsCursorPosition = false;

                    return AddonCommandResult.Failure(
                        error: "Target selection was cancelled because the mouse moved");
                }

                NativeMethods.LeftButtonDownAtCurrentCursor();
                mouseButtonDown = true;

                await Task.Delay(
                        delay: clickHoldDuration,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(continueOnCapturedContext: false);

                if (!CursorRemainsLeased(expectedPosition: commandedCursorPosition))
                {
                    NativeMethods.LeftButtonUpAtCurrentCursor();
                    mouseButtonDown = false;
                    ownsCursorPosition = false;

                    return AddonCommandResult.Failure(
                        error: "Target selection was cancelled because the mouse moved");
                }

                NativeMethods.LeftButtonUpAtCurrentCursor();
                mouseButtonDown = false;
                verifiedClickCount++;

                var confirmationDeadline =
                    DateTimeOffset.UtcNow + confirmationTimeout;

                while (DateTimeOffset.UtcNow < confirmationDeadline)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!this.TryReadActionState(
                            processId: client.ProcessId,
                            request: request,
                            state: out state,
                            error: out stateError))
                    {
                        return AddonCommandResult.Failure(error: stateError);
                    }

                    if (state.IsSelected)
                    {
                        return AddonCommandResult.Success();
                    }

                    if (ownsCursorPosition &&
                        !CursorRemainsLeased(expectedPosition: commandedCursorPosition))
                    {
                        ownsCursorPosition = false;
                        playerMovedAfterClick = true;
                    }

                    await Task.Delay(
                            delay: pollInterval,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(continueOnCapturedContext: false);
                }

                lastFailure = string.Concat(args: ["The game did not confirm the requested target selection after verified click ", verifiedClickCount, " of ", MaximumClickAttempts]);

                if (playerMovedAfterClick ||
                    attempt == MaximumClickAttempts)
                {
                    break;
                }

                await Task.Delay(
                        delay: retryDelay,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(continueOnCapturedContext: false);
            }

            return AddonCommandResult.Failure(error: lastFailure);
        }
        finally
        {
            if (mouseButtonDown)
            {
                NativeMethods.LeftButtonUpAtCurrentCursor();
            }

            if (ownsCursorPosition &&
                CursorRemainsLeased(expectedPosition: commandedCursorPosition))
            {
                _ = NativeMethods.MoveCursorToScreenPoint(
                    point: originalCursorPosition);
            }

            await hostForm
                .SetAddonOverlayInputSuppressedAsync(suppressed: false)
                .ConfigureAwait(continueOnCapturedContext: false);
        }
    }

    private async ValueTask<HoverAcquisitionResult>
        AcquireHoveredTargetAsync(
            ClientInstance client,
            AddonActionRequest request,
            Size clientSize,
            Point currentCommandedPosition,
            bool currentlyOwnsCursor,
            CancellationToken cancellationToken)
    {
        var commandedCursorPosition = currentCommandedPosition;
        var ownsCursorPosition = currentlyOwnsCursor;
        var stableHoverSamples = 0;
        var hoverDeadline = DateTimeOffset.UtcNow + hoverTimeout;
        var probeIndex = 0;

        while (DateTimeOffset.UtcNow < hoverDeadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (ownsCursorPosition &&
                !CursorRemainsLeased(expectedPosition: commandedCursorPosition))
            {
                return HoverAcquisitionResult.MouseMoved(
                    cursorPosition: commandedCursorPosition);
            }

            if (!this.TryReadActionState(
                    processId: client.ProcessId,
                    request: request,
                    state: out var state,
                    error: out var stateError))
            {
                return HoverAcquisitionResult.Failed(
                    error: stateError,
                    cursorPosition: commandedCursorPosition,
                    ownsCursorPosition: ownsCursorPosition,
                    canRetry: false);
            }

            if (state.IsSelected)
            {
                return HoverAcquisitionResult.Selected(
                    cursorPosition: commandedCursorPosition,
                    ownsCursorPosition: ownsCursorPosition);
            }

            var probeOffset = hoverProbeOffsets[
                probeIndex % hoverProbeOffsets.Length];

            probeIndex++;

            if (!TryResolveScreenPoint(
                    gameWindowHandle: client.GameWindowHandle,
                    clientSize: clientSize,
                    state: state,
                    offset: probeOffset,
                    screenPoint: out var probePosition))
            {
                return HoverAcquisitionResult.Failed(
                    error: "The nearby target no longer has a usable screen position",
                    cursorPosition: commandedCursorPosition,
                    ownsCursorPosition: ownsCursorPosition,
                    canRetry: false);
            }

            if (!ownsCursorPosition ||
                probePosition != commandedCursorPosition)
            {
                if (!NativeMethods.MoveCursorToScreenPoint(
                        point: probePosition))
                {
                    return HoverAcquisitionResult.Failed(
                        error: "Could not move the mouse to the nearby target",
                        cursorPosition: commandedCursorPosition,
                        ownsCursorPosition: ownsCursorPosition,
                        canRetry: false);
                }

                commandedCursorPosition = probePosition;
                ownsCursorPosition = true;
                stableHoverSamples = 0;

                await Task.Delay(
                        delay: pollInterval,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(continueOnCapturedContext: false);
            }

            var sampleBudget = RequiredStableHoverSamples + 1;

            for (var sample = 0;
                 sample < sampleBudget &&
                 DateTimeOffset.UtcNow < hoverDeadline;
                 sample++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!CursorRemainsLeased(expectedPosition: commandedCursorPosition))
                {
                    return HoverAcquisitionResult.MouseMoved(
                        cursorPosition: commandedCursorPosition);
                }

                if (!this.TryReadActionState(
                        processId: client.ProcessId,
                        request: request,
                        state: out state,
                        error: out stateError))
                {
                    return HoverAcquisitionResult.Failed(
                        error: stateError,
                        cursorPosition: commandedCursorPosition,
                        ownsCursorPosition: ownsCursorPosition,
                        canRetry: false);
                }

                if (state.IsSelected)
                {
                    return HoverAcquisitionResult.Selected(
                        cursorPosition: commandedCursorPosition,
                        ownsCursorPosition: ownsCursorPosition);
                }

                if (!state.IsHovered)
                {
                    stableHoverSamples = 0;
                    break;
                }

                stableHoverSamples++;

                if (stableHoverSamples >=
                    RequiredStableHoverSamples)
                {
                    return HoverAcquisitionResult.Hovered(
                        cursorPosition: commandedCursorPosition);
                }

                await Task.Delay(
                        delay: pollInterval,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(continueOnCapturedContext: false);
            }
        }

        return HoverAcquisitionResult.Failed(
            error: "The game did not confirm the requested nearby target under the mouse",
            cursorPosition: commandedCursorPosition,
            ownsCursorPosition: ownsCursorPosition,
            canRetry: true);
    }

    private bool TryReadActionState(
        int processId,
        AddonActionRequest request,
        out ClientNearbyTargetActionState state,
        out string error)
    {
        return observationCoordinator
            .TryReadNearbyTargetActionState(
                processId: processId,
                objectId: request.ObjectId,
                expectedSectorId: request.ExpectedSectorId,
                actionState: out state,
                error: out error);
    }

    private static bool TryResolveScreenPoint(
        IntPtr gameWindowHandle,
        Size clientSize,
        ClientNearbyTargetActionState state,
        Point offset,
        out Point screenPoint)
    {
        var clientPoint = new Point(
            x: Math.Clamp(
                value: (int)MathF.Round(
                           x: state.NormalizedX * clientSize.Width) +
                       offset.X,
                min: 0,
                max: clientSize.Width - 1),
            y: Math.Clamp(
                value: (int)MathF.Round(
                           x: state.NormalizedY * clientSize.Height) +
                       offset.Y,
                min: 0,
                max: clientSize.Height - 1));

        return NativeMethods.TryConvertClientPointToScreen(
            clientWindowHandle: gameWindowHandle,
            clientPoint: clientPoint,
            screenPoint: out screenPoint);
    }

    private static bool CursorRemainsLeased(Point expectedPosition)
    {
        if (!NativeMethods.TryGetCursorScreenPosition(
                point: out var currentPosition))
        {
            return false;
        }

        return Math.Abs(
                   value: currentPosition.X - expectedPosition.X) <=
               CursorLeaseTolerancePixels &&
               Math.Abs(
                   value: currentPosition.Y - expectedPosition.Y) <=
               CursorLeaseTolerancePixels;
    }

    private readonly record struct HoverAcquisitionResult(
        bool IsHovered,
        bool IsSelected,
        bool UserMoved,
        bool OwnsCursorPosition,
        Point CursorPosition,
        bool CanRetry,
        string Error)
    {
        public static HoverAcquisitionResult Hovered(
            Point cursorPosition)
        {
            return new HoverAcquisitionResult(
                IsHovered: true,
                IsSelected: false,
                UserMoved: false,
                OwnsCursorPosition: true,
                CursorPosition: cursorPosition,
                CanRetry: false,
                Error: "");
        }

        public static HoverAcquisitionResult Selected(
            Point cursorPosition,
            bool ownsCursorPosition)
        {
            return new HoverAcquisitionResult(
                IsHovered: false,
                IsSelected: true,
                UserMoved: false,
                OwnsCursorPosition: ownsCursorPosition,
                CursorPosition: cursorPosition,
                CanRetry: false,
                Error: "");
        }

        public static HoverAcquisitionResult MouseMoved(
            Point cursorPosition)
        {
            return new HoverAcquisitionResult(
                IsHovered: false,
                IsSelected: false,
                UserMoved: true,
                OwnsCursorPosition: false,
                CursorPosition: cursorPosition,
                CanRetry: false,
                Error: "Target selection was cancelled because the mouse moved");
        }

        public static HoverAcquisitionResult Failed(
            string error,
            Point cursorPosition,
            bool ownsCursorPosition,
            bool canRetry)
        {
            return new HoverAcquisitionResult(
                IsHovered: false,
                IsSelected: false,
                UserMoved: false,
                OwnsCursorPosition: ownsCursorPosition,
                CursorPosition: cursorPosition,
                CanRetry: canRetry,
                Error: error);
        }
    }
}
