namespace Net7ClientManager.ControlPlane;

using System.Globalization;
using Net7ClientManager.ControlPlane.Contracts;
using Net7ClientManager.Models;
using Net7ClientManager.Observations;

internal sealed partial class ClientManagerControlPlaneService
{
    private ControlPlaneResponse ListFleetCommands(
        ControlPlaneRequest request)
    {
        var resolution = this.ResolveSlot(request, requireRunning: true);

        if (resolution.Failure != null)
        {
            return resolution.Failure;
        }

        var client = resolution.Client!;

        if (client.LifecycleState != ClientLifecycleState.InGame)
        {
            return this.Failure(
                request,
                ControlPlaneExitCode.Unavailable,
                $"{resolution.Slot!.Name} is not in game.");
        }

        var invocationContext = new FleetCommandInvocationContext
        {
            ActiveClient = client,
        };
        var commands = this.clientManager
            .GetFleetCommandDefinitions(invocationContext)
            .Select(command => new
            {
                id = command.Id,
                label = command.Label,
                enabled = command.IsEnabled,
                category = NormalizeEnum(command.Category),
                arguments = command.Arguments,
            })
            .ToArray();
        var output = string.Join(
            Environment.NewLine,
            commands.Select(command => string.Create(
                CultureInfo.InvariantCulture,
                $"{command.id}\t{command.label}\t{(command.enabled ? "ENABLED" : "DISABLED")}\t{command.category}")));

        return this.Success(
            request,
            $"Found {commands.Length} available command(s) for {resolution.Slot!.Name}.",
            output,
            new
            {
                slot = resolution.Slot.Name,
                pilot = client.LiveCharacterIdentity.Name,
                count = commands.Length,
                commands,
            });
    }

    private async Task<ControlPlaneResponse> ExecuteFleetCommandAsync(
        ControlPlaneRequest request)
    {
        var resolution = this.ResolveSlot(request, requireRunning: true);

        if (resolution.Failure != null)
        {
            return resolution.Failure;
        }

        var commandId = GetArgument(request, "command")?.Trim();

        if (string.IsNullOrWhiteSpace(commandId))
        {
            return this.Failure(
                request,
                ControlPlaneExitCode.InvalidArguments,
                "A command id is required.");
        }

        var client = resolution.Client!;

        if (client.LifecycleState != ClientLifecycleState.InGame)
        {
            return this.Failure(
                request,
                ControlPlaneExitCode.Unavailable,
                $"{resolution.Slot!.Name} is not in game.");
        }

        var invocationContext = new FleetCommandInvocationContext
        {
            ActiveClient = client,
        };
        var command = this.clientManager
            .GetFleetCommandDefinitions(invocationContext)
            .FirstOrDefault(candidate => string.Equals(
                candidate.Id,
                commandId,
                StringComparison.OrdinalIgnoreCase));

        if (command == null)
        {
            return this.Failure(
                request,
                ControlPlaneExitCode.NotFound,
                $"Command '{commandId}' is not currently available for {resolution.Slot!.Name}.");
        }

        if (!command.IsEnabled)
        {
            return this.Failure(
                request,
                ControlPlaneExitCode.Rejected,
                $"{command.Label} is not currently available for {resolution.Slot!.Name}.");
        }

        await this.clientManager.ExecuteFleetCommandAsync(
                command,
                invocationContext)
            .ConfigureAwait(true);

        var message = $"Executed {command.Label} for {resolution.Slot!.Name}.";

        return this.Success(
                request,
                message,
                message,
                new
                {
                    slot = resolution.Slot.Name,
                    pilot = client.LiveCharacterIdentity.Name,
                    commandId = command.Id,
                    label = command.Label,
                }) with
            {
                Code = "command_executed",
            };
    }

    private async Task<ControlPlaneResponse> InviteFleetClientAsync(
        ControlPlaneRequest request)
    {
        var leaderResolution = this.ResolveSlot(request, requireRunning: true);

        if (leaderResolution.Failure != null)
        {
            return leaderResolution.Failure;
        }

        var targetSelector = GetArgument(request, "target_slot")?.Trim();

        if (string.IsNullOrWhiteSpace(targetSelector))
        {
            return this.Failure(
                request,
                ControlPlaneExitCode.InvalidArguments,
                "A target slot name is required.");
        }

        var targetRequest = request with
        {
            Arguments = new Dictionary<string, string?>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["slot"] = targetSelector,
            },
        };
        var targetResolution = this.ResolveSlot(
            targetRequest,
            requireRunning: true);

