namespace Net7ClientManager.Ctl;

using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Net7ClientManager.ControlPlane.Contracts;

internal static class Program
{
    private static readonly JsonSerializerOptions jsonOptions =
        ControlPlaneProtocol.CreateJsonOptions();

    private static async Task<int> Main(string[] args)
    {
        string? resultFilePath = null;

        try
        {
            var requestedResultFile = CommandLine.FindOptionValue(
                args,
                "--result-file");

            if (!string.IsNullOrWhiteSpace(requestedResultFile))
            {
                resultFilePath = ResultFileWriter.Prepare(
                    requestedResultFile);
            }
        }
        catch (Exception exception)
            when (exception is IOException or
                  UnauthorizedAccessException or
                  ArgumentException or
                  NotSupportedException)
        {
            Console.Error.WriteLine(
                $"Could not prepare the result file: {exception.Message}");
            return (int)ControlPlaneExitCode.InvalidArguments;
        }

        try
        {
            var invocation = CommandLine.Parse(args);

            if (invocation.ShowHelp)
            {
                Console.WriteLine(CommandLine.HelpText);
                return PublishAndReturn(
                    resultFilePath,
                    CreateLocalResponse(
                        ControlPlaneExitCode.Success,
                        "help",
                        "Help displayed.",
                        CommandLine.HelpText));
            }

            var response = await ExecuteAsync(invocation)
                .ConfigureAwait(false);
            WriteResponse(response, invocation.JsonOutput);
            return PublishAndReturn(resultFilePath, response);
        }
        catch (CommandLineException exception)
        {
            Console.Error.WriteLine(exception.Message);
            Console.Error.WriteLine("Run 'net7cmctl help' for usage.");
            return PublishAndReturn(
                resultFilePath,
                CreateLocalResponse(
                    ControlPlaneExitCode.InvalidArguments,
                    "invalid_arguments",
                    exception.Message));
        }
        catch (TimeoutException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return PublishAndReturn(
                resultFilePath,
                CreateLocalResponse(
                    ControlPlaneExitCode.TimedOut,
                    "timed_out",
                    exception.Message));
        }
        catch (UnauthorizedAccessException)
        {
            const string Message =
                "Net7 Client Manager denied this automation connection. " +
                "Make sure net7cmctl and Client Manager are from the same " +
                "current release and are running under the same Windows user.";
            Console.Error.WriteLine(Message);
            return PublishAndReturn(
                resultFilePath,
                CreateLocalResponse(
                    ControlPlaneExitCode.Rejected,
                    "rejected",
                    Message));
        }
        catch (Exception exception)
        {
            var message = $"net7cmctl failed: {exception.Message}";
            Console.Error.WriteLine(message);
            return PublishAndReturn(
                resultFilePath,
                CreateLocalResponse(
                    ControlPlaneExitCode.InternalError,
                    "internal_error",
                    message));
        }
    }

    private static async Task<ControlPlaneResponse> ExecuteAsync(
        CommandInvocation invocation)
    {
        var response = invocation.Workflow switch
        {
            CommandWorkflow.LaunchAndWait =>
                await LaunchAndWaitAsync(invocation).ConfigureAwait(false),
            CommandWorkflow.AutoPilotTo =>
                await AutoPilotToAsync(invocation).ConfigureAwait(false),
            CommandWorkflow.InviteAndWait =>
                await InviteAndWaitAsync(invocation).ConfigureAwait(false),
            _ when invocation.WaitCondition != null =>
                await WaitAsync(invocation).ConfigureAwait(false),
            _ => await SendAsync(invocation.Request).ConfigureAwait(false),
        };

        return string.IsNullOrWhiteSpace(invocation.Field)
            ? response
            : ScalarFieldProjector.Project(response, invocation.Field);
    }

    private static async Task<ControlPlaneResponse> LaunchAndWaitAsync(
        CommandInvocation invocation)
    {
        var launchResponse = await SendAsync(invocation.Request)
            .ConfigureAwait(false);

        if (!launchResponse.IsSuccess)
        {
            return launchResponse;
        }

        var waitInvocation = invocation with
        {
            Request = CreateRequest(
                "slot.status",
                invocation.Request.Arguments),
            Workflow = CommandWorkflow.None,
            WaitCondition = "in_game",
        };
        var response = await WaitAsync(waitInvocation)
            .ConfigureAwait(false);

        return response with
        {
            RequestId = invocation.Request.RequestId,
        };
    }

    private static async Task<ControlPlaneResponse> AutoPilotToAsync(
        CommandInvocation invocation)
    {
        var destination = invocation.Request.Arguments["destination"] ?? "";
        var setDestinationResponse = await SendAsync(invocation.Request)
            .ConfigureAwait(false);

        if (!setDestinationResponse.IsSuccess)
        {
            return setDestinationResponse;
        }

        var startResponse = await SendAsync(CreateRequest(
                "navigation.start_autopilot",
                invocation.Request.Arguments))
            .ConfigureAwait(false);

        if (!startResponse.IsSuccess)
        {
            return startResponse with
            {
                RequestId = invocation.Request.RequestId,
            };
        }

        var waitInvocation = invocation with
        {
            Request = CreateRequest(
                "navigation.autopilot",
                invocation.Request.Arguments),
            Workflow = CommandWorkflow.None,
            WaitCondition = "autopilot_complete",
        };
        var response = await WaitAsync(waitInvocation)
            .ConfigureAwait(false);

        return response.ExitCode switch
        {
            ControlPlaneExitCode.Success => response with
            {
                RequestId = invocation.Request.RequestId,
                Code = "arrived",
                Message = $"Arrived at {destination}.",
                Output = $"Arrived at {destination}.",
            },
            ControlPlaneExitCode.TimedOut => response with
            {
                RequestId = invocation.Request.RequestId,
                Message =
                    $"Timed out waiting for Auto Pilot to reach {destination}.",
            },
            _ => response with
            {
                RequestId = invocation.Request.RequestId,
            },
        };
    }

