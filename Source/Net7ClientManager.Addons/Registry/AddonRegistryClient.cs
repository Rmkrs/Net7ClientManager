namespace Net7ClientManager.Addons.Registry;

using System.Net.Http.Json;
using System.Security.Cryptography;

internal sealed class AddonRegistryClient : IDisposable
{
    private const long MaximumPackageSize = 16 * 1024 * 1024;

    private readonly HttpClient httpClient = new()
    {
        BaseAddress = new Uri(
            "https://net7forge.com/",
            UriKind.Absolute),
        Timeout = TimeSpan.FromSeconds(15),
    };

    public AddonRegistryClient()
    {
        this.httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Net7ClientManager/0.1 AddonRegistryClient");
    }

    public async Task<IReadOnlyList<AddonRegistrySummary>> GetAddonsAsync(
        CancellationToken cancellationToken)
    {
        return await this.httpClient
                   .GetFromJsonAsync<AddonRegistrySummary[]>(
                       "api/v1/addons",
                       cancellationToken)
                   .ConfigureAwait(false) ??
               [];
    }

    public async Task DownloadPackageAsync(
        AddonRegistryRelease release,
        string targetPath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(release);

        if (release.PackageSize <= 0 ||
            release.PackageSize > MaximumPackageSize)
        {
            throw new InvalidDataException(
                "The registry reported an invalid addon package size.");
        }

        using var response = await this.httpClient.GetAsync(
                release.DownloadUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength is { } contentLength &&
            contentLength != release.PackageSize)
        {
            throw new InvalidDataException(
                "The downloaded addon package size does not match the registry metadata.");
        }

        await using var source = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var target = new FileStream(
            targetPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81_920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        using var hash = IncrementalHash.CreateHash(
            HashAlgorithmName.SHA256);
        var buffer = new byte[81_920];
        long total = 0;

        while (true)
        {
            var read = await source.ReadAsync(
                    buffer,
                    cancellationToken)
                .ConfigureAwait(false);

            if (read == 0)
            {
                break;
            }

            total += read;

            if (total > MaximumPackageSize ||
                total > release.PackageSize)
            {
                throw new InvalidDataException(
                    "The downloaded addon package exceeded its expected size.");
            }

            hash.AppendData(buffer.AsSpan(0, read));
            await target.WriteAsync(
                    buffer.AsMemory(0, read),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        await target.FlushAsync(cancellationToken)
            .ConfigureAwait(false);

        var actualHash = Convert.ToHexStringLower(
            hash.GetHashAndReset());

        if (total != release.PackageSize ||
            !string.Equals(
                actualHash,
                release.PackageSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The downloaded addon package did not match the registry metadata.");
        }
    }

    public void Dispose()
    {
        this.httpClient.Dispose();
    }
}
