namespace Net7ClientManager.Contributions;

using System.Net.Http.Json;
using System.Text.Json;

internal sealed class ForgeContributionClient : IDisposable
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

    public async Task<ForgeContributorRegistrationResponse> RegisterAsync(
        ForgeContributorRegistrationRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await this.httpClient.PostAsJsonAsync(
                "api/v1/contributors/register",
                request,
                jsonOptions,
                cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken)
            .ConfigureAwait(false);

        return await response.Content
            .ReadFromJsonAsync<ForgeContributorRegistrationResponse>(
                jsonOptions,
                cancellationToken)
            .ConfigureAwait(false) ??
            throw new InvalidOperationException(
                "Net7 Forge returned an empty contributor registration response.");
    }

    public async Task<ForgeContributorRecoveryStartResponse> BeginRecoveryAsync(
        ForgeContributorRecoveryStartRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await this.httpClient.PostAsJsonAsync(
                "api/v1/contributors/recovery",
                request,
                jsonOptions,
                cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken)
            .ConfigureAwait(false);

        return await response.Content
            .ReadFromJsonAsync<ForgeContributorRecoveryStartResponse>(
                jsonOptions,
                cancellationToken)
            .ConfigureAwait(false) ??
            throw new InvalidOperationException(
                "Net7 Forge returned an empty contributor recovery response.");
    }

    public async Task<ForgeContributorRecoveryStatusResponse?>
        GetRecoveryStatusAsync(
            string recoveryRequestId,
            CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recoveryRequestId);
        using var response = await this.httpClient.GetAsync(
                string.Concat(
                    "api/v1/contributors/recovery/",
                    Uri.EscapeDataString(recoveryRequestId.Trim())),
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken)
            .ConfigureAwait(false);
        return await response.Content
            .ReadFromJsonAsync<ForgeContributorRecoveryStatusResponse>(
                jsonOptions,
                cancellationToken)
            .ConfigureAwait(false) ??
            throw new InvalidOperationException(
                "Net7 Forge returned an empty contributor recovery status response.");
    }

    public async Task<ForgeNpcPresenceContributionResponse> SubmitNpcPresenceAsync(
        ForgeNpcPresenceContributionRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await this.httpClient.PostAsJsonAsync(
                "api/v1/contributions/npc-presence",
                request,
                jsonOptions,
                cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken)
            .ConfigureAwait(false);

        return await response.Content
            .ReadFromJsonAsync<ForgeNpcPresenceContributionResponse>(
                jsonOptions,
                cancellationToken)
            .ConfigureAwait(false) ??
            throw new InvalidOperationException(
                "Net7 Forge returned an empty NPC contribution response.");
    }

    public async Task<ForgeNavigationObjectsContributionResponse> SubmitNavigationObjectsAsync(
        ForgeNavigationObjectsContributionRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await this.httpClient.PostAsJsonAsync(
                "api/v1/contributions/navigation-objects",
                request,
                jsonOptions,
                cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken)
            .ConfigureAwait(false);

        return await response.Content
            .ReadFromJsonAsync<ForgeNavigationObjectsContributionResponse>(
                jsonOptions,
                cancellationToken)
            .ConfigureAwait(false) ??
            throw new InvalidOperationException(
                "Net7 Forge returned an empty navigation-object contribution response.");
    }


    public async Task<ForgeAddonPublicationResponse> PublishAddonAsync(
        ForgeAddonPublicationRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await this.httpClient.PostAsJsonAsync(
                "api/v1/addons/publish",
                request,
                jsonOptions,
                cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken)
            .ConfigureAwait(false);

        return await response.Content
            .ReadFromJsonAsync<ForgeAddonPublicationResponse>(
                jsonOptions,
                cancellationToken)
            .ConfigureAwait(false) ??
            throw new InvalidOperationException(
                "Net7 Forge returned an empty addon publication response.");
    }

    public async Task<ForgeGravityWellBoundaryContributionResponse> SubmitGravityWellBoundariesAsync(
        ForgeGravityWellBoundaryContributionRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await this.httpClient.PostAsJsonAsync(
                "api/v1/contributions/gravity-well-boundaries",
                request,
                jsonOptions,
                cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken)
            .ConfigureAwait(false);

        return await response.Content
            .ReadFromJsonAsync<ForgeGravityWellBoundaryContributionResponse>(
                jsonOptions,
                cancellationToken)
            .ConfigureAwait(false) ??
            throw new InvalidOperationException(
                "Net7 Forge returned an empty gravity-well boundary contribution response.");
    }

    public async Task<ForgeMobSightingsContributionResponse> SubmitMobSightingsAsync(
        ForgeMobSightingsContributionRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await this.httpClient.PostAsJsonAsync(
                "api/v1/contributions/mob-sightings",
                request,
                jsonOptions,
                cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken)
            .ConfigureAwait(false);

        return await response.Content
            .ReadFromJsonAsync<ForgeMobSightingsContributionResponse>(
                jsonOptions,
                cancellationToken)
            .ConfigureAwait(false) ??
            throw new InvalidOperationException(
                "Net7 Forge returned an empty mob-sighting contribution response.");
    }

    public async Task<ForgeMobLootContributionResponse> SubmitMobLootAsync(
        ForgeMobLootContributionRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await this.httpClient.PostAsJsonAsync(
                "api/v1/contributions/mob-loot",
                request,
                jsonOptions,
                cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken)
            .ConfigureAwait(false);

        return await response.Content
            .ReadFromJsonAsync<ForgeMobLootContributionResponse>(
                jsonOptions,
                cancellationToken)
            .ConfigureAwait(false) ??
            throw new InvalidOperationException(
                "Net7 Forge returned an empty mob-loot contribution response.");
    }

    public async Task<ForgeHarvestableResourcesContributionResponse> SubmitHarvestableResourcesAsync(
        ForgeHarvestableResourcesContributionRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await this.httpClient.PostAsJsonAsync(
                "api/v1/contributions/harvestable-resources",
                request,
                jsonOptions,
                cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken)
            .ConfigureAwait(false);

        return await response.Content
            .ReadFromJsonAsync<ForgeHarvestableResourcesContributionResponse>(
                jsonOptions,
                cancellationToken)
            .ConfigureAwait(false) ??
            throw new InvalidOperationException(
                "Net7 Forge returned an empty harvestable-resource contribution response.");
    }

    public async Task<ForgeStationServicesContributionResponse> SubmitStationServicesAsync(
        ForgeStationServicesContributionRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await this.httpClient.PostAsJsonAsync(
                "api/v1/contributions/station-services",
                request,
                jsonOptions,
                cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken)
            .ConfigureAwait(false);

        return await response.Content
            .ReadFromJsonAsync<ForgeStationServicesContributionResponse>(
                jsonOptions,
                cancellationToken)
            .ConfigureAwait(false) ??
            throw new InvalidOperationException(
                "Net7 Forge returned an empty station-services contribution response.");
    }

    public async Task<ForgeVendorInventoryContributionResponse> SubmitVendorInventoryAsync(
        ForgeVendorInventoryContributionRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await this.httpClient.PostAsJsonAsync(
                "api/v1/contributions/vendor-inventories",
                request,
                jsonOptions,
                cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken)
            .ConfigureAwait(false);

        return await response.Content
            .ReadFromJsonAsync<ForgeVendorInventoryContributionResponse>(
                jsonOptions,
                cancellationToken)
            .ConfigureAwait(false) ??
            throw new InvalidOperationException(
                "Net7 Forge returned an empty vendor-inventory contribution response.");
    }

    public async Task<ForgeProductionRecipeContributionResponse> SubmitProductionRecipesAsync(
        ForgeProductionRecipeContributionRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await this.httpClient.PostAsJsonAsync(
                "api/v1/contributions/production-recipes",
                request,
                jsonOptions,
                cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken)
            .ConfigureAwait(false);

        return await response.Content
            .ReadFromJsonAsync<ForgeProductionRecipeContributionResponse>(
                jsonOptions,
                cancellationToken)
            .ConfigureAwait(false) ??
            throw new InvalidOperationException(
                "Net7 Forge returned an empty production-recipe contribution response.");
    }


    public async Task<ForgeMissionContributionResponse> SubmitMissionsAsync(
        ForgeMissionContributionRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await this.httpClient.PostAsJsonAsync(
                "api/v1/contributions/missions",
                request,
                jsonOptions,
                cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken)
            .ConfigureAwait(false);

        return await response.Content
            .ReadFromJsonAsync<ForgeMissionContributionResponse>(
                jsonOptions,
                cancellationToken)
            .ConfigureAwait(false) ??
            throw new InvalidOperationException(
                "Net7 Forge returned an empty mission contribution response.");
    }

    public async Task<ForgeJobOfferContributionResponse> SubmitJobOffersAsync(
        ForgeJobOfferContributionRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await this.httpClient.PostAsJsonAsync(
                "api/v1/contributions/jobs",
                request,
                jsonOptions,
                cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken)
            .ConfigureAwait(false);

        return await response.Content
            .ReadFromJsonAsync<ForgeJobOfferContributionResponse>(
                jsonOptions,
                cancellationToken)
            .ConfigureAwait(false) ??
            throw new InvalidOperationException(
                "Net7 Forge returned an empty job contribution response.");
    }

    public async Task<ForgeBuildPublicationResponse> PublishBuildAsync(
        ForgeBuildPublicationRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await this.httpClient.PostAsJsonAsync(
                "api/v1/builds/publish",
                request,
                jsonOptions,
                cancellationToken)
            .ConfigureAwait(false);
        await EnsureBuildSuccessAsync(response, cancellationToken)
            .ConfigureAwait(false);
        return await response.Content
            .ReadFromJsonAsync<ForgeBuildPublicationResponse>(
                jsonOptions,
                cancellationToken)
            .ConfigureAwait(false) ??
            throw new InvalidOperationException(
                "Net7 Forge returned an empty build publication response.");
    }

    public async Task<ForgeBuildSearchResponse> SearchBuildsAsync(
        ForgeBuildSearchRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await this.httpClient.PostAsJsonAsync(
                "api/v1/builds/search",
                request,
                jsonOptions,
                cancellationToken)
            .ConfigureAwait(false);
        await EnsureBuildSuccessAsync(response, cancellationToken)
            .ConfigureAwait(false);
        return await response.Content
            .ReadFromJsonAsync<ForgeBuildSearchResponse>(
                jsonOptions,
                cancellationToken)
            .ConfigureAwait(false) ??
            throw new InvalidOperationException(
                "Net7 Forge returned an empty build search response.");
    }

    public async Task<ForgeBuildDetailsResponse> GetBuildDetailsAsync(
        ForgeBuildAccessRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await this.httpClient.PostAsJsonAsync(
                $"api/v1/builds/{Uri.EscapeDataString(request.BuildId)}/details",
                request,
                jsonOptions,
                cancellationToken)
            .ConfigureAwait(false);
        await EnsureBuildSuccessAsync(response, cancellationToken)
            .ConfigureAwait(false);
        return await response.Content
            .ReadFromJsonAsync<ForgeBuildDetailsResponse>(
                jsonOptions,
                cancellationToken)
            .ConfigureAwait(false) ??
            throw new InvalidOperationException(
                "Net7 Forge returned an empty build-details response.");
    }

    public async Task<ForgeBuildVersionResponse> GetBuildVersionAsync(
        ForgeBuildAccessRequest request,
        CancellationToken cancellationToken)
    {
        if (!request.Version.HasValue)
        {
            throw new ArgumentException(
                "A Forge build version is required.",
                nameof(request));
        }

        using var response = await this.httpClient.PostAsJsonAsync(
                $"api/v1/builds/{Uri.EscapeDataString(request.BuildId)}/versions/{request.Version.Value}",
                request,
                jsonOptions,
                cancellationToken)
            .ConfigureAwait(false);
        await EnsureBuildSuccessAsync(response, cancellationToken)
            .ConfigureAwait(false);
        return await response.Content
            .ReadFromJsonAsync<ForgeBuildVersionResponse>(
                jsonOptions,
                cancellationToken)
            .ConfigureAwait(false) ??
            throw new InvalidOperationException(
                "Net7 Forge returned an empty build-version response.");
    }

    public async Task<ForgeBuildStarResponse> SetBuildStarAsync(
        ForgeBuildStarRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await this.httpClient.PostAsJsonAsync(
                $"api/v1/builds/{Uri.EscapeDataString(request.BuildId)}/star",
                request,
                jsonOptions,
                cancellationToken)
            .ConfigureAwait(false);
        await EnsureBuildSuccessAsync(response, cancellationToken)
            .ConfigureAwait(false);
        return await response.Content
            .ReadFromJsonAsync<ForgeBuildStarResponse>(
                jsonOptions,
                cancellationToken)
            .ConfigureAwait(false) ??
            throw new InvalidOperationException(
                "Net7 Forge returned an empty build-star response.");
    }

    public void Dispose()
    {
        this.httpClient.Dispose();
    }

    private static async Task EnsureBuildSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);
        string code = "forge_build_request_failed";
        string message = response.ReasonPhrase ??
            "Net7 Forge did not provide an error message.";
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var document = JsonDocument.Parse(body);
                if (document.RootElement.ValueKind == JsonValueKind.Object)
                {
                    if (document.RootElement.TryGetProperty("error", out var error) &&
                        error.ValueKind == JsonValueKind.String)
                    {
                        code = error.GetString() ?? code;
                    }
                    if (document.RootElement.TryGetProperty("message", out var detail) &&
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

        throw new ForgeBuildApiException(
            code,
            message,
            (int)response.StatusCode);
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
        var (code, message) = ReadError(
            body,
            "forge_request_failed",
            response.ReasonPhrase ??
            "The server did not provide an error message.");
        throw new ForgeIdentityApiException(
            code,
            message,
            (int)response.StatusCode);
    }

    private static (string Code, string Message) ReadError(
        string body,
        string fallbackCode,
        string fallbackMessage)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return (fallbackCode, fallbackMessage);
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                var code = document.RootElement.TryGetProperty(
                        "error",
                        out var error) &&
                    error.ValueKind == JsonValueKind.String
                    ? error.GetString() ?? fallbackCode
                    : fallbackCode;
                var message = document.RootElement.TryGetProperty(
                        "message",
                        out var detail) &&
                    detail.ValueKind == JsonValueKind.String
                    ? detail.GetString() ?? fallbackMessage
                    : fallbackMessage;
                return (code, message);
            }
        }
        catch (JsonException)
        {
            // Fall through to the raw response body.
        }

        return (fallbackCode, body.Trim());
    }

}