    private static async Task<ControlPlaneResponse> InviteAndWaitAsync(
        CommandInvocation invocation)
    {
        var inviteResponse = await SendAsync(invocation.Request)
            .ConfigureAwait(false);

        if (!inviteResponse.IsSuccess ||
            string.Equals(
                inviteResponse.Code,
                "already_grouped",
                StringComparison.OrdinalIgnoreCase))
        {
            return inviteResponse;
        }

        if (inviteResponse.Data is not { ValueKind: JsonValueKind.Object } data ||
            !TryReadString(data, "targetPilot", out var targetPilot) ||
            string.IsNullOrWhiteSpace(targetPilot))
        {
            return new ControlPlaneResponse
            {
                RequestId = invocation.Request.RequestId,
                ExitCode = ControlPlaneExitCode.InternalError,
                Code = "invalid_invite_response",
                Message =
                    "Net7 Client Manager did not return the invited pilot name.",
                Data = inviteResponse.Data,
            };
        }

        var waitArguments = new Dictionary<string, string?>(
            invocation.Request.Arguments,
            StringComparer.OrdinalIgnoreCase)
        {
            ["member"] = targetPilot,
        };
        var waitInvocation = invocation with
        {
            Request = CreateRequest("slot.group", waitArguments),
            Workflow = CommandWorkflow.None,
            WaitCondition = "group_member",
            Timeout = invocation.Timeout ?? TimeSpan.FromSeconds(30),
        };
        var response = await WaitAsync(waitInvocation)
            .ConfigureAwait(false);

        return response with
        {
            RequestId = invocation.Request.RequestId,
        };
    }

    private static async Task<ControlPlaneResponse> WaitAsync(
        CommandInvocation invocation)
    {
        var timeout = invocation.Timeout ?? TimeSpan.FromMinutes(2);
        var stopwatch = Stopwatch.StartNew();
        ControlPlaneResponse? lastResponse = null;
        TimeSpan? terminalFailureObservedAt = null;
        string? terminalFailureSignature = null;

        while (stopwatch.Elapsed < timeout)
        {
            lastResponse = await SendAsync(invocation.Request)
                .ConfigureAwait(false);

            if (lastResponse.IsSuccess)
            {
                var evaluation = EvaluateWaitCondition(
                    invocation,
                    lastResponse.Data);

                if (evaluation.Kind == WaitEvaluationKind.Satisfied)
                {
                    return BuildWaitSuccessResponse(
                        invocation,
                        lastResponse,
                        evaluation);
                }

                if (evaluation.Kind == WaitEvaluationKind.TerminalFailure)
                {
                    var signature = string.Concat(
                        evaluation.Code,
                        "\n",
                        evaluation.Message);

                    if (!string.Equals(
                            terminalFailureSignature,
                            signature,
                            StringComparison.Ordinal))
                    {
                        terminalFailureSignature = signature;
                        terminalFailureObservedAt = stopwatch.Elapsed;
                    }

                    if (terminalFailureObservedAt.HasValue &&
                        stopwatch.Elapsed - terminalFailureObservedAt.Value >=
                            TimeSpan.FromMilliseconds(750))
                    {
                        return new ControlPlaneResponse
                        {
                            RequestId = invocation.Request.RequestId,
                            ExitCode = ControlPlaneExitCode.ConditionFailed,
                            Code = evaluation.Code,
                            Message = evaluation.Message,
                            Output = evaluation.Message,
                            Data = lastResponse.Data,
                        };
                    }
                }
                else
                {
                    terminalFailureObservedAt = null;
                    terminalFailureSignature = null;
                }
            }

            if (lastResponse.ExitCode == ControlPlaneExitCode.Unavailable &&
                invocation.WaitCondition == "autopilot_complete")
            {
                return lastResponse with
                {
                    RequestId = invocation.Request.RequestId,
                };
            }

            if (lastResponse.ExitCode is
                ControlPlaneExitCode.InvalidArguments or
                ControlPlaneExitCode.NotFound or
                ControlPlaneExitCode.Rejected or
                ControlPlaneExitCode.ConditionFailed or
                ControlPlaneExitCode.InternalError)
            {
                return lastResponse;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(300))
                .ConfigureAwait(false);
        }

        var message = BuildWaitTimeoutMessage(invocation);

        return new ControlPlaneResponse
        {
            RequestId = invocation.Request.RequestId,
            ExitCode = ControlPlaneExitCode.TimedOut,
            Code = "timed_out",
            Message = message,
            Data = lastResponse?.Data,
        };
    }

    private static ControlPlaneResponse BuildWaitSuccessResponse(
        CommandInvocation invocation,
        ControlPlaneResponse response,
        WaitEvaluation evaluation)
    {
        var slot = invocation.Request.Arguments.TryGetValue(
            "slot",
            out var slotName)
                ? slotName
                : null;
        var message = invocation.WaitCondition switch
        {
            "in_game" when !string.IsNullOrWhiteSpace(slot) =>
                $"{slot} is now in game.",
            "in_game" => "The slot is now in game.",
            "autopilot_complete" => evaluation.Message,
            "environment" or
            "location" or
            "interaction" or
            "group_member" or
            "mission_count" or
            "inventory_quantity" => evaluation.Message,
            _ => response.Message,
        };

        return response with
        {
            RequestId = invocation.Request.RequestId,
            ExitCode = ControlPlaneExitCode.Success,
            Code = evaluation.Code,
            Message = message,
            Output = message,
        };
    }

