using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Mappers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.RepositoryInterfaces;
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
    private readonly IEncryptionService _encryption;

    public CreatePlatformServiceKeyCommandHandler(
        IPlatformServiceKeyRepository repo,
        IEncryptionService encryption)
    {
        _repo = repo;
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

        if (!PlatformServiceKeyCatalog.IsSupported(request.ServiceKey))
            return Result<PlatformServiceKeyDto>.Failure(
                $"serviceKey must be one of: {string.Join(", ", PlatformServiceKeyCatalog.SupportedServiceKeys)}.", 400);

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

        // 5. Encrypt — NEVER stored plaintext
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
