namespace Net7ClientManager.Contributions;

using System.Security.Cryptography;
using Net7ClientManager.Models;
using Net7ClientManager.Services;
using Net7ClientManager.Social;

internal sealed class ForgeContributionIdentityService
{
    private readonly SemaphoreSlim identityLock = new(1, 1);
    private readonly HashSet<string> confirmedPilots =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ForgeContributionClient client;
    private readonly ForgeContributionSettings settings;
    private readonly Action saveSettings;

    public ForgeContributionIdentityService(
        ForgeContributionClient client,
        ForgeContributionSettings settings,
        Action saveSettings)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(saveSettings);

        this.client = client;
        this.settings = settings;
        this.saveSettings = saveSettings;
    }

    public bool HasIdentity =>
        !string.IsNullOrWhiteSpace(this.settings.ContributorId);

    public ForgeIdentityStatusSnapshot GetStatus() => new(
        this.HasIdentity,
        Normalize(this.settings.ContributorId),
        Normalize(this.settings.RecoveryRequestId),
        Normalize(this.settings.RecoveryCode),
        Normalize(this.settings.RecoveryStatus),
        Normalize(this.settings.RecoveryPilotName),
        this.settings.RecoveryCreatedAtUtc,
        this.settings.RecoveryExpiresAtUtc,
        this.settings.RecoveryResolvedAtUtc);

    public ForgeContributionIdentity? TryGetExistingIdentity()
    {
        if (string.IsNullOrWhiteSpace(this.settings.ContributorId) ||
            string.IsNullOrWhiteSpace(this.settings.PublicKey))
        {
            return null;
        }

        var privateKey = PasswordProtector.Unprotect(
            this.settings.ProtectedPrivateKey);
        if (string.IsNullOrWhiteSpace(privateKey))
        {
            return null;
        }

        ValidatePrivateKey(privateKey);
        return new ForgeContributionIdentity(
            this.settings.ContributorId.Trim(),
            privateKey,
            this.settings.PublicKey.Trim());
    }

    public async Task<ForgeContributionIdentity> EnsureAsync(
        string livePilotName,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(livePilotName);
        livePilotName = livePilotName.Trim();

        await this.identityLock.WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            var identity = this.EnsureKeyMaterial();

            if (!string.IsNullOrWhiteSpace(this.settings.RecoveryRequestId) &&
                !this.HasIdentity)
            {
                var recoveryStatus = await this.RefreshRecoveryCoreAsync(
                        cancellationToken)
                    .ConfigureAwait(false);
                if (recoveryStatus.HasPendingRecovery)
                {
                    throw new ForgeIdentityRecoveryPendingException(
                        recoveryStatus);
                }

                identity = this.EnsureKeyMaterial();
            }

            if (this.HasIdentity && this.confirmedPilots.Contains(livePilotName))
            {
                return identity with
                {
                    ContributorId = this.settings.ContributorId!.Trim(),
                };
            }

            var unsignedRegistration =
                new ForgeContributorRegistrationRequest
                {
                    PublicKey = identity.PublicKey,
                    LivePilotName = livePilotName,
                };
            var registrationRequest = unsignedRegistration with
            {
                Signature = identity.Sign(unsignedRegistration),
            };

            ForgeContributorRegistrationResponse registration;
            try
            {
                registration = await this.client.RegisterAsync(
                        registrationRequest,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (ForgeIdentityApiException exception) when (
                string.Equals(
                    exception.Code,
                    "recovery_required",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    exception.Code,
                    "recovery_pending",
                    StringComparison.OrdinalIgnoreCase))
            {
                var recovery = await this.BeginRecoveryCoreAsync(
                        identity,
                        livePilotName,
                        deviceLabel: null,
                        cancellationToken)
                    .ConfigureAwait(false);
                throw new ForgeIdentityRecoveryPendingException(recovery);
            }

            if (this.HasIdentity &&
                !string.Equals(
                    this.settings.ContributorId,
                    registration.ContributorId,
                    StringComparison.Ordinal))
            {
                throw new ForgeIdentityException(
                    "Net7 Forge returned a different identity for this installation key.");
            }

            this.settings.ContributorId = registration.ContributorId;
            this.settings.RegisteredAtUtc = registration.RegisteredAtUtc;
            this.ClearRecovery();
            this.confirmedPilots.Add(livePilotName);
            this.saveSettings();

            return identity with
            {
                ContributorId = registration.ContributorId,
            };
        }
        catch (Exception exception) when (
            exception is CryptographicException or FormatException)
        {
            this.ResetKeyMaterial();
            throw;
        }
        finally
        {
            this.identityLock.Release();
        }
    }

    public async Task<ForgeContributionIdentity> EnsureMutationIdentityAsync(
        string? livePilotName,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(livePilotName))
        {
            return await this.EnsureAsync(livePilotName, cancellationToken)
                .ConfigureAwait(false);
        }

        if (!this.HasIdentity && this.GetStatus().HasPendingRecovery)
        {
            throw new ForgeIdentityRecoveryPendingException(this.GetStatus());
        }

        return this.TryGetExistingIdentity() ??
            throw new ForgeIdentityRequiredException();
    }

    public async Task ClaimPilotIfRegisteredAsync(
        string livePilotName,
        CancellationToken cancellationToken)
    {
        if (!this.HasIdentity ||
            string.IsNullOrWhiteSpace(livePilotName))
        {
            return;
        }

        _ = await this.EnsureAsync(livePilotName, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ForgeIdentityStatusSnapshot> BeginRecoveryAsync(
        string livePilotName,
        string? deviceLabel,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(livePilotName);

        await this.identityLock.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            if (this.HasIdentity)
            {
                throw new ForgeIdentityException(
                    "This Client Manager installation is already connected to a Forge identity.");
            }

            var identity = this.EnsureKeyMaterial();
            return await this.BeginRecoveryCoreAsync(
                    identity,
                    livePilotName.Trim(),
                    deviceLabel,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            this.identityLock.Release();
        }
    }

    public async Task<ForgeIdentityStatusSnapshot> RefreshRecoveryAsync(
        CancellationToken cancellationToken)
    {
        await this.identityLock.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            return await this.RefreshRecoveryCoreAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            this.identityLock.Release();
        }
    }

    private ForgeContributionIdentity EnsureKeyMaterial()
    {
        var privateKey = PasswordProtector.Unprotect(
            this.settings.ProtectedPrivateKey);

        if (string.IsNullOrWhiteSpace(privateKey) ||
            string.IsNullOrWhiteSpace(this.settings.PublicKey))
        {
            using var generated = ECDsa.Create(
                ECCurve.NamedCurves.nistP256);
            var privateKeyBytes = generated.ExportPkcs8PrivateKey();
            var publicKeyBytes = generated.ExportSubjectPublicKeyInfo();
            privateKey = Convert.ToBase64String(privateKeyBytes);
            this.settings.ProtectedPrivateKey = PasswordProtector.Protect(
                privateKey);
            this.settings.PublicKey = Convert.ToBase64String(publicKeyBytes);
            this.settings.ContributorId = null;
            this.settings.RegisteredAtUtc = null;
            this.ClearRecovery();
            this.saveSettings();
        }

        ValidatePrivateKey(privateKey);
        return new ForgeContributionIdentity(
            Normalize(this.settings.ContributorId) ?? "",
            privateKey,
            this.settings.PublicKey!.Trim());
    }

    private async Task<ForgeIdentityStatusSnapshot> BeginRecoveryCoreAsync(
        ForgeContributionIdentity identity,
        string livePilotName,
        string? deviceLabel,
        CancellationToken cancellationToken)
    {
        var unsigned = new ForgeContributorRecoveryStartRequest
        {
            PublicKey = identity.PublicKey,
            LivePilotName = livePilotName.Trim(),
            DeviceLabel = Normalize(deviceLabel),
        };
        var request = unsigned with
        {
            Signature = identity.Sign(unsigned),
        };
        var response = await this.client.BeginRecoveryAsync(
                request,
                cancellationToken)
            .ConfigureAwait(false);

        this.settings.RecoveryRequestId = response.RecoveryRequestId;
        this.settings.RecoveryCode = response.RecoveryCode;
        this.settings.RecoveryStatus = response.Status;
        this.settings.RecoveryPilotName = livePilotName.Trim();
        this.settings.RecoveryCreatedAtUtc = response.CreatedAtUtc;
        this.settings.RecoveryExpiresAtUtc = response.ExpiresAtUtc;
        this.settings.RecoveryResolvedAtUtc = null;
        this.saveSettings();
        return this.GetStatus();
    }

    private async Task<ForgeIdentityStatusSnapshot> RefreshRecoveryCoreAsync(
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(this.settings.RecoveryRequestId))
        {
            return this.GetStatus();
        }

        var response = await this.client.GetRecoveryStatusAsync(
                this.settings.RecoveryRequestId,
                cancellationToken)
            .ConfigureAwait(false);
        if (response == null)
        {
            this.settings.RecoveryStatus = "not_found";
            this.settings.RecoveryResolvedAtUtc = DateTimeOffset.UtcNow;
            this.saveSettings();
            return this.GetStatus();
        }

        this.settings.RecoveryStatus = response.Status;
        this.settings.RecoveryCreatedAtUtc = response.CreatedAtUtc;
        this.settings.RecoveryExpiresAtUtc = response.ExpiresAtUtc;
        this.settings.RecoveryResolvedAtUtc = response.ResolvedAtUtc;

        if (string.Equals(
                response.Status,
                "approved",
                StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(response.ContributorId))
        {
            this.settings.ContributorId = response.ContributorId.Trim();
            this.settings.RegisteredAtUtc ??= response.ResolvedAtUtc ??
                DateTimeOffset.UtcNow;
            if (!string.IsNullOrWhiteSpace(this.settings.RecoveryPilotName))
            {
                this.confirmedPilots.Add(
                    this.settings.RecoveryPilotName.Trim());
            }
        }

        this.saveSettings();
        return this.GetStatus();
    }

    private void ClearRecovery()
    {
        this.settings.RecoveryRequestId = null;
        this.settings.RecoveryCode = null;
        this.settings.RecoveryStatus = null;
        this.settings.RecoveryPilotName = null;
        this.settings.RecoveryCreatedAtUtc = null;
        this.settings.RecoveryExpiresAtUtc = null;
        this.settings.RecoveryResolvedAtUtc = null;
    }

    private void ResetKeyMaterial()
    {
        this.settings.ContributorId = null;
        this.settings.PublicKey = null;
        this.settings.ProtectedPrivateKey = null;
        this.settings.RegisteredAtUtc = null;
        this.ClearRecovery();
        this.confirmedPilots.Clear();
        this.saveSettings();
    }

    private static void ValidatePrivateKey(string privateKey)
    {
        var bytes = Convert.FromBase64String(privateKey);
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportPkcs8PrivateKey(bytes, out var bytesRead);

        if (bytesRead != bytes.Length || ecdsa.KeySize < 256)
        {
            throw new CryptographicException(
                "The stored contribution identity is invalid.");
        }
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal sealed record ForgeContributionIdentity(
    string ContributorId,
    string PrivateKey,
    string PublicKey)
{
    public string Sign(ForgeContributorRegistrationRequest request) =>
        this.SignPayload(ForgeContributionCanonicalizer.Canonicalize(request));

    public string Sign(ForgeContributorRecoveryStartRequest request) =>
        this.SignPayload(ForgeContributionCanonicalizer.Canonicalize(request));

    public string Sign(ForgeAddonPublicationRequest request) =>
        this.SignPayload(ForgeContributionCanonicalizer.Canonicalize(request));

    public string Sign(ForgeBuildPublicationRequest request) =>
        this.SignPayload(ForgeBuildRequestCanonicalizer.Canonicalize(request));

    public string Sign(ForgeBuildSearchRequest request) =>
        this.SignPayload(ForgeBuildRequestCanonicalizer.Canonicalize(request));

    public string Sign(ForgeBuildAccessRequest request) =>
        this.SignPayload(ForgeBuildRequestCanonicalizer.Canonicalize(request));

    public string Sign(ForgeBuildStarRequest request) =>
        this.SignPayload(ForgeBuildRequestCanonicalizer.Canonicalize(request));

    public string Sign(SocialPresenceUpsertRequest request) =>
        this.SignPayload(ForgeSocialRequestCanonicalizer.Canonicalize(request));

    public string Sign(LookingForGuildUpsertRequest request) =>
        this.SignPayload(ForgeSocialRequestCanonicalizer.Canonicalize(request));

    public string Sign(GuildRecruitmentUpsertRequest request) =>
        this.SignPayload(ForgeSocialRequestCanonicalizer.Canonicalize(request));

    public string Sign(ForgeNpcPresenceContributionRequest request) =>
        this.SignPayload(ForgeContributionCanonicalizer.Canonicalize(request));

    public string Sign(ForgeMobSightingsContributionRequest request) =>
        this.SignPayload(ForgeContributionCanonicalizer.Canonicalize(request));

    public string Sign(ForgeMobLootContributionRequest request) =>
        this.SignPayload(ForgeContributionCanonicalizer.Canonicalize(request));

    public string Sign(ForgeHarvestableResourcesContributionRequest request) =>
        this.SignPayload(ForgeContributionCanonicalizer.Canonicalize(request));

    public string Sign(ForgeStationServicesContributionRequest request) =>
        this.SignPayload(ForgeContributionCanonicalizer.Canonicalize(request));

    public string Sign(ForgeVendorInventoryContributionRequest request) =>
        this.SignPayload(ForgeContributionCanonicalizer.Canonicalize(request));

    public string Sign(ForgeNavigationObjectsContributionRequest request) =>
        this.SignPayload(ForgeContributionCanonicalizer.Canonicalize(request));

    public string Sign(ForgeProductionRecipeContributionRequest request) =>
        this.SignPayload(ForgeContributionCanonicalizer.Canonicalize(request));

    public string Sign(ForgeMissionContributionRequest request) =>
        this.SignPayload(ForgeContributionCanonicalizer.Canonicalize(request));

    public string Sign(ForgeJobOfferContributionRequest request) =>
        this.SignPayload(ForgeContributionCanonicalizer.Canonicalize(request));

    public string Sign(ForgeGravityWellBoundaryContributionRequest request) =>
        this.SignPayload(ForgeContributionCanonicalizer.Canonicalize(request));

    private string SignPayload(ReadOnlySpan<byte> payload)
    {
        var privateKeyBytes = Convert.FromBase64String(this.PrivateKey);
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportPkcs8PrivateKey(privateKeyBytes, out var bytesRead);

        if (bytesRead != privateKeyBytes.Length)
        {
            throw new CryptographicException(
                "The stored contribution private key is invalid.");
        }

        return Convert.ToBase64String(
            ecdsa.SignData(payload, HashAlgorithmName.SHA256));
    }
}
