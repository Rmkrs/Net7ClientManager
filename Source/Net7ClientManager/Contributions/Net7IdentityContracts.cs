namespace Net7ClientManager.Contributions;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

// Classes deliberately do not generate a record ToString containing credentials.
internal sealed class Net7IdentityRequest
{
    public string Action { get; init; } = "";
    public string Challenge { get; init; } = "";
    public string PublicKey { get; init; } = "";
    public int AccountId { get; init; }
    public string SessionCookie { get; init; } = "";
    public string? RecoveryCode { get; init; }
    public string Signature { get; set; } = "";
}

internal sealed record Net7IdentityChallenge(string Challenge, DateTimeOffset ExpiresAtUtc);

internal sealed class Net7IdentityResponse
{
    public string ContributorId { get; init; } = "";
    public DateTimeOffset RegisteredAtUtc { get; init; }
    public int AccountId { get; init; }
    public string RecoveryCode { get; init; } = "";
}

internal static class Net7IdentityCanonicalizer
{
    public static byte[] Canonicalize(Net7IdentityRequest request) => JsonSerializer.SerializeToUtf8Bytes(new[]
    {
        "net7-forge-identity-v1", request.Action, request.Challenge, request.PublicKey,
        request.AccountId.ToString(CultureInfo.InvariantCulture),
        Hash(request.SessionCookie), Hash(request.RecoveryCode ?? ""),
    });

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
