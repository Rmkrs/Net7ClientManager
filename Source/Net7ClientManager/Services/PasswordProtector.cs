namespace Net7ClientManager.Services;

using System.Security.Cryptography;
using System.Text;

public static class PasswordProtector
{
    public static string? Protect(string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return null;
        }

        var plainBytes = Encoding.UTF8.GetBytes(password);
        var protectedBytes = ProtectedData.Protect(
            plainBytes,
            optionalEntropy: null,
            DataProtectionScope.CurrentUser);

        return Convert.ToBase64String(protectedBytes);
    }

    public static string? Unprotect(string? protectedPassword)
    {
        if (string.IsNullOrWhiteSpace(protectedPassword))
        {
            return null;
        }

        try
        {
            var protectedBytes = Convert.FromBase64String(protectedPassword);
            var plainBytes = ProtectedData.Unprotect(
                protectedBytes,
                optionalEntropy: null,
                DataProtectionScope.CurrentUser);

            return Encoding.UTF8.GetString(plainBytes);
        }
        catch (CryptographicException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
