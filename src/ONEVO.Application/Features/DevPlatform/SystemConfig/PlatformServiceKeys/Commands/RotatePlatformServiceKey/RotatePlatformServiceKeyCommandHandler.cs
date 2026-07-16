using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Mappers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Commands.RotatePlatformServiceKey;

/// <summary>
/// Replaces the encrypted API key of an existing platform service key.
/// Resets last_verified_at because the new key has not been verified yet.
/// SECURITY: ApiKey is plaintext in this command only; encrypted immediately, never logged.
/// </summary>
public sealed record RotatePlatformServiceKeyCommand(
    string ServiceKey,
    string ApiKey,
    Guid ActorPlatformUserId) : IRequest<Result<PlatformServiceKeyDto>>;

public sealed class RotatePlatformServiceKeyCommandHandler
    : IRequestHandler<RotatePlatformServiceKeyCommand, Result<PlatformServiceKeyDto>>
{
    private readonly IPlatformServiceKeyRepository _repo;
    private readonly IEncryptionService _encryption;

    public RotatePlatformServiceKeyCommandHandler(
        IPlatformServiceKeyRepository repo,
        IEncryptionService encryption)
    {
        _repo = repo;
        _encryption = encryption;
    }

    public async Task<Result<PlatformServiceKeyDto>> Handle(
        RotatePlatformServiceKeyCommand request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ApiKey))
            return Result<PlatformServiceKeyDto>.Failure("apiKey is required.", 400);

        var entity = await _repo.GetByServiceKeyAsync(request.ServiceKey, cancellationToken);
        if (entity is null)
            return Result<PlatformServiceKeyDto>.NotFound(
                $"Platform service key '{request.ServiceKey}' was not found.");

        // Encrypt replacement key — NEVER stored plaintext
        entity.ApiKeyEncrypted = _encryption.Encrypt(request.ApiKey);
        entity.LastVerifiedAt = null;
        entity.UpdatedById = request.ActorPlatformUserId;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        await _repo.SaveChangesAsync(cancellationToken);

        return Result<PlatformServiceKeyDto>.Success(PlatformServiceKeyMapper.ToDto(entity));
    }
}
