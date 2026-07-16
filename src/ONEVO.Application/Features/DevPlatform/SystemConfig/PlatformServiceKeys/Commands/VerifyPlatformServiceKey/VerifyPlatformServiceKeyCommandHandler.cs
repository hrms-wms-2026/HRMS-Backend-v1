using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.ServiceInterfaces;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Commands.VerifyPlatformServiceKey;

/// <summary>
/// Verifies the SAVED key of an existing service key row (no request body — chosen
/// approach: verify-saved-key, not verify-before-save; rotate then verify to test a new key).
/// The stored value is decrypted in memory only, passed to the verification service,
/// and never logged or returned. On success, last_verified_at is stamped.
/// </summary>
public sealed record VerifyPlatformServiceKeyCommand(
    string ServiceKey,
    Guid ActorPlatformUserId) : IRequest<Result<ServiceKeyVerificationResultDto>>;

public sealed class VerifyPlatformServiceKeyCommandHandler
    : IRequestHandler<VerifyPlatformServiceKeyCommand, Result<ServiceKeyVerificationResultDto>>
{
    private readonly IPlatformServiceKeyRepository _repo;
    private readonly IEncryptionService _encryption;
    private readonly IPlatformServiceKeyVerificationService _verification;

    public VerifyPlatformServiceKeyCommandHandler(
        IPlatformServiceKeyRepository repo,
        IEncryptionService encryption,
        IPlatformServiceKeyVerificationService verification)
    {
        _repo = repo;
        _encryption = encryption;
        _verification = verification;
    }

    public async Task<Result<ServiceKeyVerificationResultDto>> Handle(
        VerifyPlatformServiceKeyCommand request,
        CancellationToken cancellationToken)
    {
        var entity = await _repo.GetByServiceKeyAsync(request.ServiceKey, cancellationToken);
        if (entity is null)
            return Result<ServiceKeyVerificationResultDto>.NotFound(
                $"Platform service key '{request.ServiceKey}' was not found.");

        // SECURITY: decrypted in memory only; never logged, never returned.
        var apiKeyPlaintext = _encryption.Decrypt(entity.ApiKeyEncrypted);

        var outcome = await _verification.VerifyAsync(
            entity.ServiceKey, apiKeyPlaintext, cancellationToken);

        if (outcome.Success)
        {
            entity.LastVerifiedAt = outcome.CheckedAt;
            entity.UpdatedById = request.ActorPlatformUserId;
            entity.UpdatedAt = DateTimeOffset.UtcNow;
            await _repo.SaveChangesAsync(cancellationToken);
        }

        return Result<ServiceKeyVerificationResultDto>.Success(new ServiceKeyVerificationResultDto
        {
            Success = outcome.Success,
            CheckedAt = outcome.CheckedAt,
            Message = outcome.Message
        });
    }
}