        if (targetResolution.Failure != null)
        {
            return targetResolution.Failure;
        }

        var leader = leaderResolution.Client!;
        var target = targetResolution.Client!;

        if (leader.ProcessId == target.ProcessId)
        {
            return this.Failure(
                request,
                ControlPlaneExitCode.InvalidArguments,
                "A client cannot invite itself.");
        }

        if (leader.LifecycleState != ClientLifecycleState.InGame)
        {
            return this.Failure(
                request,
                ControlPlaneExitCode.Unavailable,
                $"{leaderResolution.Slot!.Name} is not in game.");
        }

        if (target.LifecycleState != ClientLifecycleState.InGame)
        {
            return this.Failure(
                request,
                ControlPlaneExitCode.Unavailable,
                $"{targetResolution.Slot!.Name} is not in game.");
        }

        var targetPilot = target.LiveCharacterIdentity.Name?.Trim();

        if (string.IsNullOrWhiteSpace(targetPilot))
        {
            return this.Failure(
                request,
                ControlPlaneExitCode.Unavailable,
                $"The pilot name for {targetResolution.Slot!.Name} is not available yet.");
        }

        var invocationContext = new FleetCommandInvocationContext
        {
            ActiveClient = leader,
        };
        var commands = this.clientManager
            .GetFleetCommandDefinitions(invocationContext);
        var targetProcessId = target.ProcessId.ToString(
            CultureInfo.InvariantCulture);
        var inviteCommand = commands.FirstOrDefault(command =>
            command.Id.StartsWith(
                "invite-client:",
                StringComparison.OrdinalIgnoreCase) &&
            command.Arguments.TryGetValue(
                "targetProcessId",
                out var commandTargetProcessId) &&
            string.Equals(
                commandTargetProcessId,
                targetProcessId,
                StringComparison.Ordinal));

        if (inviteCommand == null)
        {
            var alreadyGrouped = commands.Any(command =>
                command.Id.StartsWith(
                    "kick-client:",
                    StringComparison.OrdinalIgnoreCase) &&
                command.Arguments.TryGetValue(
                    "targetProcessId",
                    out var commandTargetProcessId) &&
                string.Equals(
                    commandTargetProcessId,
                    targetProcessId,
                    StringComparison.Ordinal));

            if (alreadyGrouped)
            {
                var alreadyGroupedMessage =
                    $"{targetPilot} is already in {leaderResolution.Slot!.Name}'s group.";

                return this.Success(
                        request,
                        alreadyGroupedMessage,
                        alreadyGroupedMessage,
                        new
                        {
                            slot = leaderResolution.Slot.Name,
                            pilot = leader.LiveCharacterIdentity.Name,
                            targetSlot = targetResolution.Slot!.Name,
                            targetPilot,
                            alreadyGrouped = true,
                        }) with
                    {
                        Code = "already_grouped",
                    };
            }

            return this.Failure(
                request,
                ControlPlaneExitCode.Unavailable,
                $"Invite {targetPilot} is not currently available from {leaderResolution.Slot!.Name}.");
        }

        if (!inviteCommand.IsEnabled)
        {
            return this.Failure(
                request,
                ControlPlaneExitCode.Rejected,
                $"{inviteCommand.Label} is not currently available.");
        }

        await this.clientManager.ExecuteFleetCommandAsync(
                inviteCommand,
                invocationContext)
            .ConfigureAwait(true);

        var message =
            $"Invited {targetPilot} from {leaderResolution.Slot!.Name}.";

        return this.Success(
                request,
                message,
                message,
                new
                {
                    slot = leaderResolution.Slot.Name,
                    pilot = leader.LiveCharacterIdentity.Name,
                    targetSlot = targetResolution.Slot!.Name,
                    targetPilot,
                    commandId = inviteCommand.Id,
                    label = inviteCommand.Label,
                    alreadyGrouped = false,
                }) with
            {
                Code = "invite_sent",
            };
    }
}
