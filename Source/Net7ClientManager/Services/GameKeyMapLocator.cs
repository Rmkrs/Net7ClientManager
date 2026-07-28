namespace Net7ClientManager.Services;

using System.Collections.Concurrent;
using System.Diagnostics;
using Net7ClientManager.Models;

/// <summary>
/// Resolves the client-authored keymap from the monitored client.exe path.
/// A standard installation keeps client.exe in release and writes keymap.ini
/// to ..\Data\client\output.
/// </summary>
internal sealed class GameKeyMapLocator
{
    private readonly ConcurrentDictionary<int, string> cachedPaths = new();
    private readonly ConcurrentDictionary<int, string> cachedMixFilesDirectories = new();
    private readonly ConcurrentDictionary<int, string> cachedBassetIniPaths = new();

    public string? Locate(ClientInstance client)
    {
        var outputDirectory = this.LocateOutputDirectory(client);

        return outputDirectory == null
            ? null
            : Path.Combine(outputDirectory, "keymap.ini");
    }

    public string? LocateShortcutIni(ClientInstance client)
    {
        var outputDirectory = this.LocateOutputDirectory(client);

        return outputDirectory == null
            ? null
            : Path.Combine(outputDirectory, "shortcut.ini");
    }

    public string? LocateMixFilesDirectory(string outputDirectory)
    {
        var clientDataDirectory = TryResolveClientDataDirectory(outputDirectory);

        if (clientDataDirectory == null)
        {
            return null;
        }

        var path = Path.Combine(clientDataDirectory, "mixfiles");
        return Directory.Exists(path)
            ? path
            : null;
    }

    public string? LocateIniDirectory(string outputDirectory)
    {
        var clientDataDirectory = TryResolveClientDataDirectory(outputDirectory);

        if (clientDataDirectory == null)
        {
            return null;
        }

        var path = Path.Combine(clientDataDirectory, "ini");
        return Directory.Exists(path)
            ? path
            : null;
    }

    public string? LocateBassetIni(string outputDirectory)
    {
        var iniDirectory = this.LocateIniDirectory(outputDirectory);

        if (iniDirectory == null)
        {
            return null;
        }

        var path = Path.Combine(iniDirectory, "basset.ini");
        return File.Exists(path)
            ? path
            : null;
    }

    public string? LocateMixFilesDirectory(ClientInstance client)
    {
        if (this.cachedMixFilesDirectories.TryGetValue(
                client.ProcessId,
                out var cachedPath))
        {
            return cachedPath;
        }

        var path = TryResolveMixFilesDirectoryFromClientProcess(client.Process);

        if (path == null)
        {
            return null;
        }

        this.cachedMixFilesDirectories[client.ProcessId] = path;
        return path;
    }

    public string? LocateIniDirectory(ClientInstance client)
    {
        var bassetIniPath = this.LocateBassetIni(client);

        return bassetIniPath == null
            ? null
            : Path.GetDirectoryName(bassetIniPath);
    }

    public string? LocateBassetIni(ClientInstance client)
    {
        if (this.cachedBassetIniPaths.TryGetValue(
                client.ProcessId,
                out var cachedPath))
        {
            return cachedPath;
        }

        var path = TryResolveBassetIniFromClientProcess(client.Process);

        if (path == null)
        {
            return null;
        }

        this.cachedBassetIniPaths[client.ProcessId] = path;
        return path;
    }

    public string? LocateOutputDirectory(ClientInstance client)
    {
        if (this.cachedPaths.TryGetValue(
                client.ProcessId,
                out var cachedPath))
        {
            return Path.GetDirectoryName(cachedPath);
        }

        var path = TryResolveFromClientProcess(client.Process);

        if (path == null)
        {
            return null;
        }

        this.cachedPaths[client.ProcessId] = path;
        return Path.GetDirectoryName(path);
    }

    public void ForgetProcess(int processId)
    {
        _ = this.cachedPaths.TryRemove(processId, out _);
        _ = this.cachedMixFilesDirectories.TryRemove(processId, out _);
        _ = this.cachedBassetIniPaths.TryRemove(processId, out _);
    }

    private static string? TryResolveClientDataDirectory(string outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            return null;
        }

        try
        {
            var normalizedOutputDirectory = Path.GetFullPath(outputDirectory);
            var outputParent = Directory.GetParent(normalizedOutputDirectory);
            var clientDataDirectory = outputParent?.FullName;

            return clientDataDirectory != null &&
                   Directory.Exists(normalizedOutputDirectory)
                ? clientDataDirectory
                : null;
        }
        catch (Exception ex) when (
            ex is ArgumentException or
            NotSupportedException or
            PathTooLongException or
            System.Security.SecurityException)
        {
            return null;
        }
    }

    private static string? TryResolveBassetIniFromClientProcess(Process process)
    {
        var releaseDirectory = TryResolveReleaseDirectory(process);

        if (releaseDirectory == null)
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(
                Path.Combine(
                    releaseDirectory,
                    "..",
                    "Data",
                    "client",
                    "ini",
                    "basset.ini"));
        }
        catch (Exception ex) when (
            ex is ArgumentException or
            NotSupportedException or
            PathTooLongException or
            System.Security.SecurityException)
        {
            return null;
        }
    }

    private static string? TryResolveMixFilesDirectoryFromClientProcess(Process process)
    {
        var releaseDirectory = TryResolveReleaseDirectory(process);

        if (releaseDirectory == null)
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(
                Path.Combine(
                    releaseDirectory,
                    "..",
                    "Data",
                    "client",
                    "mixfiles"));
        }
        catch (Exception ex) when (
            ex is ArgumentException or
            NotSupportedException or
            PathTooLongException or
            System.Security.SecurityException)
        {
            return null;
        }
    }

    private static string? TryResolveFromClientProcess(Process process)
    {
        var releaseDirectory = TryResolveReleaseDirectory(process);

        if (releaseDirectory == null)
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(
                Path.Combine(
                    releaseDirectory,
                    "..",
                    "Data",
                    "client",
                    "output",
                    "keymap.ini"));
        }
        catch (Exception ex) when (
            ex is ArgumentException or
            NotSupportedException or
            PathTooLongException or
            System.Security.SecurityException)
        {
            return null;
        }
    }

    private static string? TryResolveReleaseDirectory(Process process)
    {
        string? executablePath;

        try
        {
            executablePath = process.MainModule?.FileName;
        }
        catch (InvalidOperationException)
        {
            // The process exited between discovery and path resolution.
            return null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Process metadata can be temporarily unavailable during startup.
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return null;
        }

        try
        {
            return Path.GetDirectoryName(Path.GetFullPath(executablePath));
        }
        catch (Exception ex) when (
            ex is ArgumentException or
            NotSupportedException or
            PathTooLongException or
            System.Security.SecurityException)
        {
            return null;
        }
    }
}
