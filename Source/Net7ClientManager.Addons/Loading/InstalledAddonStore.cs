namespace Net7ClientManager.Addons.Loading;

using System.Text.Json;

internal sealed class InstalledAddonStore(string statePath)
{
    private static readonly JsonSerializerOptions jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public AddonInstallationState Load()
    {
        if (!File.Exists(statePath))
        {
            return new AddonInstallationState();
        }

        var state = JsonSerializer.Deserialize<AddonInstallationState>(
                        File.ReadAllText(statePath),
                        jsonOptions) ??
                    throw new InvalidDataException(
                        "installed-addons.json did not contain an object.");

        if (state.FormatVersion is not 1 and not
            AddonInstallationState.CurrentFormatVersion)
        {
            throw new InvalidDataException(
                string.Concat(
                    "Unsupported installed addon state format ",
                    state.FormatVersion,
                    "."));
        }

        return state with
        {
            FormatVersion = AddonInstallationState.CurrentFormatVersion,
            Addons = new Dictionary<string, InstalledAddonReference>(
                state.Addons,
                StringComparer.Ordinal),
        };
    }

    public void Save(AddonInstallationState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var directory = Path.GetDirectoryName(statePath) ??
                        throw new InvalidOperationException(
                            "The installed addon state path has no parent directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = string.Concat(
            statePath,
            ".tmp");

        File.WriteAllText(
            temporaryPath,
            JsonSerializer.Serialize(state, jsonOptions));
        File.Move(
            temporaryPath,
            statePath,
            overwrite: true);
    }
}
