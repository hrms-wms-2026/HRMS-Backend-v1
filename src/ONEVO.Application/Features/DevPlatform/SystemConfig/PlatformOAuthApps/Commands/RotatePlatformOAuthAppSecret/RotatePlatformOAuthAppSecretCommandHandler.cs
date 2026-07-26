using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Mappers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Entities;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Commands.RotatePlatformOAuthAppSecret;

/// <summary>
/// Rotates an OAuth app's secret material: deactivates the current active credential
/// row(s) and inserts a new active row with credential_version = previous max + 1.
/// Old rows are never overwritten or deleted. One SaveChanges = one atomic transaction.
/// Rejects unsupported providers (unknown or Phase 2) before touching the repository.
/// SECURITY: ClientSecret/PrivateKey are plaintext in this command only; encrypted
/// immediately and never stored, returned, or logged raw.
/// </summary>
public sealed record RotatePlatformOAuthAppSecretCommand(
    string Provider,
    string ClientSecret,
    string? PrivateKey,
    Guid ActorPlatformUserId) : IRequest<Result<PlatformOAuthAppDto>>;

public sealed class RotatePlatformOAuthAppSecretCommandHandler
    : IRequestHandler<RotatePlatformOAuthAppSecretCommand, Result<PlatformOAuthAppDto>>
{
    private readonly IPlatformOAuthAppRepository _repo;
    private readonly IEncryptionService _encryption;

    public RotatePlatformOAuthAppSecretCommandHandler(
        IPlatformOAuthAppRepository repo,
        IEncryptionService encryption)
    {
        _repo = repo;
        _encryption = encryption;
    }

    public async Task<Result<PlatformOAuthAppDto>> Handle(
        RotatePlatformOAuthAppSecretCommand request,
        CancellationToken cancellationToken)
    {
        var provider = PlatformOAuthProviderRules.Normalize(request.Provider);

        if (!PlatformOAuthProviderCatalog.TryGet(provider, out var definition))
            return Result<PlatformOAuthAppDto>.Failure(
                $"Provider '{provider}' is not an approved OAuth provider.", 400);

        var app = await _repo.GetByProviderAsync(provider, cancellationToken);
        if (app is null)
            return Result<PlatformOAuthAppDto>.NotFound(
                $"OAuth app for provider '{provider}' was not found.");

        if (string.IsNullOrWhiteSpace(request.ClientSecret))
            return Result<PlatformOAuthAppDto>.Failure("clientSecret is required.", 400);

        if (request.PrivateKey is not null && string.IsNullOrWhiteSpace(request.PrivateKey))
            return Result<PlatformOAuthAppDto>.Failure(
                "privateKey must be omitted or non-empty.", 400);

        var now = DateTimeOffset.UtcNow;

        var activeCredentials = await _repo.GetActiveCredentialsForAppAsync(app.Id, cancellationToken);
        foreach (var credential in activeCredentials)
        {
            credential.IsActive = false;
            credential.DeactivatedById = request.ActorPlatformUserId;
            credential.DeactivatedAt = now;
        }

        var maxVersion = await _repo.GetMaxCredentialVersionAsync(app.Id, cancellationToken);

        var newCredential = new PlatformOAuthAppCredential
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
        await _repo.SaveChangesAsync(cancellationToken);

        return Result<PlatformOAuthAppDto>.Success(
            PlatformOAuthAppMapper.ToDto(definition, app, newCredential));
    }
}
