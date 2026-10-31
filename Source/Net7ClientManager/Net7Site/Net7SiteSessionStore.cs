namespace Net7ClientManager.Net7Site;

using System.Text.Json;
using Net7ClientManager.Services;

// Keeps the Net-7.org sign-in cookies when the user ticks "Remember login".
// The cookies are protected for the Windows user like stored game passwords are.
// The Net-7.org password itself is never written anywhere.
internal sealed class Net7SiteSessionStore
{
    private static readonly JsonSerializerOptions jsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string filePath;

    public Net7SiteSessionStore()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Net7ClientManager");

        Directory.CreateDirectory(directory);

        this.filePath = Path.Combine(directory, "net7-site-session.json");
    }

    public Net7SiteSession? Load()
    {
        if (!File.Exists(this.filePath))
        {
            return null;
        }

        try
        {
            var file = JsonSerializer.Deserialize<SessionFile>(
                File.ReadAllText(this.filePath),
                jsonOptions);
            var cookiesJson = PasswordProtector.Unprotect(file?.ProtectedCookies);

            if (file == null || string.IsNullOrWhiteSpace(cookiesJson))
            {
                return null;
            }

            var cookies = JsonSerializer.Deserialize<List<Net7SiteStoredCookie>>(
                cookiesJson,
                jsonOptions);

            return cookies is { Count: > 0 }
                ? new Net7SiteSession(file.UserName, cookies)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    public void Save(Net7SiteSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var file = new SessionFile
        {
            UserName = session.UserName,
            ProtectedCookies = PasswordProtector.Protect(
                JsonSerializer.Serialize(session.Cookies, jsonOptions)),
        };

        File.WriteAllText(
            this.filePath,
            JsonSerializer.Serialize(file, jsonOptions));
    }

    public void Delete()
    {
        if (File.Exists(this.filePath))
        {
            File.Delete(this.filePath);
        }
    }

    private sealed class SessionFile
    {
        public string UserName { get; set; } = "";

        public string? ProtectedCookies { get; set; }
    }
}
