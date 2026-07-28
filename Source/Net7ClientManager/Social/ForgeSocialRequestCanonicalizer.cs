namespace Net7ClientManager.Social;

using System.Globalization;
using System.Text;

internal static class ForgeSocialRequestCanonicalizer
{
    public static byte[] Canonicalize(SocialPresenceUpsertRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var builder = new StringBuilder();
        AppendEnvelope(builder, request.ProtocolVersion, request.ContributorId,
            request.RequestId, request.SubmittedAtUtc, request.ClientVersion);
        Append(builder, request.PilotName);
        Append(builder, request.IsSharing ? "1" : "0");
        Append(builder, ((int)request.AtlasVisibility).ToString(CultureInfo.InvariantCulture));
        Append(builder, request.SectorId ?? "");
        Append(builder, request.SectorKey ?? "");
        Append(builder, request.SectorName ?? "");
        Append(builder, request.SystemName ?? "");
        Append(builder, request.ActiveSectorNumber.ToString(CultureInfo.InvariantCulture));
        Append(builder, request.X?.ToString("R", CultureInfo.InvariantCulture) ?? "");
        Append(builder, request.Y?.ToString("R", CultureInfo.InvariantCulture) ?? "");
        Append(builder, request.Z?.ToString("R", CultureInfo.InvariantCulture) ?? "");
        Append(builder, request.NearestNavObjectId?.ToString(CultureInfo.InvariantCulture) ?? "");
        Append(builder, request.NearestNavName ?? "");
        Append(builder, request.StationName ?? "");
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    public static byte[] Canonicalize(LookingForGuildUpsertRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var builder = new StringBuilder();
        AppendEnvelope(builder, request.ProtocolVersion, request.ContributorId,
            request.RequestId, request.SubmittedAtUtc, request.ClientVersion);
        Append(builder, request.PilotName);
        Append(builder, request.IsLookingForGuild ? "1" : "0");
        Append(builder, request.ProfessionName ?? "");
        Append(builder, request.OverallLevel?.ToString(CultureInfo.InvariantCulture) ?? "");
        AppendValues(builder, request.InterestTags);
        AppendValues(builder, request.Languages);
        Append(builder, request.OtherLanguage ?? "");
        Append(builder, request.Region ?? "");
        Append(builder, request.OtherRegion ?? "");
        Append(builder, request.Availability ?? "");
        Append(builder, request.Message ?? "");
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    public static byte[] Canonicalize(GuildRecruitmentUpsertRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var builder = new StringBuilder();
        AppendEnvelope(builder, request.ProtocolVersion, request.ContributorId,
            request.RequestId, request.SubmittedAtUtc, request.ClientVersion);
        Append(builder, request.GuildName);
        Append(builder, request.PublishingPilotName);
        Append(builder, request.IsRecruiting ? "1" : "0");
        Append(builder, request.OtherContacts ?? "");
        AppendValues(builder, request.FocusTags);
        AppendValues(builder, request.WantedProfessions);
        AppendValues(builder, request.Languages);
        Append(builder, request.OtherLanguage ?? "");
        Append(builder, request.Region ?? "");
        Append(builder, request.OtherRegion ?? "");
        Append(builder, request.ActiveTimes ?? "");
        Append(builder, request.Requirements ?? "");
        Append(builder, request.Message ?? "");
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static void AppendEnvelope(
        StringBuilder builder,
        int protocolVersion,
        string contributorId,
        string requestId,
        DateTimeOffset submittedAtUtc,
        string clientVersion)
    {
        Append(builder, protocolVersion.ToString(CultureInfo.InvariantCulture));
        Append(builder, contributorId);
        Append(builder, requestId);
        Append(builder, submittedAtUtc.ToUniversalTime().ToString(
            "O", CultureInfo.InvariantCulture));
        Append(builder, clientVersion);
    }

    private static void AppendValues(
        StringBuilder builder,
        IReadOnlyList<string>? values)
    {
        values ??= [];
        Append(builder, values.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var value in values)
        {
            Append(builder, value);
        }
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
