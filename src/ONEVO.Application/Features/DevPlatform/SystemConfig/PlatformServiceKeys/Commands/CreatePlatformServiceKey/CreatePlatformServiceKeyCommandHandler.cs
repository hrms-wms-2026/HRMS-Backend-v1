using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Mappers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformProviders.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.PlatformProviders.Entities;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Entities;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Commands.CreatePlatformServiceKey;

/// <summary>
/// Creates a platform service key row with the API key encrypted before persistence.
/// SECURITY: ApiKey is plaintext in this command only; it is encrypted immediately
/// and never stored, returned, or logged raw.
/// </summary>
public sealed record CreatePlatformServiceKeyCommand(
    string ServiceKey,
    string DisplayName,
    string ApiKey,
    Guid ActorPlatformUserId) : IRequest<Result<PlatformServiceKeyDto>>;

public sealed class CreatePlatformServiceKeyCommandHandler
    : IRequestHandler<CreatePlatformServiceKeyCommand, Result<PlatformServiceKeyDto>>
{
    private readonly IPlatformServiceKeyRepository _repo;
    private readonly IPlatformProviderRepository _providerRepository;
    private readonly IEncryptionService _encryption;

    public CreatePlatformServiceKeyCommandHandler(
        IPlatformServiceKeyRepository repo,
        IPlatformProviderRepository providerRepository,
        IEncryptionService encryption)
    {
        _repo = repo;
        _providerRepository = providerRepository;
        _encryption = encryption;
    }

    public async Task<Result<PlatformServiceKeyDto>> Handle(
        CreatePlatformServiceKeyCommand request,
        CancellationToken cancellationToken)
    {
        // 1. Validate service key slug + allowlist
        if (!PlatformServiceKeyCatalog.IsValidSlug(request.ServiceKey))
            return Result<PlatformServiceKeyDto>.Failure(
                "serviceKey must be a lowercase slug (letters, digits, underscores; max 50 chars).", 400);

        var providerFamily = await _providerRepository.GetActiveFamilyAsync(
            request.ServiceKey,
            cancellationToken);
        if (providerFamily is null ||
            !PlatformProviderFamilies.PlatformServiceKeyFamilies.Contains(providerFamily))
        {
            return Result<PlatformServiceKeyDto>.Failure(
                $"Provider '{request.ServiceKey}' is not an active platform service-key provider.", 400);
        }

        // 2. Validate display name
        if (string.IsNullOrWhiteSpace(request.DisplayName) || request.DisplayName.Length > 80)
            return Result<PlatformServiceKeyDto>.Failure(
                "displayName is required and must be at most 80 characters.", 400);

        // 3. Validate API key presence (content is verified separately via /verify)
        if (string.IsNullOrWhiteSpace(request.ApiKey))
            return Result<PlatformServiceKeyDto>.Failure("apiKey is required.", 400);

        // 4. Enforce service_key uniqueness
        var existing = await _repo.GetByServiceKeyAsync(request.ServiceKey, cancellationToken);
        if (existing is not null)
            return Result<PlatformServiceKeyDto>.Conflict(
                $"A platform service key '{request.ServiceKey}' already exists. Use rotate-key to replace its credential.");

        // 4b. A newly created service key is always active immediately. If this is a
        // transactional email provider (sendgrid/resend), it must not conflict with
        // another already-active transactional email provider - the operator must
        // explicitly deactivate the current one first. The backend never auto-deactivates it.
        if (providerFamily == PlatformProviderFamilies.TransactionalEmail)
        {
            var activeEmailProviders = await _repo.ListActiveForProviderFamilyAsync(
                PlatformProviderFamilies.TransactionalEmail,
                cancellationToken);
            foreach (var key in activeEmailProviders)
            {
                return Result<PlatformServiceKeyDto>.Conflict(
                    $"Active transactional email provider '{key.ServiceKey}' already exists. " +
                    $"Deactivate it before activating '{request.ServiceKey}'.");
            }
        }

        // 5. Encrypt - NEVER stored plaintext
        var apiKeyEncrypted = _encryption.Encrypt(request.ApiKey);

        var entity = new PlatformServiceKey
        {
            Id = Guid.NewGuid(),
            ServiceKey = request.ServiceKey,
            DisplayName = request.DisplayName.Trim(),
            ApiKeyEncrypted = apiKeyEncrypted,
            IsActive = true,
            LastVerifiedAt = null,
            UpdatedById = request.ActorPlatformUserId,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await _repo.AddAsync(entity, cancellationToken);
        await _repo.SaveChangesAsync(cancellationToken);

        return Result<PlatformServiceKeyDto>.Success(PlatformServiceKeyMapper.ToDto(entity));
    }
}
