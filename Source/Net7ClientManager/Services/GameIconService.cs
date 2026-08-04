namespace Net7ClientManager.Services;

using Net7ClientManager.Models;
using Net7ClientManager.Observations.Observers;

/// <summary>
/// Resolves client-authored skill and item icon resources through the game's
/// native catalogs, decodes the packaged DDS payload, and caches a compact
/// presentation bitmap for application UI surfaces.
/// </summary>
internal sealed class GameIconService(
    GameKeyMapLocator keyMapLocator) : IDisposable
{
    private readonly object gate = new();
    private readonly Dictionary<string, EnbMixResourceCatalog?> mixCatalogs =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, EnbBassetIconCatalog?> bassetCatalogs =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<BaseIconCacheKey, Bitmap?> baseIcons = [];
    private readonly Dictionary<PresentationIconCacheKey, Bitmap> presentationIcons = [];
    private bool disposed;

    public Image? GetShortcutIcon(
        ClientInstance client,
        GameShortcutPaletteEntry action,
        Size size)
    {
        if (this.disposed)
        {
            return null;
        }

        return action.Kind switch
        {
            GameShortcutKind.Skill => this.GetResourceIcon(
                client,
                action.IconResourceName,
                size,
                action.TintRed,
                action.TintGreen,
                action.TintBlue),
            GameShortcutKind.Equipment or GameShortcutKind.Cargo =>
                this.GetItemIcon(
                    client,
                    action.ItemTemplateId,
                    size),
            _ => null,
        };
    }

    /// <summary>
    /// Resolves an item template through cdata.dat and basset.ini, then reads
    /// the dedicated inventory-icon artwork from the game's MIX1 archives.
    /// This entry point is intentionally reusable by Fleet Loot, World Finder,
    /// vendor, mob-loot, and harvestable-resource presentation.
    /// </summary>
    public Image? GetItemIcon(
        ClientInstance client,
        int? itemTemplateId,
        Size size)
    {
        return this.GetItemIconCore(
            keyMapLocator.LocateBassetIni(client),
            keyMapLocator.LocateMixFilesDirectory(client),
            itemTemplateId,
            size);
    }

    /// <summary>
    /// Resolves an item icon from a previously witnessed Earth & Beyond
    /// installation, without requiring a live client process.
    /// </summary>
    public Image? GetItemIcon(
        string outputDirectory,
        int? itemTemplateId,
        Size size)
    {
        return this.GetItemIconCore(
            keyMapLocator.LocateBassetIni(outputDirectory),
            keyMapLocator.LocateMixFilesDirectory(outputDirectory),
            itemTemplateId,
            size);
    }

    /// <summary>
    /// Resolves a logical game resource from a previously witnessed
    /// installation. Skill-family presentation uses this path while the
    /// Pilot Archive is open without a running client.
    /// </summary>
    public Image? GetResourceIcon(
        string outputDirectory,
        string logicalResourceName,
        Size size,
        float? tintRed,
        float? tintGreen,
        float? tintBlue)
    {
        return this.GetResourceIconCore(
            keyMapLocator.LocateMixFilesDirectory(outputDirectory),
            logicalResourceName,
            size,
            tintRed,
            tintGreen,
            tintBlue);
    }

    private Image? GetItemIconCore(
        string? bassetIniPath,
        string? mixFilesDirectory,
        int? itemTemplateId,
        Size size)
    {
        if (this.disposed ||
            itemTemplateId is not > 0 ||
            size.Width <= 0 ||
            size.Height <= 0 ||
            string.IsNullOrWhiteSpace(bassetIniPath) ||
            string.IsNullOrWhiteSpace(mixFilesDirectory) ||
            !ClientItemTemplateCatalog.TryGetDefinition(
                itemTemplateId.Value,
                out var definition) ||
            definition.IconBassetId is not > 0)
        {
            return null;
        }

        var normalizedBassetPath = NormalizePathKey(bassetIniPath);
        string? iconResourceName;

        lock (this.gate)
        {
            if (this.disposed)
            {
                return null;
            }

            if (!this.bassetCatalogs.TryGetValue(
                    normalizedBassetPath,
                    out var catalog))
            {
                catalog = EnbBassetIconCatalog.Load(normalizedBassetPath);
                this.bassetCatalogs[normalizedBassetPath] = catalog;
            }

            iconResourceName = catalog != null &&
                catalog.TryGetIconResourceName(
                    definition.IconBassetId.Value,
                    out var resolvedResourceName)
                    ? resolvedResourceName
                    : null;
        }

        return string.IsNullOrWhiteSpace(iconResourceName)
            ? null
            : this.GetResourceIconCore(
                mixFilesDirectory,
                iconResourceName,
                size,
                null,
                null,
                null);
    }

    public void ForgetProcess(int processId)
    {
        keyMapLocator.ForgetProcess(processId);
    }

    public void Dispose()
    {
        lock (this.gate)
        {
            if (this.disposed)
            {
                return;
            }

            this.disposed = true;

            foreach (var image in this.presentationIcons.Values)
            {
                image.Dispose();
            }

            foreach (var image in this.baseIcons.Values)
            {
                image?.Dispose();
            }

            this.presentationIcons.Clear();
            this.baseIcons.Clear();
            this.bassetCatalogs.Clear();
            this.mixCatalogs.Clear();
        }
    }

    internal Image? GetResourceIcon(
        ClientInstance client,
        string logicalResourceName,
        Size size,
        float? tintRed,
        float? tintGreen,
        float? tintBlue)
    {
        return this.GetResourceIconCore(
            keyMapLocator.LocateMixFilesDirectory(client),
            logicalResourceName,
            size,
            tintRed,
            tintGreen,
            tintBlue);
    }

    private Image? GetResourceIconCore(
        string? mixFilesDirectory,
        string logicalResourceName,
        Size size,
        float? tintRed,
        float? tintGreen,
        float? tintBlue)
    {
        if (this.disposed ||
            string.IsNullOrWhiteSpace(mixFilesDirectory) ||
            string.IsNullOrWhiteSpace(logicalResourceName) ||
            size.Width <= 0 ||
            size.Height <= 0)
        {
            return null;
        }

        var directoryKey = NormalizePathKey(mixFilesDirectory);
        var baseKey = new BaseIconCacheKey(
            directoryKey,
            logicalResourceName.Trim());
        var presentationKey = new PresentationIconCacheKey(
            baseKey,
            size.Width,
            size.Height,
            GetFloatBits(tintRed),
            GetFloatBits(tintGreen),
            GetFloatBits(tintBlue));

        lock (this.gate)
        {
            if (this.disposed)
            {
                return null;
            }

            if (this.presentationIcons.TryGetValue(
                    presentationKey,
                    out var cachedPresentation))
            {
                return cachedPresentation;
            }

            if (!this.baseIcons.TryGetValue(baseKey, out var baseIcon))
            {
                baseIcon = this.LoadBaseIcon(
                    directoryKey,
                    baseKey.ResourceName);
                this.baseIcons[baseKey] = baseIcon;
            }

            if (baseIcon == null)
            {
                return null;
            }

            var presentation = DdsTextureDecoder.CreatePresentationBitmap(
                baseIcon,
                size,
                tintRed,
                tintGreen,
                tintBlue);

            this.presentationIcons[presentationKey] = presentation;
            return presentation;
        }
    }

    private Bitmap? LoadBaseIcon(
        string mixFilesDirectory,
        string logicalResourceName)
    {
        if (!this.mixCatalogs.TryGetValue(
                mixFilesDirectory,
                out var catalog))
        {
            catalog = EnbMixResourceCatalog.Load(mixFilesDirectory);
            this.mixCatalogs[mixFilesDirectory] = catalog;
        }

        if (catalog == null ||
            !catalog.TryReadResource(
                logicalResourceName,
                out var resourceBytes,
                out _))
        {
            return null;
        }

        return DdsTextureDecoder.DecodeTopMip(resourceBytes);
    }

    private static string NormalizePathKey(string path)
    {
        try
        {
            return Path
                .GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception ex) when (
            ex is ArgumentException or
            NotSupportedException or
            PathTooLongException or
            System.Security.SecurityException)
        {
            return path.Trim();
        }
    }

    private static int GetFloatBits(float? value)
    {
        return value.HasValue
            ? BitConverter.SingleToInt32Bits(value.Value)
            : BitConverter.SingleToInt32Bits(1.0f);
    }

    private sealed record BaseIconCacheKey(
        string MixFilesDirectory,
        string ResourceName);

    private sealed record PresentationIconCacheKey(
        BaseIconCacheKey BaseIcon,
        int Width,
        int Height,
        int TintRedBits,
        int TintGreenBits,
        int TintBlueBits);
}
