namespace Net7ClientManager.Services;

internal static class UiObfuscationMode
{
    private const string AccountMask = "********";

    public static bool IsEnabled { get; private set; }

    public static void Initialize(IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        IsEnabled = arguments.Any(argument =>
            string.Equals(
                argument.Trim(),
                "--obfuscate",
                StringComparison.OrdinalIgnoreCase));
    }

    public static string AccountName(
        string? accountName,
        string fallback = "")
    {
        if (string.IsNullOrWhiteSpace(accountName))
        {
            return fallback;
        }

        return IsEnabled
            ? AccountMask
            : accountName;
    }
}