    private static WaitEvaluation EvaluateWaitCondition(
        CommandInvocation invocation,
        JsonElement? data)
    {
        if (data is not { ValueKind: JsonValueKind.Object } value)
        {
            return WaitEvaluation.Pending;
        }

        var condition = invocation.WaitCondition!;

        if (condition == "in_game")
        {
            return TryReadString(value, "lifecycle", out var lifecycle) &&
                   string.Equals(
                       lifecycle,
                       "in_game",
                       StringComparison.OrdinalIgnoreCase)
                ? WaitEvaluation.Satisfied(
                    "in_game",
                    "The slot is now in game.")
                : WaitEvaluation.Pending;
        }

        if (condition == "environment")
        {
            var requestedEnvironment = GetWaitArgument(
                invocation,
                "value");

            if (!TryReadString(value, "environment", out var environment) ||
                !MatchesEnvironment(environment, requestedEnvironment))
            {
                return WaitEvaluation.Pending;
            }

            return WaitEvaluation.Satisfied(
                "environment_matched",
                $"{GetWaitSlotName(invocation)} is now in {requestedEnvironment}.");
        }

        if (condition == "location")
        {
            var requestedStation = GetWaitArgument(
                invocation,
                "station");
            var stationMatches =
                TryReadString(value, "station", out var station) &&
                string.Equals(
                    station,
                    requestedStation,
                    StringComparison.OrdinalIgnoreCase);
            var starbaseMatches =
                TryReadString(value, "starbase", out var starbase) &&
                string.Equals(
                    starbase,
                    requestedStation,
                    StringComparison.OrdinalIgnoreCase);

            if (!stationMatches && !starbaseMatches)
            {
                return WaitEvaluation.Pending;
            }

            return WaitEvaluation.Satisfied(
                "location_matched",
                $"{GetWaitSlotName(invocation)} is now at {requestedStation}.");
        }

        if (condition == "interaction")
        {
            var requestedVerb = GetWaitArgument(invocation, "verb");
            var verbMatches =
                TryReadString(value, "verb", out var verb) &&
                string.Equals(
                    verb,
                    requestedVerb,
                    StringComparison.OrdinalIgnoreCase);
            var executable =
                TryReadBoolean(value, "executable", out var canExecute) &&
                canExecute;

            if (!verbMatches || !executable)
            {
                return WaitEvaluation.Pending;
            }

            return WaitEvaluation.Satisfied(
                "interaction_ready",
                $"{requestedVerb} is ready for {GetWaitSlotName(invocation)}.");
        }

        if (condition == "group_member")
        {
            if (!TryReadBoolean(value, "present", out var present) || !present)
            {
                return WaitEvaluation.Pending;
            }

            var member = GetWaitArgument(invocation, "member");
            return WaitEvaluation.Satisfied(
                "group_member_present",
                $"{member} is present in {GetWaitSlotName(invocation)}'s group.");
        }

        if (condition == "mission_count")
        {
            var minimum = GetWaitMinimum(invocation);

            if (!TryReadInt32(value, "count", out var count) || count < minimum)
            {
                return WaitEvaluation.Pending;
            }

            return WaitEvaluation.Satisfied(
                "mission_count_reached",
                $"{GetWaitSlotName(invocation)} has {count} matching mission(s).");
        }

        if (condition == "inventory_quantity")
        {
            var minimum = GetWaitMinimum(invocation);

            if (!TryReadInt32(value, "quantity", out var quantity) ||
                quantity < minimum)
            {
                return WaitEvaluation.Pending;
            }

            var item = GetWaitArgument(invocation, "item");
            var collection = invocation.Request.Arguments.TryGetValue(
                "collection",
                out var requestedCollection) &&
                !string.IsNullOrWhiteSpace(requestedCollection)
                    ? requestedCollection
                    : "cargo";

            return WaitEvaluation.Satisfied(
                "inventory_quantity_reached",
                $"{GetWaitSlotName(invocation)} has {quantity} {item} in {collection}.");
        }

        if (condition != "autopilot_complete")
        {
            return WaitEvaluation.Pending;
        }

        _ = TryReadString(value, "state", out var state);
        _ = TryReadString(value, "stopReason", out var stopReason);
        _ = TryReadString(value, "statusText", out var statusText);

        if (string.Equals(
                state,
                "arrived",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                stopReason,
                "destination_reached",
                StringComparison.OrdinalIgnoreCase))
        {
            return WaitEvaluation.Satisfied(
                "arrived",
                string.IsNullOrWhiteSpace(statusText)
                    ? "Auto Pilot arrived at its destination."
                    : statusText);
        }

        if (string.Equals(
                state,
                "stopped",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                state,
                "manual_final_leg",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                state,
                "inactive",
                StringComparison.OrdinalIgnoreCase))
        {
            var reason = string.IsNullOrWhiteSpace(stopReason)
                ? "unknown"
                : stopReason;
            var message = string.IsNullOrWhiteSpace(statusText)
                ? $"Auto Pilot stopped before arrival ({reason})."
                : statusText;

            return WaitEvaluation.TerminalFailure(
                "autopilot_stopped",
                message);
        }

        return WaitEvaluation.Pending;
    }

    private static string BuildWaitTimeoutMessage(
        CommandInvocation invocation)
    {
        return invocation.WaitCondition switch
        {
            "in_game" => "Timed out waiting for the slot to enter the game.",
            "autopilot_complete" =>
                "Timed out waiting for Auto Pilot to complete.",
            "environment" =>
                $"Timed out waiting for {GetWaitSlotName(invocation)} " +
                $"to enter {GetWaitArgument(invocation, "value")}.",
            "location" =>
                $"Timed out waiting for {GetWaitSlotName(invocation)} " +
                $"to reach {GetWaitArgument(invocation, "station")}.",
            "interaction" =>
                $"Timed out waiting for {GetWaitArgument(invocation, "verb")} " +
                $"to become ready for {GetWaitSlotName(invocation)}.",
            "group_member" =>
                $"Timed out waiting for {GetWaitArgument(invocation, "member")} " +
                $"to join {GetWaitSlotName(invocation)}'s group.",
            "mission_count" =>
                $"Timed out waiting for {GetWaitSlotName(invocation)} " +
                $"to have at least {GetWaitMinimum(invocation)} matching mission(s).",
            "inventory_quantity" =>
                $"Timed out waiting for {GetWaitSlotName(invocation)} " +
                $"to have at least {GetWaitMinimum(invocation)} " +
                $"{GetWaitArgument(invocation, "item")}.",
            _ => "The requested wait timed out.",
        };
    }

