namespace Net7ClientManager.Contributions;

using System.Globalization;
using System.Text;

internal static class ForgeBuildRequestCanonicalizer
{
    public static byte[] Canonicalize(ForgeBuildPublicationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var builder = new StringBuilder();
        AppendEnvelope(builder, request.ProtocolVersion, request.ContributorId, request.RequestId,
            request.SubmittedAtUtc, request.ClientVersion, request.LivePilotName);
        Append(builder, request.BuildId ?? "");
        Append(builder, request.DocumentSha256);
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    public static byte[] Canonicalize(ForgeBuildSearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var builder = new StringBuilder();
        AppendEnvelope(builder, request.ProtocolVersion, request.ContributorId, request.RequestId,
            request.SubmittedAtUtc, request.ClientVersion, request.LivePilotName);
        Append(builder, request.Query);
        Append(builder, request.ProfessionIndex?.ToString(CultureInfo.InvariantCulture) ?? "");
        Append(builder, request.PublisherPilotName);
        Append(builder, request.StarredOnly ? "1" : "0");
        Append(builder, request.OwnedOnly ? "1" : "0");
        Append(builder, request.Sort);
        Append(builder, request.Offset.ToString(CultureInfo.InvariantCulture));
        Append(builder, request.Limit.ToString(CultureInfo.InvariantCulture));
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    public static byte[] Canonicalize(ForgeBuildAccessRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var builder = new StringBuilder();
        AppendEnvelope(builder, request.ProtocolVersion, request.ContributorId, request.RequestId,
            request.SubmittedAtUtc, request.ClientVersion, request.LivePilotName);
        Append(builder, request.BuildId);
        Append(builder, request.Version?.ToString(CultureInfo.InvariantCulture) ?? "");
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    public static byte[] Canonicalize(ForgeBuildStarRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var builder = new StringBuilder();
        AppendEnvelope(builder, request.ProtocolVersion, request.ContributorId, request.RequestId,
            request.SubmittedAtUtc, request.ClientVersion, request.LivePilotName);
        Append(builder, request.BuildId);
        Append(builder, request.Starred ? "1" : "0");
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static void AppendEnvelope(
        StringBuilder builder,
        int protocolVersion,
        string contributorId,
        string requestId,
        DateTimeOffset submittedAtUtc,
        string clientVersion,
        string livePilotName)
    {
        Append(builder, protocolVersion.ToString(CultureInfo.InvariantCulture));
        Append(builder, contributorId);
        Append(builder, requestId);
        Append(builder, submittedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        Append(builder, clientVersion);
        Append(builder, livePilotName);
    }

    private static void Append(StringBuilder builder, string? value)
    {
        value ??= "";
        builder.Append(value.Length.ToString(CultureInfo.InvariantCulture));
        builder.Append(':');
        builder.Append(value);
        builder.Append('\n');
    }
}
