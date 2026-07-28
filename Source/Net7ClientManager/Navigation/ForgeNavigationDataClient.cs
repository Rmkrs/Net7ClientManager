namespace Net7ClientManager.Navigation;

using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

internal sealed class ForgeNavigationDataClient : IDisposable
{
    private const int MaximumSupportedContractVersion = 1;
    private const long MaximumPackageBytes = 64L * 1024 * 1024;
    private const int MaximumRecipeCatalogBytes = 16 * 1024 * 1024;
    private const int MaximumMissionCatalogBytes = 32 * 1024 * 1024;
    private static readonly Uri serviceBaseUri = new(
        "https://net7forge.com/",
        UriKind.Absolute);

    private readonly HttpClient httpClient;
    private readonly NavigationDataPathProvider paths = new();

    public ForgeNavigationDataClient()
    {
        this.httpClient = new HttpClient
        {
            BaseAddress = serviceBaseUri,
            Timeout = TimeSpan.FromSeconds(30),
        };
    }

    public async Task<ForgeNavigationUpdateResponse?> GetUpdateAsync(
        string datasetEpoch,
        long fromRevision,
        int contractVersion,
        string activeSnapshotSha256,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(datasetEpoch);
        ArgumentException.ThrowIfNullOrWhiteSpace(activeSnapshotSha256);

        var requestUri = string.Create(
            CultureInfo.InvariantCulture,
            $"api/v1/navigation/updates?fromRevision={fromRevision}&contractVersion={contractVersion}&supportedContractVersion={MaximumSupportedContractVersion}&datasetEpoch={Uri.EscapeDataString(datasetEpoch)}&activeSnapshotSha256={Uri.EscapeDataString(activeSnapshotSha256)}");
        using var response = await this.httpClient.GetAsync(
                requestUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return null;
        }

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            throw new InvalidOperationException(
                "Net7 Forge no longer supports this navigation distribution contract.");
        }