    private static bool MatchesEnvironment(
        string actual,
        string requested)
    {
        var normalizedActual = NormalizeEnvironment(actual);
        var normalizedRequested = NormalizeEnvironment(requested);

        return string.Equals(
            normalizedActual,
            normalizedRequested,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeEnvironment(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "station" => "starbase",
            _ => value.Trim().ToLowerInvariant(),
        };
    }

    private static string GetWaitArgument(
        CommandInvocation invocation,
        string name)
    {
        return invocation.Request.Arguments.TryGetValue(name, out var value)
            ? value ?? ""
            : "";
    }

    private static string GetWaitSlotName(CommandInvocation invocation)
    {
        return GetWaitArgument(invocation, "slot") is { Length: > 0 } slot
            ? slot
            : "The slot";
    }

    private static int GetWaitMinimum(CommandInvocation invocation)
    {
        return invocation.Request.Arguments.TryGetValue(
                   "at_least",
                   out var value) &&
               int.TryParse(
                   value,
                   NumberStyles.Integer,
                   CultureInfo.InvariantCulture,
                   out var minimum)
            ? minimum
            : 0;
    }

    private static bool TryReadString(
        JsonElement value,
        string propertyName,
        out string result)
    {
        result = "";

        if (!value.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        result = property.GetString() ?? "";
        return true;
    }

    private static bool TryReadBoolean(
        JsonElement value,
        string propertyName,
        out bool result)
    {
        result = false;

        if (!value.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.True &&
            property.ValueKind != JsonValueKind.False)
        {
            return false;
        }

        result = property.GetBoolean();
        return true;
    }

    private static bool TryReadInt32(
        JsonElement value,
        string propertyName,
        out int result)
    {
        result = 0;

        return value.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.Number &&
               property.TryGetInt32(out result);
    }

    private static ControlPlaneRequest CreateRequest(
        string operation,
        IReadOnlyDictionary<string, string?> sourceArguments)
    {
        Dictionary<string, string?> arguments =
            new(StringComparer.OrdinalIgnoreCase);

        foreach (var argument in sourceArguments)
        {
            arguments[argument.Key] = argument.Value;
        }

        return new ControlPlaneRequest
        {
            Operation = operation,
            Arguments = arguments,
        };
    }

    private static async Task<ControlPlaneResponse> SendAsync(
        ControlPlaneRequest request)
    {
        using var pipe = new NamedPipeClientStream(
            serverName: ".",
            pipeName: ControlPlaneProtocol.GetPipeName(),
            direction: PipeDirection.InOut,
            options: PipeOptions.Asynchronous);

        using var connectCancellation =
            new CancellationTokenSource(TimeSpan.FromSeconds(3));

        try
        {
            await pipe.ConnectAsync(connectCancellation.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException(
                "Net7 Client Manager is not running or did not answer in time.");
        }

        using var writer = new StreamWriter(
            pipe,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            bufferSize: 4096,
            leaveOpen: true)
        {
            AutoFlush = true,
        };
        using var reader = new StreamReader(
            pipe,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 4096,
            leaveOpen: true);

        await writer.WriteLineAsync(
                JsonSerializer.Serialize(request, jsonOptions))
            .ConfigureAwait(false);

        var responseLine = await reader.ReadLineAsync()
            .WaitAsync(TimeSpan.FromSeconds(15))
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(responseLine))
        {
            throw new InvalidOperationException(
                "Net7 Client Manager closed the control connection without a response.");
        }

        return JsonSerializer.Deserialize<ControlPlaneResponse>(
                   responseLine,
                   jsonOptions) ??
               throw new InvalidOperationException(
                   "Net7 Client Manager returned an invalid response.");
    }

    private static void WriteResponse(
        ControlPlaneResponse response,
        bool jsonOutput)
    {
        if (jsonOutput)
        {
            Console.WriteLine(JsonSerializer.Serialize(
                response,
                ControlPlaneProtocol.CreateJsonOptions(indented: true)));
            return;
        }

        var output = !string.IsNullOrWhiteSpace(response.Output)
            ? response.Output
            : response.Message;

        if (response.IsSuccess)
        {
            if (!string.IsNullOrWhiteSpace(output))
            {
                Console.WriteLine(output);
            }
            return;
        }

        Console.Error.WriteLine(output);
    }

    private static ControlPlaneResponse CreateLocalResponse(
        ControlPlaneExitCode exitCode,
        string code,
        string message,
        string? output = null)
    {
        return new ControlPlaneResponse
        {
            ExitCode = exitCode,
            Code = code,
            Message = message,
            Output = output,
        };
    }

    private static int PublishAndReturn(
        string? resultFilePath,
        ControlPlaneResponse response)
    {
        if (string.IsNullOrWhiteSpace(resultFilePath))
        {
            return (int)response.ExitCode;
        }

        try
        {
            ResultFileWriter.Publish(resultFilePath, response);
            return (int)response.ExitCode;
        }
        catch (Exception exception)
            when (exception is IOException or
                  UnauthorizedAccessException or
                  ArgumentException or
                  NotSupportedException)
        {
            Console.Error.WriteLine(
                $"Could not publish the result file: {exception.Message}");
            return (int)ControlPlaneExitCode.InternalError;
        }
    }
}

internal static class ScalarFieldProjector
{
    public static ControlPlaneResponse Project(
        ControlPlaneResponse response,
        string field)
    {
        if (!response.IsSuccess)
        {
            return response;
        }

        if (response.Data is not { } data)
        {
            return Failure(
                response,
                "field_not_found",
                $"Field '{field}' is not available in this response.");
        }

        var current = data;

        foreach (var segment in field.Split(
                     '.',
                     StringSplitOptions.RemoveEmptyEntries |
                     StringSplitOptions.TrimEntries))
        {
            if (current.ValueKind != JsonValueKind.Object ||
                !TryGetProperty(current, segment, out var next))
            {
                return Failure(
                    response,
                    "field_not_found",
                    $"Field '{field}' was not found.");
            }

            current = next;
        }

        var output = current.ValueKind switch
        {
            JsonValueKind.String => current.GetString() ?? "",
            JsonValueKind.Number => current.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "null",
            _ => null,
        };

        if (output == null)
        {
            return Failure(
                response,
                "field_not_scalar",
                $"Field '{field}' is an object or collection, not a scalar value.");
        }

        return response with
        {
            Code = "ok",
            Message = output,
            Output = output,
            Data = current.Clone(),
        };
    }

    private static bool TryGetProperty(
        JsonElement source,
        string requestedName,
        out JsonElement value)
    {
        if (source.TryGetProperty(requestedName, out value))
        {
            return true;
        }

        var normalizedRequestedName = Normalize(requestedName);

        foreach (var property in source.EnumerateObject())
        {
            if (string.Equals(
                    Normalize(property.Name),
                    normalizedRequestedName,
                    StringComparison.Ordinal))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string Normalize(string value)
    {
        return new string(value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
    }

    private static ControlPlaneResponse Failure(
        ControlPlaneResponse response,
        string code,
        string message)
    {
        return new ControlPlaneResponse
        {
            RequestId = response.RequestId,
            ExitCode = ControlPlaneExitCode.InvalidArguments,
            Code = code,
            Message = message,
            Output = message,
            Data = response.Data,
        };
    }
}

internal enum CommandWorkflow
{
    None,
    LaunchAndWait,
    AutoPilotTo,
    InviteAndWait,
}

internal enum WaitEvaluationKind
{
    Pending,
    Satisfied,
    TerminalFailure,
}

internal readonly record struct WaitEvaluation(
    WaitEvaluationKind Kind,
    string Code,
    string Message)
{
    public static WaitEvaluation Pending =>
        new(WaitEvaluationKind.Pending, "", "");

    public static WaitEvaluation Satisfied(
        string code,
        string message) =>
        new(WaitEvaluationKind.Satisfied, code, message);

    public static WaitEvaluation TerminalFailure(
        string code,
        string message) =>
        new(WaitEvaluationKind.TerminalFailure, code, message);
}

internal sealed record CommandInvocation(
    ControlPlaneRequest Request,
    bool JsonOutput,
    bool ShowHelp = false,
    string? WaitCondition = null,
    TimeSpan? Timeout = null,
    CommandWorkflow Workflow = CommandWorkflow.None,
    string? Field = null);

internal sealed class CommandLineException(string message) :
    Exception(message);

internal static class CommandLine
{
    public const string HelpText = """
net7cmctl controls and queries a running Net7 Client Manager.

Queries:
  net7cmctl slots [--json]
  net7cmctl get status --slot <name> [--field <path>] [--json]
  net7cmctl get location --slot <name> [--field <path>] [--json]
  net7cmctl get target --slot <name> [--field <path>] [--json]
  net7cmctl get interaction --slot <name> [--field <path>] [--json]
  net7cmctl get group --slot <name> [--member <pilot>] [--field <path>] [--json]
  net7cmctl get missions --slot <name> [--name <text>] [--state <state>]
      [--type <type>] [--field <path>] [--json]
  net7cmctl get inventory --slot <name> [--collection <name>] [--item <name>]
      [--field <path>] [--json]
  net7cmctl get route --slot <name> [--field <path>] [--json]
  net7cmctl get autopilot --slot <name> [--field <path>] [--json]

Query notes:
  --field reads one scalar field such as environment, nearest-nav,
  member-count, active-count, cargo.free, or quantity.
  Mission states: active, complete, failed, expired, terminal, all.
  Mission types: mission, job, combat-job, trade-job, explore-job, all.
  Inventory collections: cargo, equipment, ammo, secure (or vault),
  reward, overflow, vendor. --item without --collection searches cargo.

Commands:
  net7cmctl commands --slot <name> [--json]
  net7cmctl run-command <id> --slot <name> [--json]
  net7cmctl interact --slot <name> [--json]
  net7cmctl invite <target-slot> --slot <name> [--timeout <seconds>] [--json]
  net7cmctl launch --slot <name> [--wait] [--timeout <seconds>]
  net7cmctl autopilot-to <destination> --slot <name> [--timeout <seconds>]
  net7cmctl focus game --slot <name>
  net7cmctl focus navigation --slot <name>
  net7cmctl show navigation --slot <name>
  net7cmctl show mission-wiki --slot <name>
  net7cmctl set-destination <name> --slot <name>
  net7cmctl start-autopilot --slot <name>
  net7cmctl stop-autopilot --slot <name>
  net7cmctl plan-return --slot <name>
  net7cmctl clear-route --slot <name>

Waits:
  net7cmctl wait in-game --slot <name> [--timeout <seconds>] [--json]
  net7cmctl wait autopilot-complete --slot <name> [--timeout <seconds>] [--json]
  net7cmctl wait environment --slot <name> --value <environment>
      [--timeout <seconds>] [--json]
  net7cmctl wait location --slot <name> --station <name>
      [--timeout <seconds>] [--json]
  net7cmctl wait interaction --slot <name> --verb <verb>
      [--timeout <seconds>] [--json]
  net7cmctl wait group-member --slot <name> --member <pilot>
      [--timeout <seconds>] [--json]
  net7cmctl wait mission-count --slot <name> --at-least <count>
      [--name <text>] [--state <state>] [--type <type>]
      [--timeout <seconds>] [--json]
  net7cmctl wait inventory --slot <name> --item <name> --at-least <count>
      [--collection <name>] [--timeout <seconds>] [--json]

Automation output:
  Add --result-file <path> to any command. The file is cleared when the
  command starts, then atomically receives three UTF-8 lines without a BOM:
  numeric exit code, stable result code, and human-readable message.

Exit codes:
  0 success, 2 invalid arguments, 3 not found, 4 unavailable,
  5 rejected, 6 timed out, 7 condition failed, 10 internal error.
""";

    public static CommandInvocation Parse(string[] args)
    {
        if (args.Length == 0)
        {
            return new CommandInvocation(
                new ControlPlaneRequest(),
                JsonOutput: false,
                ShowHelp: true);
        }

        var tokens = new List<string>(args);
        var json = RemoveFlag(tokens, "--json");
        var launchWait = RemoveFlag(tokens, "--wait");
        var timeout = RemoveOption(tokens, "--timeout");
        var timeoutValue = ParseTimeout(timeout);
        var slot = RemoveOption(tokens, "--slot");
        var field = RemoveOption(tokens, "--field");
        var member = RemoveOption(tokens, "--member");
        var missionName = RemoveOption(tokens, "--name");
        var missionState = RemoveOption(tokens, "--state");
        var missionType = RemoveOption(tokens, "--type");
        var inventoryCollection = RemoveOption(tokens, "--collection");
        var inventoryItem = RemoveOption(tokens, "--item");
        var environmentValue = RemoveOption(tokens, "--value");
        var stationName = RemoveOption(tokens, "--station");
        var interactionVerb = RemoveOption(tokens, "--verb");
        var atLeast = ParseNonNegativeInteger(
            RemoveOption(tokens, "--at-least"),
            "--at-least");
        _ = RemoveOption(tokens, "--result-file");

        if (tokens.Count == 0)
        {
            throw new CommandLineException("No command was supplied.");
        }

        if (tokens[0].ToLowerInvariant() is "help" or "--help" or "-h")
        {
            EnsureCount(tokens, 1, "help");
            return new CommandInvocation(
                new ControlPlaneRequest(),
                JsonOutput: json,
                ShowHelp: true);
        }

        string operation;
        string? waitCondition = null;
        var workflow = CommandWorkflow.None;
        Dictionary<string, string?> arguments =
            new(StringComparer.OrdinalIgnoreCase);

        switch (tokens[0].ToLowerInvariant())
        {
            case "slots":
                EnsureCount(tokens, 1, "slots");
                operation = "slots.list";
                break;

            case "get":
            {
                EnsureCount(
                    tokens,
                    2,
                    "get <status|location|target|interaction|group|" +
                    "missions|inventory|route|autopilot>");
                RequireSlot(slot);
                var query = tokens[1].ToLowerInvariant();
                operation = query switch
                {
                    "status" => "slot.status",
                    "location" => "slot.location",
                    "target" => "slot.target",
                    "interaction" => "slot.interaction",
                    "group" => "slot.group",
                    "missions" => "slot.missions",
                    "inventory" => "slot.inventory",
                    "route" => "navigation.route",
                    "autopilot" => "navigation.autopilot",
                    _ => throw new CommandLineException(
                        $"Unknown query '{tokens[1]}'."),
                };
                arguments["slot"] = slot;

                if (query == "group")
                {
                    arguments["member"] = member;
                }
                else if (member != null)
                {
                    throw new CommandLineException(
                        "--member is supported only by 'get group'.");
                }

                if (query == "missions")
                {
                    arguments["name"] = missionName;
                    arguments["state"] = missionState;
                    arguments["type"] = missionType;
                }
                else if (missionName != null ||
                         missionState != null ||
                         missionType != null)
                {
                    throw new CommandLineException(
                        "--name, --state, and --type are supported only by 'get missions'.");
                }

                if (query == "inventory")
                {
                    arguments["collection"] = inventoryCollection;
                    arguments["item"] = inventoryItem;
                }
                else if (inventoryCollection != null || inventoryItem != null)
                {
                    throw new CommandLineException(
                        "--collection and --item are supported only by 'get inventory'.");
                }

                if (environmentValue != null ||
                    stationName != null ||
                    interactionVerb != null ||
                    atLeast != null)
                {
                    throw new CommandLineException(
                        "--value, --station, --verb, and --at-least are supported only by waits.");
                }
                break;
            }

            case "commands":
                EnsureCount(tokens, 1, "commands");
                RequireSlot(slot);
                operation = "fleet.commands";
                arguments["slot"] = slot;
                break;

            case "run-command":
                EnsureCount(tokens, 2, "run-command <id>");
                RequireSlot(slot);
                operation = "fleet.execute";
                arguments["slot"] = slot;
                arguments["command"] = tokens[1];
                break;

            case "interact":
                EnsureCount(tokens, 1, "interact");
                RequireSlot(slot);
                operation = "fleet.execute";
                arguments["slot"] = slot;
                arguments["command"] = "interact";
                break;

            case "invite":
                if (tokens.Count < 2)
                {
                    throw new CommandLineException(
                        "invite requires a target slot name.");
                }
                RequireSlot(slot);
                operation = "fleet.invite";
                arguments["slot"] = slot;
                arguments["target_slot"] = string.Join(' ', tokens.Skip(1));
                workflow = CommandWorkflow.InviteAndWait;
                break;

            case "focus":
                EnsureCount(tokens, 2, "focus <game|navigation>");
                RequireSlot(slot);
                operation = tokens[1].ToLowerInvariant() switch
                {
                    "game" => "window.focus_game",
                    "navigation" => "window.focus_navigation",
                    _ => throw new CommandLineException(
                        $"Unknown focus target '{tokens[1]}'."),
                };
                arguments["slot"] = slot;
                break;

            case "show":
                EnsureCount(tokens, 2, "show <navigation|mission-wiki>");
                RequireSlot(slot);
                operation = tokens[1].ToLowerInvariant() switch
                {
                    "navigation" => "window.show_navigation",
                    "mission-wiki" or "missionwiki" =>
                        "window.show_mission_wiki",
                    _ => throw new CommandLineException(
                        $"Unknown window '{tokens[1]}'."),
                };
                arguments["slot"] = slot;
                break;

            case "launch":
                EnsureCount(tokens, 1, "launch");
                RequireSlot(slot);
                operation = "slot.launch";
                arguments["slot"] = slot;
                workflow = launchWait
                    ? CommandWorkflow.LaunchAndWait
                    : CommandWorkflow.None;
                break;

            case "autopilot-to":
                if (tokens.Count < 2)
                {
                    throw new CommandLineException(
                        "autopilot-to requires a destination name.");
                }
                RequireSlot(slot);
                operation = "navigation.set_destination";
                arguments["slot"] = slot;
                arguments["destination"] = string.Join(' ', tokens.Skip(1));
                workflow = CommandWorkflow.AutoPilotTo;
                break;

            case "set-destination":
                if (tokens.Count < 2)
                {
                    throw new CommandLineException(
                        "set-destination requires a destination name.");
                }
                RequireSlot(slot);
                operation = "navigation.set_destination";
                arguments["slot"] = slot;
                arguments["destination"] = string.Join(' ', tokens.Skip(1));
                break;

            case "start-autopilot":
                EnsureCount(tokens, 1, "start-autopilot");
                RequireSlot(slot);
                operation = "navigation.start_autopilot";
                arguments["slot"] = slot;
                break;

            case "stop-autopilot":
                EnsureCount(tokens, 1, "stop-autopilot");
                RequireSlot(slot);
                operation = "navigation.stop_autopilot";
                arguments["slot"] = slot;
                break;

            case "plan-return":
                EnsureCount(tokens, 1, "plan-return");
                RequireSlot(slot);
                operation = "navigation.plan_return";
                arguments["slot"] = slot;
                break;

            case "clear-route":
                EnsureCount(tokens, 1, "clear-route");
                RequireSlot(slot);
                operation = "navigation.clear_route";
                arguments["slot"] = slot;
                break;

            case "wait":
            {
                EnsureCount(
                    tokens,
                    2,
                    "wait <in-game|autopilot-complete|environment|location|" +
                    "interaction|group-member|mission-count|inventory>");
                RequireSlot(slot);
                var requestedWait = tokens[1].ToLowerInvariant();
                waitCondition = requestedWait switch
                {
                    "in-game" or "ingame" => "in_game",
                    "autopilot-complete" or "autopilot" =>
                        "autopilot_complete",
                    "environment" => "environment",
                    "location" => "location",
                    "interaction" => "interaction",
                    "group-member" or "groupmember" => "group_member",
                    "mission-count" or "missioncount" => "mission_count",
                    "inventory" => "inventory_quantity",
                    _ => throw new CommandLineException(
                        $"Unknown wait condition '{tokens[1]}'."),
                };
                operation = waitCondition switch
                {
                    "in_game" => "slot.status",
                    "autopilot_complete" => "navigation.autopilot",
                    "environment" or "location" => "slot.location",
                    "interaction" => "slot.interaction",
                    "group_member" => "slot.group",
                    "mission_count" => "slot.missions",
                    "inventory_quantity" => "slot.inventory",
                    _ => throw new InvalidOperationException(
                        $"Unsupported wait condition '{waitCondition}'."),
                };
                arguments["slot"] = slot;

                switch (waitCondition)
                {
                    case "environment":
                        arguments["value"] = RequireTextOption(
                            environmentValue,
                            "--value");
                        break;

                    case "location":
                        arguments["station"] = RequireTextOption(
                            stationName,
                            "--station");
                        break;

                    case "interaction":
                        arguments["verb"] = RequireTextOption(
                            interactionVerb,
                            "--verb");
                        break;

                    case "group_member":
                        arguments["member"] = RequireTextOption(
                            member,
                            "--member");
                        break;

                    case "mission_count":
                        arguments["name"] = missionName;
                        arguments["state"] = missionState;
                        arguments["type"] = missionType;
                        arguments["at_least"] = RequireIntegerOption(
                                atLeast,
                                "--at-least")
                            .ToString(CultureInfo.InvariantCulture);
                        break;

                    case "inventory_quantity":
                        arguments["collection"] = inventoryCollection;
                        arguments["item"] = RequireTextOption(
                            inventoryItem,
                            "--item");
                        arguments["at_least"] = RequireIntegerOption(
                                atLeast,
                                "--at-least")
                            .ToString(CultureInfo.InvariantCulture);
                        break;
                }
                break;
            }

            default:
                throw new CommandLineException(
                    $"Unknown command '{tokens[0]}'.");
        }

        var commandName = tokens[0].ToLowerInvariant();

        if (commandName != "get")
        {
            if (field != null)
            {
                throw new CommandLineException(
                    "--field is supported only by get queries.");
            }

            if (commandName == "wait")
            {
                ValidateWaitOptionUsage(
                    waitCondition!,
                    member,
                    missionName,
                    missionState,
                    missionType,
                    inventoryCollection,
                    inventoryItem,
                    environmentValue,
                    stationName,
                    interactionVerb,
                    atLeast);
            }
            else if (member != null ||
                     missionName != null ||
                     missionState != null ||
                     missionType != null ||
                     inventoryCollection != null ||
                     inventoryItem != null ||
                     environmentValue != null ||
                     stationName != null ||
                     interactionVerb != null ||
                     atLeast != null)
            {
                throw new CommandLineException(
                    "Query and wait filters are supported only by their matching command.");
            }
        }

        if (launchWait && workflow != CommandWorkflow.LaunchAndWait)
        {
            throw new CommandLineException(
                "--wait is supported only by the launch command.");
        }

        if (timeoutValue != null &&
            waitCondition == null &&
            workflow != CommandWorkflow.LaunchAndWait &&
            workflow != CommandWorkflow.AutoPilotTo &&
            workflow != CommandWorkflow.InviteAndWait)
        {
            throw new CommandLineException(
                "--timeout is supported only by waits, launch --wait, autopilot-to, and invite.");
        }

        return new CommandInvocation(
            new ControlPlaneRequest
            {
                Operation = operation,
                Arguments = arguments,
            },
            JsonOutput: json,
            WaitCondition: waitCondition,
            Timeout: timeoutValue,
            Workflow: workflow,
            Field: field);
    }

    public static string? FindOptionValue(
        IReadOnlyList<string> args,
        string option)
    {
        for (var index = 0; index < args.Count; index++)
        {
            if (!string.Equals(
                    args[index],
                    option,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (index + 1 >= args.Count ||
                args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                return null;
            }

            return args[index + 1];
        }

        return null;
    }

    private static bool RemoveFlag(
        List<string> tokens,
        string option)
    {
        var index = tokens.FindIndex(token =>
            string.Equals(token, option, StringComparison.OrdinalIgnoreCase));

        if (index < 0)
        {
            return false;
        }

        tokens.RemoveAt(index);
        return true;
    }

    private static string? RemoveOption(
        List<string> tokens,
        string option)
    {
        var index = tokens.FindIndex(token =>
            string.Equals(token, option, StringComparison.OrdinalIgnoreCase));

        if (index < 0)
        {
            return null;
        }

        if (index + 1 >= tokens.Count ||
            tokens[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new CommandLineException(
                $"{option} requires a value.");
        }

        var value = tokens[index + 1];
        tokens.RemoveAt(index + 1);
        tokens.RemoveAt(index);
        return value;
    }

    private static int? ParseNonNegativeInteger(
        string? value,
        string option)
    {
        if (value == null)
        {
            return null;
        }

        if (!int.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var result) ||
            result < 0)
        {
            throw new CommandLineException(
                $"{option} must be a non-negative whole number.");
        }

        return result;
    }

    private static string RequireTextOption(
        string? value,
        string option)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new CommandLineException(
                $"This wait requires {option} <value>.");
        }

        return value;
    }

    private static int RequireIntegerOption(
        int? value,
        string option)
    {
        if (!value.HasValue)
        {
            throw new CommandLineException(
                $"This wait requires {option} <value>.");
        }

        return value.Value;
    }

    private static void ValidateWaitOptionUsage(
        string waitCondition,
        string? member,
        string? missionName,
        string? missionState,
        string? missionType,
        string? inventoryCollection,
        string? inventoryItem,
        string? environmentValue,
        string? stationName,
        string? interactionVerb,
        int? atLeast)
    {
        if (member != null && waitCondition != "group_member")
        {
            throw new CommandLineException(
                "--member is supported only by 'wait group-member'.");
        }

        if ((missionName != null || missionState != null || missionType != null) &&
            waitCondition != "mission_count")
        {
            throw new CommandLineException(
                "--name, --state, and --type are supported only by 'wait mission-count'.");
        }

        if ((inventoryCollection != null || inventoryItem != null) &&
            waitCondition != "inventory_quantity")
        {
            throw new CommandLineException(
                "--collection and --item are supported only by 'wait inventory'.");
        }

        if (environmentValue != null && waitCondition != "environment")
        {
            throw new CommandLineException(
                "--value is supported only by 'wait environment'.");
        }

        if (stationName != null && waitCondition != "location")
        {
            throw new CommandLineException(
                "--station is supported only by 'wait location'.");
        }

        if (interactionVerb != null && waitCondition != "interaction")
        {
            throw new CommandLineException(
                "--verb is supported only by 'wait interaction'.");
        }

        if (atLeast != null &&
            waitCondition is not "mission_count" and not "inventory_quantity")
        {
            throw new CommandLineException(
                "--at-least is supported only by mission-count and inventory waits.");
        }
    }

    private static TimeSpan? ParseTimeout(string? value)
    {
        if (value == null)
        {
            return null;
        }

        if (!double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var seconds) ||
            seconds <= 0 ||
            seconds > 86400)
        {
            throw new CommandLineException(
                "--timeout must be a number of seconds between 0 and 86400.");
        }

        return TimeSpan.FromSeconds(seconds);
    }

    private static void RequireSlot(string? slot)
    {
        if (string.IsNullOrWhiteSpace(slot))
        {
            throw new CommandLineException(
                "This command requires --slot <name>.");
        }
    }

    private static void EnsureCount(
        IReadOnlyCollection<string> tokens,
        int expected,
        string usage)
    {
        if (tokens.Count != expected)
        {
            throw new CommandLineException(
                $"Usage: net7cmctl {usage}.");
        }
    }
}

internal static class ResultFileWriter
{
    private static readonly Encoding Utf8WithoutBom =
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static string Prepare(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException(
                "The result-file path is empty.",
                nameof(path));
        }

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(fullPath, "", Utf8WithoutBom);
        return fullPath;
    }

    public static void Publish(
        string path,
        ControlPlaneResponse response)
    {
        var directory = Path.GetDirectoryName(path) ?? "";
        var fileName = Path.GetFileName(path);
        var temporaryPath = Path.Combine(
            directory,
            string.Create(
                CultureInfo.InvariantCulture,
                $".{fileName}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp"));
        var resultCode = string.IsNullOrWhiteSpace(response.Code)
            ? ControlPlaneProtocol.GetDefaultResultCode(response.ExitCode)
            : response.Code;
        var message = !string.IsNullOrWhiteSpace(response.Message)
            ? response.Message
            : response.Output ?? resultCode;
        var content = string.Concat(
            ((int)response.ExitCode).ToString(CultureInfo.InvariantCulture),
            "\r\n",
            MakeSingleLine(resultCode),
            "\r\n",
            MakeSingleLine(message),
            "\r\n");

        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 4096,
                       FileOptions.WriteThrough))
            using (var writer = new StreamWriter(
                       stream,
                       Utf8WithoutBom,
                       bufferSize: 4096,
                       leaveOpen: true))
            {
                writer.Write(content);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            ReplaceWithRetry(temporaryPath, path);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void ReplaceWithRetry(
        string temporaryPath,
        string resultPath)
    {
        const int MaximumAttempts = 20;

        for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
        {
            try
            {
                File.Move(temporaryPath, resultPath, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < MaximumAttempts)
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(50));
            }
        }
    }

    private static string MakeSingleLine(string value)
    {
        return value
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
    }
}
