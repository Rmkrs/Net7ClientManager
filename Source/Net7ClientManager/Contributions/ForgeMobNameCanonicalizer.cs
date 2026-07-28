namespace Net7ClientManager.Contributions;

using System.Text.RegularExpressions;

internal static class ForgeMobNameCanonicalizer
{
    private const string JobPrefix = "[Job] ";
    private static readonly Regex JobSuffixPattern = new(
        @"^(?<base>.+?)\s+for\s+\S+\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    public static string Canonicalize(string value)
    {
        var trimmed = value.Trim();
        var match = JobSuffixPattern.Match(trimmed);

        if (!match.Success)
        {
            return trimmed;
        }

        var baseName = match.Groups["base"].Value.TrimEnd();

        if (baseName.Length == 0)
        {
            return trimmed;
        }

        if (baseName.StartsWith(JobPrefix, StringComparison.OrdinalIgnoreCase))
        {
            baseName = baseName[JobPrefix.Length..].TrimStart();
        }

        return baseName.Length == 0
            ? trimmed
            : string.Concat(JobPrefix, baseName);
    }
}