        response.EnsureSuccessStatusCode();
        var update = await response.Content
            .ReadFromJsonAsync<ForgeNavigationUpdateResponse>(
                ForgeNavigationDataJson.ReadOptions,
                cancellationToken)
            .ConfigureAwait(false) ??
            throw new InvalidOperationException(
                "Net7 Forge returned an empty navigation update response.");
        ValidateUpdate(update, datasetEpoch, fromRevision, contractVersion);
        return update;
    }

    public async Task<string> DownloadPackageAsync(
        ForgeNavigationUpdateResponse update,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);
        this.paths.EnsureDirectories();
        var downloadUri = ResolveDownloadUri(update.DownloadUrl);
        var expectedPath = string.Concat(
            "/api/v1/data-packages/",
            update.PackageSha256);

        if (!string.Equals(
                downloadUri.AbsolutePath,
                expectedPath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Net7 Forge package URL does not match its advertised hash.");
        }

        var temporaryPath = Path.Combine(
            this.paths.DownloadDirectory,
            string.Concat(
                update.PackageSha256,
                ".",
                Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture),
                ".download"));

        try
        {
            using var response = await this.httpClient.GetAsync(
                    downloadUri,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            if (response.Content.Headers.ContentLength is { } contentLength &&
                contentLength != update.PackageSize)
            {
                throw new InvalidOperationException(
                    "Net7 Forge package length does not match its metadata.");
            }

            await using var input = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            await using var output = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                useAsync: true);
            var buffer = new byte[81920];
            long totalBytes = 0;

            while (true)
            {
                var bytesRead = await input.ReadAsync(
                        buffer,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (bytesRead == 0)
                {
                    break;
                }

                totalBytes += bytesRead;

                if (totalBytes > update.PackageSize ||
                    totalBytes > MaximumPackageBytes)
                {
                    throw new InvalidOperationException(
                        "Net7 Forge package exceeds the advertised size.");
                }

                await output.WriteAsync(
                        buffer.AsMemory(0, bytesRead),
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            await output.FlushAsync(cancellationToken)
                .ConfigureAwait(false);

            if (totalBytes != update.PackageSize)
            {
                throw new InvalidOperationException(
                    "Net7 Forge package download was truncated.");
            }

            output.Close();

            if (!string.Equals(
                    ForgeNavigationHash.ComputeFileSha256(temporaryPath),
                    update.PackageSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Net7 Forge package download failed its SHA-256 check.");
            }

            return temporaryPath;
        }
        catch
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            throw;
        }
    }

    public async Task<ForgeProductionRecipeCatalogResponse>
        GetProductionRecipeCatalogAsync(
            CancellationToken cancellationToken)
    {
        using var response = await this.httpClient.GetAsync(
                "api/v1/production/recipes",
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength is { } contentLength &&
            contentLength > MaximumRecipeCatalogBytes)
        {
            throw new InvalidOperationException(
                "Net7 Forge returned an oversized production-recipe catalogue.");
        }

        var bytes = await ReadBoundedContentAsync(
                response.Content,
                MaximumRecipeCatalogBytes,
                cancellationToken)
            .ConfigureAwait(false);

        return JsonSerializer.Deserialize<ForgeProductionRecipeCatalogResponse>(
                bytes,
                ForgeNavigationDataJson.ReadOptions) ??
            throw new InvalidOperationException(
                "Net7 Forge returned an empty production-recipe catalogue.");
    }

    public async Task<ForgeMissionCatalogResponse>
        GetMissionCatalogAsync(
            CancellationToken cancellationToken)
    {
        using var response = await this.httpClient.GetAsync(
                "api/v1/missions",
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength is { } contentLength &&
            contentLength > MaximumMissionCatalogBytes)
        {
            throw new InvalidOperationException(
                "Net7 Forge returned an oversized mission catalogue.");
        }

        var bytes = await ReadBoundedContentAsync(
                response.Content,
                MaximumMissionCatalogBytes,
                cancellationToken)
            .ConfigureAwait(false);

        return JsonSerializer.Deserialize<ForgeMissionCatalogResponse>(
                bytes,
                ForgeNavigationDataJson.ReadOptions) ??
            throw new InvalidOperationException(
                "Net7 Forge returned an empty mission catalogue.");
    }

    public void Dispose()
    {
        this.httpClient.Dispose();
    }

    private static async Task<byte[]> ReadBoundedContentAsync(
        HttpContent content,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        await using var input = await content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using MemoryStream output = new();
        var buffer = new byte[81920];

        while (true)
        {
            var bytesRead = await input.ReadAsync(
                    buffer,
                    cancellationToken)
                .ConfigureAwait(false);

            if (bytesRead == 0)
            {
                return output.ToArray();
            }

            if (output.Length + bytesRead > maximumBytes)
            {
                throw new InvalidOperationException(
                    "Net7 Forge returned an oversized response.");
            }

            await output.WriteAsync(
                    buffer.AsMemory(0, bytesRead),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static void ValidateUpdate(
        ForgeNavigationUpdateResponse update,
        string datasetEpoch,
        long fromRevision,
        int contractVersion)
    {
        var epochMatches = string.Equals(
            update.DatasetEpoch,
            datasetEpoch,
            StringComparison.Ordinal);
        var isFull = string.Equals(
            update.Mode,
            "full",
            StringComparison.Ordinal);
        var isDelta = string.Equals(
            update.Mode,
            "delta",
            StringComparison.Ordinal);

        if (update.FromRevision != fromRevision ||
            string.IsNullOrWhiteSpace(update.DatasetEpoch) ||
            string.IsNullOrWhiteSpace(update.Reason) ||
            update.ToRevision <= 0 ||
            (epochMatches && update.ToRevision <= fromRevision) ||
            (!epochMatches && !isFull) ||
            update.ContractVersion != contractVersion ||
            update.ContractVersion != MaximumSupportedContractVersion ||
            update.PackageSize <= 0 ||
            update.PackageSize > MaximumPackageBytes ||
            !ForgeNavigationHash.IsSha256(update.PackageSha256) ||
            !ForgeNavigationHash.IsSha256(update.ResultSnapshotSha256) ||
            (!isFull && !isDelta))
        {
            throw new InvalidOperationException(
                "Net7 Forge returned invalid navigation update metadata.");
        }
    }

    private static Uri ResolveDownloadUri(string downloadUrl)
    {
        if (string.IsNullOrWhiteSpace(downloadUrl) ||
            downloadUrl.Contains("\\", StringComparison.Ordinal) ||
            downloadUrl.Contains("#", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Net7 Forge returned an invalid package URL.");
        }

        if (Uri.TryCreate(downloadUrl, UriKind.Absolute, out var absoluteUri))
        {
            if (!string.Equals(absoluteUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(absoluteUri.Host, serviceBaseUri.Host, StringComparison.OrdinalIgnoreCase) ||
                (absoluteUri.Port != 443 && absoluteUri.Port != -1) ||
                !string.IsNullOrEmpty(absoluteUri.UserInfo) ||
                !string.IsNullOrEmpty(absoluteUri.Query) ||
                !string.IsNullOrEmpty(absoluteUri.Fragment) ||
                !absoluteUri.AbsolutePath.StartsWith(
                    "/api/v1/data-packages/",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Net7 Forge returned a package URL outside the trusted origin.");
            }

            return absoluteUri;
        }

        if (downloadUrl.StartsWith("//", StringComparison.Ordinal) ||
            !downloadUrl.StartsWith(
                "/api/v1/data-packages/",
                StringComparison.Ordinal) ||
            downloadUrl.Contains("?", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Net7 Forge returned an invalid relative package URL.");
        }

        return new Uri(serviceBaseUri, downloadUrl);
    }
}
