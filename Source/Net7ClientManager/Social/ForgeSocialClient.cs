namespace Net7ClientManager.Social;

using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Net7ClientManager.Contributions;

internal sealed class ForgeSocialClient : IDisposable
{
    private static readonly Uri serviceBaseUri = new(
        "https://net7forge.com/",
        UriKind.Absolute);

    private static readonly JsonSerializerOptions jsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly HttpClient httpClient = new()
    {
        BaseAddress = serviceBaseUri,
        Timeout = TimeSpan.FromSeconds(30),
    };

    public Task<SocialUpsertResponse> UpsertPresenceAsync(
        SocialPresenceUpsertRequest request,
        CancellationToken cancellationToken)
    {
        return this.PostAsync<SocialPresenceUpsertRequest, SocialUpsertResponse>(
            "api/v1/social/presence",
            request,
            cancellationToken);
    }

    public Task<SocialUpsertResponse> UpsertLookingForGuildAsync(
        LookingForGuildUpsertRequest request,
        CancellationToken cancellationToken)
    {
        return this.PostAsync<LookingForGuildUpsertRequest, SocialUpsertResponse>(
            "api/v1/social/looking-for-guild",
            request,
            cancellationToken);
    }

    public Task<SocialUpsertResponse> UpsertGuildRecruitmentAsync(
        GuildRecruitmentUpsertRequest request,
        CancellationToken cancellationToken)
    {
        return this.PostAsync<GuildRecruitmentUpsertRequest, SocialUpsertResponse>(
            "api/v1/social/guild-recruitment",
            request,
            cancellationToken);
    }

    public Task<IReadOnlyList<SocialPresenceRecord>> GetPresenceAsync(
        DateTimeOffset updatedAfterUtc,
        CancellationToken cancellationToken)
    {
        return this.GetAsync<SocialPresenceRecord>(
            "api/v1/social/presence",
            updatedAfterUtc,
            cancellationToken);
    }

    public Task<IReadOnlyList<LookingForGuildRecord>> GetLookingForGuildAsync(
        DateTimeOffset updatedAfterUtc,
        CancellationToken cancellationToken)
    {
        return this.GetAsync<LookingForGuildRecord>(
            "api/v1/social/looking-for-guild",
            updatedAfterUtc,
            cancellationToken);
    }

    public Task<IReadOnlyList<GuildRecruitmentRecord>> GetGuildRecruitmentAsync(
        DateTimeOffset updatedAfterUtc,
        CancellationToken cancellationToken)
    {
        return this.GetAsync<GuildRecruitmentRecord>(
            "api/v1/social/guild-recruitment",
            updatedAfterUtc,
            cancellationToken);
    }

    public void Dispose()
    {
        this.httpClient.Dispose();
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(
        string path,
        TRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await this.httpClient.PostAsJsonAsync(
                path,
                request,
                jsonOptions,
                cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken)
            .ConfigureAwait(false);

        return await response.Content.ReadFromJsonAsync<TResponse>(
                jsonOptions,
                cancellationToken)
            .ConfigureAwait(false) ??
            throw new InvalidOperationException(
                "Net7 Forge returned an empty social response.");
    }

    private async Task<IReadOnlyList<T>> GetAsync<T>(
        string path,
        DateTimeOffset updatedAfterUtc,
        CancellationToken cancellationToken)
    {
        var requestUri = string.Create(
            CultureInfo.InvariantCulture,
            $"{path}?updatedAfterUtc={Uri.EscapeDataString(updatedAfterUtc.ToString("O", CultureInfo.InvariantCulture))}");
        using var response = await this.httpClient.GetAsync(
                requestUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken)
            .ConfigureAwait(false);

        return await response.Content.ReadFromJsonAsync<T[]>(
                jsonOptions,
                cancellationToken)
            .ConfigureAwait(false) ?? [];
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);
        var code = "forge_social_request_failed";
        var message = response.ReasonPhrase ??
            "Net7 Forge did not provide an error message.";

        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var document = JsonDocument.Parse(body);
                if (document.RootElement.ValueKind == JsonValueKind.Object)
                {
                    if (document.RootElement.TryGetProperty(
                            "error", out var error) &&
                        error.ValueKind == JsonValueKind.String)
                    {
                        code = error.GetString() ?? code;
                    }

                    if (document.RootElement.TryGetProperty(
                            "message", out var detail) &&
                        detail.ValueKind == JsonValueKind.String)
                    {
                        message = detail.GetString() ?? message;
                    }
                }
            }
            catch (JsonException)
            {
                message = body.Trim();
            }
        }

        throw new ForgeIdentityApiException(
            code,
            message,
            (int)response.StatusCode);
    }

}
