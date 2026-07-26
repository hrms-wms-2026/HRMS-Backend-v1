using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Mappers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Entities;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Commands.ConfigurePlatformOAuthApp;

/// <summary>
/// Configures an approved OAuth provider (upsert): creates the platform_oauth_apps row
/// on first call, updates operator-writable fields on later calls. Provider comes from
/// the route only. Protocol metadata (authorizationUrl/tokenUrl/defaultScopes) always
/// comes from PlatformOAuthProviderCatalog - the request cannot set or override it.
/// Unsupported providers (unknown, or Phase 2 such as slack) are rejected before any
/// read or write.
/// SECURITY: ClientSecret/PrivateKey are plaintext in this command only; they are
/// encrypted immediately and never stored, returned, or logged raw.
/// </summary>
public sealed record ConfigurePlatformOAuthAppCommand(
    string Provider,
    string? AppName,
    string? LogoUrl,
    string? ClientId,
    string? ClientSecret,
    string? PrivateKey,
    bool? IsActive,
    Guid ActorPlatformUserId) : IRequest<Result<PlatformOAuthAppDto>>;

public sealed class ConfigurePlatformOAuthAppCommandHandler
    : IRequestHandler<ConfigurePlatformOAuthAppCommand, Result<PlatformOAuthAppDto>>
{
    private readonly IPlatformOAuthAppRepository _repo;
    private readonly IEncryptionService _encryption;

    public ConfigurePlatformOAuthAppCommandHandler(
        IPlatformOAuthAppRepository repo,
        IEncryptionService encryption)
    {
        _repo = repo;
        _encryption = encryption;
    }

    public async Task<Result<PlatformOAuthAppDto>> Handle(
        ConfigurePlatformOAuthAppCommand request,
        CancellationToken cancellationToken)
    {
        var provider = PlatformOAuthProviderRules.Normalize(request.Provider);

        if (PlatformOAuthProviderRules.IsPhase2Provider(provider))
            return Result<PlatformOAuthAppDto>.Failure(
                $"Provider '{provider}' is Phase 2 and is not an approved OAuth provider yet.", 400);

        if (!PlatformOAuthProviderCatalog.TryGet(provider, out var definition))
            return Result<PlatformOAuthAppDto>.Failure(
                $"Provider '{provider}' is not an approved OAuth provider.", 400);

        if (request.AppName is not null && request.AppName.Length > 100)
            return Result<PlatformOAuthAppDto>.Failure(
                "appName must be at most 100 characters.", 400);

        if (request.LogoUrl is not null && request.LogoUrl.Length > 500)
            return Result<PlatformOAuthAppDto>.Failure(
                "logoUrl must be at most 500 characters.", 400);

        if (request.ClientId is not null && request.ClientId.Length > 200)
            return Result<PlatformOAuthAppDto>.Failure(
                "clientId must be at most 200 characters.", 400);

        if (request.ClientSecret is not null && string.IsNullOrWhiteSpace(request.ClientSecret))
            return Result<PlatformOAuthAppDto>.Failure(
                "clientSecret must be omitted or non-empty.", 400);

        if (request.PrivateKey is not null && string.IsNullOrWhiteSpace(request.PrivateKey))
            return Result<PlatformOAuthAppDto>.Failure(
                "privateKey must be omitted or non-empty.", 400);

        var now = DateTimeOffset.UtcNow;
        var app = await _repo.GetByProviderAsync(provider, cancellationToken);
        var isNewApp = app is null;

        if (isNewApp)
        {
            app = new PlatformOAuthApp
            {
                Id = Guid.NewGuid(),
                Provider = provider,
                AppName = !string.IsNullOrWhiteSpace(request.AppName)
                    ? request.AppName.Trim()
                    : definition.DisplayName,
                LogoUrl = request.LogoUrl,
                ClientId = request.ClientId?.Trim() ?? string.Empty,
                AuthorizationUrl = definition.AuthorizationUrl,
                TokenUrl = definition.TokenUrl,
                DefaultScopes = definition.DefaultScopes,
                IsActive = false,
                LastVerifiedAt = null,
                UpdatedById = request.ActorPlatformUserId,
                UpdatedAt = now
            };
            await _repo.AddAsync(app, cancellationToken);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(request.AppName))
                app!.AppName = request.AppName.Trim();

            if (request.LogoUrl is not null)
                app!.LogoUrl = request.LogoUrl;

            if (request.ClientId is not null)
                app!.ClientId = request.ClientId.Trim();

            // Protocol metadata always refreshed from the backend-owned catalog,
            // never accepted from the request body.
            app!.AuthorizationUrl = definition.AuthorizationUrl;
            app.TokenUrl = definition.TokenUrl;
            app.DefaultScopes = definition.DefaultScopes;
            app.UpdatedById = request.ActorPlatformUserId;
            app.UpdatedAt = now;
        }

        PlatformOAuthAppCredential? newCredential = null;
        if (request.ClientSecret is not null)
        {
            var activeCredentials = await _repo.GetActiveCredentialsForAppAsync(app!.Id, cancellationToken);
            foreach (var credential in activeCredentials)
            {
                credential.IsActive = false;
                credential.DeactivatedById = request.ActorPlatformUserId;
                credential.DeactivatedAt = now;
            }

            var maxVersion = await _repo.GetMaxCredentialVersionAsync(app.Id, cancellationToken);
            newCredential = new PlatformOAuthAppCredential
            {
                Id = Guid.NewGuid(),
                PlatformOAuthAppId = app.Id,
                ClientSecretEncrypted = _encryption.Encrypt(request.ClientSecret),
                PrivateKeyEncrypted = request.PrivateKey is not null
                    ? _encryption.Encrypt(request.PrivateKey)
                    : null,
                EncryptionKeyVersion = "v1",
                CredentialVersion = maxVersion + 1,
                IsActive = true,
                RotatedById = request.ActorPlatformUserId,
                RotatedAt = now
            };
            await _repo.AddCredentialAsync(newCredential, cancellationToken);
        }

        var activeCredentialAfterChange = newCredential
            ?? (await _repo.GetActiveCredentialsForAppAsync(app!.Id, cancellationToken)).FirstOrDefault();

        if (request.IsActive == true)
        {
            var hasClientId = !string.IsNullOrWhiteSpace(app!.ClientId);
            var hasRequiredCredential = !definition.ClientSecretRequired || activeCredentialAfterChange is not null;

            if (!hasClientId || !hasRequiredCredential)
                return Result<PlatformOAuthAppDto>.Failure(
                    $"OAuth app '{provider}' cannot be activated: clientId and, if required, an active credential must be configured first.",
                    400);

            app.IsActive = true;
        }
        else if (request.IsActive == false)
        {
            app!.IsActive = false;
        }

        await _repo.SaveChangesAsync(cancellationToken);

        return Result<PlatformOAuthAppDto>.Success(
            PlatformOAuthAppMapper.ToDto(definition, app, activeCredentialAfterChange));
    }
}
