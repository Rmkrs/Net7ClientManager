namespace Net7ClientManager.Navigation;

using System.Security.Cryptography;

internal static class ForgeNavigationHash
{
    public static bool IsSha256(string? value)
    {
        return value is { Length: 64 } &&
               value.All(Uri.IsHexDigit);
    }

    public static string ComputeSha256(ReadOnlySpan<byte> value)
    {
        return Convert.ToHexString(SHA256.HashData(value))
            .ToLowerInvariant();
    }

    public static string ComputeFileSha256(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);

        return Convert.ToHexString(SHA256.HashData(stream))
            .ToLowerInvariant();
    }
}
