namespace Net7ClientManager.ControlPlane.Contracts;

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

public static class ControlPlaneProtocol
{
    public const int Version = 1;

    public const string PipeNamePrefix = "Net7ClientManager.Control.v1";

    public static string GetPipeName()
    {
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{PipeNamePrefix}.session-{Process.GetCurrentProcess().SessionId}");
    }

    public static JsonSerializerOptions CreateJsonOptions(bool indented = false)
    {
        return new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = indented,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters =
            {
                new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower),
            },
        };
    }

    public static string GetDefaultResultCode(
        ControlPlaneExitCode exitCode)
    {
        return exitCode switch
        {
            ControlPlaneExitCode.Success => "ok",
            ControlPlaneExitCode.InvalidArguments => "invalid_arguments",
            ControlPlaneExitCode.NotFound => "not_found",
            ControlPlaneExitCode.Unavailable => "unavailable",
            ControlPlaneExitCode.Rejected => "rejected",
            ControlPlaneExitCode.TimedOut => "timed_out",
            ControlPlaneExitCode.ConditionFailed => "condition_failed",
            _ => "internal_error",
        };
    }
}

public enum ControlPlaneExitCode
{
    Success = 0,
    InvalidArguments = 2,
    NotFound = 3,
    Unavailable = 4,
    Rejected = 5,
    TimedOut = 6,
    ConditionFailed = 7,
    InternalError = 10,
}

public sealed record ControlPlaneRequest
{
    public int Version { get; init; } = ControlPlaneProtocol.Version;

    public string RequestId { get; init; } = Guid.NewGuid().ToString("N");

    public string Operation { get; init; } = "";

    public Dictionary<string, string?> Arguments { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed record ControlPlaneResponse
{
    public int Version { get; init; } = ControlPlaneProtocol.Version;

    public string RequestId { get; init; } = "";

    public ControlPlaneExitCode ExitCode { get; init; }

    public string Code { get; init; } = "";

    public bool IsSuccess => this.ExitCode == ControlPlaneExitCode.Success;

    public string Message { get; init; } = "";

    public string? Output { get; init; }

    public JsonElement? Data { get; init; }
}
