using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Mappers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Entities;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Commands.CreatePlatformOAuthApp;

/// <summary>
/// Creates an OAuth app registration plus its first active credential (version 1)
/// in one atomic SaveChanges.
/// SECURITY: ClientSecret/PrivateKey are plaintext in this command only; they are
/// encrypted immediately and never stored, returned, or logged raw.
/// </summary>
public sealed record CreatePlatformOAuthAppCommand(
    string Provider,
    string AppName,
    string? LogoUrl,
    string ClientId,
    string ClientSecret,
    string? PrivateKey,
    string AuthorizationUrl,
    string TokenUrl,
    string[] DefaultScopes,
    bool IsActive,
    Guid ActorPlatformUserId) : IRequest<Result<PlatformOAuthAppDto>>;

public sealed class CreatePlatformOAuthAppCommandHandler
    : IRequestHandler<CreatePlatformOAuthAppCommand, Result<PlatformOAuthAppDto>>
{
    private readonly IPlatformOAuthAppRepository _repo;
    private readonly IEncryptionService _encryption;

    public CreatePlatformOAuthAppCommandHandler(
        IPlatformOAuthAppRepository repo,
        IEncryptionService encryption)
    {
        _repo = repo;
        _encryption = encryption;
    }

    public async Task<Result<PlatformOAuthAppDto>> Handle(
        CreatePlatformOAuthAppCommand request,
        CancellationToken cancellationToken)
    {
        // 1. Normalize + validate provider slug
        var provider = PlatformOAuthProviderRules.Normalize(request.Provider);

        if (!PlatformOAuthProviderRules.IsValidSlug(provider))
            return Result<PlatformOAuthAppDto>.Failure(
                "provider must be a lowercase slug (letters, digits, hyphens; max 30 chars).", 400);

        if (PlatformOAuthProviderRules.IsPhase2Provider(provider))
            return Result<PlatformOAuthAppDto>.Failure(
                "Provider 'slack' is Phase 2 and cannot be registered yet.", 400);

        // 2. Validate metadata
        if (string.IsNullOrWhiteSpace(request.AppName) || request.AppName.Length > 100)
            return Result<PlatformOAuthAppDto>.Failure(
                "appName is required and must be at most 100 characters.", 400);

        if (string.IsNullOrWhiteSpace(request.ClientId) || request.ClientId.Length > 200)
            return Result<PlatformOAuthAppDto>.Failure(
                "clientId is required and must be at most 200 characters.", 400);

        if (!PlatformOAuthProviderRules.IsAbsoluteHttpUrl(request.AuthorizationUrl)
            || request.AuthorizationUrl.Length > 500)
            return Result<PlatformOAuthAppDto>.Failure(
                "authorizationUrl is required and must be an absolute http/https URL (max 500 chars).", 400);

        if (!PlatformOAuthProviderRules.IsAbsoluteHttpUrl(request.TokenUrl)
            || request.TokenUrl.Length > 500)
            return Result<PlatformOAuthAppDto>.Failure(
                "tokenUrl is required and must be an absolute http/https URL (max 500 chars).", 400);

        if (request.LogoUrl is not null && request.LogoUrl.Length > 500)
            return Result<PlatformOAuthAppDto>.Failure(
                "logoUrl must be at most 500 characters.", 400);

        if (request.DefaultScopes.Length == 0
            || request.DefaultScopes.Any(string.IsNullOrWhiteSpace))
            return Result<PlatformOAuthAppDto>.Failure(
                "defaultScopes is required and must contain at least one non-empty scope.", 400);

        // 3. Validate secret presence (content is not verified against providers in this step)
        if (string.IsNullOrWhiteSpace(request.ClientSecret))
            return Result<PlatformOAuthAppDto>.Failure("clientSecret is required.", 400);

        if (request.PrivateKey is not null && string.IsNullOrWhiteSpace(request.PrivateKey))
            return Result<PlatformOAuthAppDto>.Failure(
                "privateKey must be omitted or non-empty.", 400);

        // 4. Enforce provider uniqueness
        var existing = await _repo.GetByProviderAsync(provider, cancellationToken);
        if (existing is not null)
            return Result<PlatformOAuthAppDto>.Conflict(
                $"An OAuth app for provider '{provider}' already exists. Use rotate-secret to replace its credential.");

        // 5. Encrypt — plaintext is NEVER persisted
        var clientSecretEncrypted = _encryption.Encrypt(request.ClientSecret);
        var privateKeyEncrypted = request.PrivateKey is not null
            ? _encryption.Encrypt(request.PrivateKey)
            : null;

        var now = DateTimeOffset.UtcNow;

        var app = new PlatformOAuthApp
        {
            Id = Guid.NewGuid(),
            Provider = provider,
            AppName = request.AppName.Trim(),
            LogoUrl = request.LogoUrl,
            ClientId = request.ClientId.Trim(),
            AuthorizationUrl = request.AuthorizationUrl.Trim(),
            TokenUrl = request.TokenUrl.Trim(),
            DefaultScopes = request.DefaultScopes.Select(s => s.Trim()).ToArray(),
            IsActive = request.IsActive,
            LastVerifiedAt = null,
            UpdatedById = request.ActorPlatformUserId,
            UpdatedAt = now
        };

        var credential = new PlatformOAuthAppCredential
        {
            Id = Guid.NewGuid(),
            PlatformOAuthAppId = app.Id,
            ClientSecretEncrypted = clientSecretEncrypted,
            PrivateKeyEncrypted = privateKeyEncrypted,
            EncryptionKeyVersion = "v1",
            CredentialVersion = 1,
            IsActive = true,
            RotatedById = request.ActorPlatformUserId,
            RotatedAt = now
        };

        // 6. One SaveChanges = one atomic transaction for both rows
        await _repo.AddAsync(app, cancellationToken);
        await _repo.AddCredentialAsync(credential, cancellationToken);
        await _repo.SaveChangesAsync(cancellationToken);

        return Result<PlatformOAuthAppDto>.Success(PlatformOAuthAppMapper.ToDto(app, credential));
    }
}
